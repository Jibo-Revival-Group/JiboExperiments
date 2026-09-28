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

## Next acceptance sequence

1. Confirm a builder with adequate memory and record OS/CPU/Docker platform.
2. Build an explicit local Whisper/model variant and record image identity.
3. Inspect the executable and model, then transcribe the known WAV with
   `whisper-cli` and compare normalized words with the expected phrase.
4. Send real encoded audio through the API's buffered speech path with synthetic
   hint/bypass routes disabled; verify the actual local provider and transcript.
5. Test response/TTS separately, then perform a physical-robot voice test.
6. Use the Ubuntu laptop for independent native-Linux install, persistence and
   restore evidence if its resources are suitable. Windows Docker Desktop's
   Linux VM alone is not that independent host test.

No production changes or release publication were performed in this step.
