# Guarded-source experimental preview — 2026-10-11

Owner-authorized fresh publication from
`b9dd0de89dfdb7bea558c2ae0923eefd50758b52`, after
[CI 38165035304 passed](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38165035304).
Both profiles passed [publisher 38165346370](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38165346370)
and [independent verification 38165660015](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38165660015).

[Public prerelease runtime-preview-38165346370](https://github.com/transcendentsoftware-jd/JiboExperiments/releases/tag/runtime-preview-38165346370)
targets that exact source, is marked prerelease and was created with `latest=false`.
Earlier releases/assets, recommended download selectors and deployments were not
changed. No stable promotion, automatic upgrade or physical robot change occurred.

## Exact identities

Image namespace: `ghcr.io/transcendent-software-llc/openjibo-runtime`.

| Profile | Image digest | Starter ZIP SHA-256 |
| --- | --- | --- |
| portable | `sha256:f8f475c3ca78fb89e2a78b18e989e39dfb4e51dbe70c323bf3a4f7895eb98468` | `e8a63cc2ebe27be0499dc6ec5e235382b0063fcd26fc8caeb9e536adc5414cb8` |
| avx2 | `sha256:e40b84cce0428fd75d2c2c938af587286253bee56caecd098b0d3e127c578881` | `660b599a89ac71056699cbf66a1ac76f492c3b3d662702e8f5a3a1ab31daa569` |

Both use the audio FFmpeg profile and pinned `base.en` model. AVX2 requires the
documented CPU prerequisites; it is not the portable fallback.

## Verification actually performed

- Publisher checked native CPU build flags, Whisper server help, model bytes,
  source checkout identity, retained source hashes and FFmpeg configuration.
- Independent workflow pulled both immutable images, checked CPU/source/FFmpeg
  labels, verified image and SPDX attestations, checked FFmpeg source hashes,
  then independently verified both starter ZIP signatures and image identities.
- Downloaded publisher ZIPs and final GitHub release ZIPs each passed local
  cryptographic verification with exact source/checksum and fixed signer workflow.
- Anonymous GHCR manifest requests returned HTTP 200 and the expected digest
  for each profile. This is manifest access, not anonymous download of every layer.
- Release metadata and all 16 asset names were inspected after creation.

Whisper tag checkout is guarded against commit
`371b5a7561823ab2bb32142d2751e35e7534727b` before compilation. The two retained
source archives and the downloaded public release archive have SHA-256
`b2771600e40e0817112242402359b5a7c3712c4623283913142f6b5978038537`.
The archive is retained in each image and exposed as `whisper-source.tar.gz`,
with source commit/hash files. The hash file retains its original image path;
compare its hash to the downloaded archive, not that local path.

Each profile's evidence ZIP retains its inventories, source/configuration,
package checks and verification reports. Standalone Syft/SPDX inventories,
starter attestations and FFmpeg source/signature/key are also public assets.
Evidence ZIPs and adjacent source assets are not themselves signed subjects;
image and starter attestations cover their declared subjects only.

Local artifacts: `artifact-output/runtime-preview-38165346370/`,
`artifact-output/runtime-verification-38165660015/`, and
`artifact-output/release-download-38165346370/`.

## Native Ubuntu acceptance of the new digests

The preparer now accepts this version by explicit selection only, with mandatory
ZIP provenance verification. The launcher recognizes the exact manifest hashes
below and preserves the native/local-daemon CPU check for AVX2. Older identities,
defaults and the site's recommended download remain unchanged.

| Profile | MANIFEST.json SHA-256 | Fresh local project / port |
| --- | --- | --- |
| portable | `287973b7e409ede338b0044dee79b7a3f846fea6219b6e3fd497c30e3fc4eb7b` | `openjibo-guarded-portable-38165346370` / 8089 |
| avx2 | `25045af1b2d7342b1862eaef04470b1e2d49e81b0524a9942cf9ba6c42b39c80` | `openjibo-guarded-avx2-38165346370` / 8090 |

Both fresh signed preparations, isolated loopback launches, migrations and health
checks passed on the authorized Ubuntu laptop (i5-8250U, 8 logical CPUs, 8 GB
RAM). The configured image reference was checked against the exact digest above;
Azure Speech was disabled. Each profile passed two-robot authentication and all
three socket connections. This `--skip-turn` socket check alone does not exercise
speech or proactive response handling.

Separate recorded-audio checks passed through `whisper-server-buffered-audio`:

- Portable: three cloud-version turns, 9248 / 7346 / 6806 ms, all expected
  LISTEN/EOS/SKILL_ACTION responses and phrase checks passed.
- AVX2: ten cloud-version turns, first 3263 ms and remaining 1008–1699 ms;
  three second-phrase transcription turns, 655 / 702 / 695 ms.
- AVX2 overlap: two concurrent clients, five turns each, all passed in
  1039–1471 ms with ten local Whisper provider entries.

Containers reported running, zero restarts and no OOM after audio/overlap checks.
These are bounded fixture results, not a capacity SLA or peak-memory study.
Both profiles retained their two probe device rows across PostgreSQL restart
while their API was stopped; rows were compared before API startup. Health and
all sockets passed after restarting the API (573 / 618 ms socket checks).
The probes then issued new tokens, so this does not prove old-token survival.

Evidence remains at `/home/jake-dubin/openjibo-guarded-tests.UPKrcp` and locally
at `artifact-output/ubuntu-guarded-preview-20261011/`. Only generated JSON/text
reports were copied; secrets, databases and recordings were not transferred.
New stacks, private environments and volumes are preserved alongside earlier
tests. The starter regression suite passed 77 tests with two platform skips on
Windows; the real preparations/launches above exercised native Linux paths.

## Remaining gates

This establishes build/byte/provenance identity, not stable readiness,
redistribution clearance or reproducible builds. Fresh backup/restore acceptance
of these **new exact digests** remains to be run; the earlier image acceptance
cannot be transferred automatically.
Physical microphone/playback testing remains deferred.

Notice completeness, native dependency and model terms, package signature trust
and complete-image corresponding-source review remain open. See the
[earlier audio preview evidence](audio-preview-publication-20261011.md) for the
separate prior-image notice and byte-linkage findings, not verification of the
new inventories. The new release is explicitly selectable for experimental
testing, not the site's recommended download or a stable channel.
