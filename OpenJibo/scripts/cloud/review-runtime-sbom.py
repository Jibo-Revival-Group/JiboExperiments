"""Bound a Syft inventory to a reviewed image; report gaps, never approve licenses."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

IMAGE = "ghcr.io/transcendent-software-llc/openjibo-runtime"
SOURCE = "fd32678bb29ade975c71a9bcd7dc7a89babaf541"
DIGESTS = {
    "portable": "sha256:6bb69fd68d2c4863feaf17fd90319125ed1534f8cf728a4070ce04ee116c6010",
    "avx2": "sha256:5311f91890754171b9f87bac288e9179b4ba6512a527c3c66569d2525e228fa6",
}
MAX_BYTES = 64 * 1024 * 1024


def load_inventory(path):
    with Path(path).open("rb") as stream:
        raw = stream.read(MAX_BYTES + 1)
    if len(raw) > MAX_BYTES:
        raise ValueError("Inventory exceeds the evidence size limit")
    return json.loads(raw), hashlib.sha256(raw).hexdigest()


def review(document, profile, sha256):
    digest = DIGESTS[profile]
    source = document.get("source", {})
    metadata = source.get("metadata", {})
    labels = metadata.get("labels", {})
    descriptor = document.get("descriptor", {})
    if (source.get("type") != "image" or metadata.get("manifestDigest") != digest
            or metadata.get("os") != "linux" or metadata.get("architecture") != "amd64"
            or labels.get("org.opencontainers.image.revision") != SOURCE
            or labels.get("org.openjibo.cpu-profile") != profile
            or labels.get("org.opencontainers.image.source") !=
                "https://github.com/transcendentsoftware-jd/JiboExperiments"
            or descriptor.get("name") != "syft" or descriptor.get("version") != "1.54.1"):
        raise ValueError("Inventory image, platform, source, profile or scanner does not match reviewed policy")
    packages = document.get("artifacts")
    if not isinstance(packages, list) or not packages or len(packages) > 100000:
        raise ValueError("Expected a bounded, nonempty package inventory")
    types = Counter()
    unknown = []
    for package in packages:
        if not isinstance(package, dict) or not isinstance(package.get("name"), str):
            raise ValueError("Malformed package inventory")
        types[str(package.get("type", "unknown"))] += 1
        licenses = package.get("licenses", [])
        if not isinstance(licenses, list):
            raise ValueError("Malformed license inventory")
        expressions = [entry.get("spdxExpression", "") for entry in licenses if isinstance(entry, dict)]
        if not expressions or any(not expression or expression in ("NOASSERTION", "NONE")
                                  for expression in expressions):
            unknown.append({"name": package["name"], "version": package.get("version"),
                            "type": package.get("type")})
    return {
        "schema_version": 1, "runtime_image": IMAGE + "@" + digest,
        "source_commit": SOURCE, "cpu_profile": profile, "syft_json_sha256": sha256,
        "scanner": descriptor.get("name"), "scanner_version": descriptor.get("version"),
        "package_count": len(packages), "package_types": dict(sorted(types.items())),
        "packages_without_complete_spdx_expression": unknown,
        "license_review_approved": False, "stable_promotion_allowed": False,
        "supplemental_review_required": [
            "Whisper native source, linked libraries and bundled build tree",
            "base.en model provenance, license and redistribution terms",
            "FFmpeg build configuration and corresponding obligations",
            "base OS, .NET runtime, NuGet components and required notices",
            "repository MIT notice and third-party notice completeness",
        ],
        "scope": "Automated inventory only; not a vulnerability scan, complete dependency inventory, legal approval or host certification.",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True)
    parser.add_argument("--cpu-profile", choices=sorted(DIGESTS), required=True)
    args = parser.parse_args()
    try:
        document, digest = load_inventory(args.inventory)
        print(json.dumps(review(document, args.cpu_profile, digest), sort_keys=True))
    except (ValueError, OSError, TypeError, AttributeError, KeyError):
        parser.exit(1, "SBOM review failed: inventory does not satisfy the bounded reviewed-image contract.\n")


if __name__ == "__main__":
    main()
