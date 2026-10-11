import importlib.util
import hashlib
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("notices", ROOT / "OpenJibo/scripts/cloud/package-runtime-notices.py")
notices = importlib.util.module_from_spec(spec)
spec.loader.exec_module(notices)


def archive(entries):
    result = io.BytesIO()
    with tarfile.open(fileobj=result, mode="w") as tar:
        for name, data, kind in entries:
            entry = tarfile.TarInfo(name)
            if kind == "link":
                entry.type = tarfile.SYMTYPE
                entry.linkname = "../other"
                tar.addfile(entry)
            else:
                entry.size = len(data)
                tar.addfile(entry, io.BytesIO(data))
    return result.getvalue()


class RuntimeNoticesTests(unittest.TestCase):
    def test_checklist_preserves_versions_and_source_evidence(self):
        first = {"name": "libtest", "version": "1", "type": "deb", "licenses": []}
        second = dict(first, version="2", metadata={"source": "test-src", "sourceVersion": "2-1"})
        result = notices.dependency_checklist({"artifacts": [first, first, second]},
                                              {"notices/doc/libtest/copyright": b"text"})
        self.assertEqual(result["inventory_record_count"], 3)
        self.assertEqual(result["review_entry_count"], 2)
        entries = {entry["identity"]["version"]: entry for entry in result["entries"]}
        self.assertEqual(entries["1"]["inventory_record_count"], 2)
        self.assertEqual(entries["1"]["source_candidate"]["name"]["basis"], "inferred_binary_fallback")
        self.assertEqual(entries["2"]["source_candidate"]["version"]["value"], "2-1")
        self.assertFalse(entries["2"]["source_verified"])
        self.assertEqual(entries["1"]["copied_notice"], "notices/doc/libtest/copyright")

    def test_checklist_does_not_hide_differing_license_evidence_or_guess_nuget(self):
        base = {"name": "Jibo.Cloud", "version": "1", "type": "dotnet", "licenses": []}
        other = dict(base, licenses=[{"value": "MIT"}])
        result = notices.dependency_checklist({"artifacts": [base, other]}, {})
        self.assertEqual(result["review_entry_count"], 2)
        self.assertTrue(all(e["component_kind"] == "dotnet_record_unclassified" for e in result["entries"]))
        hostile = {"name": "../test", "version": "1", "type": "deb"}
        entry = notices.dependency_checklist({"artifacts": [hostile]}, {})["entries"][0]
        self.assertIsNone(entry["copied_notice"])
        with self.assertRaises(ValueError):
            notices.dependency_checklist({"artifacts": [dict(base, metadata=[])]}, {})

    def test_selects_copyright_without_extracting_or_following_links(self):
        content = archive([("doc/libtest/copyright", b"License text", "file"),
                           ("doc/other", b"", "link"), ("doc/libtest/README", b"ignored", "file")])
        files, links = notices.collect(content)
        self.assertEqual(files, {"notices/doc/libtest/copyright": b"License text"})
        self.assertEqual(links[0]["path"], "doc/other")

    def test_whisper_requires_actual_license_bytes(self):
        files, links = notices.collect(archive([("LICENSE", b"MIT notice", "file")]), whisper=True)
        self.assertIn("notices/whisper/LICENSE", files)
        with self.assertRaises(ValueError):
            notices.collect(archive([("LICENSE", b"", "link")]), whisper=True)

    def test_collects_referenced_common_license_texts(self):
        files, links = notices.collect(archive([
            ("common-licenses/GPL-2", b"Actual common license", "file"),
            ("common-licenses/GPL", b"", "link")]), common=True)
        self.assertEqual(files["notices/common-licenses/GPL-2"], b"Actual common license")
        self.assertEqual(len(links), 1)

    def test_rejects_traversal_absolute_and_duplicate_members(self):
        for path in ("../copyright", "/doc/pkg/copyright", "doc/../pkg/copyright", "doc\\pkg\\copyright"):
            with self.assertRaises(ValueError):
                notices.collect(archive([(path, b"notice", "file")]))
        with self.assertRaises(ValueError):
            notices.collect(archive([("doc/pkg/copyright", b"notice", "file")] * 2))

    def test_rejects_empty_or_oversized_license(self):
        with self.assertRaises(ValueError):
            notices.collect(archive([("doc/pkg/copyright", b"", "file")]))
        old = notices.MAX_NOTICE
        try:
            notices.MAX_NOTICE = 2
            with self.assertRaises(ValueError):
                notices.collect(archive([("doc/pkg/copyright", b"large", "file")]))
        finally:
            notices.MAX_NOTICE = old

    def test_workflow_never_starts_or_publishes_images(self):
        workflow = (ROOT / ".github/workflows/openjibo-runtime-notices-evidence.yml").read_text()
        for forbidden in ("docker run", "docker start", "docker build", "docker push", "contents: write", "az "):
            self.assertNotIn(forbidden, workflow)
        self.assertIn("docker create --network none --entrypoint /bin/false", workflow)

    def test_package_preserves_bytes_identity_and_refuses_overwrite(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            docs, whisper, common, inventory, output = [root / name for name in
                ("docs.tar", "whisper.tar", "common.tar", "inventory.json", "review.zip")]
            docs.write_bytes(archive([("doc/pkg/copyright", b"Copyright bytes", "file")]))
            whisper.write_bytes(archive([("LICENSE", b"Whisper bytes", "file")]))
            common.write_bytes(archive([("common-licenses/GPL-2", b"Shared bytes", "file")]))
            inventory.write_text(json.dumps({
                "source": {"type": "image", "metadata": {
                    "manifestDigest": notices.policy.DIGESTS["portable"], "os": "linux", "architecture": "amd64",
                    "labels": {"org.opencontainers.image.revision": notices.policy.SOURCE,
                               "org.openjibo.cpu-profile": "portable",
                               "org.opencontainers.image.source": "https://github.com/transcendentsoftware-jd/JiboExperiments"}}},
                "descriptor": {"name": "syft", "version": "1.54.1"},
                "artifacts": [{"name": "pkg", "type": "deb", "licenses": []}]}))
            report = notices.package(docs, whisper, common, inventory, "portable", output)
            self.assertEqual(report["notice_count"], 3)
            self.assertFalse(report["license_review_approved"])
            with zipfile.ZipFile(output) as bundle:
                self.assertEqual(bundle.read("notices/doc/pkg/copyright"), b"Copyright bytes")
                manifest = json.loads(bundle.read("manifest.json"))
                self.assertFalse(manifest["stable_promotion_allowed"])
                self.assertEqual(manifest["source_commit"], notices.policy.SOURCE)
                checklist_bytes = bundle.read("dependency-review.json")
                self.assertEqual(manifest["dependency_review_sha256"], hashlib.sha256(checklist_bytes).hexdigest())
                self.assertEqual(json.loads(checklist_bytes)["inventory_record_count"], 1)
            original = output.read_bytes()
            with self.assertRaises(FileExistsError):
                notices.package(docs, whisper, common, inventory, "portable", output)
            self.assertEqual(output.read_bytes(), original)
            with self.assertRaises(ValueError):
                notices.package(docs, whisper, common, inventory, "avx2", root / "wrong.zip")
            self.assertFalse((root / "wrong.zip").exists())


if __name__ == "__main__":
    unittest.main()
