# Official robot builds and OTA delivery

Status: planned, 2026-09-26. This is a separate release product from server
containers. This plan enables no update offers, publishes no firmware and
authorizes no robot writes.

## Goal and existing evidence

Build official Open Jibo robot OS and software releases and deliver them through
an OTA service compatible with the robot's existing update process. Reuse that
process where verified; a generic download server is not an OTA implementation.

[Robot internals](jibo-internals.md) identifies `Update_20160301` metadata
operations (`ListUpdates`, `ListUpdatesFrom`, `GetUpdateFrom`), rootfs A/B slots
and `S72jibo-apply-update`. The [development plan](development-plan.md)
distinguishes hosted metadata from robot-local scheduler/update-menu state.
Existing handlers and proof endpoints do not prove signed, bootable updates or
working recovery. Confirm these findings for each supported OOBE, v1 and v2 build.

The [initial contract checkpoint](robot-ota-contract-checkpoint.md) records the
current metadata scaffolding and its dependency-preservation/public-write
boundary repairs. It is not a working OTA deployment or a release offer.

The subsequent [updater 1.3.10 source inspection](robot-ota-stock-1.3.10-evidence.md)
provides version-scoped package/download/apply evidence from an owner-supplied
image. In particular, its A/B implementation does not retain the previous root
through final service verification. Installation/recovery remains unproven.

The [scheduler and trust follow-up](robot-ota-scheduler-and-trust-evidence.md)
traces signed metadata requests and the handoff to native system manager;
native dependency ordering and effective runtime TLS configuration remain open.

## Architecture and ownership

The neutral runtime exposes the compatible robot-facing update API, backed by
a release catalog and immutable artifact storage. Build tooling produces robot
packages; isolated signing and promotion controls approve releases. The public
API server must neither build releases nor hold release-signing private keys.

JiboAutoMod handles owner-approved initial conversion, endpoint/trust setup and
rescue where stock OTA cannot safely bootstrap the robot. Routine updates then
use the verified OTA path. Version robot OS, services/skills, server runtime and
AutoMod independently with an explicit compatibility matrix. OpenJiboCloud can
display status but robot updates must not require its paid membership or identity
provider. Do not duplicate the robot protocol in the commercial application.

## Delivery milestones

1. **Trace the stock contract.** Inspect available source/dumps and collect
   controlled, redacted request traces: subsystem/from-version/filter rules,
   metadata, authorization, download/range/resume behavior, package format,
   digest/signature checks, trust roots, apply/reboot triggers and status reports.
   Replay scheduler/UI transitions too: absent/rejected updates must not cause
   phantom update or backup prompts. Create fixtures before offering packages.
2. **Establish reproducible build inputs.** Inventory source, toolchains,
   architecture/ABI, boot components, partitions and licenses. Separate OS images
   from services/skills packages. Identify what we can build/distribute versus
   owner-supplied proprietary dependencies; do not assume complete OS source,
   redistribution rights for stock dumps or access to Jibo, Inc. signing keys.
   Pin inputs and retain provenance, dependency/license information and SBOMs.
3. **Validate packages offline.** Record immutable version/digest/size, supported
   hardware, component dependencies, minimum source version, migration/storage
   requirements and recovery constraints. Check archive paths, permissions,
   partition bounds and integrity without flashing. Reject unsupported devices
   and unknown formats. Define protected signing, key rotation/revocation and
   anti-replay/downgrade policy. If a new trust anchor is necessary, bootstrap it
   explicitly and reversibly through AutoMod; do not disable stock verification
   as an undocumented shortcut.
4. **Implement a dormant OTA server.** Serve stock-compatible metadata and
   immutable downloads with tested resume behavior. Separate release admin from
   robot polling; authenticate promotion and control eligibility by device,
   subsystem and version. Default to no update; reject withdrawn/incompatible
   releases. Test signatures, metadata freshness and mirror verification—a
   checksum alone is not publisher identity. Support verified local delivery
   without turning self-hosting into an unsigned update path.
5. **Prove installation and recovery on lab hardware.** Confirm the actual boot
   selection/A/B recovery protocol before relying on it. Test corrupt or
   wrong-device payloads, invalid signatures, low disk space, interrupted
   downloads, power loss during write phases, failed first boot and network loss
   after reboot. Verify boot-health/commit semantics and an independent rescue
   path. Preserve calibration, credentials, personal data and memories; updates
   must not implicitly factory-reset the robot. VM tests cannot certify physical
   flashing or power-loss recovery.
6. **Promote through controlled rings.** Lab, explicitly opted-in canary, then
   broader stable availability only with retained acceptance evidence. Provide
   owner-visible changes, consent/scheduling, progress, safe deferral and redacted
   diagnostics. Define measured rollout-stop thresholds and release withdrawal.
   Withdrawal stops new offers; it cannot undo writes already underway. Rehearse
   authorized recovery separately from ordinary anti-downgrade enforcement.

## Distribution boundaries

- Isolated owners can stage verified packages locally, including offline import
  with documented freshness limits. A managed Cloud account is not required.
- Hybrid/managed hosts may cache or relay official artifacts. Trusted-server
  admission does not confer release-signing authority.
- Mirrors preserve signed artifact identity and trust; fallback must not silently
  replace payloads, versions or trust roots.
- Publish robot builds separately from server containers on the neutral site,
  with exact supported starting firmware/hardware, known issues, installation
  path and recovery prerequisites.

## Exit gate and delivery order

An official robot release requires build/provenance evidence AND real-device
OTA/recovery results for its supported matrix. A metadata response, successful
download, synthetic version change or one successful boot is insufficient.
Retain CI/package tests, lab traces and recovery results with the release.

This lane complements the [official distribution plan](official-distribution-plan.md)
and AutoMod. Contract/source/trust discovery can proceed while the cloud preview
progresses. Hardware flashing, signing authority, proprietary redistribution,
public update offers and fleet rollout require separate explicit approval.
No live robot test is requested for this planning change.
