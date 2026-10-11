# Experimental audio FFmpeg preview — 2026-10-11

This is a new experimental prerelease, not a stable channel or an in-place
upgrade. Existing releases, the site's tested-preview recommendation, and
deployments remain unchanged. Portable Ubuntu fresh-install acceptance passed
over SSH on 2026-10-11; the evidence and limits are recorded below.
Physical robot microphone/playback testing remains deferred. No legal clearance
or capacity certification is implied by the signed inventory.

Source: `0b7b56345177156a7da830cfc22c478b5f2b9c80`.
[Source CI 38139230155](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38139230155)
passed tests and smoke. Both profiles published successfully in
[38139481435](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38139481435).
[Independent verification 38139752757](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38139752757)
passed registry pulls, source/CPU/audio labels, image provenance, signed SPDX
inventories, FFmpeg source checksums/configuration, and both starter signatures.
Local ZIP signature verification also passed; anonymous manifests returned 200.

Release tag: `runtime-preview-38139481435`, targeting the source commit above.
Image namespace: `ghcr.io/transcendent-software-llc/openjibo-runtime`.

| Profile | Registry digest | ZIP SHA-256 |
| --- | --- | --- |
| portable | `sha256:e1b2aa06fc1f69bd498c5394388636331ba646abf2f39ca66ddcb6c0ee2697a9` | `42e967f6863b3f6a6ddd0cf7756f7ac840fb14f7326e928c0e9a73028a1c1b01` |
| avx2 | `sha256:5e051559692710205532ec27866d4eb5810e9e69685bfa8c6d3676948f7afb7c` | `4fba38e4528d484238661a5c82150f1dd13594a9d40fa894203727edeb46192d` |

Each inventory has 210 records: 98 Debian, 93 .NET, 13 binary, three Java archives
and three npm. Inventory is evidence, not complete notice/source/license review.
FFmpeg 6.1.6 exact source, signature, public key, license and configuration are
retained in the image at `/usr/share/openjibo-ffmpeg` and in publisher evidence.
The audio profile disables GPL/version3/nonfree/autodetection/network features.
Do not infer the entire image's licensing from that one component configuration.
The previous fixed-policy SBOM reports remain tied to the old digests.

The portable manifest SHA-256 is
`e0b04a517ce103ad0615b21cbcc51cc8ac4602db81fb796976ad3f0069bd2381`.
The guarded preparer/launcher default to this release's portable ZIP. AVX2 is
an explicit `--cpu-profile avx2` preparation choice. Launch additionally requires
`--avx2-local-daemon`, a confirmed daemon on the same native Linux host, the local
system Docker socket, and all required CPU flags. The flag never selects an image.
Local socket/CPU checks are not remote-daemon certification; do not make that
confirmation for a forwarded socket or a daemon running on another host.

## Ubuntu fresh-install acceptance

Use a fresh directory/project and port 8087; retain all existing stacks and
backups. If a command fails, stop and retain the partial directory and volumes.
Do not delete the private `.env` to retry. This creates only synthetic probe
robots in the new local project, with Azure Speech disabled by the launcher.

```bash
(
set -euo pipefail
cd "$HOME/JiboExperiments"
git pull --ff-only
gh attestation verify --help >/dev/null
sudo -v

candidate="$HOME/Downloads/openjibo-audio-preview-$(date -u +%Y%m%dT%H%M%SZ)"
echo "Candidate directory: $candidate"
python3 -B OpenJibo/scripts/cloud/prepare-published-starter.py \
  --channel preview --version runtime-preview-38139481435 \
  --destination "$candidate"
python3 -B OpenJibo/scripts/cloud/launch-published-starter.py \
  --directory "$candidate" --project openjibo-audio-preview-test \
  --port 8087 --sudo-docker
python3 -B OpenJibo/scripts/cloud/launch-published-starter.py \
  --directory "$candidate" --project openjibo-audio-preview-test \
  --port 8087 --sudo-docker --start

node OpenJibo/src/Jibo.Cloud/node/invoke-jetstream-compatibility-probe.mjs \
  --entrypoint-url http://localhost:8087 --hub-url ws://localhost:8087 \
  --notification-url ws://localhost:8087 --mode authenticated --robots 2 \
  --device-prefix audio-preview --skip-turn
node OpenJibo/src/Jibo.Cloud/node/invoke-real-audio-probe.mjs \
  --audio "$HOME/Downloads/cloud-version.ogg" --base-url http://localhost:8087 \
  --robot-id speech-acceptance-audio-preview --turns 3
)
```

Expected: signed preparation, health pass, both synthetic robots connect to all
three socket types, and all three acoustic turns match the phrase and return
`LISTEN`, `EOS`, `SKILL_ACTION`. Portable timing is not expected to match AVX2.
Both profiles' acceptance results are recorded below. Remaining gates are
distribution notice/source review and separately approved release promotion.

## Native Ubuntu acceptance — passed over SSH

On 2026-10-11 the portable published candidate was installed and tested on the
owner's Ubuntu laptop (i5-8250U, 8 GiB RAM). Docker 29.8.2 and Compose 5.6.0 were
observed. The tracked checkout fast-forwarded to `da6c6edc`; unrelated untracked
files, existing projects on ports 8082–8086, and backups were preserved.

