import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("nuget", ROOT / "OpenJibo/scripts/cloud/collect-runtime-nuget-metadata.py")
nuget = importlib.util.module_from_spec(spec)
spec.loader.exec_module(nuget)


def artifact(name="Test.Package", version="1.2.3"):
    return {"name": name, "version": version, "type": "dotnet",
            "metadata": {"type": "package"}, "purl": f"pkg:nuget/{name}@{version}", "licenses": []}


def inventory():
    return {"source": {"type": "image", "metadata": {
        "manifestDigest": nuget.policy.DIGESTS["portable"], "os": "linux", "architecture": "amd64",
        "labels": {"org.opencontainers.image.revision": nuget.policy.SOURCE,
                   "org.openjibo.cpu-profile": "portable",
                   "org.opencontainers.image.source": "https://github.com/transcendentsoftware-jd/JiboExperiments"}}},
        "descriptor": {"name": "syft", "version": "1.54.1"}, "artifacts": [artifact()]}


MANIFEST = b'''<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
<metadata><id>Test.Package</id><version>1.2.3</version><license type="file">../LICENSE</license>
<licenseUrl>https://example.invalid/license</licenseUrl></metadata></package>'''


class NugetMetadataTests(unittest.TestCase):
    def test_deduplicates_exact_versions_only_and_excludes_projects(self):
        doc = {"artifacts": [artifact(), artifact(), artifact(version="2.0.0"),
                             dict(artifact(), metadata={"type": "project"})]}
        selected, excluded = nuget.packages(doc)
        self.assertEqual(len(selected), 2)
        self.assertEqual(selected[0]["inventory_record_count"], 2)
        self.assertEqual(len(excluded), 1)

    def test_refuses_unsafe_or_inconsistent_identity(self):
        for item in (artifact("../pkg"), artifact(version="1/2"),
                     dict(artifact(), purl="pkg:nuget/Wrong@1.2.3")):
            with self.assertRaises(ValueError):
                nuget.packages({"artifacts": [item]})

    def test_retains_conflicting_scanner_hashes_without_verifying_them(self):
        first, second = artifact(), artifact()
        first["metadata"]["sha512"] = "sha512-first"
        second["metadata"]["sha512"] = "sha512-second"
        selected, _ = nuget.packages({"artifacts": [first, second]})
        self.assertEqual(selected[0]["scanner_sha512_values"], ["sha512-first", "sha512-second"])

    def test_total_limit_fails_before_creating_bundle(self):
        with tempfile.TemporaryDirectory() as temp:
            path, output = Path(temp) / "inventory.json", Path(temp) / "out.zip"
            path.write_text(json.dumps(inventory()))
            old = nuget.MAX_TOTAL
            try:
                nuget.MAX_TOTAL = 1
                with self.assertRaises(ValueError):
                    nuget.collect(path, "portable", output, True, lambda url: MANIFEST)
                self.assertFalse(output.exists())
            finally:
                nuget.MAX_TOTAL = old

    def test_url_is_fixed_and_does_not_use_package_metadata(self):
        item = artifact()
        item["metadata"]["url"] = "http://localhost/private"
        selected, _ = nuget.packages({"artifacts": [item]})
        self.assertEqual(nuget.manifest_url(selected[0]),
                         "https://api.nuget.org/v3-flatcontainer/test.package/1.2.3/test.package.nuspec")

    def test_keeps_license_file_and_url_unfollowed(self):
        fields = nuget.inspect_manifest(MANIFEST, artifact())
        self.assertEqual(fields["license"]["text"], "../LICENSE")
        self.assertEqual(fields["license"]["attributes"]["type"], "file")

    def test_refuses_mismatch_xml_entities_duplicate_fields_and_oversize(self):
        for data in (MANIFEST.replace(b"Test.Package", b"Wrong"),
                     b'<!DOCTYPE package [<!ENTITY x "test">]>' + MANIFEST,
                     MANIFEST.replace(b"</metadata>", b"<id>Test.Package</id></metadata>"),
                     b"x" * (nuget.MAX_MANIFEST + 1)):
            with self.assertRaises(ValueError):
                nuget.inspect_manifest(data, artifact())

    def test_plan_never_fetches_or_creates_output_and_wrong_image_fails(self):
        with tempfile.TemporaryDirectory() as temp:
            path, output = Path(temp) / "inventory.json", Path(temp) / "out.zip"
            path.write_text(json.dumps(inventory()))
            def forbidden(url):
                self.fail("Plan called network")
            report = nuget.collect(path, "portable", output, downloader=forbidden)
            self.assertFalse(report["fetched"])
            self.assertFalse(output.exists())
            with self.assertRaises(ValueError):
                nuget.collect(path, "avx2", output, True, forbidden)

    def test_bundle_preserves_bytes_and_identity_without_claiming_package_verification(self):
        with tempfile.TemporaryDirectory() as temp:
            path, output = Path(temp) / "inventory.json", Path(temp) / "out.zip"
            path.write_text(json.dumps(inventory()))
            calls = []
            def fetch(url):
                calls.append(url)
                return MANIFEST
            nuget.collect(path, "portable", output, True, fetch)
            self.assertEqual(len(calls), 1)
            with zipfile.ZipFile(output) as archive:
                report = json.loads(archive.read("review.json"))
                entry = report["packages"][0]
                self.assertEqual(archive.read(entry["manifest_path"]), MANIFEST)
                self.assertFalse(report["package_archive_hashes_verified"])
                self.assertFalse(report["license_review_approved"])
            original = output.read_bytes()
            with self.assertRaises(ValueError):
                nuget.collect(path, "portable", output, True, fetch)
            self.assertEqual(len(calls), 1)
            self.assertEqual(output.read_bytes(), original)

    def test_redirects_are_rejected(self):
        with self.assertRaises(ValueError):
            nuget.NoRedirect().redirect_request(None, None, 302, None, None, "https://evil.invalid")

    def test_workflow_collects_manifests_without_installing_packages(self):
        workflow = (ROOT / ".github/workflows/openjibo-runtime-notices-evidence.yml").read_text()
        self.assertIn("collect-runtime-nuget-metadata.py", workflow)
        self.assertNotIn("dotnet restore", workflow)


if __name__ == "__main__":
    unittest.main()
