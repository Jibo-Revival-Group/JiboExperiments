"""Verify downloaded SPDX bytes and exact evidence signer; no installation or license approval."""
import argparse
import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile

spec = importlib.util.spec_from_file_location("sbom_policy", Path(__file__).with_name("review-runtime-sbom.py"))
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)
REPOSITORY = "transcendentsoftware-jd/JiboExperiments"
WORKFLOW = "github.com/" + REPOSITORY + "/.github/workflows/openjibo-runtime-sbom-evidence.yml"
PUBLISHER_WORKFLOW = "github.com/" + REPOSITORY + "/.github/workflows/openjibo-runtime-preview-publish.yml"
PREDICATE = "https://spdx.dev/Document/v2.3"


def bounded(path, limit):
    with Path(path).open("rb") as stream:
        data = stream.read(limit + 1)
    if not data or len(data) > limit:
        raise ValueError("Empty or oversized evidence")
    return data


def verify(spdx, attestation, profile, evidence_commit, release_version=policy.LEGACY_RELEASE):
    if not re.fullmatch(r"[0-9a-f]{40}", evidence_commit):
        raise ValueError("An independently trusted full evidence-tooling commit is required")
    image_source, digest = policy.release_policy(release_version, profile)
    workflow = WORKFLOW
    if release_version != policy.LEGACY_RELEASE:
        if evidence_commit != image_source:
            raise ValueError("Publisher evidence commit must match the pinned build source")
        workflow = PUBLISHER_WORKFLOW
    data = bounded(spdx, 16 * 1024 * 1024)
    proof = bounded(attestation, 32 * 1024 * 1024)
    document = json.loads(data)
    if document.get("spdxVersion") != "SPDX-2.3" or document.get("SPDXID") != "SPDXRef-DOCUMENT":
        raise ValueError("Expected SPDX 2.3 inventory")
    bundle = json.loads(proof)
    statement = json.loads(base64.b64decode(bundle["dsseEnvelope"]["payload"], validate=True))
    expected_subject = [{"name": policy.IMAGE, "digest": {"sha256": digest[7:]}}]
    if (statement.get("_type") != "https://in-toto.io/Statement/v1"
            or statement.get("predicateType") != PREDICATE
            or statement.get("subject") != expected_subject
            or statement.get("predicate") != document):
        raise ValueError("Downloaded SPDX bytes do not match the image's signed statement")
    reference = policy.IMAGE + "@" + digest
    with tempfile.TemporaryDirectory() as temp:
        snapshot = Path(temp) / "attestation.json"
        snapshot.write_bytes(proof)
        command = ["gh", "attestation", "verify", "oci://" + reference,
                   "--repo", REPOSITORY, "--signer-workflow", workflow,
                   "--source-digest", evidence_commit, "--source-ref", "refs/heads/main",
                   "--deny-self-hosted-runners", "--cert-oidc-issuer", "https://token.actions.githubusercontent.com",
                   "--predicate-type", PREDICATE, "--bundle", str(snapshot), "--format", "json"]
        result = subprocess.run(command, capture_output=True, text=True, check=False, timeout=60,
                                env=dict(os.environ, GH_HOST="github.com"))
        if result.returncode or len(result.stdout) > 32 * 1024 * 1024:
            raise ValueError("Cryptographic inventory verification failed")
        results = json.loads(result.stdout)
        if (not isinstance(results, list) or not results
                or not all(isinstance(item, dict) and item.get("verificationResult") for item in results)):
            raise ValueError("Missing successful cryptographic verification")
    return {"sbom_verified": True, "cpu_profile": profile, "runtime_image": reference,
            "evidence_commit": evidence_commit, "image_source_commit": image_source,
            "release_version": release_version, "trusted_workflow": workflow,
            "spdx_sha256": hashlib.sha256(data).hexdigest(),
            "license_review_approved": False, "stable_promotion_allowed": False,
            "scope": "Signed SPDX inventory and evidence signer only; not complete coverage, license approval, vulnerability clearance or host certification."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--spdx", required=True)
    parser.add_argument("--attestation", required=True)
    parser.add_argument("--cpu-profile", choices=sorted(policy.DIGESTS), required=True)
    parser.add_argument("--evidence-commit", required=True)
    parser.add_argument("--release-version", choices=policy.RELEASES, default=policy.LEGACY_RELEASE)
    args = parser.parse_args()
    try:
        print(json.dumps(verify(args.spdx, args.attestation, args.cpu_profile, args.evidence_commit,
                               args.release_version), sort_keys=True))
    except (OSError, ValueError, TypeError, KeyError, AttributeError, subprocess.TimeoutExpired):
        parser.exit(1, "Signed SBOM verification failed. Check the exact evidence commit, downloaded files, signer and GitHub CLI. No unsigned fallback is allowed.\n")


if __name__ == "__main__":
    main()
