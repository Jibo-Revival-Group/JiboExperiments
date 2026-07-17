# Device Bootstrap Path

## Supported First Path

The first supported OpenJibo recovery path is:

```text
QR Wi-Fi -> inject OpenJibo region config -> set robot region ->
RCM/device patch -> Azure-hosted OpenJibo cloud at api.openjibo.com,
open-jibo-socket.openjibo.com, and neohub.openjibo.com
```

This is the path we can document, repeat, and improve.

The `1.0.20` conversion planning track builds on this bootstrap path in [open-jibo-mode-conversion-plan.md](open-jibo-mode-conversion-plan.md).
The next-session checklist lives in [open-jibo-conversion-test-runbook.md](open-jibo-conversion-test-runbook.md).
The extracted-image file map lives in [robot-image-script-map.md](robot-image-script-map.md).

For the managed production path, keep the robot-facing contract on `api.openjibo.com`, `open-jibo-socket.openjibo.com`, and `neohub.openjibo.com`. Treat `openjibo.com` as the public app/docs surface, not the robot API target.

## Why This Path Comes First

- it matches the region-driven configuration seams observed on the robot
- it keeps the hosted cloud work grounded in real device traffic
- it avoids blocking the entire revival on OTA before cloud compatibility exists

## Parallel OOBE OTA Bootstrap Candidate

The Jibo Revival Group now has a promising OOBE-first OTA path under test. It does not replace the supported path above yet, but it changes the planning priority because it may let a wiped or OOBE-started robot reach Open Jibo with app plus QR setup instead of ShofEL/firewall/SSH first.

Candidate sequence:

1. Generate the OOBE setup QR with Wi-Fi plus static network fields, placing the bootstrap appliance IP in `dns1`.
2. Use appliance DNS to resolve stock Jibo hosts, especially `api.jibo.com`, to the LAN bootstrap server.
3. Serve NTP responses with a 2017-era time so the robot accepts the historical `*.jibo.com` GoDaddy certificate chain.
4. Emulate enough OOBE and update endpoints for the robot to complete setup and ask for subsystem updates.
5. Serve signed-by-manifest OTA assets with correct SHA-1 and Content-Length values.
6. Apply an Open Jibo conversion/update payload that installs durable trust and region targeting before any temporary bootstrap state is lost.
7. Reboot into Open Jibo and verify cloud connectivity against the managed or selected self-hosted target.

Planning consequence:

- keep ShofEL plus firewall/SSH as the universal recovery, backup, non-OOBE, and rollback path
- promote OOBE OTA bootstrap to the lowest-friction candidate for fresh/wiped robots if Maaarcna's traces prove repeatable
- design the "easy button" appliance so it can eventually offer both modes: OTA-over-OOBE for owner-friendly setup and ShofEL/SSH for rescue or non-wipe conversion
- do not store historical certificate private keys or stock OTA packages in this repository until legal/provenance and security handling are resolved

## Bootstrap Checklist

1. Connect the robot to a controlled Wi-Fi network.
2. Remount the robot partitions read-write with `jibo-mount --rw`.
3. Add an OpenJibo region entry to `/usr/local/etc/jibo-jetstream-service.json` that points `entrypoint_hostname` to `api.openjibo.com` and `hub_hostname` to `neohub.openjibo.com`.
4. Update `/usr/local/etc/jibo-server-service.json` so `NotificationSubsystem.serverURLSuffix` points at the Open Jibo socket suffix and the robot resolves `open-jibo-socket.openjibo.com` without further code changes.
5. Set the robot `region` field in `/var/jibo/credentials.json` to the OpenJibo region after audit, plan, and backup are complete.
6. Gain RCM/device access for targeted TLS or host validation changes.
7. Verify robot startup, token flow, socket flow, and first-turn behavior against the Azure-hosted Open Jibo API.

## Easy Button Flow

The "easy button" recovery appliance should follow the same staged sequence every time:

