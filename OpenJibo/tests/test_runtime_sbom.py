import importlib.util
import base64
import json
from pathlib import Path
import tempfile
import subprocess
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("sbom", ROOT / "OpenJibo/scripts/cloud/review-runtime-sbom.py")
sbom = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sbom)
spec = importlib.util.spec_from_file_location("sbom_verifier", ROOT / "OpenJibo/scripts/cloud/verify-runtime-sbom.py")
verifier = importlib.util.module_from_spec(spec)
spec.loader.exec_module(verifier)


class RuntimeSbomTests(unittest.TestCase):
    def fixture(self, profile="portable"):
        return {"source": {"type": "image", "metadata": {
            "manifestDigest": sbom.DIGESTS[profile], "os": "linux", "architecture": "amd64",
            "labels": {"org.opencontainers.image.revision": sbom.SOURCE,
                       "org.openjibo.cpu-profile": profile,
                       "org.opencontainers.image.source": "https://github.com/transcendentsoftware-jd/JiboExperiments"}}},
            "descriptor": {"name": "syft", "version": "1.54.1"},
            "artifacts": [{"name": "known", "type": "deb", "licenses": [{"spdxExpression": "MIT"}]},
                          {"name": "unknown", "type": "dotnet", "licenses": []}]}

    def test_reports_gaps_without_approving_licenses(self):
        for profile in sbom.DIGESTS:
            result = sbom.review(self.fixture(profile), profile, "a" * 64)
            self.assertEqual(result["package_count"], 2)
            self.assertEqual(result["packages_without_complete_spdx_expression"][0]["name"], "unknown")
            self.assertFalse(result["license_review_approved"])
            self.assertFalse(result["stable_promotion_allowed"])

    def test_audio_policy_is_explicit_and_cannot_replace_legacy(self):
        for profile in sbom.AUDIO_DIGESTS:
            document = self.fixture(profile)
            metadata = document['source']['metadata']
            metadata['manifestDigest'] = sbom.AUDIO_DIGESTS[profile]
            metadata['labels']['org.opencontainers.image.revision'] = sbom.AUDIO_SOURCE
            metadata['labels']['org.openjibo.ffmpeg-profile'] = 'audio'
            result = sbom.review(document, profile, 'a' * 64, sbom.AUDIO_RELEASE)
            self.assertEqual(result['release_version'], sbom.AUDIO_RELEASE)
            self.assertFalse(result['stable_promotion_allowed'])
            with self.assertRaises(ValueError):
                sbom.review(document, profile, 'a' * 64)
            metadata['labels']['org.openjibo.ffmpeg-profile'] = 'distro'
            with self.assertRaises(ValueError):
                sbom.review(document, profile, 'a' * 64, sbom.AUDIO_RELEASE)
            with self.assertRaises(ValueError):
                sbom.review(self.fixture(profile), profile, 'a' * 64, sbom.AUDIO_RELEASE)
        with self.assertRaises(ValueError):
            sbom.review(self.fixture(), 'portable', 'a' * 64, 'latest')

    def test_rejects_wrong_identity_platform_or_scanner(self):
        for key, value in [("manifestDigest", "sha256:" + "a" * 64), ("os", "windows"),
                           ("architecture", "arm64")]:
            document = self.fixture()
            document["source"]["metadata"][key] = value
            with self.assertRaises(ValueError):
                sbom.review(document, "portable", "a" * 64)
        for key in self.fixture()["source"]["metadata"]["labels"]:
            document = self.fixture()
            document["source"]["metadata"]["labels"][key] = "wrong"
            with self.assertRaises(ValueError):
                sbom.review(document, "portable", "a" * 64)
        document = self.fixture()
        document["descriptor"]["version"] = "future"
        with self.assertRaises(ValueError):
            sbom.review(document, "portable", "a" * 64)

    def test_rejects_empty_and_malformed_packages(self):
        for packages in ([], None, [None], [{"name": "bad", "licenses": "MIT"}]):
            document = self.fixture()
            document["artifacts"] = packages
            with self.assertRaises(ValueError):
                sbom.review(document, "portable", "a" * 64)

    def test_bounds_inventory_read_and_hashes_exact_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "inventory.json"
            path.write_text(json.dumps(self.fixture()), encoding="utf-8")
            document, digest = sbom.load_inventory(path)
            self.assertEqual(len(digest), 64)
            self.assertEqual(document, self.fixture())
            old = sbom.MAX_BYTES
            try:
                sbom.MAX_BYTES = 4
                with self.assertRaises(ValueError):
                    sbom.load_inventory(path)
            finally:
                sbom.MAX_BYTES = old

    def test_workflow_cannot_publish_rebuild_or_deploy(self):
        workflow = (ROOT / ".github/workflows/openjibo-runtime-sbom-evidence.yml").read_text()
        for forbidden in ("contents: write", "packages: write", "docker build", "docker run", "az ", "gh release"):
            self.assertNotIn(forbidden, workflow)
        self.assertIn("registry:$IMAGE", workflow)
        self.assertIn("--scope squashed", workflow)
        self.assertIn("syft-version: v1.54.1", workflow)
        self.assertIn("fffec3d851af758d56fee099cf3fbf8aa8779843", workflow)
        self.assertIn("push-to-registry: false", workflow)
        self.assertIn("--predicate-type https://spdx.dev/Document/v2.3", workflow)
        self.assertIn('.spdxVersion == "SPDX-2.3"', workflow)
        self.assertIn('--source-digest "$GITHUB_SHA" --deny-self-hosted-runners', workflow)
        self.assertIn("--bundle evidence/sbom-attestation.json", workflow)

    def test_independent_verifier_checks_exact_payload_and_signer(self):
        with tempfile.TemporaryDirectory() as temp:
            spdx = Path(temp) / "sbom.json"
            proof = Path(temp) / "proof.json"
            document = {"spdxVersion": "SPDX-2.3", "SPDXID": "SPDXRef-DOCUMENT"}
            statement = {"_type": "https://in-toto.io/Statement/v1", "predicateType": verifier.PREDICATE,
                         "predicate": document, "subject": [{"name": sbom.IMAGE,
                             "digest": {"sha256": sbom.DIGESTS["portable"][7:]}}]}
            spdx.write_text(json.dumps(document))
            proof.write_text(json.dumps({"dsseEnvelope": {"payload": base64.b64encode(json.dumps(statement).encode()).decode()}}))

            def run(command, **kwargs):
                self.assertIn(verifier.WORKFLOW, command)
                self.assertIn("--deny-self-hosted-runners", command)
                self.assertIn("--source-ref", command)
                self.assertIn("a" * 40, command)
                self.assertIn(verifier.PREDICATE, command)
                snapshot = Path(command[command.index("--bundle") + 1])
                self.assertNotEqual(snapshot, proof)
                self.assertEqual(snapshot.read_bytes(), proof.read_bytes())
                return subprocess.CompletedProcess(command, 0, '[{"verificationResult":{"verified":true}}]', '')

            with patch.object(verifier.subprocess, "run", side_effect=run):
                self.assertTrue(verifier.verify(spdx, proof, "portable", "a" * 40)["sbom_verified"])
            with patch.object(verifier.subprocess, "run", return_value=subprocess.CompletedProcess([], 1, "", "failed")):
                with self.assertRaises(ValueError):
                    verifier.verify(spdx, proof, "portable", "a" * 40)
            for profile, commit in [("avx2", "a" * 40), ("portable", "short")]:
                with self.assertRaises(ValueError):
                    verifier.verify(spdx, proof, profile, commit)
            document["tampered"] = True
            spdx.write_text(json.dumps(document))
            with patch.object(verifier.subprocess, "run") as run:
                with self.assertRaises(ValueError):
                    verifier.verify(spdx, proof, "portable", "a" * 40)
                run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
