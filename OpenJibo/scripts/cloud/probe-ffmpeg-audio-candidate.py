"""Synthetic conversion regression only; not microphone or speech recognition acceptance."""
import argparse
import array
import json
import math
from pathlib import Path
import subprocess
import sys
import wave

FILTER = "silenceremove=start_periods=1:start_duration=0.03:start_threshold=-45dB:stop_periods=-1:stop_duration=0.5:stop_threshold=-45dB,volume=8dB"
CASES = ("stereo.wav", "audio.ogg", "audio.flac", "raw.pcm")


def convert_args(name, output):
    before = ["-f", "s16le", "-ar", "48000", "-ac", "2"] if name == "raw.pcm" else []
    return ["-y", *before, "-i", name, "-af", FILTER, "-ar", "16000", "-ac", "1", "-f", "wav", str(output)]


def run(command, cwd):
    subprocess.run(command, cwd=cwd, check=True, timeout=30, capture_output=True)


def prepare(directory, reference):
    directory.mkdir()
    samples = array.array("h")
    for index in range(48000 * 2):
        time = index / 48000
        active = 0.4 <= time < 1.4
        for frequency in (440, 660):
            samples.append(round(4000 * math.sin(2 * math.pi * frequency * time)) if active else 0)
    if sys.byteorder != "little":
        samples.byteswap()
    raw = samples.tobytes()
    with wave.open(str(directory / "stereo.wav"), "wb") as wav:
        wav.setnchannels(2)
        wav.setsampwidth(2)
        wav.setframerate(48000)
        wav.writeframes(raw)
    (directory / "raw.pcm").write_bytes(raw)
    for name, codec in (("audio.ogg", "libopus"), ("audio.flac", "flac")):
        run([reference, "-y", "-i", "stereo.wav", "-c:a", codec, name], directory)
    for name in CASES:
        run([reference, *convert_args(name, "reference-" + name + ".wav")], directory)
    (directory / "invalid.ogg").write_bytes(b"invalid synthetic input")


def read_pcm(path):
    if not 44 <= path.stat().st_size <= 1024 * 1024:
        raise ValueError("Invalid result size")
    with wave.open(str(path), "rb") as wav:
        if (wav.getnchannels(), wav.getsampwidth(), wav.getframerate(), wav.getcomptype()) != (1, 2, 16000, "NONE"):
            raise ValueError("Wrong output format")
        pcm = array.array("h", wav.readframes(wav.getnframes()))
    if sys.byteorder != "little":
        pcm.byteswap()
    # The 1-second tone retains ~0.5 seconds while stop_duration is detected.
    # Check both reference and candidate, not just a guessed tone-only duration.
    if len(pcm) < 22000 or len(pcm) > 26000 or max(map(abs, pcm)) < 1000:
        raise ValueError("Missing or implausible synthetic audio")
    return pcm


def compare(reference, candidate):
    if abs(len(reference) - len(candidate)) > 320:
        raise ValueError("Conversion duration differs by more than 20 ms")
    count = min(len(reference), len(candidate))
    rmse = math.sqrt(sum((reference[i] - candidate[i]) ** 2 for i in range(count)) / count) / 32768
    if rmse > 0.025:
        raise ValueError("Conversion differs beyond synthetic PCM tolerance")
    return {"reference_samples": len(reference), "candidate_samples": len(candidate), "normalized_rmse": rmse}


def verify(fixtures, results):
    report = {"scope": __doc__, "cases": {}}
    for name in CASES:
        report["cases"][name] = compare(read_pcm(fixtures / ("reference-" + name + ".wav")),
                                      read_pcm(results / (name + ".wav")))
    report["passed"] = True
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixtures", type=Path, required=True)
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--reference", default="ffmpeg")
    parser.add_argument("--results", type=Path)
    args = parser.parse_args()
    if args.prepare:
        prepare(args.fixtures, args.reference)
    elif args.results:
        print(json.dumps(verify(args.fixtures, args.results), sort_keys=True))
    else:
        parser.error("Select --prepare or --results")


if __name__ == "__main__":
    main()
