"""Read bounded NuGet archives as data and preserve supplemental notice evidence."""
import argparse
import base64
import hashlib
import importlib.util
import io
import json
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import urllib.request
import zipfile

spec = importlib.util.spec_from_file_location("metadata", Path(__file__).with_name("collect-runtime-nuget-metadata.py"))
metadata = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metadata)
MAX_PACKAGE = 128 * 1024 * 1024
MAX_DOWNLOAD = 512 * 1024 * 1024
MAX_NOTICE = 1024 * 1024
MAX_NOTICES = 32 * 1024 * 1024


def package_url(item):
    name, version = item["name"].lower(), item["version"].lower()
    return f"{metadata.BASE}{name}/{version}/{name}.{version}.nupkg"


def download(url):
    if not url.startswith(metadata.BASE):
        raise ValueError("Unexpected package feed")
    request = urllib.request.Request(url, headers={"Accept-Encoding": "identity", "User-Agent": "OpenJibo-release-review/1"})
    with urllib.request.build_opener(metadata.NoRedirect()).open(request, timeout=30) as response:
        if response.geturl() != url:
            raise ValueError("Unexpected final URL")
        data = response.read(MAX_PACKAGE + 1)
    if not data or len(data) > MAX_PACKAGE:
        raise ValueError("Empty or oversized package")
    return data


def declared_license_path(manifest):
    declaration = manifest.get("license")
    if declaration is None:
        return None, "missing_declaration"
    if declaration["attributes"].get("type") != "file":
        return None, "not_file_declaration"
    name = declaration["text"]
    path = PurePosixPath(name)
    if (not name or len(name) > 2048 or path.is_absolute() or ".." in path.parts
            or str(path) != name or "\\" in name or ":" in name or "\x00" in name):
        return None, "unsafe_reference_unfollowed"
    return name, "pending"


def inspect_package(data, item):
    if not data or len(data) > MAX_PACKAGE:
        raise ValueError("Invalid package size")
    files, seen, members, manifest = {}, set(), {}, None
    license_status = "missing_declaration"
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for index, member in enumerate(archive.infolist()):
            name = member.filename.rstrip("/")
            path = PurePosixPath(name)
            if (index >= 10000 or not name or path.is_absolute() or ".." in path.parts
                    or str(path) != name or "\\" in name or ":" in name or name.casefold() in seen
                    or member.orig_filename != member.filename
                    or member.flag_bits & 1
                    or stat.S_ISLNK(member.external_attr >> 16)):
                raise ValueError("Unsafe, ambiguous or unsupported package member")
            seen.add(name.casefold())
            members[name] = member
            if name == ".signature.p7s" and member.file_size > MAX_NOTICE:
                raise ValueError("Oversized package signature metadata")
            if member.is_dir():
                continue
            is_manifest = len(path.parts) == 1 and name.lower().endswith(".nuspec")
            is_notice = bool(re.fullmatch(r"(?:licen[cs]e|notice|third[-_. ]party[-_. ]notices?)(?:[._-].*)?",
                                          path.name, flags=re.IGNORECASE))
            if not (is_manifest or is_notice):
                continue
            if member.file_size <= 0 or member.file_size > MAX_NOTICE:
                raise ValueError("Invalid selected member size")
            with archive.open(member) as stream:
                content = stream.read(MAX_NOTICE + 1)
            if len(content) != member.file_size:
                raise ValueError("Truncated selected member")
            if is_manifest:
                if manifest is not None:
                    raise ValueError("Multiple package manifests")
                manifest = metadata.inspect_manifest(content, item)
            files[name] = content
            if sum(map(len, files.values())) > MAX_NOTICES:
                raise ValueError("Oversized notice collection")
        if manifest is not None:
            reference, license_status = declared_license_path(manifest)
            if reference is not None:
                member = members.get(reference)
                if member is None:
                    license_status = "referenced_member_missing"
                else:
                    if member.is_dir():
                        license_status = "referenced_member_is_directory"
                    else:
                        if not 0 < member.file_size <= MAX_NOTICE:
                            raise ValueError("Invalid declared license member size")
                        with archive.open(member) as stream:
                            content = stream.read(MAX_NOTICE + 1)
                        if len(content) != member.file_size:
                            raise ValueError("Truncated declared license member")
                        files[reference] = content
                        if sum(map(len, files.values())) > MAX_NOTICES:
                            raise ValueError("Oversized notice collection")
                        license_status = "copied_exact_package_member"
    if manifest is None:
        raise ValueError("Missing package manifest")
    raw_hash = "sha512-" + base64.b64encode(hashlib.sha512(data).digest()).decode("ascii")
    scanner_hashes = item["scanner_sha512_values"]
    return files, {"manifest_fields": manifest, "notice_member_count": len(files) - 1,
                   "declared_license_file_status": license_status,
                   "archive_sha256": hashlib.sha256(data).hexdigest(),
                   "raw_archive_sha512": raw_hash,
                   "raw_archive_sha512_matches_scanner": len(scanner_hashes) == 1 and scanner_hashes[0] == raw_hash,
                   "nuget_content_hash_verified": False, "package_signature_verified": False}