Fresh directory:
`/home/jake-dubin/Downloads/openjibo-audio-ssh-20261011T141159Z`.
Project: `openjibo-audio-ssh-20261011141159`, bound to loopback port 8087.
Signed public-release preparation and guarded launch/health checks passed.
Azure Speech was confirmed disabled before probing.

Two authenticated synthetic robots passed notification, listen and proactive
socket checks in 600 ms. Three recorded `cloud-version.ogg` turns all matched
the phrase/instruction and returned `LISTEN`, `EOS`, `SKILL_ACTION`, in 8388,
6899 and 6588 ms. Logs confirmed three `whisper-server-buffered-audio`
successes. The API remained running without OOM or automatic restart; a
post-test sample showed 333.4 MiB RAM, not a peak-memory measurement.

The new project's API was stopped, the two probe records were read directly
from PostgreSQL, and only that project's PostgreSQL was restarted. Both records
were present unchanged before API startup. Health recovered after one expected
startup connection-reset retry; both authenticated robots then passed all socket
checks again in 577 ms. The inspected image matched the pinned portable digest.

Reports are retained on Ubuntu at
`/home/jake-dubin/openjibo-audio-ssh-results.2R4OVc` and copied locally to
`artifact-output/ubuntu-audio-preview-20261011/`. No secrets or databases were
copied. The new project remains available for further inspection; no old stack
was restarted or modified. This establishes portable fresh-install, recorded
audio, restart and device-row persistence acceptance—not a new backup/restore,
AVX2 published-starter, physical-robot or license-clearance result.

## Published AVX2 native Ubuntu acceptance — passed

The explicitly selected signed AVX2 ZIP was installed with the guarded launcher
on 2026-10-11, using the same Ubuntu laptop. All eight local logical processors
passed the feature check. Docker selected `unix:///var/run/docker.sock` and the
host's Docker service had a running PID; the same-host confirmation was made
for this inspected machine, not a remote daemon.

Fresh directory:
`/home/jake-dubin/Downloads/openjibo-avx2-ssh-20261011T162232Z`.
Project: `openjibo-avx2-ssh-20261011162232`, loopback port 8088.
Signed preparation, preflight and health passed. Azure Speech was disabled.
Both authenticated robots passed all three socket types in 595 ms.

Ten sequential cloud-version acoustic turns all passed: 2632 ms first turn,
then 987–1659 ms warm turns (median 1094 ms). Two simultaneous clients passed
five turns each in 957–1383 ms. A second, separately hash-checked recording
matched `quick brown fox lazy dog time` in all three transcription turns,
548, 590 and 587 ms. Replies were `LISTEN`, `EOS`, `SKILL_ACTION` for cloud-version
and `LISTEN`, `EOS` for transcription. Overlap logs confirmed ten local Whisper
successes. No OOM or automatic restart occurred; post-test RAM was 335.1 MiB,
not a peak or a capacity certification.

The initial overlap invocation used IDs lacking the required
`speech-acceptance-` prefix and was rejected before audio was sent. It is retained
as a failed probe invocation, not a runtime failure. Corrected IDs passed.
Only the new API was stopped and its PostgreSQL restarted. Both device rows
survived while the API was stopped; health recovered after one startup retry
and all sockets passed again in 549 ms. The image ID matched the AVX2 pin.

Results: `/home/jake-dubin/openjibo-avx2-ssh-results.XeXn9A`, copied to
`artifact-output/ubuntu-avx2-preview-20261011/`. Existing stacks/backups were
untouched. Neither profile's published acoustic acceptance certifies physical
robot input/playback, additional speakers/noise, backup/restore, or licensing.

## Candidate-specific owner-review packet

The reviewer now accepts an explicitly selected `--release-version
runtime-preview-38139481435` policy with these exact new image/source/profile
pins and the audio FFmpeg label. Legacy defaults and digest policies remain
unchanged; wrong-release and wrong-profile inputs are rejected.

Both inventories have 210 records and 179 without a complete scanner SPDX
expression. This is an evidence-completeness finding, not a count of prohibited
components. License-text trees were copied read-only from the exact tested
containers; no settings, databases, secrets or recordings were copied.
Bounded notice packaging retained 108 notice files, 28 unfollowed links and
162 grouped review entries per profile. No links were extracted/followed and
no component was marked approved. FFmpeg's separate source/build/license evidence
is retained in the image and publisher artifacts, not inferred from a scanner
license string.

The bundles are at
`artifact-output/audio-candidate-notices-20261011/portable/notices-review.zip`
and `artifact-output/audio-candidate-notices-20261011/avx2/notices-review.zip`.
Each contains `dependency-review.json` and a source/digest-bound `manifest.json`.
Remote raw inputs remain at
`/home/jake-dubin/openjibo-candidate-notices-20261011T162811Z`.

Owner review is required before these images become the recommended default or
stable release. Open technical evidence still includes exact-candidate NuGet
notice/content linkage, native source/build completeness and model conversion/
terms linkage. Do not use the old images' supplemental evidence as approval for
the new digests, or treat owner approval as filling those technical gaps.
