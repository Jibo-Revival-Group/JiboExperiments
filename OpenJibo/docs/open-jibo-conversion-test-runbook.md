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

