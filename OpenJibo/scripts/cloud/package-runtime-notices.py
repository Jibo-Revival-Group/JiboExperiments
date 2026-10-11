"""Collect bounded license texts from image-copy tar streams without filesystem extraction."""
import argparse
import hashlib
import importlib.util
import io
import json
from pathlib import Path, PurePosixPath
import re
import tarfile
import zipfile

spec = importlib.util.spec_from_file_location("notice_policy", Path(__file__).with_name("review-runtime-sbom.py"))
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)
MAX_ARCHIVE = 64 * 1024 * 1024
MAX_NOTICE = 1024 * 1024
MAX_TOTAL = 32 * 1024 * 1024


def read_tar(path):
    with Path(path).open("rb") as stream:
        data = stream.read(MAX_ARCHIVE + 1)
    if not data or len(data) > MAX_ARCHIVE:
        raise ValueError("Empty or oversized notice archive")
    return data


def dependency_checklist(document, notice_files):
    """Group identical review evidence, not names alone; never infer approval."""
    groups = {}
    for package in document["artifacts"]:
        metadata = package.get("metadata", {})
        if metadata is None:
            metadata = {}
        if not isinstance(metadata, dict):
            raise ValueError("Malformed package metadata")
        identity = {}
        for field in ("name", "version", "type", "purl"):
            value = package.get(field, "")
            if value is None:
                value = ""
            if not isinstance(value, str) or len(value) > 4096:
                raise ValueError("Malformed package identity")
            identity[field] = value
        item = {"identity": identity, "review_status": "unreviewed",
                "scanner_license_declarations": package.get("licenses", []),
                "copied_notice": None, "source_verified": False}
        if identity["type"] == "deb":
            source = {}
            for target, field, fallback in (("name", "source", identity["name"]),
                                             ("version", "sourceVersion", identity["version"])):
                value = metadata.get(field, "")
                if not isinstance(value, str) or len(value) > 4096:
                    raise ValueError("Malformed source metadata")
                source[target] = {"value": value or fallback,
                                  "basis": "scanner_metadata" if value else "inferred_binary_fallback"}
            item["source_candidate"] = source
            name = identity["name"]
            if re.fullmatch(r"[a-z0-9][a-z0-9+.-]*", name):
                candidate = "notices/doc/" + name + "/copyright"
                if candidate in notice_files:
                    item["copied_notice"] = candidate
            item["required_check"] = "Verify exact distro source, patches/build configuration and notice coverage"
        elif identity["type"] == "dotnet":
            item["component_kind"] = ("nuget_package_metadata" if metadata.get("type") == "package"
                                      and identity["purl"].startswith("pkg:nuget/") else "dotnet_record_unclassified")
            item["required_check"] = "Verify package/framework/project identity, exact terms and required notices"
        else:
            item["required_check"] = "Verify shipped component identity, source and notice coverage"
        key = json.dumps(item, sort_keys=True)
        if key not in groups:
            groups[key] = dict(item, inventory_record_count=0)
        groups[key]["inventory_record_count"] += 1
    return {"schema_version": 1, "inventory_record_count": len(document["artifacts"]),
            "review_entry_count": len(groups), "entries": [groups[key] for key in sorted(groups)],
            "license_review_approved": False,
            "scope": "Offline review checklist. Matching notice paths are not completeness evidence. Source candidates are not verified corresponding source; no links are fetched or code executed."}


def collect(data, whisper=False, common=False):
    notices, links, seen = {}, [], set()
    with tarfile.open(fileobj=io.BytesIO(data), mode="r:") as archive:
        for index, member in enumerate(archive):
            if index >= 20000:
                raise ValueError("Too many archive entries")
            name = member.name.rstrip("/")
            path = PurePosixPath(name)
            if (not name or path.is_absolute() or ".." in path.parts
                    or str(path) != name or "\\" in name or name in seen):
                raise ValueError("Unsafe or repeated archive path")
            seen.add(name)
            if member.issym() or member.islnk():
                links.append({"path": name, "target": member.linkname})
                continue
            if member.isdir():
                continue
            if not member.isfile():
                raise ValueError("Unsupported archive entry")
            selected = name == "LICENSE" if whisper else bool(re.fullmatch(
                r"common-licenses/[A-Za-z0-9_.+-]+" if common else
                r"doc/[a-z0-9][a-z0-9+.-]*/copyright", name))
            if not selected:
                continue
            if member.size <= 0 or member.size > MAX_NOTICE:
                raise ValueError("Invalid notice size")
            content = archive.extractfile(member).read(MAX_NOTICE + 1)
            if len(content) != member.size:
                raise ValueError("Truncated notice")
            notices[("notices/whisper/LICENSE" if whisper else "notices/" + name)] = content
            if sum(map(len, notices.values())) > MAX_TOTAL:
                raise ValueError("Notice collection exceeds size limit")
    if not notices:
        raise ValueError("No expected notices found")
    return notices, links


