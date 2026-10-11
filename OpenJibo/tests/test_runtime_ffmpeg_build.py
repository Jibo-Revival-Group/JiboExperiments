import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("build", Path(__file__).resolve().parents[1] / "scripts/cloud/collect-ffmpeg-build-evidence.py")
build = importlib.util.module_from_spec(spec)
spec.loader.exec_module(build)
fixture_spec = importlib.util.spec_from_file_location("fixture", Path(__file__).with_name("test_runtime_nuget_metadata.py"))
fixture = importlib.util.module_from_spec(fixture_spec)
fixture_spec.loader.exec_module(fixture)


def binary():
    header = bytearray(64)
    header[:7] = b"\x7fELF\x02\x01\x01"
    header[16:20] = b"\x03\x00\x3e\x00"
    return bytes(header) + b"\x00--prefix=/usr --enable-gpl --enable-shared\x00"


def archive(data, name=None, kind=tarfile.REGTYPE, extra=False):
    stream = io.BytesIO()
    with tarfile.open(fileobj=stream, mode="w") as tar:
        member = tarfile.TarInfo(name or Path(build.LIBRARY).name)
        member.type = kind
        member.size = len(data) if kind == tarfile.REGTYPE else 0
        tar.addfile(member, io.BytesIO(data) if member.size else None)
        if extra:
            tar.addfile(tarfile.TarInfo("extra"))
    return stream.getvalue()


class FfmpegBuildTests(unittest.TestCase):
    def test_exact_identity_hash_configuration_and_no_overwrite(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            inv, copied, output = [root / name for name in ("inventory.json", "library.tar", "review.json")]
            data = binary()
            doc = fixture.inventory()
            doc["artifacts"].append({"name": "libavutil58", "type": "deb", "version": build.VERSION,
                                    "metadata": {"files": [{"path": build.LIBRARY, "digest": {
                                        "algorithm": "md5", "value": hashlib.md5(data).hexdigest()}}]}})
            inv.write_text(json.dumps(doc))
            copied.write_bytes(archive(data))
            report = build.collect(inv, "portable", copied, output)
            self.assertEqual(report["library_sha256"], hashlib.sha256(data).hexdigest())
            self.assertTrue(report["observed_switches"]["--enable-gpl"])
            self.assertFalse(report["observed_switches"]["--enable-nonfree"])
            self.assertFalse(report["stable_promotion_allowed"])
            self.assertFalse(report["corresponding_source_verified"])
            saved = output.read_bytes()
            with self.assertRaises(ValueError):
                build.collect(inv, "portable", copied, output)
            self.assertEqual(saved, output.read_bytes())
            copied.write_bytes(archive(data + b"tampered"))
            with self.assertRaises(ValueError):
                build.collect(inv, "portable", copied, root / "wrong.json")
            self.assertFalse((root / "wrong.json").exists())
            with self.assertRaises(ValueError):
                build.collect(inv, "avx2", copied, root / "other.json")

    def test_rejects_paths_links_multiple_members_and_wrong_elf(self):
        for raw in (archive(binary(), "../library"), archive(binary(), "/absolute"),
                    archive(binary(), kind=tarfile.SYMTYPE), archive(binary(), kind=tarfile.LNKTYPE),
                    archive(binary(), extra=True), archive(b"not ELF"), b"", b"not tar"):
            with self.assertRaises((ValueError, tarfile.TarError)):
                build.library_bytes(raw)

    def test_rejects_missing_ambiguous_unterminated_or_oversized_config(self):
        for data in (b"--prefix=/usr\x00", binary() + binary(),
                     binary().rstrip(b"\x00"),
                     b"\x00--prefix=" + b"x" * build.MAX_CONFIG + b" --enable-shared\x00"):
            with self.assertRaises(ValueError):
                build.configuration(data)
        self.assertFalse("--enable-gpl" in build.configuration(
            binary().replace(b"--enable-gpl", b"--enable-gpl-extra"))[1].split())

    def test_bounds_and_inventory_ambiguity(self):
        with self.assertRaises(ValueError):
            build.library_bytes(b"x" * (build.MAX_TAR + 1))
        package = {"name": "libavutil58", "type": "deb", "version": build.VERSION,
                   "metadata": {"files": []}}
        for doc in ({"artifacts": []}, {"artifacts": [package]}, {"artifacts": [package, package]}):
            with self.assertRaises(ValueError):
                build.expected_digest(doc)


if __name__ == "__main__":
    unittest.main()
