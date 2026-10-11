import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("model", Path(__file__).resolve().parents[1] / "scripts/cloud/collect-runtime-model-evidence.py")
model = importlib.util.module_from_spec(spec)
spec.loader.exec_module(model)
fixture_spec = importlib.util.spec_from_file_location("fixture", Path(__file__).with_name("test_runtime_nuget_metadata.py"))
fixture = importlib.util.module_from_spec(fixture_spec)
fixture_spec.loader.exec_module(fixture)
DATA = b"test model bytes only"


def archive(data=DATA, name=None, kind=tarfile.REGTYPE, extra=False):
    stream = io.BytesIO()
    with tarfile.open(fileobj=stream, mode="w") as tar:
        member = tarfile.TarInfo(name or Path(model.MODEL).name)
        member.type = kind
        member.size = len(data) if kind == tarfile.REGTYPE else 0
        tar.addfile(member, io.BytesIO(data) if member.size else None)
        if extra:
            tar.addfile(tarfile.TarInfo("extra"))
    return stream.getvalue()


class ModelEvidenceTests(unittest.TestCase):
    def test_candidate_model_requires_explicit_release_before_reading_model(self):
        with tempfile.TemporaryDirectory() as temp, patch.object(model, "EXPECTED_SIZE", len(DATA)), patch.object(
                model, "EXPECTED_SHA256", hashlib.sha256(DATA).hexdigest()):
            root = Path(temp)
            inv, copied, output = [root / name for name in ("inventory.json", "model.tar", "review.json")]
            document = fixture.inventory()
            identity = document["source"]["metadata"]
            identity["manifestDigest"] = model.policy.AUDIO_DIGESTS["portable"]
            identity["labels"]["org.opencontainers.image.revision"] = model.policy.AUDIO_SOURCE
            identity["labels"]["org.openjibo.ffmpeg-profile"] = "audio"
            inv.write_text(json.dumps(document))
            with patch.object(model, "hash_model", side_effect=AssertionError("Must reject identity first")):
                with self.assertRaises(ValueError):
                    model.collect(inv, "portable", copied, output)
            self.assertFalse(output.exists())
            copied.write_bytes(archive())
            report = model.collect(inv, "portable", copied, output, model.policy.AUDIO_RELEASE)
            self.assertEqual(report["source_commit"], model.policy.AUDIO_SOURCE)
            self.assertEqual(report["release_version"], model.policy.AUDIO_RELEASE)
            self.assertFalse(report["model_license_review_approved"])

    def test_identity_digest_and_no_overwrite(self):
        with tempfile.TemporaryDirectory() as temp, patch.object(model, "EXPECTED_SIZE", len(DATA)), patch.object(
                model, "EXPECTED_SHA256", hashlib.sha256(DATA).hexdigest()):
            root = Path(temp)
            inv, copied, output = [root / name for name in ("inventory.json", "model.tar", "review.json")]
            inv.write_text(json.dumps(fixture.inventory()))
            copied.write_bytes(archive())
            report = model.collect(inv, "portable", copied, output)
            self.assertTrue(report["reviewed_publisher_lfs_sha256_matched"])
            self.assertFalse(report["publisher_signature_verified"])
            self.assertFalse(report["stable_promotion_allowed"])
            saved = output.read_bytes()
            with self.assertRaises(ValueError):
                model.collect(inv, "portable", copied, output)
            self.assertEqual(output.read_bytes(), saved)
            with self.assertRaises(ValueError):
                model.collect(inv, "avx2", copied, root / "wrong.json")
            self.assertFalse((root / "wrong.json").exists())

    def test_rejects_links_paths_extra_members_and_tampered_bytes(self):
        with tempfile.TemporaryDirectory() as temp, patch.object(model, "EXPECTED_SIZE", len(DATA)), patch.object(
                model, "EXPECTED_SHA256", hashlib.sha256(DATA).hexdigest()):
            path = Path(temp) / "model.tar"
            for raw in (archive(name="../model"), archive(name="/absolute"),
                        archive(kind=tarfile.SYMTYPE), archive(kind=tarfile.LNKTYPE),
                        archive(extra=True), archive(DATA + b"extra"),
                        archive(DATA.replace(b"test", b"evil")), b"not tar", b""):
                path.write_bytes(raw)
                with self.assertRaises((ValueError, tarfile.TarError)):
                    model.hash_model(path)

    def test_archive_limit_and_failure_do_not_create_output(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            inv, copied, output = [root / name for name in ("inventory.json", "model.tar", "review.json")]
            inv.write_text(json.dumps(fixture.inventory()))
            copied.write_bytes(archive())
            with patch.object(model, "MAX_TAR", 10), self.assertRaises(ValueError):
                model.collect(inv, "portable", copied, output)
            self.assertFalse(output.exists())
            with self.assertRaises(ValueError):
                model.collect(inv, "portable", copied, output)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
