# Release `1.0.20` Plan

## Purpose

This release carries `1.0.19` forward into a cleaner delivery phase.

The job for `1.0.20` is to tighten the update and backup story, prove the remaining regression gaps from the latest live runs, and keep the personality/presence ladder moving without letting the backlog blur together.

## Snapshot

- Kickoff date: `2026-06-10`
- Cloud version source of truth: [OpenJiboCloudBuildInfo.cs](../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Application/Services/OpenJiboCloudBuildInfo.cs)
- Active release constant: `1.0.20`

## Scope

### 1. Update, Backup, And Restore Proof

- finish the update path investigation with the phantom-update false positive resolved or explicitly characterized
- keep the backup prompt and update menu state aligned with the robot-local behavior we observed in stock Jibo
- prove restore as a persisted-state rehydration path, not as a new hosted API shape
- keep the cloud compatibility bridge only where the updater helper still expects it

### 2. Regression Carryover From The Latest Runs

- grocery list follow-up/listening needs to stay open long enough to accept a real add-item phrase
- bare `twerk` needs to be separated from greeting-like fallback behavior and from the polite `can you twerk` variant
- keep `sleep` and motion parity under review so the robot does not drift into an idle-looking state when the original skill should stay asleep
- keep `turn around` and other motion/personality commands source-backed instead of relying on accidental matches

### 3. Personality And Presence Continuation

- continue the source-backed favorites, identity, and presence slices from `1.0.19`
- keep the question-vs-command split sharp so polite variants do not become the only route that works
- preserve the stronger authored reply cadence where Pegasus gives it to us

### 4. STT And Turn Reliability

- keep the low-signal screen and short-utterance handling tuned against the latest regression evidence
- treat the bare `twerk` miss as an STT/parsing proof item until the capture says otherwise
- keep the shared yes/no and constrained follow-up flows stable while the new regression items are retested

### 5. Platform Conversion And Deployment Foundation

- convert the robot into Open Jibo with explicit mode targets instead of an implicit one-off patch
- define the mode set we actually want to support:
  - `open-jibo`
  - `open-jibo-ai`
  - `open-jibo-self-hosted`
  - `open-jibo-developer`
- install an Open Jibo onboarding/config skill that can:
  - enable the Open Jibo mode
  - return Jibo to stock mode when disabled
  - stay visible in the menu after toggling so owners can re-enable it later
  - trigger first-boot/OOBE setup behavior when the robot is converted
- prove the conversion path against the real device variants we care about:
  - newer OOBE devices
  - older stock devices such as the `1.9.2` baseline
  - alternate distributions such as NTT or MIT-special variants where available
- design the hardware-assisted "easy button" path with the Jibo Revival Group so RCM/file-system setup can be repeated safely
- stand up the cloud deployment path with CI/CD into the Azure environment
- keep the hosted software able to run as:
  - self-hosted with no external cloud dependency
  - hybrid cloud with shared identity/storage
  - a managed cloud service
- abstract storage so different server implementations can satisfy the same contract without the rest of the system caring
- define the network trust and consensus story for cloud peers, including bad-actor handling and revocation semantics
- plan the openjibo.com web UI and paid-access surface alongside the free/self-hosted options
- keep Loop advancement, family/friend recognition, and multiple Jibo support in the same platform track so the network and identity model stays future-proof

## Working Order

The suggested order for early `1.0.20` execution is:

1. update / backup / restore proof
2. grocery list follow-up and add-item reliability
3. motion and personality command parity, including `twerk` and `go to sleep`
4. STT cleanup for the remaining short-utterance misses
5. continue the broader personality and presence queue once the regression gaps are understood
6. split the platform-conversion track into named backlog items and work the topmost one at a time
7. keep the cloud deployment, self-hosting, storage, and multi-Jibo architecture tracks in discovery until they are ready for their own proof slices

## Closeout Note

`1.0.19` is now treated as closed history. This plan is the active queue for the next pass, and the backlog should point here for current work ordering.
