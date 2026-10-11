# Experimental audio FFmpeg preview — 2026-10-11

This is a new experimental prerelease, not a stable channel or an in-place
upgrade. Existing releases, the site's tested-preview recommendation, and
deployments remain unchanged. Ubuntu fresh-install acceptance is outstanding.
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
The guarded preparer/launcher support only this release's portable ZIP; AVX2
publication is verified but is not silently selected by host CPU detection.

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
Next gates: Ubuntu acceptance, fresh AVX2 starter integration/acceptance, remaining
distribution notice/source review, and then separately approved release promotion.