def package(docs_tar, whisper_tar, common_tar, inventory, profile, output, release_version=policy.LEGACY_RELEASE):
    document, inventory_hash = policy.load_inventory(inventory)
    review = policy.review(document, profile, inventory_hash, release_version)
    docs = read_tar(docs_tar)
    whisper = read_tar(whisper_tar)
    common = read_tar(common_tar)
    notices, links = collect(docs)
    native, native_links = collect(whisper, whisper=True)
    shared, shared_links = collect(common, common=True)
    notices.update(native)
    notices.update(shared)
    if sum(map(len, notices.values())) > MAX_TOTAL:
        raise ValueError("Combined notice collection exceeds size limit")
    checklist = dependency_checklist(document, notices)
    checklist.update(runtime_image=review["runtime_image"], syft_json_sha256=inventory_hash,
                     source_commit=review["source_commit"], cpu_profile=profile, release_version=release_version)
    checklist_bytes = json.dumps(checklist, indent=2, sort_keys=True).encode("utf-8")
    if len(checklist_bytes) > MAX_TOTAL:
        raise ValueError("Dependency checklist exceeds size limit")
    manifest = {
        "schema_version": 1, "runtime_image": review["runtime_image"],
        "cpu_profile": profile, "source_commit": review["source_commit"], "release_version": release_version,
        "syft_json_sha256": inventory_hash,
        "dependency_review_sha256": hashlib.sha256(checklist_bytes).hexdigest(),
        "input_tar_sha256": {"docs": hashlib.sha256(docs).hexdigest(),
                             "whisper": hashlib.sha256(whisper).hexdigest(),
                             "common_licenses": hashlib.sha256(common).hexdigest()},
        "files": {name: hashlib.sha256(data).hexdigest() for name, data in sorted(notices.items())},
        "unfollowed_links": links + native_links + shared_links,
        "review_owner": "Repository owner (designated by user)",
        "license_review_approved": False, "stable_promotion_allowed": False,
        "remaining_review": ["NuGet notices missing from final filesystem inventory",
                             "Speech-model redistribution terms and provenance",
                             "Corresponding source availability and actual FFmpeg build obligations",
                             "Native linked-library completeness and unfollowed documentation links"],
        "scope": "Copied license texts only, not complete notices, legal approval or a source offer.",
    }
    with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED) as bundle:
        for name, data in sorted(notices.items()):
            bundle.writestr(name, data)
        bundle.writestr("dependency-review.json", checklist_bytes)
        bundle.writestr("manifest.json", json.dumps(manifest, indent=2, sort_keys=True))
    return {"packaged": True, "notice_count": len(notices),
            "dependency_review_entry_count": checklist["review_entry_count"],
            "unfollowed_link_count": len(links) + len(native_links) + len(shared_links),
            "runtime_image": review["runtime_image"], "license_review_approved": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--docs-tar", required=True)
    parser.add_argument("--whisper-tar", required=True)
    parser.add_argument("--common-tar", required=True)
    parser.add_argument("--inventory", required=True)
    parser.add_argument("--cpu-profile", required=True, choices=sorted(policy.DIGESTS))
    parser.add_argument("--output", required=True)
    parser.add_argument("--release-version", choices=[policy.LEGACY_RELEASE, policy.AUDIO_RELEASE], default=policy.LEGACY_RELEASE)
    args = parser.parse_args()
    try:
        print(json.dumps(package(args.docs_tar, args.whisper_tar, args.common_tar, args.inventory,
                                 args.cpu_profile, args.output, args.release_version), sort_keys=True))
    except (OSError, ValueError, TypeError, AttributeError, tarfile.TarError):
        parser.exit(1, "Notice packaging failed; preserve inputs and inspect the bounded archive/image policy.\n")


if __name__ == "__main__":
    main()