1. Enter RCM through the robot's button sequence and USB recovery port.
2. Use the exploit path to open the patch window.
3. Patch only enough to make SSH reachable over the USB/LAN path.
4. Reboot out of RCM and confirm SSH access.
5. Run `jibo-mount --rw` before any helper writes robot partitions.
6. Snapshot the robot before any conversion writes.
7. Run the predictive conversion audit and plan helpers before any write step.
8. Install the Open Jibo skill and conversion assets.
9. Apply the staged region/mode entries and the first-boot/OOBE pending state while keeping the proven live credentials region intact until onboarding finishes.
10. Reboot into the Open Jibo mode.
11. Let the skill complete the onboarding and conversion.
12. If onboarding is abandoned or a write step fails, restore the snapshot and return to stock behavior.

## Region-Driven Configuration

Current findings suggest the preferred OpenJibo bootstrap path is to inject a new region configuration rather than override every hostname manually.

Confirmed paths:

- `/usr/local/etc/jibo-jetstream-service.json`
  Add an OpenJibo region definition that points Jibo to our cloud. The default managed target is `api.openjibo.com` for the robot-facing API entrypoint. The hub hostname should be `neohub.openjibo.com` for the managed path.
- `/usr/local/etc/jibo-server-service.json`
  Set `NotificationSubsystem.serverURLSuffix` so the converted robot resolves `open-jibo-socket.openjibo.com` for notification traffic without needing a robot software change.
- `/var/jibo/credentials.json`
  Set the robot `region` field to the injected OpenJibo region.

Observed additional region-related files worth documenting and auditing:

- `/etc/jibo-ssm/*.json`
- `/skills/jibo/Jibo/Skills/@be/be/node_modules/language-subtag-registry/data/json/registry.json`
- `/skills/jibo/Jibo/Skills/oobe-config/config.json`

These should be treated as configuration discovery targets, not yet as the authoritative complete list.

## Required Hosts

The currently relevant public hostnames for the OpenJibo cloud path are:

- `api.openjibo.com`: canonical managed Open Jibo robot-facing API entrypoint
- `open-jibo-socket.openjibo.com`: managed notification socket hostname derived from the staged robot suffix
- `neohub.openjibo.com`: managed listen/proactive hostname
- `api.jibo.com`, `api-socket.jibo.com`, and `neo-hub.jibo.com`: historical stock hostnames that the conversion path should preserve as rollback evidence, not use as the managed Open Jibo target

## Scripted Helpers

Bootstrap helper scripts live in [scripts/bootstrap](/OpenJibo/scripts/bootstrap):

- `Audit-OpenJiboConversion.ps1`
- `Plan-OpenJiboConversion.ps1`
- `audit-openjibo-conversion.sh`
- `plan-openjibo-conversion.sh`
- `apply-openjibo-conversion.sh`
- `invoke-openjibo-conversion.sh`
- `Discover-JiboHosts.ps1`
- `Generate-JiboDnsOverrides.ps1`
- `Test-OpenJiboRouting.ps1`

These are intentionally conservative helpers for discovery and verification, not destructive patch tools. The Linux shell helpers are the canonical robot-facing conversion path; the PowerShell helpers remain useful for local staging and analysis.

Example managed conversion planning command:

```bash
./scripts/bootstrap/invoke-openjibo-conversion.sh \
  --robot-root /mnt/jibo-root \
  --target-mode open-jibo \
  --api-hostname api.openjibo.com \
  --hub-hostname neohub.openjibo.com \
  --strict
```

For the currently verified physical-device local setup, including the tested
`.NET` server command, region caveat, persistent init script, TLS patch, and
post-reboot verification, see [local-jibo-device-runbook.md](local-jibo-device-runbook.md).

## TLS And Runtime Patching

Patching requirements will vary by device version and by where certificate validation is enforced.

Near-term guidance:

- record each patch location by software version
- prefer small, repeatable changes over ad hoc edits
- keep a versioned host inventory and patch checklist
- keep a versioned region-config checklist
- do not describe OTA as the primary bootstrap method until the hosted cloud is stable

## Smoke Test Goals

The first real-device smoke test should confirm:

- robot startup reaches the hosted cloud
- token issuance succeeds
- required sockets connect
- the robot can complete one simple turn
- update metadata calls do not break startup
