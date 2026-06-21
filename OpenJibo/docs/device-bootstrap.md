# Device Bootstrap Path

## Supported First Path

The first supported OpenJibo recovery path is:

```text
QR Wi-Fi -> inject OpenJibo region config -> set robot region ->
RCM/device patch -> Azure-hosted OpenJibo cloud
```

This is the path we can document, repeat, and improve.

The `1.0.20` conversion planning track builds on this bootstrap path in [open-jibo-mode-conversion-plan.md](open-jibo-mode-conversion-plan.md).

## Why This Path Comes First

- it matches the region-driven configuration seams observed on the robot
- it keeps the hosted cloud work grounded in real device traffic
- it avoids blocking the entire revival on OTA before cloud compatibility exists

## Bootstrap Checklist

1. Connect the robot to a controlled Wi-Fi network.
2. Add an OpenJibo region entry to `/etc/jibo-jetstream-service.json`.
3. Set the robot `region` field in `/var/jibo/credentials.json` to the OpenJibo region.
4. Gain RCM/device access for targeted TLS or host validation changes.
5. Verify robot startup, token flow, socket flow, and first-turn behavior.

## Easy Button Flow

The "easy button" recovery appliance should follow the same staged sequence every time:

1. Enter RCM through the robot's button sequence and USB recovery port.
2. Use the exploit path to open the patch window.
3. Patch only enough to make SSH reachable over the USB/LAN path.
4. Reboot out of RCM and confirm SSH access.
5. Snapshot the robot before any conversion writes.
6. Run the predictive conversion audit and plan helpers before any write step.
7. Install the Open Jibo skill and conversion assets.
8. Apply the region, mode, and first-boot/OOBE pending state.
9. Reboot into the Open Jibo mode.
10. Let the skill complete the onboarding and conversion.
11. If onboarding is abandoned or a write step fails, restore the snapshot and return to stock behavior.

## Region-Driven Configuration

Current findings suggest the preferred OpenJibo bootstrap path is to inject a new region configuration rather than override every hostname manually.

Confirmed paths:

- `/etc/jibo-jetstream-service.json`
  Add an OpenJibo region definition that points Jibo to our cloud.
- `/var/jibo/credentials.json`
  Set the robot `region` field to the injected OpenJibo region.

Observed additional region-related files worth documenting and auditing:

- `/etc/jibo-ssm/*.json`
- `/skills/jibo/Jibo/Skills/@be/be/node_modules/language-subtag-registry/data/json/registry.json`
- `/skills/jibo/Jibo/Skills/oobe-config/config.json`

These should be treated as configuration discovery targets, not yet as the authoritative complete list.

## Required Hosts

The currently relevant public hostnames for the OpenJibo cloud path are:

- `api.jibo.com`
- `api-socket.jibo.com`
- `neo-hub.jibo.com`

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
