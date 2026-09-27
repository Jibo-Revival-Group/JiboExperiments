import hashlib
import importlib.util
import io
import os
import sys
import tarfile
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


SCRIPT = Path(__file__).with_name("inspect-robot-ota-package.py")
SPEC = importlib.util.spec_from_file_location("robot_ota_package_inspector", SCRIPT)
inspector = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
sys.modules[SPEC.name] = inspector
SPEC.loader.exec_module(inspector)


def make_tar(path: Path, members: list[tuple[tarfile.TarInfo, bytes]]) -> None:
    with path.open("wb") as destination:
        with tarfile.open(fileobj=destination, mode="w:") as archive:
            root = tarfile.TarInfo("./")
            root.type = tarfile.DIRTYPE
            archive.addfile(root)
            for info, content in members:
                info.size = len(content)
                archive.addfile(info, io.BytesIO(content))


class OuterOtaPackageInspectorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def inspect_members(self, members: list[tuple[tarfile.TarInfo, bytes]], **kwargs):
        path = self.root / "package.tar"
        make_tar(path, members)
        return inspector.inspect_package(str(path), **kwargs)

    def test_accepts_filesystem_envelope_and_reports_hooks_without_running_them(self) -> None:
        filesystem = b"synthetic nested filesystem archive bytes"
        hook = b"synthetic hook body"
        hook_info = tarfile.TarInfo("./preinstall")
        hook_info.mode = 0o755
        result = self.inspect_members([
            (tarfile.TarInfo("./filesystem.tar.bz2"), filesystem),
            (hook_info, hook),
        ])

        self.assertTrue(result["OuterEnvelopeAccepted"])
        self.assertFalse(result["CanOfferUpdates"])
        self.assertFalse(result["FilesystemExtractionPerformed"])
        self.assertFalse(result["HooksExecuted"])
        self.assertEqual(result["HooksRequiringManualReview"], ["preinstall"])
        self.assertEqual(result["Members"]["filesystem.tar.bz2"]["memberSha256"],
                         hashlib.sha256(filesystem).hexdigest())
        self.assertEqual(result["ArchiveDigests"]["sha256"],
                         hashlib.sha256((self.root / "package.tar").read_bytes()).hexdigest())
        self.assertIn("executable-hooks-require-manual-review", result["Blockers"])
        self.assertIn("nested-filesystem-archive-not-inspected", result["Blockers"])
        self.assertNotIn("memberSha1CompatibilityOnly", result["Members"]["preinstall"])

    def test_optional_sha1_is_labeled_as_compatibility_only(self) -> None:
        content = b"synthetic archive content"
        result = self.inspect_members(
            [(tarfile.TarInfo("filesystem.tar.bz2"), content)], include_sha1_compat=True)

        self.assertEqual(result["Members"]["filesystem.tar.bz2"]["memberSha1CompatibilityOnly"],
                         hashlib.sha1(content).hexdigest())
        self.assertIn("sha1CompatibilityOnly", result["ArchiveDigests"])
        self.assertIn("not publisher identity", result["Sha1Purpose"])

    def test_rejects_absolute_traversal_backslash_and_unsupported_names(self) -> None:
        for name in ("/filesystem.tar.bz2", "../filesystem.tar.bz2", "dir/../filesystem.tar.bz2",
                     r"..\filesystem.tar.bz2", "u-boot.img"):
            with self.subTest(name=name):
                with self.assertRaises(inspector.InspectionError):
                    self.inspect_members([(tarfile.TarInfo(name), b"payload")])

    def test_rejects_duplicate_normalized_paths(self) -> None:
        with self.assertRaisesRegex(inspector.InspectionError, "duplicate-normalized"):
            self.inspect_members([
                (tarfile.TarInfo("./filesystem.tar.bz2"), b"one"),
                (tarfile.TarInfo("filesystem.tar.bz2"), b"two"),
            ])

    def test_rejects_links_and_raw_extension_headers(self) -> None:
        link = tarfile.TarInfo("filesystem.tar.bz2")
        link.type = tarfile.SYMTYPE
        link.linkname = "../../outside"
        with self.assertRaises(inspector.InspectionError):
            self.inspect_members([(link, b"")])

        pax = tarfile.TarInfo("pax-header")
        pax.type = tarfile.XHDTYPE
        with self.assertRaisesRegex(inspector.InspectionError, "extension-members"):
            self.inspect_members([(pax, b"path=filesystem.tar.bz2\n")])

    def test_rejects_empty_and_noop_archives(self) -> None:
        path = self.root / "empty.tar"
        make_tar(path, [])
        with self.assertRaisesRegex(inspector.InspectionError, "empty-or-noop"):
            inspector.inspect_package(str(path))

    def test_rejects_compressed_and_truncated_outer_archives(self) -> None:
        path = self.root / "compressed.tar.gz"
        make_tar(path, [(tarfile.TarInfo("filesystem.tar.bz2"), b"payload")])
        import gzip
        path.write_bytes(gzip.compress(path.read_bytes()))
        with self.assertRaisesRegex(inspector.InspectionError, "compressed-outer-tar"):
            inspector.inspect_package(str(path))

        path = self.root / "truncated.tar"
        make_tar(path, [(tarfile.TarInfo("filesystem.tar.bz2"), b"payload")])
        path.write_bytes(path.read_bytes()[:1536])
        with self.assertRaises(inspector.InspectionError):
            inspector.inspect_package(str(path))

    def test_enforces_member_and_declared_byte_limits(self) -> None:
        path = self.root / "limited.tar"
        make_tar(path, [(tarfile.TarInfo("filesystem.tar.bz2"), b"1234")])
        with patch.object(inspector, "MAX_MEMBERS", 1):
            with self.assertRaisesRegex(inspector.InspectionError, "member-count-limit"):
                inspector.inspect_package(str(path))
        with patch.object(inspector, "MAX_MEMBER_BYTES", 3):
            with self.assertRaisesRegex(inspector.InspectionError, "member-byte-limit"):
                inspector.inspect_package(str(path))

    def test_rejects_nonregular_input(self) -> None:
        with self.assertRaises((inspector.InspectionError, OSError)):
            inspector.inspect_package(str(self.root))

    def test_rejects_nonzero_data_after_end_and_bad_checksum(self) -> None:
        path = self.root / "tampered.tar"
        make_tar(path, [(tarfile.TarInfo("filesystem.tar.bz2"), b"payload")])
        original = path.read_bytes()
        changed = bytearray(original)
        changed[-2048] = 1
        path.write_bytes(changed)
        with self.assertRaises(inspector.InspectionError):
            inspector.inspect_package(str(path))
        changed = bytearray(original)
        changed[0] ^= 1
        path.write_bytes(changed)
        with self.assertRaises(inspector.InspectionError):
            inspector.inspect_package(str(path))

    def test_cli_failure_never_authorizes_offers(self) -> None:
        import contextlib
        import json
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = inspector.main([str(self.root / "missing.tar")])
        self.assertEqual(result, 2)
        self.assertFalse(json.loads(output.getvalue())["CanOfferUpdates"])


if __name__ == "__main__":
    unittest.main()
