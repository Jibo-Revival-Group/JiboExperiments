import importlib.util
import contextlib
import io
import json
from pathlib import Path
import tarfile
import tempfile
import sys
import unittest
from unittest.mock import patch
import zipfile

spec = importlib.util.spec_from_file_location("framework", Path(__file__).resolve().parents[1] / "scripts/cloud/compare-runtime-framework-files.py")
framework = importlib.util.module_from_spec(spec)
spec.loader.exec_module(framework)
fixture_spec = importlib.util.spec_from_file_location("fixture", Path(__file__).with_name("test_runtime_nuget_metadata.py"))
fixture = importlib.util.module_from_spec(fixture_spec)
fixture_spec.loader.exec_module(fixture)


def inventory(candidate=False):
    document = fixture.inventory()
    document["artifacts"] = [fixture.artifact(name, framework.VERSION) for name in framework.FRAMEWORKS]
    if candidate:
        identity = document["source"]["metadata"]
        identity["manifestDigest"] = framework.policy.AUDIO_DIGESTS["portable"]
        identity["labels"]["org.opencontainers.image.revision"] = framework.policy.AUDIO_SOURCE
        identity["labels"]["org.openjibo.ffmpeg-profile"] = "audio"
    return document


def package(name, extra=()):
    stream = io.BytesIO()
    manifest = f"<package><metadata><id>{name}</id><version>{framework.VERSION}</version></metadata></package>"
    with zipfile.ZipFile(stream, "w") as archive:
        archive.writestr("framework.nuspec", manifest)
        archive.writestr("runtimes/linux-x64/lib/net10.0/Test.dll", b"exact bytes")
        for path, content in extra:
            archive.writestr(path, content)
    return stream.getvalue()


def copied_tar(entries=None):
    stream = io.BytesIO()
    if entries is None:
        entries = [(f"shared/{name}/{framework.VERSION}/Test.dll", b"exact bytes", tarfile.REGTYPE)
                   for name in framework.FRAMEWORKS.values()]
    with tarfile.open(fileobj=stream, mode="w") as archive:
        for name, data, kind in entries:
            member = tarfile.TarInfo(name)
            member.type = kind
            member.size = len(data) if kind == tarfile.REGTYPE else 0
            archive.addfile(member, io.BytesIO(data) if member.size else None)
    return stream.getvalue()


