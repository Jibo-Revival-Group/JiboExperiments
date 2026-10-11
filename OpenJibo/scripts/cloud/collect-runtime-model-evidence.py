"""Hash the shipped speech model as data; never load it or run the runtime image."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import tarfile

spec = importlib.util.spec_from_file_location("policy", Path(__file__).with_name("review-runtime-sbom.py"))
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)
MODEL = "/usr/bin/whisper.cpp/models/ggml-base.en.bin"
EXPECTED_SIZE = 147964211
EXPECTED_SHA256 = "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002"
MAX_TAR = 192 * 1024 * 1024
PUBLISHER_METADATA = "https://huggingface.co/api/models/ggerganov/whisper.cpp/tree/main?recursive=false"


def hash_model(path):
    if not 0 < Path(path).stat().st_size <= MAX_TAR:
        raise ValueError("Invalid model archive size")
    # Uncompressed TAR only, no filesystem extraction, model parsing or execution.
    with Path(path).open("rb") as stream, tarfile.open(fileobj=stream, mode="r:") as archive:
        member = archive.next()
        if (member is None or member.name != Path(MODEL).name or not member.isfile()
                or member.sparse is not None or member.size != EXPECTED_SIZE):
            raise ValueError("Expected one exact-size regular model file")
        model = archive.extractfile(member)
        digest, total = hashlib.sha256(), 0
        while chunk := model.read(1024 * 1024):
            total += len(chunk)
            if total > EXPECTED_SIZE:
                raise ValueError("Model exceeds expected size")
            digest.update(chunk)
        if total != EXPECTED_SIZE or archive.next() is not None:
            raise ValueError("Incomplete or multiple model members")
    actual = digest.hexdigest()
    if actual != EXPECTED_SHA256:
        raise ValueError("Model differs from reviewed publisher LFS digest")
    return actual


def collect(inventory, profile, model_tar, output, release_version=policy.LEGACY_RELEASE):
    if Path(output).exists():
        raise ValueError("Select a fresh output")
    document, inventory_hash = policy.load_inventory(inventory)
    identity = policy.review(document, profile, inventory_hash, release_version)
    digest = hash_model(model_tar)
    report = {"schema_version": 1, "runtime_image": identity["runtime_image"],
              "cpu_profile": profile, "source_commit": identity["source_commit"],
              "release_version": release_version,
              "syft_json_sha256": inventory_hash, "model_path": MODEL,
              "model_size": EXPECTED_SIZE, "model_sha256": digest,
              "reviewed_publisher_lfs_sha256_matched": True,
              "publisher_metadata_url": PUBLISHER_METADATA,
              "publisher_metadata_observed_date": "2026-10-11",
              "publisher_signature_verified": False, "model_license_review_approved": False,
              "license_review_approved": False, "stable_promotion_allowed": False,
              "scope": "Copied model-file bytes match a reviewed SHA-256/size from the publisher's HTTPS LFS metadata. This collector does not attest how the TAR was obtained or prove the file was never modified in a container. No model loading, runtime execution, cryptographic publisher-signature verification, conversion reproducibility, model accuracy certification or legal approval. The main-branch metadata URL is mutable; expected bytes are pinned in reviewed tooling."}
    with Path(output).open("x", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2, sort_keys=True)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True)
    parser.add_argument("--release-version", choices=policy.RELEASES,
                        default=policy.LEGACY_RELEASE)
    parser.add_argument("--cpu-profile", required=True, choices=sorted(policy.DIGESTS))
    parser.add_argument("--model-tar", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(collect(args.inventory, args.cpu_profile, args.model_tar, args.output,
                                 args.release_version), sort_keys=True))
    except (OSError, ValueError, TypeError, AttributeError, KeyError, tarfile.TarError):
        parser.exit(1, "Model evidence failed; no distribution approval recorded.\n")


if __name__ == "__main__":
    main()
