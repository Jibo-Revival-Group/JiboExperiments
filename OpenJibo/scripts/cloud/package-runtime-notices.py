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


def collect(data, whisper=False):
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
            selected = name == "LICENSE" if whisper else bool(
                re.fullmatch(r"doc/[a-z0-9][a-z0-9+.-]*/copyright", name))
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


def package(docs_tar, whisper_tar, inventory, profile, output):
    document, inventory_hash = policy.load_inventory(inventory)
    review = policy.review(document, profile, inventory_hash)
    docs = read_tar(docs_tar)
    whisper = read_tar(whisper_tar)
    notices, links = collect(docs)
    native, native_links = collect(whisper, whisper=True)
    notices.update(native)
    if sum(map(len, notices.values())) > MAX_TOTAL:
        raise ValueError("Combined notice collection exceeds size limit")
    manifest = {
        "schema_version": 1, "runtime_image": review["runtime_image"],
        "cpu_profile": profile, "source_commit": policy.SOURCE,
        "syft_json_sha256": inventory_hash,
        "input_tar_sha256": {"docs": hashlib.sha256(docs).hexdigest(),
                             "whisper": hashlib.sha256(whisper).hexdigest()},
        "files": {name: hashlib.sha256(data).hexdigest() for name, data in sorted(notices.items())},
        "unfollowed_links": links + native_links,
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
        bundle.writestr("manifest.json", json.dumps(manifest, indent=2, sort_keys=True))
    return {"packaged": True, "notice_count": len(notices),
            "unfollowed_link_count": len(links) + len(native_links),
            "runtime_image": review["runtime_image"], "license_review_approved": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--docs-tar", required=True)
    parser.add_argument("--whisper-tar", required=True)
    parser.add_argument("--inventory", required=True)
    parser.add_argument("--cpu-profile", required=True, choices=sorted(policy.DIGESTS))
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(package(args.docs_tar, args.whisper_tar, args.inventory,
                                 args.cpu_profile, args.output), sort_keys=True))
    except (OSError, ValueError, TypeError, AttributeError, tarfile.TarError):
        parser.exit(1, "Notice packaging failed; preserve inputs and inspect the bounded archive/image policy.\n")


if __name__ == "__main__":
    main()
