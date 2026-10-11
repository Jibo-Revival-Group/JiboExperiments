# Audio-focused FFmpeg candidate

Status: opt-in experiment, 2026-10-11. Not a stable release or deployment.

The owner selected an audio-focused replacement candidate after both published
preview images were found to contain Ubuntu FFmpeg configured with
`--enable-gpl`. This does not alter those published digests or imply that the
existing application must be relicensed. Free/noncommercial distribution still
needs applicable notices and source obligations reviewed; see
[FFmpeg's own guidance](https://ffmpeg.org/legal.html).

## Candidate boundary

`FFMPEG_PROFILE=distro` remains the Dockerfile default. The new opt-in `audio`
profile builds FFmpeg 6.1.6 from the official signed source release. This is the
point release listed for the 6.1 branch on the
[official download page](https://ffmpeg.org/download.html), selected to keep
this experiment on the existing preview's major branch. It is not a claim that
6.1 is the newest major release or vulnerability-free.

Source SHA-256:
`d4fcb164028dd3beee5d92c0ac72e46aac6973c75ea12dc14de07bf8f407370a`.
The build also pins the signature/key bytes, checks release key fingerprint
`FCF986EA15E6E293A5644F10B4322F04D67658D8` from the download page, and requires
a valid source signature before extraction/compilation. No key-server trust
discovery or automatic key retrieval occurs.

The configuration disables autodetection, GPL/version3/nonfree components,
network access, video/device libraries and external codec libraries. It retains
native Ogg/Opus, FLAC, selected PCM/WAV decoding; 16-bit WAV/PCM output; file/pipe
access; resampling/downmixing; and the exact runtime silence-removal/volume
filters. It is not a general-purpose media transcoder. Other formats are not
promised merely because distro FFmpeg accepted them.

FFmpeg libraries are shared, not statically embedded into the .NET application.
`/usr/bin/ffmpeg` stays at the path expected by the runtime. The candidate includes
its exact source archive, signature/key, checksum list, build script, configure
results, and license files under `/usr/share/openjibo-ffmpeg`. This is component
build evidence, not a complete image-wide corresponding-source or notice offer.

## Automated validation

The manual `openjibo-ffmpeg-audio-candidate.yml` workflow builds only the
`ffmpeg-audio` stage. It never publishes a registry image, modifies existing
releases, deploys, enrolls a robot or uses operator credentials.

Synthetic stereo WAV, raw PCM, FLAC and Ogg/Opus fixtures are converted using
the production filter/options. Output must be 16 kHz mono PCM16, contain the
expected non-silent interval, and agree with distro-reference output within
20 ms and normalized PCM RMSE 0.025. An invalid input must fail. Synthetic
fixtures are generated on the runner; no private speech recordings are used.
This tests decoding/filtering, not speech accuracy or actual robot behavior.
The two-second input contains one second of tone; the production filter retains
approximately another half-second while detecting `stop_duration=0.5`. Both
reference and candidate must therefore produce about 1.5 seconds, not a
tone-only one-second result. Reference WAVs and fixture hashes are retained.

After the isolated stage passes, a full runtime candidate must be built with
`--build-arg FFMPEG_PROFILE=audio`. Portable and AVX2 Whisper profiles each need
their own acoustic/socket checks and fresh inventory/source/notice evidence.
The existing publication workflow intentionally continues using the distro
default until that separate promotion decision. Physical-robot acceptance
remains deferred, not waived. No stable/latest tag or deployment is authorized
by this candidate experiment.

`openjibo-speech-build-preview.yml` now has an explicit `ffmpeg_profile=audio`
selection (default remains `distro`). It builds portable and AVX2 Whisper
candidates, starts an ephemeral localhost-only PostgreSQL/API stack, disables
Azure Speech, checks two authenticated synthetic robots, and sends three
Ogg/Opus turns synthesized on the runner. Successful transcripts must use the
warm local Whisper server and return the cloud-version response contract. CI
stack volumes are removed after the job; operator installations are untouched.
This adds acoustic/protocol integration evidence without publishing an image.
