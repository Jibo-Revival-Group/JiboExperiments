import base64
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("notices", ROOT / "OpenJibo/scripts/cloud/collect-runtime-nuget-notices.py")
notices = importlib.util.module_from_spec(spec)
spec.loader.exec_module(notices)
fixture_spec = importlib.util.spec_from_file_location("fixture", Path(__file__).with_name("test_runtime_nuget_metadata.py"))
fixture = importlib.util.module_from_spec(fixture_spec)
fixture_spec.loader.exec_module(fixture)


def package(extra=()):
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w") as z:
        z.writestr("Test.Package.nuspec", fixture.MANIFEST)
        for name, data in extra:
            if isinstance(name, str):
                member = zipfile.ZipInfo()
                member.filename = name
                member.orig_filename = name
            else:
                member = name
            z.writestr(member, data)
    return out.getvalue()


def item(hashes=()):
    selected, _ = notices.metadata.packages({"artifacts": [fixture.artifact()]})
    selected[0]["scanner_sha512_values"] = list(hashes)
    return selected[0]


class NugetNoticesTests(unittest.TestCase):
    def test_actual_sdk_verifier_accepts_expected_content_and_refuses_tampering(self):
        verifier = ROOT / "OpenJibo/scripts/cloud/nuget-content-verifier/bin/Debug/net10.0/NugetContentVerifier.dll"
        self.assertTrue(verifier.is_file(), "Build NugetContentVerifier before running this suite")
        data = package((("LICENSE", b"original"),))
        expected = "sha512-" + base64.b64encode(hashlib.sha512(data).digest()).decode()
        result = notices.verify_content(data, item([expected]), verifier)
        self.assertTrue(result["nuget_content_hash_verified"])
        self.assertFalse(result["signed_package_integrity_checked"])
        with self.assertRaises(notices.subprocess.CalledProcessError):
            notices.verify_content(package((("LICENSE", b"modified"),)), item([expected]), verifier)
        forged = package(((".signature.p7s", b"not a signature"),))
        forged_hash = "sha512-" + base64.b64encode(hashlib.sha512(forged).digest()).decode()
        with self.assertRaises(notices.subprocess.CalledProcessError):
            notices.verify_content(forged, item([forged_hash]), verifier)

    def test_content_verifier_requires_matching_explicit_result(self):
        selected = item(["sha512-expected"])
        result = {"nuget_content_hash_verified": True, "content_hash": "sha512-expected",
                  "package_signature_trust_verified": False}
        with patch.object(notices.subprocess, "run", return_value=SimpleNamespace(stdout=json.dumps(result).encode())) as run:
            self.assertTrue(notices.verify_content(b"package", selected, "verifier.dll")["nuget_content_hash_verified"])
            self.assertEqual(run.call_args.kwargs["input"], b"package")
            self.assertEqual(run.call_args.kwargs["timeout"], 60)
        result["content_hash"] = "sha512-wrong"
        with patch.object(notices.subprocess, "run", return_value=SimpleNamespace(stdout=json.dumps(result).encode())):
            with self.assertRaises(ValueError):
                notices.verify_content(b"package", selected, "verifier.dll")
        with self.assertRaises(ValueError):
            notices.verify_content(b"package", item([]), "verifier.dll")

    def test_selects_raw_notice_bytes_without_copying_binaries(self):
        data = package((("LICENSE.txt", b"License bytes"), ("lib/test.dll", b"not copied"),
                        ("docs/THIRD-PARTY-NOTICES.TXT", b"Third party")))
        files, report = notices.inspect_package(data, item())
        self.assertEqual(files["LICENSE.txt"], b"License bytes")
        self.assertNotIn("lib/test.dll", files)
        self.assertEqual(report["notice_member_count"], 2)
        self.assertFalse(report["nuget_content_hash_verified"])

    def test_raw_archive_match_is_not_content_hash_or_signature_verification(self):
        data = package()
        expected = "sha512-" + base64.b64encode(hashlib.sha512(data).digest()).decode()
        _, report = notices.inspect_package(data, item([expected]))
        self.assertTrue(report["raw_archive_sha512_matches_scanner"])
        self.assertFalse(report["package_signature_verified"])
        _, report = notices.inspect_package(data, item([expected, "other"]))
        self.assertFalse(report["raw_archive_sha512_matches_scanner"])

    def test_refuses_unsafe_duplicate_symlink_and_oversized_entries(self):
        for path in ("../LICENSE", "/LICENSE", "a/../LICENSE", "a\\LICENSE", "C:/LICENSE"):
            with self.assertRaises(ValueError):
                notices.inspect_package(package(((path, b"x"),)), item())
        with self.assertRaises(ValueError):
            notices.inspect_package(package((("LICENSE", b"one"), ("license", b"two"))), item())
        link = zipfile.ZipInfo("LICENSE")
        link.create_system = 3
        link.external_attr = 0o120777 << 16
        with self.assertRaises(ValueError):
            notices.inspect_package(package(((link, b"target"),)), item())
        with self.assertRaises(ValueError):
            notices.inspect_package(package((("LICENSE", b"x" * (notices.MAX_NOTICE + 1)),)), item())

    def test_refuses_missing_or_wrong_manifest(self):
        out = io.BytesIO()
        with zipfile.ZipFile(out, "w") as z:
            z.writestr("LICENSE", b"x")
        with self.assertRaises(ValueError):
            notices.inspect_package(out.getvalue(), item())
        wrong = item()
        wrong["version"] = "2.0"
        with self.assertRaises(ValueError):
            notices.inspect_package(package(), wrong)

    def test_plan_never_fetches_and_bundle_refuses_overwrite(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            inventory, output = root / "inventory.json", root / "notices.zip"
            inventory.write_text(json.dumps(fixture.inventory()))
            calls = []
            def fetch(url):
                calls.append(url)
                return package((("LICENSE", b"exact"),))
            notices.collect(inventory, "portable", output, downloader=fetch)
            self.assertEqual(calls, [])
            self.assertFalse(output.exists())
            notices.collect(inventory, "portable", output, True, fetch)
            original = output.read_bytes()
            with zipfile.ZipFile(output) as z:
                report = json.loads(z.read("review.json"))
                self.assertFalse(report["stable_promotion_allowed"])
                for path, expected in report["packages"][0]["files"].items():
                    self.assertEqual(hashlib.sha256(z.read(path)).hexdigest(), expected)
            with self.assertRaises(ValueError):
                notices.collect(inventory, "portable", output, True, fetch)
            self.assertEqual(len(calls), 1)
            self.assertEqual(output.read_bytes(), original)

    def test_download_budget_fails_before_writing(self):
        with tempfile.TemporaryDirectory() as temp:
            inventory, output = Path(temp) / "inv.json", Path(temp) / "out.zip"
            inventory.write_text(json.dumps(fixture.inventory()))
            old = notices.MAX_DOWNLOAD
            try:
                notices.MAX_DOWNLOAD = 1
                with self.assertRaises(ValueError):
                    notices.collect(inventory, "portable", output, True, lambda url: package())
                self.assertFalse(output.exists())
            finally:
                notices.MAX_DOWNLOAD = old


if __name__ == "__main__":
    unittest.main()
