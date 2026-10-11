"""Compare copied framework files with exact NuGet payloads as data, not trust approval."""
import argparse
import hashlib
import importlib.util
import io
import json
from pathlib import Path, PurePosixPath
import tarfile
import zipfile

spec = importlib.util.spec_from_file_location("notices", Path(__file__).with_name("collect-runtime-nuget-notices.py"))
notices = importlib.util.module_from_spec(spec)
spec.loader.exec_module(notices)
policy = notices.metadata.policy
FRAMEWORKS = {"Microsoft.AspNetCore.App.Runtime.linux-x64": "Microsoft.AspNetCore.App",
              "Microsoft.NETCore.App.Runtime.linux-x64": "Microsoft.NETCore.App"}
VERSION = "10.0.12"
MAX_TAR = 512 * 1024 * 1024
MAX_FILE = 64 * 1024 * 1024
MAX_MEMBERS = 4000


def safe_path(name):
    path = PurePosixPath(name)
    if (not name or path.is_absolute() or ".." in path.parts or str(path) != name
            or "\\" in name or ":" in name or "\x00" in name):
        raise ValueError("Unsafe or ambiguous member path")
    return path


def digest_stream(stream, size):
    if not 0 < size <= MAX_FILE:
        raise ValueError("Invalid file size")
    digest, total = hashlib.sha256(), 0
    while chunk := stream.read(1024 * 1024):
        total += len(chunk)
        if total > size:
            raise ValueError("Oversized member")
        digest.update(chunk)
    if total != size:
        raise ValueError("Truncated member")
    return digest.hexdigest()


def image_files(path):
    if not 0 < Path(path).stat().st_size <= MAX_TAR:
        raise ValueError("Invalid copied TAR size")
    files, seen, total = {}, set(), 0
    with tarfile.open(path, mode="r:") as archive:
        for index, member in enumerate(archive):
            name = member.name.rstrip("/") if member.isdir() else member.name
            parts = safe_path(name).parts
            if index >= MAX_MEMBERS or name.casefold() in seen:
                raise ValueError("Too many or duplicate TAR members")
            seen.add(name.casefold())
            if member.isdir():
                if parts[0] != "shared":
                    raise ValueError("Unexpected directory")
                continue
            if (not member.isfile() or member.sparse is not None or len(parts) < 4
                    or parts[0] != "shared" or parts[1] not in FRAMEWORKS.values()
                    or parts[2] != VERSION):
                raise ValueError("Unexpected file or unsupported TAR member")
            total += member.size
            if total > MAX_TAR:
                raise ValueError("Copied payload exceeds limit")
            files[name] = digest_stream(archive.extractfile(member), member.size)
    if not files:
        raise ValueError("Empty framework copy")
    return files


def package_files(data, item):
    # Reuse bounded ZIP safety and exact manifest-identity validation.
    notices.inspect_package(data, item)
    files, total = {}, 0
    prefixes = ("runtimes/linux-x64/lib/net10.0/", "runtimes/linux-x64/native/")
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for member in archive.infolist():
            prefix = next((p for p in prefixes if member.filename.startswith(p)), None)
            if prefix is None or member.is_dir():
                continue
            relative = member.filename[len(prefix):]
            safe_path(relative)
            target = f"shared/{FRAMEWORKS[item['name']]}/{VERSION}/{relative}"
            if item["name"] == "Microsoft.NETCore.App.Runtime.linux-x64" and relative == "libhostfxr.so":
                target = f"host/fxr/{VERSION}/libhostfxr.so"
            if target.casefold() in {name.casefold() for name in files}:
                raise ValueError("Ambiguous package payload mapping")
            total += member.file_size
            if total > MAX_TAR:
                raise ValueError("Package payload exceeds limit")
            with archive.open(member) as stream:
                files[target] = digest_stream(stream, member.file_size)
    if not files:
        raise ValueError("Missing framework payload")
    return files


def hostfxr_files(path):
    if not 0 < Path(path).stat().st_size <= MAX_FILE:
        raise ValueError("Invalid hostfxr TAR size")
    with tarfile.open(path, mode="r:") as archive:
        member = archive.next()
        if (member is None or member.name != "libhostfxr.so" or not member.isfile()
                or member.sparse is not None):
            raise ValueError("Expected one regular hostfxr file")
        digest = digest_stream(archive.extractfile(member), member.size)
        if archive.next() is not None:
            raise ValueError("Unexpected extra hostfxr members")
    return {f"host/fxr/{VERSION}/libhostfxr.so": digest}