def verify_content(data, item, verifier):
    if len(item["scanner_sha512_values"]) != 1:
        raise ValueError("Expected one scanner content hash")
    expected = item["scanner_sha512_values"][0]
    result = subprocess.run(["dotnet", str(verifier), expected], input=data,
                            capture_output=True, check=True, timeout=60)
    if len(result.stdout) > 16384:
        raise ValueError("Oversized verifier output")
    report = json.loads(result.stdout)
    if (report.get("nuget_content_hash_verified") is not True or report.get("content_hash") != expected
            or report.get("package_signature_trust_verified") is not False):
        raise ValueError("Unexpected content verification result")
    return report


def collect(inventory, profile, output, fetch=False, downloader=download, verifier=None,
            release_version=metadata.policy.LEGACY_RELEASE):
    if fetch and (output is None or Path(output).exists()):
        raise ValueError("Select a fresh output before fetching")
    if verifier is not None and not Path(verifier).is_file():
        raise ValueError("Verifier must be built before collection")
    document, inventory_hash = metadata.policy.load_inventory(inventory)
    review = metadata.policy.review(document, profile, inventory_hash, release_version)
    selected, excluded = metadata.packages(document)
    report = {"schema_version": 1, "runtime_image": review["runtime_image"], "source_commit": review["source_commit"],
              "release_version": release_version,
              "cpu_profile": profile, "syft_json_sha256": inventory_hash, "packages": [],
              "excluded_dotnet_records": excluded, "license_review_approved": False,
              "stable_promotion_allowed": False,
              "scope": "Unsigned supplemental archive/notice evidence. Optional SDK verifier checks package integrity/content hashes where scanner hashes exist, not certificate trust. Raw archive hashes are a separate check. Neither proves image binary linkage, complete notices or corresponding source. No packages installed, extracted to filesystem or executed."}
    files, downloaded = {}, 0
    for item in selected:
        entry = dict(item, package_url=package_url(item), review_status="unreviewed")
        if fetch:
            data = downloader(entry["package_url"])
            downloaded += len(data)
            if downloaded > MAX_DOWNLOAD:
                raise ValueError("Download collection exceeds limit")
            contents, package_report = inspect_package(data, item)
            entry.update(package_report)
            if verifier is not None:
                if not item["scanner_sha512_values"]:
                    entry["content_verification_status"] = "scanner_hash_missing"
                else:
                    entry.update(verify_content(data, item, verifier))
                    entry["content_verification_status"] = "matched_scanner_hash"
            prefix = f"packages/{item['name'].lower()}/{item['version'].lower()}/"
            entry["files"] = {prefix + path: hashlib.sha256(content).hexdigest()
                              for path, content in sorted(contents.items())}
            files.update({prefix + path: content for path, content in contents.items()})
            if sum(map(len, files.values())) > MAX_NOTICES:
                raise ValueError("Combined notices exceed limit")
        report["packages"].append(entry)
    if fetch:
        with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED) as archive:
            for path, content in sorted(files.items()):
                archive.writestr(path, content)
            archive.writestr("review.json", json.dumps(report, indent=2, sort_keys=True))
    return {"fetched": fetch, "runtime_image": review["runtime_image"], "package_count": len(selected),
            "copied_file_count": len(files), "downloaded_bytes": downloaded,
            "raw_archive_hash_matches": sum(p.get("raw_archive_sha512_matches_scanner", False) for p in report["packages"]),
            "content_hash_verified_count": sum(p.get("nuget_content_hash_verified", False) for p in report["packages"]),
            "license_review_approved": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True)
    parser.add_argument("--release-version", choices=metadata.policy.RELEASES,
                        default=metadata.policy.LEGACY_RELEASE)
    parser.add_argument("--cpu-profile", required=True, choices=sorted(metadata.policy.DIGESTS))
    parser.add_argument("--output")
    parser.add_argument("--fetch", action="store_true")
    parser.add_argument("--content-verifier", help="Explicit built SDK NuGet verifier DLL; never executes package code")
    args = parser.parse_args()
    try:
        print(json.dumps(collect(args.inventory, args.cpu_profile, args.output, args.fetch,
                                  verifier=args.content_verifier, release_version=args.release_version), sort_keys=True))
    except (OSError, ValueError, TypeError, AttributeError, KeyError, zipfile.BadZipFile, RuntimeError, subprocess.SubprocessError):
        parser.exit(1, "NuGet archive collection failed; preserve existing evidence. No approval recorded.\n")


if __name__ == "__main__":
    main()
