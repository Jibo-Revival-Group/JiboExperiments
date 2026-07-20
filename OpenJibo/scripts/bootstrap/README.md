# Bootstrap Scripts

These scripts support the first OpenJibo recovery path:

- discover which hosts the robot is trying to reach
- generate DNS override records for a controlled environment
- verify that the robot-facing domains resolve and answer as expected
- audit a mounted robot filesystem for the conversion-relevant config files before any write helper runs
- orchestrate the Linux conversion flow with audit, plan, and gated apply helpers
- inspect restored/OOBE robot images for `@be/first-contact`, name-learning, pronoun, and identity-recognition hooks before porting the Open Jibo awakening flow
- produce a video-ready conversion evidence bundle that ties the rollback-safe harness to cloud loop/member enrollment and recognition smoke evidence
- plan the OOBE static-DNS OTA bootstrap lane without checking historical certificate material into the repository

Windows PowerShell wrappers remain available for local staging and analysis, but the robot-facing conversion path is shell-based.

Audit and plan are read-only. Apply and rollback are intentionally narrow, hash-gated, and backup-first.

## Harness Scaffold

Use the harness scaffold when you want to test conversion against a disposable copy of the extracted robot image.

The intended flow is:

1. scaffold a writable overlay from `jibo_full_emmc`
2. run audit/plan/apply against the overlay
3. reset the overlay from the source snapshot when you want a clean run

On a physical robot, run `jibo-mount --rw` before any helper that writes robot partitions. The helpers are safe to inspect against a mounted copy, but the real device must be remounted writable first.

The apply helper also patches `/usr/local/lib/libJiboServerService.so`. It accepts only an explicitly supported stock or already-patched hash pair: v1 (`ae82f1dd7407f8d74b287917cb9a8b24` -> `e55e18e92aa6365569f13214e0118745`) or v2/lastdance (`a863a238d6f2531446d0eb0d1d358c19` -> `688ec2940ed1fc7d1b86d2fd29bc6b30`). For either build it replaces exactly two equal-length `jibo.com` byte sequences with `jibo.pro`. The resulting native token host is `open-jibo.jibo.pro`; Azure must bind that hostname directly to the API service.

Physical-robot apply command:

```sh
jibo-mount --rw
sh ./invoke-openjibo-conversion.sh \
  --robot-root / \
  --output-directory /var/jibo/openjibo-conversion \
  --apply \
  --strict
```

Keep physical-device output and backups under `/var/jibo`, not `/tmp`, so rollback evidence survives reboot.

The scaffold normalizes the extracted image layout into robot-root paths such as `var/jibo`, `usr/local/etc`, `skills`, `etc`, and `boot` so the conversion helpers can run against the overlay without special-case path handling.

Entry points:

- `Scaffold-OpenJiboHarness.ps1`
- `Run-OpenJiboHarness.ps1`
- `Rollback-OpenJiboConversion.ps1`
- `Roundtrip-OpenJiboHarness.ps1`
- `Validate-OpenJiboHarnessRoundTrip.ps1`
- `Demo-OpenJiboHarness.ps1`
- `Recommend-OpenJiboHarnessMode.ps1`
- `record-openjibo-conversion-demo.sh`
- `Build-LinuxFilesystemFromCopies.ps1`
- `Inspect-LinuxFilesystemDemo.ps1`
- `Run-OpenJiboFilesystemDemo.ps1`

Example:

