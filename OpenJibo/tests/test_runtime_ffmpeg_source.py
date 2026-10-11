import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("source", ROOT / "OpenJibo/scripts/cloud/collect-ffmpeg-source-evidence.py")
source = importlib.util.module_from_spec(spec)
spec.loader.exec_module(source)
fixture_spec = importlib.util.spec_from_file_location("fixture", Path(__file__).with_name("test_runtime_nuget_metadata.py"))
fixture = importlib.util.module_from_spec(fixture_spec)
fixture_spec.loader.exec_module(fixture)
FILES = {"ffmpeg_6.1.1.orig.tar.xz": b"original", "ffmpeg_6.1.1.orig.tar.xz.asc": b"signature",
         "ffmpeg_6.1.1-3ubuntu5.debian.tar.xz": b"patches"}


def descriptor():
    rows = "\n".join(f" {hashlib.sha256(data).hexdigest()} {len(data)} {name}" for name, data in FILES.items())
    return ("-----BEGIN PGP SIGNED MESSAGE-----\nHash: SHA256\n\nSource: ffmpeg\n"
            f"Version: {source.VERSION}\nChecksums-Sha256:\n{rows}\n"
            "\n-----BEGIN PGP SIGNATURE-----\nnot-verified\n-----END PGP SIGNATURE-----\n").encode()


class FfmpegSourceTests(unittest.TestCase):
    def test_descriptor_preserves_exact_version_and_archive_set(self):
        entries = source.descriptor_files(descriptor())
        self.assertEqual({p["name"] for p in entries}, set(FILES))
        self.assertEqual(sum(p["size"] for p in entries), sum(map(len, FILES.values())))

    def test_refuses_wrong_identity_unsafe_files_duplicates_and_missing_signature(self):
        original = descriptor()
        for data in (original.replace(b"Source: ffmpeg", b"Source: other"),
                     original.replace(source.VERSION.encode(), b"7:6.1.1-other"),
                     original.replace(b"ffmpeg_6.1.1.orig.tar.xz.asc", b"../secret"),
                     original.replace(b"Version:", b"Source: ffmpeg\nVersion:"),
                     original.replace(b"-----END PGP SIGNATURE-----", b"")):
            with self.assertRaises(ValueError):
                source.descriptor_files(data)

    def test_plan_has_no_network_and_collection_verifies_bytes_without_approval(self):
        with tempfile.TemporaryDirectory() as temp:
            inv, output = Path(temp) / "inventory.json", Path(temp) / "source.zip"
            doc = fixture.inventory()
            doc["artifacts"].append({"name": "ffmpeg", "type": "deb", "version": source.VERSION, "licenses": []})
            inv.write_text(json.dumps(doc))
            calls = []
            def download(name, limit):
                calls.append(name)
                return descriptor() if name == source.DESCRIPTOR else FILES[name]
            source.collect(inv, "portable", output, downloader=download)
            self.assertEqual(calls, [])
            self.assertFalse(output.exists())
            source.collect(inv, "portable", output, True, download)
            with zipfile.ZipFile(output) as z:
                report = json.loads(z.read("review.json"))
                self.assertTrue(report["descriptor_listed_checksums_verified"])
                self.assertFalse(report["descriptor_signature_verified"])
                self.assertFalse(report["corresponding_source_verified"])
                for entry in report["files"]:
                    self.assertEqual(z.read(entry["name"]), FILES[entry["name"]])
            original = output.read_bytes()
            with self.assertRaises(ValueError):
                source.collect(inv, "portable", output, True, download)
            self.assertEqual(len(calls), 4)
            self.assertEqual(output.read_bytes(), original)

    def test_hash_mismatch_fails_before_creating_bundle(self):
        with tempfile.TemporaryDirectory() as temp:
            inv, output = Path(temp) / "inventory.json", Path(temp) / "out.zip"
            doc = fixture.inventory()
            doc["artifacts"].append({"name": "ffmpeg", "type": "deb", "version": source.VERSION, "licenses": []})
            inv.write_text(json.dumps(doc))
            with self.assertRaises(ValueError):
                source.collect(inv, "portable", output, True,
                               lambda name, limit: descriptor() if name == source.DESCRIPTOR else b"tampered")
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
