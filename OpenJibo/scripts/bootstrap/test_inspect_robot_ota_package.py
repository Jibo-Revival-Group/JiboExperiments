import hashlib
import importlib.util
import io
import bz2
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


def make_nested_tar(members: list[tuple[tarfile.TarInfo, bytes]]) -> bytes:
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode="w:") as archive:
        root = tarfile.TarInfo("./")
        root.type = tarfile.DIRTYPE
        archive.addfile(root)
        for info, content in members:
            info.size = len(content)
            archive.addfile(info, io.BytesIO(content))
    return output.getvalue()


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

    def test_optional_nested_scan_reports_bounded_structural_inventory_only(self) -> None:
        directory = tarfile.TarInfo("etc/")
        directory.type = tarfile.DIRTYPE
        nested = make_nested_tar([
            (directory, b""),
            (tarfile.TarInfo("etc/config"), b"synthetic config"),
            (tarfile.TarInfo("usr/bin/tool"), b"synthetic executable"),
        ])
        result = self.inspect_members(
            [(tarfile.TarInfo("filesystem.tar.bz2"), bz2.compress(nested))],
            inspect_filesystem=True)

        inspection = result["NestedFilesystemInspection"]
        self.assertTrue(inspection["Performed"])
        self.assertFalse(inspection["CompatibilityCertification"])
        self.assertFalse(result["CanOfferUpdates"])
        self.assertIn("filesystem-content-abi-ownership-hardware-not-validated", result["Blockers"])
        self.assertIn("publisher-trust-and-signature-not-verified", result["Blockers"])
        self.assertEqual(inspection["RegularFileCount"], 2)
        self.assertEqual(inspection["DirectoryCount"], 2)

    def test_nested_scan_rejects_traversal_links_modes_and_path_collisions(self) -> None:
        escape = tarfile.TarInfo("../escape")
        link = tarfile.TarInfo("etc/link")
        link.type = tarfile.SYMTYPE
        link.linkname = "../../escape"
        setid = tarfile.TarInfo("usr/bin/setid")
        setid.mode = 0o4755
        collisions = [
            [(tarfile.TarInfo("a"), b"file"), (tarfile.TarInfo("a/b"), b"child")],
            [(tarfile.TarInfo("a/b"), b"child"), (tarfile.TarInfo("a"), b"file")],
        ]
        cases = [
            [(escape, b"bad")],
            [(link, b"")],
            [(setid, b"bad")],
            *collisions,
        ]
        for members in cases:
            with self.subTest(names=[info.name for info, _ in members]):
                with self.assertRaises(inspector.InspectionError):
                    self.inspect_members(
                        [(tarfile.TarInfo("filesystem.tar.bz2"),
                          bz2.compress(make_nested_tar(members)))],
                        inspect_filesystem=True)

    def test_nested_elf_headers_are_inventory_not_compatibility(self) -> None:
        def elf(bits, order, machine):
            data = bytearray(52 if bits == 32 else 64)
            data[:7] = b"\x7fELF" + bytes([1 if bits == 32 else 2, 1 if order == "little" else 2, 1])
            data[18:20] = machine.to_bytes(2, order)
            data[20:24] = (1).to_bytes(4, order)
            offset = 40 if bits == 32 else 52
            data[offset:offset + 2] = len(data).to_bytes(2, order)
            return bytes(data)

        arm = elf(32, "little", 40)
        other = elf(64, "big", 183)
        nested = make_nested_tar([
            (tarfile.TarInfo("lib/one.so"), arm),
            (tarfile.TarInfo("lib/two.so"), arm),
            (tarfile.TarInfo("lib/other.so"), other),
            (tarfile.TarInfo("etc/plain"), b"ordinary data"),
        ])
        members = [(tarfile.TarInfo("filesystem.tar.bz2"), bz2.compress(nested))]
        result = self.inspect_members(members, inspect_filesystem=True)
        inventory = result["NestedFilesystemInspection"]["ElfHeaderInventory"]
        self.assertEqual([entry["Machine"] for entry in inventory], [40, 183])
        self.assertEqual([entry["FileCount"] for entry in inventory], [2, 1])
        self.assertEqual(inventory[1]["ByteOrder"], "big")
        self.assertFalse(result["CanOfferUpdates"])
        self.assertFalse(result["NestedFilesystemInspection"]["CompatibilityCertification"])
        with patch.object(inspector, "MAX_ELF_HEADER_VARIANTS", 1):
            with self.assertRaisesRegex(inspector.InspectionError, "variant-limit"):
                self.inspect_members(members, inspect_filesystem=True)

        for invalid in (b"\x7fELF", arm[:30], arm[:6] + b"\x02" + arm[7:],
                        arm[:40] + b"\0\0" + arm[42:]):
            with self.subTest(length=len(invalid)):
                with self.assertRaises(inspector.InspectionError):
                    self.inspect_members([(tarfile.TarInfo("filesystem.tar.bz2"),
                        bz2.compress(make_nested_tar([(tarfile.TarInfo("bin/bad"), invalid)])))],
                        inspect_filesystem=True)

    def test_nested_scan_bounds_bzip2_expansion_and_rejects_bad_streams(self) -> None:
        plain_tar = make_nested_tar([(tarfile.TarInfo("large"), b"x" * 8192)])
        compressed = bz2.compress(plain_tar)
        path = self.root / "nested-limits.tar"

        make_tar(path, [(tarfile.TarInfo("filesystem.tar.bz2"), compressed)])
        with patch.object(inspector, "MAX_NESTED_ENTRIES", 1):
            with self.assertRaisesRegex(inspector.InspectionError, "entry-count-limit"):
                inspector.inspect_package(str(path), inspect_filesystem=True)
        with patch.object(inspector, "MAX_NESTED_DECOMPRESSED_BYTES", 1024):
            with self.assertRaisesRegex(inspector.InspectionError, "decompressed-byte-limit"):
                inspector.inspect_package(str(path), inspect_filesystem=True)
        with patch.object(inspector, "MAX_NESTED_COMPRESSED_BYTES", 1):
            with self.assertRaisesRegex(inspector.InspectionError, "compressed-byte-limit"):
                inspector.inspect_package(str(path), inspect_filesystem=True)

        bad_streams = [
            compressed[:-5],
            compressed[:-1] + bytes([compressed[-1] ^ 0xFF]),
            compressed + bz2.compress(b"extra stream"),
            bz2.compress(plain_tar[:-10240]),
            bz2.compress(plain_tar[:-512] + b"x" + b"\0" * 511),
        ]
        for bad in bad_streams:
            with self.subTest(compressedLength=len(bad)):
                make_tar(path, [(tarfile.TarInfo("filesystem.tar.bz2"), bad)])
                with self.assertRaises(inspector.InspectionError):
                    inspector.inspect_package(str(path), inspect_filesystem=True)

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