```powershell
.\jibo-mount --rw
.\Scaffold-OpenJiboHarness.ps1 -SourceRoot C:\Users\JacobDubin\Downloads\jibo_full_emmc -OverlayRoot C:\Projects\JiboExperiments\artifacts\harness-overlay -Clean
.\Run-OpenJiboHarness.ps1 -SourceRoot C:\Users\JacobDubin\Downloads\jibo_full_emmc -OverlayRoot C:\Projects\JiboExperiments\artifacts\harness-overlay -TargetMode open-jibo -Apply -Strict -Clean
.\Rollback-OpenJiboConversion.ps1 -RobotRoot C:\Projects\JiboExperiments\artifacts\harness-overlay -ApplyPath C:\Projects\JiboExperiments\artifacts\harness-overlay\run-output\invoke\conversion-apply.json -Strict
.\Roundtrip-OpenJiboHarness.ps1 -SourceRoot C:\Users\JacobDubin\Downloads\jibo_full_emmc -OverlayRoot C:\Projects\JiboExperiments\artifacts\harness-overlay -TargetMode open-jibo -Strict -Clean
.\Validate-OpenJiboHarnessRoundTrip.ps1 -OutputDirectory C:\Projects\JiboExperiments\artifacts\harness-overlay-output
.\Demo-OpenJiboHarness.ps1 -SourceRoot C:\Users\JacobDubin\Downloads\jibo_full_emmc -OverlayRoot C:\Projects\JiboExperiments\artifacts\demo-overlay -Strict -Clean
.\Recommend-OpenJiboHarnessMode.ps1 -Goal demo
.\Build-LinuxFilesystemFromCopies.ps1 -SourceRoot C:\Users\JacobDubin\Downloads\jibo_full_emmc -OutputRoot C:\Projects\JiboExperiments\artifacts\linux-fs-demo -Clean
.\Inspect-LinuxFilesystemDemo.ps1 -OutputRoot C:\Projects\JiboExperiments\artifacts\linux-fs-demo5
.\Run-OpenJiboFilesystemDemo.ps1 -DemoRoot C:\Projects\JiboExperiments\artifacts\linux-fs-demo5\demo-root -Strict
```


## OOBE OTA bootstrap planning

Use `plan-oobe-ota-bootstrap.sh` before attempting the static-DNS OOBE OTA path described in the release plan. The helper is intentionally planning-only: it records DNS/NTP/HTTPS/OTA metadata expectations, names the request traces that must be captured, and fails closed in `--strict` mode while certificate provenance or trace evidence is missing.

Example:

```bash
./plan-oobe-ota-bootstrap.sh \
  --api-hostname api.openjibo.com \
  --trace-bundle /path/to/oobe-ota-traces \
  --certificate-mode external \
  --output-path /tmp/openjibo-oobe-ota-plan.json
```

Keep `--certificate-mode external` for any owner-facing or repository-backed flow. `lab-only` is only a blocker-marked planning state for private research environments and must not be used to commit historical `*.jibo.com` certificate or private-key material.

## First-contact / identity inspection

Use `inspect-openjibo-first-contact.sh` against a mounted or scaffolded robot root before scripting the conversion video. It does not modify the image; it reports candidate `@be/first-contact`/OOBE skill roots plus targeted matches for `name_learning`, `pronoun_`, `WhoAmI`, and recognition-related payloads. Pair the output with `scripts/cloud/inspect-websocket-recognition-candidates.py` from a live capture before labeling a demo as stable face/person recognition.

Example:

```bash
./inspect-openjibo-first-contact.sh --robot-root /path/to/harness-overlay --output-path /tmp/openjibo-first-contact.json
```

## Conversion video evidence bundle

Use `record-openjibo-conversion-demo.sh` when preparing a filmed Jibo-to-Open-Jibo conversion. The script stays on a disposable overlay, runs the rollback-validated harness, inspects first-contact and recognition hooks, optionally runs the cloud smoke against `BASE_URL`, and writes a single `conversion-video-manifest.json` plus `conversion-video-blockers.json` for the operator to review before touching a physical robot.

Example:

```bash
./record-openjibo-conversion-demo.sh \
  --source-root /path/to/jibo_full_emmc \
  --overlay-root /tmp/openjibo-demo-overlay \
  --target-mode open-jibo \
  --base-url https://api.openjibo.com \
  --api-hostname api.openjibo.com \
  --hub-hostname neohub.openjibo.com \
  --strict \
  --clean \
  --output-directory /tmp/openjibo-conversion-video
```

If the cloud is not running yet, add `--skip-cloud-smoke`; the manifest will mark cloud evidence as skipped so the video cannot accidentally present an unverified connection. For managed or self-hosted rehearsals, pass `--api-hostname` and `--hub-hostname` so the conversion harness stages the same robot-facing hostnames that the smoke run proves with `--base-url`.
