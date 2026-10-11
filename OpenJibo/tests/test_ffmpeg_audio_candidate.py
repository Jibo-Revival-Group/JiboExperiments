import importlib.util
import array
from pathlib import Path
import sys
import tempfile
import unittest
import wave

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("probe", ROOT / "OpenJibo/scripts/cloud/probe-ffmpeg-audio-candidate.py")
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class AudioCandidateTests(unittest.TestCase):
    def test_existing_builds_remain_distro_and_audio_is_separate(self):
        dockerfile = (ROOT / "OpenJibo/Dockerfile").read_text()
        self.assertIn("ARG FFMPEG_PROFILE=distro", dockerfile)
        self.assertIn("AS ffmpeg-distro", dockerfile)
        self.assertIn("AS ffmpeg-audio", dockerfile)
        self.assertIn("FROM ffmpeg-${FFMPEG_PROFILE} AS runtime", dockerfile)
        audio = dockerfile.split("AS ffmpeg-audio\n", 1)[1].split("FROM ffmpeg-${", 1)[0]
        self.assertNotIn("install -y --no-install-recommends ffmpeg", audio)
        self.assertIn("/usr/share/openjibo-ffmpeg", audio)
        publication = (ROOT / ".github/workflows/openjibo-runtime-preview-publish.yml").read_text()
        self.assertNotIn("FFMPEG_PROFILE=audio", publication)

    def test_source_and_signer_pins_precede_extraction_and_compile(self):
        script = (ROOT / "OpenJibo/scripts/cloud/build-ffmpeg-audio.sh").read_text()
        self.assertIn("version=6.1.6", script)
        self.assertIn("FCF986EA15E6E293A5644F10B4322F04D67658D8", script)
        for token in ("sha256sum --check SOURCE-SHA256SUMS", 'test "$actual" = "$fingerprint"',
                      '--verify "ffmpeg-$version.tar.xz.asc"', 'VALIDSIG $fingerprint'):
            self.assertLess(script.index(token), script.index("tar -xJf"))
        self.assertIn('--max-time 120', script)
        self.assertIn('make -j"$jobs"', script)
        self.assertIn('test "$jobs" -ge 1 && test "$jobs" -le 32', script)
        for name in ("SOURCE-SHA256SUMS", "source-signature-status.txt", "config.mak", "config.h",
                     "COPYING.LGPLv2.1", "LICENSE.md", "build-ffmpeg-audio.sh"):
            self.assertIn(name, script)

    def test_configuration_is_audio_only_with_runtime_filters(self):
        script = (ROOT / "OpenJibo/scripts/cloud/build-ffmpeg-audio.sh").read_text()
        for flag in ("--disable-everything", "--disable-autodetect", "--disable-network",
                     "--disable-gpl", "--disable-version3", "--disable-nonfree", "--enable-shared",
                     "--enable-decoder=opus,flac,pcm_s16le", "--enable-protocol=file,pipe",
                     "silenceremove,volume", "aresample", "--enable-muxer=wav,pcm_s16le"):
            self.assertIn(flag, script)
        for forbidden in ("--enable-gpl", "--enable-nonfree", "--enable-libopus", "--enable-libx264"):
            self.assertNotIn(forbidden, script)
        for path in (ROOT / "OpenJibo/src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/Audio").glob("*BufferedAudioSttStrategy.cs"):
            if "FfmpegAudioPreprocessFilter" in path.read_text():
                self.assertIn(probe.FILTER, path.read_text())

    def test_candidate_workflow_cannot_publish_or_deploy(self):
        workflow = (ROOT / ".github/workflows/openjibo-ffmpeg-audio-candidate.yml").read_text()
        self.assertIn("--target ffmpeg-audio", workflow)
        self.assertIn("--network none --read-only", workflow)
        self.assertIn("--results evidence", workflow)
        for forbidden in ("docker push", "gh release", "az ", "packages: write", "contents: write", "secrets."):
            self.assertNotIn(forbidden, workflow)

    def test_full_runtime_acceptance_is_opt_in_and_azure_is_disabled(self):
        workflow = (ROOT / ".github/workflows/openjibo-speech-build-preview.yml").read_text()
        self.assertIn("options: [distro, audio]", workflow)
        self.assertIn("default: distro", workflow)
        self.assertIn('if: env.FFMPEG_PROFILE == \'audio\'', workflow)
        self.assertIn('--build-arg FFMPEG_PROFILE="$FFMPEG_PROFILE"', workflow)
        self.assertIn("--turns 3", workflow)
        self.assertIn("--mode authenticated --robots 2", workflow)
        self.assertIn("provider=whisper-server-buffered-audio", workflow)
        self.assertIn("-page_duration 100000", workflow)
        self.assertIn("compose down -v", workflow)
        self.assertNotIn("docker push", workflow)
        overlay = (ROOT / "OpenJibo/scripts/cloud/compose-speech-audio-candidate.yml").read_text()
        self.assertIn('OpenJibo__Stt__EnableAzureSpeech: "false"', overlay)
        self.assertIn('"127.0.0.1:8080:8080"', overlay)
        self.assertIn("ports: !reset []", overlay)

    def test_comparison_detects_duration_or_content_regressions(self):
        pcm = [1000, -1000] * 8000
        self.assertEqual(probe.compare(pcm, pcm)["normalized_rmse"], 0)
        with self.assertRaises(ValueError):
            probe.compare(pcm, pcm[:-321])
        with self.assertRaises(ValueError):
            probe.compare(pcm, [-value for value in pcm])
        args = probe.convert_args("raw.pcm", "output.wav")
        self.assertEqual(args[:7], ["-y", "-f", "s16le", "-ar", "48000", "-ac", "2"])
        self.assertIn(probe.FILTER, args)

    def test_fixture_duration_includes_stop_detection_interval(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / "fixture.wav"
            for count, expected in ((24000, True), (16000, False), (32000, False)):
                samples = array.array("h", [2000, -2000] * (count // 2))
                if sys.byteorder != "little":
                    samples.byteswap()
                with wave.open(str(output), "wb") as wav:
                    wav.setnchannels(1)
                    wav.setsampwidth(2)
                    wav.setframerate(16000)
                    wav.writeframes(samples.tobytes())
                if expected:
                    self.assertEqual(len(probe.read_pcm(output)), count)
                else:
                    with self.assertRaises(ValueError):
                        probe.read_pcm(output)


if __name__ == "__main__":
    unittest.main()
