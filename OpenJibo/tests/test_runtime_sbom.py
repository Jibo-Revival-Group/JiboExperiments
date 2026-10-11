import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("sbom", ROOT / "OpenJibo/scripts/cloud/review-runtime-sbom.py")
sbom = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sbom)


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


if __name__ == "__main__":
    unittest.main()