def compare(inventory, profile, copied_tar, output, release_version=policy.LEGACY_RELEASE,
            downloader=notices.download, hostfxr_tar=None):
    if Path(output).exists():
        raise ValueError("Preserve existing output")
    document, inventory_hash = policy.load_inventory(inventory)
    identity = policy.review(document, profile, inventory_hash, release_version)
    selected, _ = notices.metadata.packages(document)
    items = [item for item in selected if item["name"] in FRAMEWORKS]
    if len(items) != 2 or any(item["version"] != VERSION for item in items):
        raise ValueError("Expected both exact framework package identities")
    shipped = image_files(copied_tar)
    with Path(copied_tar).open("rb") as stream:
        copied_hash = hashlib.file_digest(stream, "sha256").hexdigest()
    hostfxr_hash = None
    if hostfxr_tar is not None:
        shipped.update(hostfxr_files(hostfxr_tar))
        with Path(hostfxr_tar).open("rb") as stream:
            hostfxr_hash = hashlib.file_digest(stream, "sha256").hexdigest()
    report = {"schema_version": 1, "release_version": release_version,
              "runtime_image": identity["runtime_image"], "source_commit": identity["source_commit"],
              "cpu_profile": profile, "syft_json_sha256": inventory_hash, "packages": [],
              "copied_tar_sha256": copied_hash, "copied_file_sha256": shipped,
              "hostfxr_tar_sha256": hostfxr_hash,
              "license_review_approved": False, "stable_promotion_allowed": False,
              "scope": "File equality for copied framework trees and exact-version public NuGet payloads only. Acquisition is not attested; container modifications, package signature trust, scanner content hashes, notices, corresponding source and legal approval are not certified. No package or runtime code is executed."}
    for item in items:
        data = downloader(notices.package_url(item))
        expected = package_files(data, item)
        missing = sorted(set(expected) - set(shipped))
        mismatched = sorted(name for name, digest in expected.items()
                            if name in shipped and shipped[name] != digest)
        root = f"shared/{FRAMEWORKS[item['name']]}/{VERSION}/"
        extras = sorted(name for name in shipped if name.startswith(root) and name not in expected)
        report["packages"].append({"name": item["name"], "version": item["version"],
            "package_url": notices.package_url(item), "archive_sha256": hashlib.sha256(data).hexdigest(),
            "payload_file_count": len(expected), "matching_file_count": len(expected) - len(missing) - len(mismatched),
            "missing_files": missing, "mismatched_files": mismatched, "uncompared_image_files": extras,
            "all_selected_payload_files_match": not missing and not mismatched,
            "package_signature_trust_verified": False, "scanner_content_hash_verified": False,
            "payload_sha256": expected})
    with Path(output).open("x", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2, sort_keys=True)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True)
    parser.add_argument("--cpu-profile", required=True, choices=sorted(policy.DIGESTS))
    parser.add_argument("--release-version", choices=policy.RELEASES, default=policy.LEGACY_RELEASE)
    parser.add_argument("--framework-tar", required=True)
    parser.add_argument("--hostfxr-tar", help="Optional separate TAR containing only the relocated libhostfxr.so")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try:
        report = compare(args.inventory, args.cpu_profile, args.framework_tar, args.output, args.release_version,
                         hostfxr_tar=args.hostfxr_tar)
        print(json.dumps({"runtime_image": report["runtime_image"], "packages": [
            {key: item[key] for key in ("name", "payload_file_count", "matching_file_count", "all_selected_payload_files_match")}
            for item in report["packages"]], "stable_promotion_allowed": False}, sort_keys=True))
        if any(not item["all_selected_payload_files_match"] for item in report["packages"]):
            parser.exit(2, "Framework payload differs or is missing; review the preserved report.\n")
    except (OSError, ValueError, KeyError, TypeError, AttributeError, notices.metadata.ET.ParseError,
            tarfile.TarError, zipfile.BadZipFile):
        parser.exit(1, "Framework comparison failed; preserve existing evidence. No trust approval recorded.\n")


if __name__ == "__main__":
    main()
