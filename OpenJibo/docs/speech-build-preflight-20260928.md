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

## Repeated-turn and response acceptance

Additional local tests used the same image, four-CPU/two-GiB container limits,
internal network and synthetic audio. Each batch reused one authenticated
WebSocket with a fresh transaction ID per turn. The probe waited for final ASR
and EOS before sending the next turn, with no transcript hints.

| Provider | Consecutive turns | End-to-end turn durations (milliseconds) |
| --- | --- | --- |
| Per-turn local Whisper CLI | 5/5 correct transcripts and EOS | 9544, 5035, 4788, 4487, 4379 |
| Local warm Whisper server, CLI fallback disabled | 5/5 correct transcripts and EOS | 6422, 4621, 4532, 4682, 4563 |

Logs confirmed `whisper-server-buffered-audio` for the warm batch. These are
small sequential correctness samples, not capacity measurements or evidence
that warm mode is materially faster. They do not model microphone streaming,
network delay or multi-robot concurrency.

A second Microsoft David Desktop fixture said “Please tell me your cloud
version.” Its WAV SHA-256 is
`81f6d73bbbf6a7551b36392a241d7dc6307e54aad3f897905c0598750f552cf9`.
After offline conversion to Ogg/Opus, three same-socket turns passed through the
warm provider in 5132, 4613 and 4708 milliseconds. Each produced LISTEN, EOS and
SKILL_ACTION with the expected Cloud version 1.0.20 ESML speech instruction.

This verifies acoustic input through the cloud response contract. Jibo's own
software performs speech playback; the cloud container does not synthesize this
ESML into audible speech. No robot was contacted. Test containers were stopped
afterward; synthetic fixtures and local probe remain excluded from Git.

Review also identified an independent reliability defect: the long-running
Whisper server redirected stdout and stderr without consuming them. Enough
child output can fill a pipe and block the server. The short acoustic batches
did not reproduce a stall; a targeted process-output regression test covers
the fix. Child output is discarded rather than logged because it can contain
transcripts. The measured batches above used the original pre-fix image.

The pipe-drain regression writes 256 KiB to each redirected stream and requires
the child process and both drains to finish within bounded timeouts. The related
speech tests passed 32/32. The full local cloud suite passed 2,493 tests, with
34 PostgreSQL-dependent integration tests skipped and zero failures. PostgreSQL
integration verification was not performed in this run.

Post-fix Linux container verification also passed. Rebuilt local image
`openjibo-cloud:speech-drain-20260928` has identity
`sha256:94f186029dacfc7f2f08c832124dd23d9634feed17d00e933e48ab60126df6d7`.
With CLI fallback and Azure disabled, three consecutive acoustic cloud-version
turns returned the expected LISTEN/EOS/SKILL_ACTION and ESML in 7000, 5007 and
4783 milliseconds. Logs confirmed the warm provider for all three. Stopping the
container stopped its owned Whisper process and the API exited zero. This is
a short post-fix smoke, not a long-duration load certification.

## Remaining acceptance sequence

1. Verify actual response playback with a physical-robot voice test.
2. Run longer speech-load tests, including concurrency and resource measurements.
3. Use the Ubuntu laptop for independent native-Linux install, persistence and
   restore evidence if its resources are suitable. Windows Docker Desktop's
   Linux VM alone is not that independent host test.

No production changes or release publication were performed in this step.
