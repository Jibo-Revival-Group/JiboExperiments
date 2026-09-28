# Speech-enabled build preflight — 2026-09-28

Status: build safeguards and acoustic fixture prepared. **The full speech image
has not been built or acoustically accepted yet.**

## Resource boundary

The current Windows VM reports approximately 4 GiB total RAM and only 0.5 GiB
free at inspection. Docker reports 18 CPUs but about 1.85 GiB RAM. Starting a
large parallel native/.NET build in that state risks exhausting memory.
The operator was asked either to increase VM RAM (8–12 GiB is a practical test
target, not a proven product requirement) or provide the Ubuntu laptop's specs.
No additional computers are needed until this single-builder path is assessed.

The Dockerfile now defaults Whisper compilation to two jobs, validates explicit
values from 1 through 32, and disables native-host tuning plus newer x86 feature
requirements for a conservative baseline. Actual compilation and performance
must still be tested; settings alone do not certify CPU compatibility. Upstream
Whisper v1.9.3 defines these options in its
[GGML configuration](https://github.com/ggml-org/whisper.cpp/blob/v1.9.3/ggml/CMakeLists.txt)
and [CPU configuration](https://github.com/ggml-org/whisper.cpp/blob/v1.9.3/ggml/src/ggml-cpu/CMakeLists.txt).

## Build context protection

Local `.env` files, captures, artifact outputs, backups and `node_modules` are
excluded from the Docker context; `.env.example` and required source files are
retained. A real scratch Docker build using synthetic sentinels confirms those
rules. No personal capture files are used by that test. This fixes future build
input selection; it is not a claim that prior releases exposed secrets.

## Acoustic test preparation

Existing protocol fixtures contain placeholder audio or transcript hints. They
are useful protocol tests but cannot establish actual speech recognition.

A local Windows `Microsoft David Desktop` voice generated a non-personal test
clip for the phrase:

> The quick brown fox jumps over the lazy dog. Please tell me what time it is.

The local WAV is retained under
`artifact-output/speech-acceptance-20260928/known-phrase.wav`, excluded from Git.
SHA-256: `8b2843fc337b1ab7de56ea7b02e1162eac85ec66293dc42f7107ca3b9803ef16`.
Offline `ffprobe` confirmed 16 kHz, 16-bit mono PCM. This fixture's creation is not
evidence of Jibo TTS functionality, Whisper accuracy or natural-microphone quality.
It is not included in a public release.

## Local speech acceptance results — September 28

After the Windows VM was increased to 12 GB, Docker reported 6,208,786,432
bytes available. The speech image built successfully with two native compile
jobs. No registry push or deployment was performed.

- Local tag: `openjibo-cloud:speech-acceptance-20260928`.
- Image identity: `sha256:9a6249b2c4d2c282c908edb3ec2e12c4a9e040eb6059e58d3db8c2c827ddcb0a`.
- Whisper source release ref: `v1.9.3`; model: `base.en`.
- Model SHA-256: `a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002`.
- Offline CLI test: exact phrase recovered, 15.49 seconds reported total for
  approximately six seconds of audio, four threads and default decoding settings.
- API test: converted the same WAV to Ogg/Opus and sent LISTEN, binary Ogg pages,
  then CLIENT_ASR over an authenticated loopback WebSocket. No transcript hints
  were sent. The API received nine frames (seven audio pages, two metadata pages).
- Final transcript: `the quick brown fox jumps over the lazy dog please tell me what time it is`.
- Observed reply types: LISTEN and EOS. Logs confirmed provider
  `local-whispercpp-buffered-audio`, successful ffmpeg conversion and Whisper exit
  zero. The API used four threads, audio context 512 and beam size one; the
  transcription span was approximately five seconds (log timestamps).

The API used a fresh File-backed disposable container, not the PostgreSQL
restore-test stack. An internal Docker network prevented external-provider
access. Azure Speech and both Whisper-server configuration switches were off;
warm-server autostart was also off. Docker Desktop did not expose this internal
network's published port to Windows, so a Node 24 Alpine probe runner shared the
API network namespace and used localhost there. Only the synthetic fixture,
probe and WebSocket library were mounted read-only into the runner.

This establishes local build and genuine acoustic API transcription, not TTS,
physical-robot compatibility, sustained latency, native-Linux acceptance or
release readiness. The CLI and API timings use different decoding settings and
are not a controlled performance comparison. The test container is stopped after
acceptance; existing starter/restore volumes are preserved.

## Remaining acceptance sequence

1. Test response/TTS separately, then perform a physical-robot voice test.
2. Measure repeated-turn speech latency and warm-server behavior separately.
3. Use the Ubuntu laptop for independent native-Linux install, persistence and
   restore evidence if its resources are suitable. Windows Docker Desktop's
   Linux VM alone is not that independent host test.

No production changes or release publication were performed in this step.
