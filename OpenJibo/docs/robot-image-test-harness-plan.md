# Robot Image Test Harness Plan

## Purpose

Use the extracted robot images to build a disposable, filesystem-backed harness that improves conversion testing, rollback testing, and config-diff testing without requiring the physical robot for every iteration.

This is not a full VM plan. The first goal is a repeatable test root that mirrors the robot's writable state and service config layout closely enough for the Open Jibo scripts.

## Recommendation

Build the harness first. Defer a full VM until the harness proves useful and we can justify the extra fidelity work.

Reason:

- the current conversion work is mostly config/state oriented
- the live failures still need real hardware timing
- the EMMC dump is already rich enough for state and file-layout regression
- the flash bundle is better as a recovery/reference source than a boot target

For the current phase, treat the copied-volume harness as the demo filesystem. A VM can wait until we need boot-time fidelity or kernel/runtime behavior that the copied volume cannot expose.

## What To Use

### Primary baseline

- `C:\Users\JacobDubin\Downloads\jibo_full_emmc`

Use this as the source of truth for:

- `/var/jibo` state
- `/usr/local/etc` service configs
- `/opt/jibo` skill and bootstrap behavior
- boot layout and init wiring

### Secondary reference

- `C:\Users\JacobDubin\Downloads\jibo-pvt-flash-build-dev-531`

Use this as the source of truth for:

- flash layout
- bootloader artifacts
- recovery/reset packaging
- partition names and image boundaries

## Harness Shape

Build a disposable test tree with three layers:

1. Read-only source snapshot
   - keep the extracted images untouched
   - treat them as the known-good baseline
2. Writable overlay
   - copy only the files the scripts might change
   - keep backups and outputs separate from the baseline
3. Script target root
   - point audit/plan/apply helpers at the overlay root
   - make the harness behave like a robot filesystem from the script's point of view

## What To Model First

Start with the files that the conversion scripts already care about:

- `/var/jibo/credentials.json`
- `/var/jibo/identity.json`
- `/var/jibo/mode.json`
- `/var/jibo/keys/`
- `/usr/local/etc/jibo-jetstream-service.json`
- `/usr/local/etc/jibo-system-manager.json`
- `/usr/local/etc/jibo-ssm/*.json`
- `/opt/jibo/Jibo/Skills/oobe-config/config.json`
- `/opt/jibo/Jibo/Skills/@be/be/config/*.json`

These are the best early-value targets because they let us validate:

- region and mode routing
- first-boot conversion markers
- backup and rollback metadata
- clone/identity warnings

## What To Test In The Harness

### 1. Audit fidelity

Goal:

- ensure audit sees the same files the robot uses
- ensure missing-file and wrong-path conditions fail closed

Checks:

- `credentials.json` region is read correctly
- Jetstream region entries are read correctly
- OOBE config is read correctly
- required backup candidates are reported

### 2. Plan fidelity

Goal:

- ensure plan output matches the real files and not invented paths

Checks:

- proposed backup paths are correct
- staged conversion marker is planned
- rollback plan is present
- `credentials.json` is still treated as the baseline until onboarding completes

### 3. Apply fidelity

Goal:

- verify the apply helper writes only the staged conversion state
- verify backups are created where expected

Checks:

- Jetstream gets the `open-jibo` entry
- OOBE gets the pending conversion marker
- conversion metadata is written under `/var/jibo/identity`
- `credentials.json` remains on `api`
- backup copies land in the output tree

### 4. Rollback fidelity

Goal:

- verify the scripts can restore the baseline and preserve the robot-facing menu state

Checks:

- backup copies can be used to revert the changed files
- rollback leaves the source tree untouched
- the harness can be reset quickly for another run

### 5. Negative cases

Goal:

- prove the harness catches broken baselines before a robot run

Cases:

- missing `credentials.json`
- missing Jetstream config
- missing OOBE config
- wrong `region` value
- malformed JSON
- no writable overlay for backups

## What Not To Model Yet

Do not spend the first pass on:

- full audio pipeline timing
- websocket turn boundaries
- real STT model behavior
- body or camera hardware
- the full Tegra boot process

Those belong on the physical robot or in a later high-fidelity emulation effort.

## Suggested Build Order

1. Create a disposable overlay tree from the EMMC baseline.
2. Wire the existing audit helper to point at the overlay tree.
3. Wire the plan helper to emit a diff against the overlay tree.
4. Wire the apply helper to mutate only the overlay tree.
5. Add rollback using the backups created during apply.
6. Add a minimal test runner that can reset the overlay between runs.
7. Use the harness for conversion, backup, and rollback regression.
8. Keep live robot sessions for STT, turn timing, and hardware-specific behavior.

## Immediate Implementation Checklist

When you are at the workstation, do this in order:

1. Create a working directory for the harness overlay.
2. Copy only the writable robot files from `jibo_full_emmc` into that overlay using normalized robot-root paths.
3. Keep the source image tree read-only and untouched.
4. Point the audit helper at the overlay and confirm it reports the expected `api` baseline.
5. Point the plan helper at the same overlay and confirm the proposed writes match the documented conversion state.
6. Point the apply helper at the overlay and confirm it writes only:
   - `usr/local/etc/jibo-jetstream-service.json`
   - `skills/jibo/Jibo/Skills/oobe-config/config.json`
   - `var/jibo/identity/openjibo-conversion.json`
7. Verify the helper writes backups into the apply output tree.
8. Add a one-command reset that deletes the overlay and recreates it from the source snapshot.
9. Add negative tests for missing or malformed `credentials.json`, Jetstream, and OOBE config.
10. Once the harness is stable, use it as the default target for conversion and rollback script changes.
11. Keep the real robot for websocket timing, STT, and EOS validation.

## Radio Skill Note

The iHeart behavior is localized in the robot skill, not the Open Jibo cloud.

Relevant files:

- `C:\Users\JacobDubin\Downloads\jibo_full_emmc\5.skills\jibo\Jibo\Skills\@be\be\node_modules\@be\radio\index.js`
- `C:\Users\JacobDubin\Downloads\jibo_full_emmc\5.skills\jibo\Jibo\Skills\@be\be\node_modules\@be\radio\mims\en-us\PresentingIHeart.mim`
- `C:\Users\JacobDubin\Downloads\jibo_full_emmc\5.skills\jibo\Jibo\Skills\@be\be\node_modules\@be\radio\mims\en-us\CurrentStation.mim`

Observed behavior:

- the skill calls `getCountry()` and then reduces the result to `us` or `ca`
- non-US NPR is explicitly blocked in the skill
- the iHeart presentation prompts are country-specific inside the skill package

Implication:

- this is not an easy Open Jibo Cloud fix
- the practical fix path is either a robot-skill update or replacing the skill with a cloud-commanded radio experience later
- if we need to test parity in the harness, we should model the country code and station menu state as robot-local inputs

## Success Criteria

The harness is worth keeping if it can reliably do all of these:

- run audit/plan/apply without touching the real robot
- preserve the `api` credential baseline
- stage Open Jibo conversion state cleanly
- create and use backups for rollback
- reset fast enough for repeated test cycles

If it cannot do those things, do not expand it into a VM project yet.

## Current Status

Implemented so far:

- disposable overlay scaffold from `jibo_full_emmc`
- normalized robot-root layout in the overlay
- combined scaffold/run entry points for audit and plan
- rollback helper that restores the staged overlay from apply backups

Next step:

- validate the staged conversion writes and rollback round-trip against the overlay-backed harness

## One-Command Round Trip

Use the round-trip wrapper when you want a single pass that scaffolds the overlay, applies the staged conversion, and then rolls it back.

PowerShell:

```powershell
.\Roundtrip-OpenJiboHarness.ps1 -SourceRoot C:\Users\JacobDubin\Downloads\jibo_full_emmc -OverlayRoot C:\Projects\JiboExperiments\artifacts\harness-overlay -TargetMode open-jibo -Strict -Clean
```

This runs the same overlay-backed flow used during validation and leaves a `harness-roundtrip.json` summary in the chosen output directory.

Follow it with `Validate-OpenJiboHarnessRoundTrip.ps1` to confirm the summary and artifact files exist before you trust the output for a regression run.

## Demo Filesystem

Use `Demo-OpenJiboHarness.ps1` when you want the copied-volume demo filesystem path in one command. That is the preferred demo target until we have a concrete reason to introduce a VM.

## Mode Recommendation

If you are unsure what to run, use `Recommend-OpenJiboHarnessMode.ps1`.

Current defaults:

- `demo` means copied-volume demo filesystem
- `roundtrip` means scaffold, apply, rollback, and validate
- `vm` means wait until boot/runtime fidelity is required

## Linux Filesystem Composer

Use `Build-LinuxFilesystemFromCopies.ps1` when you want an actual Linux filesystem root assembled from the extracted partitions.

It currently does this:

- copies `0.rootfsA` to the demo root as the primary Linux filesystem
- preserves `1.rootfsB` as the secondary slot reference
- overlays `3.services` onto `usr/local`
- overlays `4.var` onto `var`
- overlays `5.skills` onto `opt/jibo/Jibo/Skills`
- exposes `demo-root` as a single-folder view of the composed Linux filesystem

That is the right target if you want to inspect the demo filesystem as a mounted Linux tree instead of just a normalized overlay harness. Point the harness at `demo-root` when you want one path.

The builder also writes `filesystem-progress.json` after each stage so a large copy can be inspected even if the run is interrupted.

## Demo Inspector

Use `Inspect-LinuxFilesystemDemo.ps1` after a build to confirm:

- `filesystem-manifest.json` exists
- `filesystem-progress.json` exists
- `demo-root` resolves to the composed rootfs
- the current stage and copy count match the last build pass
