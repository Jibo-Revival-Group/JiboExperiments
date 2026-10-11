"""Offline contract checks for the neutral static-site payload."""
from html.parser import HTMLParser
from pathlib import Path
import importlib.util
import unittest

ROOT = Path(__file__).resolve().parents[1] / "src" / "OpenJibo.Site"


class Page(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []
        self.ids = set()
        self.h1 = 0
        self.lang = None

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "html":
            self.lang = attrs.get("lang")
        if tag == "h1":
            self.h1 += 1
        if "id" in attrs:
            self.ids.add(attrs["id"])
        for key in ("href", "src"):
            if key in attrs:
                self.links.append(attrs[key])


class PublicSiteTests(unittest.TestCase):
    def test_accessible_structure_and_local_links(self):
        for path in ROOT.glob("*.html"):
            with self.subTest(page=path.name):
                parsed = Page()
                parsed.feed(path.read_text(encoding="utf-8"))
                self.assertEqual(parsed.lang, "en")
                self.assertEqual(parsed.h1, 1)
                self.assertIn("main", parsed.ids)
                self.assertIn("#main", parsed.links)
                for link in parsed.links:
                    if link.startswith("#"):
                        self.assertIn(link[1:], parsed.ids)
                    elif not link.startswith("https://"):
                        file, _, anchor = link.partition("#")
                        self.assertNotIn(":", file)
                        target = ROOT / file
                        self.assertTrue(target.is_file(), link)
                        if anchor:
                            other = Page()
                            other.feed(target.read_text(encoding="utf-8"))
                            self.assertIn(anchor, other.ids)

    def test_downloads_are_targeted_preview(self):
        page = (ROOT / "downloads.html").read_text(encoding="utf-8")
        for profile in ("portable", "avx2"):
            self.assertIn(f"runtime-preview-36863178102/starter-{profile}-preview.zip", page)
        for checksum in ("68f7181b4c50b08c632a482efd9f0a20be3ab3a2ad1de97fe6281a5e320b6a91", "eaab909b46e1166d08344c7582fb3833fe9bdc596a005e0ce4ee4a8c3bdb6466"):
            self.assertIn(checksum, page)
        self.assertIn("Not a stable release", page)
        self.assertIn("loopback port", page)
        self.assertIn("operator-reviewed", page)

    def test_payload_has_no_operational_files(self):
        self.assertEqual({p.name for p in ROOT.iterdir()}, {"index.html", "downloads.html", "site.css"})

    def test_signed_preview_is_primary_without_claiming_stable_certification(self):
        page = (ROOT / "downloads.html").read_text(encoding="utf-8")
        self.assertLess(page.index('id="signed-preview"'), page.index('id="legacy-preview"'))
        for profile in ("portable", "avx2"):
            self.assertIn(f"runtime-preview-38165346370/starter-{profile}-preview.zip", page)
            self.assertIn(f"runtime-preview-38165346370/starter-{profile}-attestation.json", page)
        for digest in ("e8a63cc2ebe27be0499dc6ec5e235382b0063fcd26fc8caeb9e536adc5414cb8", "660b599a89ac71056699cbf66a1ac76f492c3b3d662702e8f5a3a1ab31daa569"):
            self.assertIn(digest, page)
        self.assertIn("gh attestation verify --help", page)
        self.assertIn("set -euo pipefail", page)
        self.assertIn("--port 8093", page)
        self.assertIn("--port 8094", page)
        self.assertIn("--cpu-profile avx2", page)
        self.assertIn("--avx2-local-daemon", page)
        self.assertNotIn("automatic preparer/launcher currently supports portable only", page)
        self.assertIn("--plan", page)
        self.assertIn("--start", page)
        self.assertIn("checksum-only ZIPs", page)
        self.assertIn("encrypted-data decryption", page)

    def test_primary_downloads_match_reviewed_preparer_pins(self):
        script = ROOT.parents[1] / "scripts/cloud/prepare-published-starter.py"
        spec = importlib.util.spec_from_file_location("site_reviewed_preparer", script)
        preparer = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(preparer)
        page = (ROOT / "downloads.html").read_text(encoding="utf-8")
        primary = page.split('id="signed-preview"', 1)[1].split('</section>', 1)[0]
        for profile in ('portable', 'avx2'):
            record = preparer.release_identity(preparer.GUARDED_VERSION, profile)
            self.assertIn(record["url"], primary)
            self.assertIn(record["archive_sha256"], primary)
            self.assertIn("--version " + record["version"], page)
            self.assertTrue(record["release_zip_attested"])
        for old in (preparer.VERSION, preparer.SIGNED_VERSION, preparer.AUDIO_VERSION):
            self.assertNotIn(old, primary)

    def test_promotion_keeps_experimental_limits_and_prior_releases(self):
        page = (ROOT / 'downloads.html').read_text(encoding='utf-8')
        for phrase in ('not clearance or stable readiness', 'License/notice review',
                       'physical-robot acceptance remain open', 'existing-token survival',
                       'No latest/stable selector', 'operator-reviewed'):
            self.assertIn(phrase, page)
        for version in ('runtime-preview-37540893708', 'runtime-preview-38139481435'):
            self.assertIn('/releases/tag/' + version, page)


if __name__ == "__main__":
    unittest.main()
