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
LEGACY_RELEASE = "runtime-preview-37540893708"
AUDIO_RELEASE = "runtime-preview-38139481435"
AUDIO_SOURCE = "0b7b56345177156a7da830cfc22c478b5f2b9c80"
AUDIO_DIGESTS = {
    "portable": "sha256:e1b2aa06fc1f69bd498c5394388636331ba646abf2f39ca66ddcb6c0ee2697a9",
    "avx2": "sha256:5e051559692710205532ec27866d4eb5810e9e69685bfa8c6d3676948f7afb7c",
}
GUARDED_RELEASE = "runtime-preview-38165346370"
GUARDED_SOURCE = "b9dd0de89dfdb7bea558c2ae0923eefd50758b52"
GUARDED_DIGESTS = {
    "portable": "sha256:f8f475c3ca78fb89e2a78b18e989e39dfb4e51dbe70c323bf3a4f7895eb98468",
    "avx2": "sha256:e40b84cce0428fd75d2c2c938af587286253bee56caecd098b0d3e127c578881",
}
RELEASES = (LEGACY_RELEASE, AUDIO_RELEASE, GUARDED_RELEASE)
MAX_BYTES = 64 * 1024 * 1024


def release_policy(release_version, profile):
    releases = {LEGACY_RELEASE: (SOURCE, DIGESTS), AUDIO_RELEASE: (AUDIO_SOURCE, AUDIO_DIGESTS),
                GUARDED_RELEASE: (GUARDED_SOURCE, GUARDED_DIGESTS)}
    if release_version not in releases or profile not in DIGESTS:
        raise ValueError("Unreviewed release or CPU policy")
    source, digests = releases[release_version]
    return source, digests[profile]


def load_inventory(path):
    with Path(path).open("rb") as stream:
        raw = stream.read(MAX_BYTES + 1)
    if len(raw) > MAX_BYTES:
        raise ValueError("Inventory exceeds the evidence size limit")
    return json.loads(raw), hashlib.sha256(raw).hexdigest()


def review(document, profile, sha256, release_version=LEGACY_RELEASE):
    expected_source, digest = release_policy(release_version, profile)
    source = document.get("source", {})
    metadata = source.get("metadata", {})
    labels = metadata.get("labels", {})
    descriptor = document.get("descriptor", {})
    if (source.get("type") != "image" or metadata.get("manifestDigest") != digest
            or metadata.get("os") != "linux" or metadata.get("architecture") != "amd64"
            or labels.get("org.opencontainers.image.revision") != expected_source
            or labels.get("org.openjibo.cpu-profile") != profile
            or labels.get("org.opencontainers.image.source") !=
                "https://github.com/transcendentsoftware-jd/JiboExperiments"
            or descriptor.get("name") != "syft" or descriptor.get("version") != "1.54.1"):
        raise ValueError("Inventory image, platform, source, profile or scanner does not match reviewed policy")
    if release_version != LEGACY_RELEASE and labels.get("org.openjibo.ffmpeg-profile") != "audio":
        raise ValueError("Audio preview configuration label does not match policy")
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
        "source_commit": expected_source, "cpu_profile": profile, "syft_json_sha256": sha256,
        "release_version": release_version,
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
    parser.add_argument("--release-version", choices=RELEASES, default=LEGACY_RELEASE)
    args = parser.parse_args()
    try:
        document, digest = load_inventory(args.inventory)
        print(json.dumps(review(document, args.cpu_profile, digest, args.release_version), sort_keys=True))
    except (ValueError, OSError, TypeError, AttributeError, KeyError):
        parser.exit(1, "SBOM review failed: inventory does not satisfy the bounded reviewed-image contract.\n")


if __name__ == "__main__":
    main()
