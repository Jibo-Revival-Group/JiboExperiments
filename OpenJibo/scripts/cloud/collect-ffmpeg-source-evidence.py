"""Preserve exact-version Ubuntu FFmpeg source candidates; never approve distribution."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import urllib.request
import zipfile

spec = importlib.util.spec_from_file_location("metadata", Path(__file__).with_name("collect-runtime-nuget-metadata.py"))
metadata = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metadata)
BASE = "https://archive.ubuntu.com/ubuntu/pool/universe/f/ffmpeg/"
VERSION = "7:6.1.1-3ubuntu5"
DESCRIPTOR = "ffmpeg_6.1.1-3ubuntu5.dsc"
MAX_DESCRIPTOR = 128 * 1024
MAX_FILE = 32 * 1024 * 1024
MAX_TOTAL = 64 * 1024 * 1024


def descriptor_files(data):
    if not data or len(data) > MAX_DESCRIPTOR:
        raise ValueError("Invalid source descriptor size")
    text = data.decode("utf-8")
    if not text.startswith("-----BEGIN PGP SIGNED MESSAGE-----\n"):
        raise ValueError("Expected preserved clear-signed descriptor")
    try:
        body = text.split("\n\n", 1)[1].split("\n-----BEGIN PGP SIGNATURE-----", 1)[0]
    except IndexError:
        raise ValueError("Invalid source descriptor envelope")
    if "\n-----BEGIN PGP SIGNATURE-----" not in text or "-----END PGP SIGNATURE-----" not in text:
        raise ValueError("Incomplete source descriptor envelope")
    fields, current = {}, None
    for line in body.splitlines():
        if not line:
            continue
        if line.startswith(" "):
            if current is None:
                raise ValueError("Unbound continuation")
            fields[current].append(line.strip())
        else:
            key, separator, value = line.partition(":")
            if not separator or key in fields:
                raise ValueError("Invalid or duplicate source field")
            fields[key] = [value.strip()]
            current = key
    if fields.get("Source") != ["ffmpeg"] or fields.get("Version") != [VERSION]:
        raise ValueError("Source descriptor identity mismatch")
    files, seen = [], set()
    for line in fields.get("Checksums-Sha256", [])[1:]:
        parts = line.split()
        if len(parts) != 3:
            raise ValueError("Invalid checksum row")
        digest, size, name = parts
        if (not re.fullmatch(r"[0-9a-f]{64}", digest) or not size.isdecimal()
                or not re.fullmatch(r"ffmpeg_6\.1\.1(?:\.orig\.tar\.xz(?:\.asc)?|-3ubuntu5\.debian\.tar\.xz)", name)
                or name in seen or not 0 < int(size) <= MAX_FILE):
            raise ValueError("Unsafe or oversized source entry")
        seen.add(name)
        files.append({"name": name, "size": int(size), "sha256": digest})
    if seen != {"ffmpeg_6.1.1.orig.tar.xz", "ffmpeg_6.1.1.orig.tar.xz.asc", "ffmpeg_6.1.1-3ubuntu5.debian.tar.xz"}:
        raise ValueError("Incomplete source candidates")
    if sum(item["size"] for item in files) > MAX_TOTAL:
        raise ValueError("Source collection exceeds limit")
    return files


def download(name, limit):
    # Names are fixed by policy or validated against the exact-version allowlist.
    request = urllib.request.Request(BASE + name, headers={"Accept-Encoding": "identity"})
    with urllib.request.build_opener(metadata.NoRedirect()).open(request, timeout=30) as response:
        if response.geturl() != BASE + name:
            raise ValueError("Source redirect refused")
        data = response.read(limit + 1)
    if not data or len(data) > limit:
        raise ValueError("Invalid source download size")
    return data


def collect(inventory, profile, output, fetch=False, downloader=download):
    if fetch and (output is None or Path(output).exists()):
        raise ValueError("Select a fresh output before fetching")
    document, inventory_hash = metadata.policy.load_inventory(inventory)
    review = metadata.policy.review(document, profile, inventory_hash)
    found = [p for p in document["artifacts"] if p.get("type") == "deb" and p.get("name") == "ffmpeg"]
    if not found or any(p.get("version") != VERSION for p in found):
        raise ValueError("Unexpected image FFmpeg package version")
    report = {"schema_version": 1, "runtime_image": review["runtime_image"], "cpu_profile": profile,
              "source_commit": metadata.policy.SOURCE, "syft_json_sha256": inventory_hash,
              "candidate_source_version": VERSION, "descriptor_url": BASE + DESCRIPTOR,
              "descriptor_signature_verified": False, "upstream_signature_verified": False,
              "corresponding_source_verified": False, "license_review_approved": False,
              "stable_promotion_allowed": False,
              "scope": "HTTPS source availability and descriptor-listed checksum evidence only. No source extraction/build, publisher-key verification, binary build-configuration match, complete dependency source offer or legal approval."}
    contents = {}
    if fetch:
        descriptor = downloader(DESCRIPTOR, MAX_DESCRIPTOR)
        files = descriptor_files(descriptor)
        contents[DESCRIPTOR] = descriptor
        for item in files:
            data = downloader(item["name"], item["size"])
            if len(data) != item["size"] or hashlib.sha256(data).hexdigest() != item["sha256"]:
                raise ValueError("Source archive size/hash mismatch")
            contents[item["name"]] = data
        report.update(files=files, descriptor_sha256=hashlib.sha256(descriptor).hexdigest(),
                      descriptor_listed_checksums_verified=True)
        with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_STORED) as archive:
            for name, data in sorted(contents.items()):
                archive.writestr(name, data)
            archive.writestr("review.json", json.dumps(report, indent=2, sort_keys=True))
    return {"fetched": fetch, "runtime_image": review["runtime_image"], "source_file_count": len(contents),
            "corresponding_source_verified": False, "license_review_approved": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True)
    parser.add_argument("--cpu-profile", required=True, choices=sorted(metadata.policy.DIGESTS))
    parser.add_argument("--output")
    parser.add_argument("--fetch", action="store_true")
    args = parser.parse_args()
    try:
        print(json.dumps(collect(args.inventory, args.cpu_profile, args.output, args.fetch), sort_keys=True))
    except (OSError, ValueError, TypeError, AttributeError, KeyError):
        parser.exit(1, "FFmpeg source evidence failed; no distribution approval recorded.\n")


if __name__ == "__main__":
    main()
