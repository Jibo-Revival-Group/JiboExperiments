"""Read an embedded configuration from a copied, never-executed FFmpeg library."""
import argparse
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import re
import tarfile

spec = importlib.util.spec_from_file_location("policy", Path(__file__).with_name("review-runtime-sbom.py"))
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)
LIBRARY = "/usr/lib/x86_64-linux-gnu/libavutil.so.58.29.100"
VERSION = "7:6.1.1-3ubuntu5"
MAX_TAR = 16 * 1024 * 1024
MAX_LIBRARY = 8 * 1024 * 1024
MAX_CONFIG = 16 * 1024


def expected_digest(document):
    packages = [p for p in document["artifacts"] if p.get("type") == "deb" and p.get("name") == "libavutil58"]
    if len(packages) != 1 or packages[0].get("version") != VERSION:
        raise ValueError("Unexpected libavutil identity")
    files = packages[0].get("metadata", {}).get("files", [])
    matches = [f for f in files if f.get("path") == LIBRARY]
    if len(matches) != 1:
        raise ValueError("Missing or ambiguous inventory file")
    digest = matches[0].get("digest", {})
    value = digest.get("value", "")
    if digest.get("algorithm") != "md5" or not re.fullmatch(r"[0-9a-f]{32}", value):
        raise ValueError("Unexpected inventory checksum")
    return value


def library_bytes(raw):
    if not raw or len(raw) > MAX_TAR:
        raise ValueError("Invalid library tar size")
    with tarfile.open(fileobj=io.BytesIO(raw), mode="r:") as archive:
        member = archive.next()
        if (member is None or member.name != Path(LIBRARY).name or not member.isfile()
                or not 0 < member.size <= MAX_LIBRARY or member.sparse is not None):
            raise ValueError("Expected one regular library, no links or extraction")
        data = archive.extractfile(member).read(MAX_LIBRARY + 1)
        if len(data) != member.size or archive.next() is not None:
            raise ValueError("Incomplete or multiple library members")
    # ELF64, little-endian, current ELF version, shared object, x86-64.
    if (len(data) < 64 or data[:7] != b"\x7fELF\x02\x01\x01"
            or data[16:20] != b"\x03\x00\x3e\x00"):
        raise ValueError("Unexpected library format/platform")
    return data


def configuration(data):
    candidates = []
    # Deliberately read bytes only: no dlopen, shell interpretation, or subprocess.
    for match in re.finditer(rb"(?:\A|\x00)(--prefix=[\x20-\x7e]{1,16384})\x00", data):
        value = match.group(1)
        if len(value) <= MAX_CONFIG and b"--enable-shared" in value.split():
            candidates.append((match.start(1), value.decode("ascii")))
    if len(candidates) != 1:
        raise ValueError("Missing or ambiguous embedded configuration")
    return candidates[0]


def collect(inventory, profile, tar_path, output):
    if Path(output).exists():
        raise ValueError("Select a fresh output")
    document, inventory_hash = policy.load_inventory(inventory)
    identity = policy.review(document, profile, inventory_hash)
    expected = expected_digest(document)
    with Path(tar_path).open("rb") as stream:
        raw = stream.read(MAX_TAR + 1)
    data = library_bytes(raw)
    if hashlib.md5(data, usedforsecurity=False).hexdigest() != expected:
        raise ValueError("Copied library differs from inventory checksum")
    offset, text = configuration(data)
    options = text.split()
    report = {"schema_version": 1, "runtime_image": identity["runtime_image"],
              "cpu_profile": profile, "source_commit": policy.SOURCE,
              "syft_json_sha256": inventory_hash, "library_path": LIBRARY,
              "package_version": VERSION, "library_sha256": hashlib.sha256(data).hexdigest(),
              "input_tar_sha256": hashlib.sha256(raw).hexdigest(),
              "inventory_md5_matched": True, "configuration_offset": offset,
              "embedded_configuration": text,
              "observed_switches": {flag: flag in options for flag in
                                    ("--enable-gpl", "--enable-version3", "--enable-nonfree", "--enable-shared")},
              "corresponding_source_verified": False, "license_review_approved": False,
              "stable_promotion_allowed": False,
              "scope": "One ELF library's embedded ASCII configuration, read without execution. MD5 is only a package-metadata consistency check, not authenticity proof. Not executable output, a complete linked-library inventory, reproducible build, verified corresponding source or legal approval."}
    with Path(output).open("x", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2, sort_keys=True)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True)
    parser.add_argument("--cpu-profile", required=True, choices=sorted(policy.DIGESTS))
    parser.add_argument("--library-tar", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(collect(args.inventory, args.cpu_profile, args.library_tar, args.output), sort_keys=True))
    except (OSError, ValueError, TypeError, AttributeError, KeyError, tarfile.TarError):
        parser.exit(1, "FFmpeg build evidence failed; no distribution approval recorded.\n")


if __name__ == "__main__":
    main()