class FrameworkFilesTests(unittest.TestCase):
    def test_cli_mismatch_returns_failure_while_preserving_report_result(self):
        report = {"runtime_image": "image", "packages": [{"name": "package", "payload_file_count": 1,
                  "matching_file_count": 0, "all_selected_payload_files_match": False}]}
        args = ["compare", "--inventory", "inventory", "--cpu-profile", "portable",
                "--framework-tar", "framework.tar", "--output", "report.json"]
        with patch.object(sys, "argv", args), patch.object(framework, "compare", return_value=report), \
                contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as error:
                framework.main()
        self.assertEqual(error.exception.code, 2)

    def test_cli_malformed_package_metadata_fails_with_bounded_diagnostic(self):
        args = ["compare", "--inventory", "inventory", "--cpu-profile", "portable",
                "--framework-tar", "framework.tar", "--output", "report.json"]
        diagnostic = io.StringIO()
        with patch.object(sys, "argv", args), patch.object(framework, "compare",
                side_effect=framework.notices.metadata.ET.ParseError("untrusted metadata")), \
                contextlib.redirect_stderr(diagnostic):
            with self.assertRaises(SystemExit) as error:
                framework.main()
        self.assertEqual(error.exception.code, 1)
        self.assertNotIn("untrusted metadata", diagnostic.getvalue())

    def run_comparison(self, document, raw=None, release=None):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            inv, copied, output = [root / name for name in ("inventory.json", "framework.tar", "review.json")]
            inv.write_text(json.dumps(document))
            copied.write_bytes(copied_tar() if raw is None else raw)
            def fetch(url):
                name = next(name for name in framework.FRAMEWORKS if name.lower() in url)
                return package(name)
            return framework.compare(inv, "portable", copied, output,
                                     release or framework.policy.LEGACY_RELEASE, fetch)

    def test_both_frameworks_match_without_claiming_scanner_or_signature_trust(self):
        report = self.run_comparison(inventory())
        self.assertEqual(len(report["packages"]), 2)
        self.assertEqual(len(report["copied_tar_sha256"]), 64)
        for item in report["packages"]:
            self.assertTrue(item["all_selected_payload_files_match"])
            self.assertFalse(item["scanner_content_hash_verified"])
            self.assertFalse(item["package_signature_trust_verified"])
        self.assertFalse(report["stable_promotion_allowed"])

    def test_candidate_is_explicit_and_output_cannot_be_overwritten(self):
        with self.assertRaises(ValueError):
            self.run_comparison(inventory(True))
        report = self.run_comparison(inventory(True), release=framework.policy.AUDIO_RELEASE)
        self.assertEqual(report["source_commit"], framework.policy.AUDIO_SOURCE)
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / "review.json"
            output.write_text("preserve")
            with self.assertRaises(ValueError):
                framework.compare("absent", "portable", "absent", output,
                                  downloader=lambda url: self.fail("Fetched before guard"))
            self.assertEqual(output.read_text(), "preserve")

    def test_mismatch_missing_and_extra_files_are_retained_not_approved(self):
        raw = copied_tar([(f"shared/Microsoft.NETCore.App/{framework.VERSION}/Test.dll", b"changed", tarfile.REGTYPE),
                          (f"shared/Microsoft.NETCore.App/{framework.VERSION}/extra.json", b"extra", tarfile.REGTYPE)])
        report = self.run_comparison(inventory(), raw)
        by_name = {item["name"]: item for item in report["packages"]}
        self.assertEqual(len(by_name["Microsoft.NETCore.App.Runtime.linux-x64"]["mismatched_files"]), 1)
        self.assertEqual(len(by_name["Microsoft.NETCore.App.Runtime.linux-x64"]["uncompared_image_files"]), 1)
        self.assertEqual(len(by_name["Microsoft.AspNetCore.App.Runtime.linux-x64"]["missing_files"]), 1)
        self.assertTrue(all(not item["all_selected_payload_files_match"] for item in report["packages"]))

    def test_unsafe_duplicate_link_and_unexpected_tar_members_are_rejected(self):
        good = f"shared/Microsoft.NETCore.App/{framework.VERSION}/Test.dll"
        for entries in ([('../escape', b'x', tarfile.REGTYPE)], [(good, b'x', tarfile.SYMTYPE)],
                        [(good, b'x', tarfile.REGTYPE)] * 2, [('shared/Other/10.0.12/file', b'x', tarfile.REGTYPE)]):
            with self.subTest(entries=entries), self.assertRaises(ValueError):
                self.run_comparison(inventory(), copied_tar(entries))

    def test_wrong_version_and_missing_framework_fail_before_download(self):
        document = inventory()
        item = document["artifacts"][0]
        item["version"] = "10.0.11"
        item["purl"] = f"pkg:nuget/{item['name']}@10.0.11"
        with self.assertRaises(ValueError):
            self.run_comparison(document)
        document = inventory()
        document["artifacts"].pop()
        with self.assertRaises(ValueError):
            self.run_comparison(document)

    def test_payload_mapping_duplicates_wrong_manifest_and_size_limits_rejected(self):
        name = next(iter(framework.FRAMEWORKS))
        selected, _ = framework.notices.metadata.packages(inventory())
        item = next(item for item in selected if item["name"] == name)
        with self.assertRaises(ValueError):
            framework.package_files(package(name, (("runtimes/linux-x64/native/Test.dll", b"other"),)), item)
        with self.assertRaises(ValueError):
            framework.package_files(package("Wrong"), item)
        with patch.object(framework, "MAX_FILE", 1), self.assertRaises(ValueError):
            framework.package_files(package(name), item)
        with patch.object(framework, "MAX_TAR", 1), self.assertRaises(ValueError):
            self.run_comparison(inventory())

    def test_hostfxr_relocation_is_explicit_and_requires_exact_single_file(self):
        name = "Microsoft.NETCore.App.Runtime.linux-x64"
        selected, _ = framework.notices.metadata.packages(inventory())
        item = next(item for item in selected if item["name"] == name)
        files = framework.package_files(package(name, (("runtimes/linux-x64/native/libhostfxr.so", b"host"),)), item)
        target = f"host/fxr/{framework.VERSION}/libhostfxr.so"
        self.assertIn(target, files)
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "host.tar"
            path.write_bytes(copied_tar([("libhostfxr.so", b"host", tarfile.REGTYPE)]))
            self.assertEqual(framework.hostfxr_files(path), {target: files[target]})
            for entries in ([("libhostfxr.so", b"host", tarfile.SYMTYPE)],
                            [("../libhostfxr.so", b"host", tarfile.REGTYPE)],
                            [("libhostfxr.so", b"host", tarfile.REGTYPE)] * 2):
                path.write_bytes(copied_tar(entries))
                with self.assertRaises(ValueError):
                    framework.hostfxr_files(path)


if __name__ == "__main__":
    unittest.main()
