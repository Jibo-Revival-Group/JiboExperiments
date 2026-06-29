# Open Jibo Conversion Test Runbook

Use this checklist for the next robot session after the Linux-first conversion helper update.

## Goal

Prove the staged conversion flow on the real robot root without breaking the known-good `api` baseline.

## Before You Start

- confirm you have the latest `main`
- have the real robot root or mounted capture tree ready
- have the latest robot logs and capture folder available
- do not skip the audit step

## Test Order

### 1. Audit

Run the strict audit helper first.

Expected result:

- the robot root is found
- `credentials.json` is present
- `credentials.json` still says `region: api`
- Jetstream config is present
- OOBE config is present
- the audit report can be written to disk

### 2. Plan

Run the strict plan helper next.

Expected result:

- the plan reports `CanApply: true`
- the proposed changes are limited to staged conversion state
- the rollback plan is present
- the target mode is `open-jibo`

### 3. Apply

Run the strict apply helper after the plan passes.

Expected result:

- Jetstream gets an added `open-jibo` region entry
- OOBE config gets the pending conversion marker
- `var/jibo/identity/openjibo-conversion.json` is written
- backup files are written under a sibling `backups/` directory
- `/var/jibo/credentials.json` still remains on `api`

### 4. Verify

After apply, verify the written files directly.

Check:

- backup copies exist
- the staged Open Jibo marker exists
- Jetstream still parses as JSON
- OOBE config still parses as JSON
- the robot root does not show unexpected writes outside the conversion files

### 5. Record

Capture the following for the session record:

- audit JSON
- plan JSON
- apply JSON
- robot logs
- any changed config files

## Expected Pass Criteria

- audit passes
- plan passes
- apply passes
- backups are created
- `credentials.json` stays on `api`
- the conversion state is staged cleanly

## Failure Handling

If any step fails:

- stop before making more writes
- save the audit/plan/apply outputs
- save the robot logs and capture folder
- note whether the failure happened during audit, plan, or apply
- do not retry the apply step until the failure is understood


## Demo Success Clip Path

The video-ready path should show one continuous operator story rather than a vague “success clip.” Record these checkpoints in order:

1. **Baseline proof**: show the strict audit output with `credentials.json` still on `api` and the robot root recognized.
2. **Conversion intent**: show the plan output with `CanApply: true`, target mode `open-jibo`, and rollback entries.
3. **Safe conversion write**: run apply, then show that `credentials.json` remains on `api` while the staged Open Jibo marker and backups were created.
4. **Cloud connection**: start the local or managed Open Jibo cloud, connect the converted robot/harness to the robot-facing API host, and capture at least one websocket turn.
5. **Loop and member proof**: open the portal dashboard or API response and show the loop, robot, registered device, and loop member relationships.
6. **Recognition observation proof**: record one recognition observation from a known source. Until live robot metadata is mapped, seed this with the demo/smoke source and label it as a manual/demo observation rather than live face/voice recognition.
7. **Persistence proof**: restart the cloud against the same state snapshot path or PostgreSQL database, reopen the portal identity graph/evidence bundle, and show the recognition evidence signal still present.
8. **Retained artifact proof**: download or verify the identity graph evidence bundle so the clip ends with an offline artifact that survives outside the running cloud.

### Concrete Questions For The Next Robot Session

Please capture or confirm these specific gaps; they are the items that decide whether the demo can be fully live instead of partly smoke-seeded:

- During a recognized-person interaction, does any websocket `CLIENT_ASR`, `CLIENT_NLU`, `TRIGGER`, or adjacent message include a stable person/member/speaker identifier, recognized name, enrollment id, confidence, or score?
- If websocket messages only include `data.text`, is there a robot-local log, POST body, or filesystem state update that records the face/voice match immediately before or after the ASR turn?
- Which enrolled member should be used in the first public demo, and what friendly name should appear in the portal/evidence bundle?
- Should the first video use local self-hosted Docker Compose, the managed Azure endpoint, or both? The checklist is the same, but the restart/persistence proof differs.
- For the conversion clip, should the robot remain staged on `api` credentials until a later reboot/OOBE step, or should we show the host override/DNS path that makes the staged conversion connect to Open Jibo immediately?

### Recognition Capture Inspection

Use the recognition-candidate scanner before and after the next live session:

```bash
scripts/cloud/inspect-websocket-recognition-candidates.sh captures/websocket
```

A useful capture should expose candidate fields such as `person`, `speaker`, `face`, `voice`, `recognition`, `enrollment`, `confidence`, or `score` close to a `CLIENT_ASR`/`CLIENT_NLU` turn. If the scanner reports only transcript text, keep the demo observation smoke-seeded and treat live recognition wiring as blocked on a richer capture source.
