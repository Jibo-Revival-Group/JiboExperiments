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
- the concrete restore contract is `Backup_20170222.Restore`: it rehydrates a prior backup snapshot, returns success with `rebootRequired = true`, and does not introduce a new top-level restore service
- keep the cloud compatibility bridge only where the updater helper still expects it, and keep it returning no content instead of a fabricated manifest when nothing is staged
- verify the update-related protocol shapes against the robot capture: `ListUpdates`, `ListUpdatesFrom`, `GetUpdateFrom`, `CreateUpdate`, and `RemoveUpdate`
- keep menu truth on robot-local backup/update status, not on the compatibility bridge
- prove the smallest live or replayable path that shows update, backup, and restore without a fabricated update announcement
- accept restore requests that carry the mapped backup `location.url` (or the location URL as a string) so stock callers can round-trip the `Backup_20170222.List` / `Create` response without manually extracting the `etag`
- treat the current false-positive as robot-side OTA KB state first, especially `updatesAvailable`, rather than a cloud `GetUpdateFrom` bug

### 2. Regression Carryover From The Latest Runs

- grocery list now carries an explicit follow-up listen context in the cloud path, so the remaining work is live/hardware verification rather than inventing a new capture flow
- keep the grocery alias on its dedicated listen/capture state so the robot stays active long enough to accept an item phrase
- bare `twerk` is source-backed in Pegasus/OpenJibo and now has a cloud wire regression, so the remaining issue is the robot-side STT landing on `hello` instead of `twerk`
- keep `sleep` and motion parity under review so the robot does not drift into an idle-looking state when the original skill should stay asleep; the legacy snapshot already has a real `GlobalCommand.SLEEP` path, the ASLEEP state is event-driven rather than timer-driven, wake is driven by `dayStarts`, `headTouch`, or `hjHeard`, and the legacy sleep behavior tree includes a sleeping-idle loop that we need to preserve in the parity path
- the Open Jibo cloud sleep replay path now has regression coverage for the legacy `@be/idle` redirect plus follow-up acknowledgment speech, so the remaining work is parity checking rather than contract discovery
- keep `turn around` / `spin around` / `twirl` source-backed instead of relying on accidental matches
- `turn around` is now reported as working on the robot, so the remaining command-gap work is the bare `twerk` short-turn and any other short-utterance mishears
- favorites, `show santa tracker`, and `can you sing` are already source-backed in the cloud path, so any remaining regression is live robot playback or launch-state handling rather than missing intent coverage
- Santa Tracker now emits a tracker presentation payload in the cloud path, but the live robot still needs verification for the fuller snow/santa animation and jingle-bell style audio

### 3. Personality And Presence Continuation

- continue the source-backed favorites, identity, and presence slices from `1.0.19`
- keep the question-vs-command split sharp so polite variants do not become the only route that works
- preserve the stronger authored reply cadence where Pegasus gives it to us

### 4. STT And Turn Reliability

- record the turn-boundary and EOS parity decision in [architecture/turn-boundary-eos-parity.md](architecture/turn-boundary-eos-parity.md) so the hard timeout remains a safety net rather than the normal close path
- make the decisive-turn branch match Pegasus more closely: if the current transcript already matches an intent or action, finalize the turn immediately and only keep listening when the response plan explicitly owns the follow-up
- keep the low-signal screen and short-utterance handling tuned against the latest regression evidence
- treat the bare `twerk` miss as an STT/parsing proof item until the capture says otherwise
- `turn around` is no longer part of the open STT cleanup because it passed on the robot
- keep the shared yes/no and constrained follow-up flows stable while the new regression items are retested

### 5. Platform Conversion And Deployment Foundation

Detailed planning starts in [open-jibo-mode-conversion-plan.md](open-jibo-mode-conversion-plan.md).
Cloud deployment planning starts in [cloud-deployment-topology-plan.md](cloud-deployment-topology-plan.md).
Storage trust planning starts in [storage-trust-consensus-plan.md](storage-trust-consensus-plan.md).

- convert the robot into Open Jibo with explicit mode targets instead of an implicit one-off patch
- make the conversion helper predictive and rollback-safe: audit first, refuse to write when the baseline or target state is unclear, and require a recorded rollback snapshot before any conversion write
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
  - preserve any on-robot persisted state and data such as holidays, Jibo birthdate, pictures and videos, person voice and face training and recognition, and favorites lists
- prove the conversion path against the real device variants we care about:
  - newer OOBE devices
  - older stock devices such as the `1.9.2` baseline
  - alternate distributions such as NTT or MIT-special variants where available
- design the hardware-assisted "easy button" path with the Jibo Revival Group so RCM/file-system setup can be repeated safely
- stand up the cloud deployment path with CI/CD into the Azure environment
- use Azure Container Apps as the first managed deployment target unless robot compatibility proves it unsuitable
- use Docker Compose as the first self-hosted packaging target
- use PostgreSQL as the first Docker Compose database
- deploy auth as a separate service under the Open Jibo domain family
- keep auth in the shared repo/solution initially, but as its own project and deployable
- make `api.openjibo.com` the canonical robot-facing hosted API, with `neo-hub.openjibo.com` only if we later need a distinct host or route boundary for listen/proactive traffic
- launch `openjibo.com` as a real public web app and account entry surface for the release, not just a brochure site
- keep the infrastructure plan flexible enough to map multiple hostnames to the same deployment when that is the simplest safe option
- publish managed images to Azure Container Registry first
- gate real-robot deployment with a virtual-Jibo or purpose-built smoke client
- prefer recorded onboarding/session replay as the first CI-friendly deployment gate
- run PostgreSQL migrations through explicit CI/CD or admin commands, with self-hosted startup migration behind an intentional switch
- use a DbUp-style SQL script runner with an Open Jibo wrapper for apply, preview, dry-run/report, and container-entrypoint modes
- keep the hosted software able to run as:
  - self-hosted with no external cloud dependency
  - hybrid cloud with shared identity/storage
  - a managed cloud service
- abstract storage so different server implementations can satisfy the same contract without the rest of the system caring
- keep only transient session/onboarding artifacts and device-local secrets permanently local-only for now
- define the network trust and consensus story for cloud peers, including bad-actor handling and revocation semantics
- treat robot-provided identity as an untrusted legacy claim until Open Jibo issues and persists its own robot identity
- treat self-hosted-to-network sync as a one-way setup choice until the trust model is mature
- use the storage trust plan to define admission, revocation, quarantine, and sync rules before multi-server rollout
- use deny-by-evidence admission and full versioned snapshots as the first sync model
- sign identity/topology, admission/revocation, issued-identity, provider handoff, and versioned snapshot records before replication
- use hardware-stable `DeviceId`, cert thumbprint, issued-identity lineage, and build/config hashes only as corroborating signals for clone detection
- plan the openjibo.com web UI and paid-access surface alongside the free/self-hosted options
- support provider-specific onboarding steps such as signup/payment before returning to robot onboarding
- support signed provider onboarding events and signed return flows
- use short-lived signed onboarding session tokens plus provider-signed callbacks/returns with nonce/state binding
- on later boots, prefer the selected provider cloud first and use root Open Jibo as an explicit recovery broker rather than silently switching clouds
- allow HTTP only for developer/smoke-only self-hosted paths; owner-facing robot paths should default to HTTPS/self-signed or equivalent patched trust behavior until safe HTTP is proven
- keep Loop advancement, family/friend recognition, and multiple Jibo support in the same platform track so the network and identity model stays future-proof
- scope `1.0.20` to the identity graph and relationship model first; defer direct Jibo-to-Jibo transport and messaging until the peer model is ready

### Progress Update (`2026-06-24`)

- expanded the identity graph snapshot so it now carries explicit account-to-loop ownership, loop-to-robot service, robot-to-device, person-to-account, member-to-loop, and loop-member-to-account relationships
- kept the relationship graph derived from existing persisted loop/member/person/device state so backup/restore and self-hosted snapshots do not need a new hosted API shape for this slice
- added focused regression coverage for the default robot topology and added family-member relationship edges before moving toward peer admission or direct Jibo-to-Jibo transport
- tightened dialog parsing guardrails so dance ability questions, preference questions, explicit dance commands, and unrelated dance-topic chat resolve separately while preserving Pegasus-style command-vs-question behavior
- extended the identity graph slice with recognition-enrollment edges so face and voice trained loop members are explicitly tied back to the serving robot before peer admission or direct Jibo-to-Jibo transport work begins
- added a deterministic identity graph snapshot version and content hash so future signed snapshot/admission work has a stable evidence payload before any peer replication is introduced
- added a first signed identity graph envelope with deterministic HMAC-SHA256 metadata and a portal-readable graph endpoint so owner-facing tooling can inspect the evidence payload before peer admission is enabled
- exposed the identity graph signature payload in both the portal API and dashboard UI so owners can inspect the exact version/account/loop/hash tuple being signed before later admission and replication work
- added corroborating identity graph evidence signals for device ID, robot ID, firmware/application versions, and host mappings so the signed owner-visible graph can distinguish relationship truth from clone-detection inputs before peer admission work begins
- added the first deny-by-evidence admission assessment to the signed identity graph and portal API so future peer admission can start from explicit `admit`/`quarantine` decisions rather than implicit relationship presence
- tightened deny-by-evidence admission so legacy cloud host mappings that have not been redirected to an Open Jibo/self-hosted target remain quarantined even when the required evidence fields are present
- expanded the owner-visible admission assessment with satisfied and blocking evidence lists so quarantined snapshots explain exactly which missing or untrusted signal prevents peer admission
- added deterministic recommended admission actions so owner-visible quarantines now describe the next remediation step, while admitted snapshots tell the portal to retain the signed evidence bundle for future peer admission
- signed the deny-by-evidence admission decision separately from the identity graph snapshot so future peer admission can verify both the relationship payload and the resulting admit/quarantine recommendation
- added child/guardian relationship edges to the identity graph so family membership snapshots preserve dependent-care context before multi-Jibo admission and replication work
- added optional certificate thumbprint, issued identity, build hash, and config hash corroborating signals to the signed identity graph so clone-detection evidence can travel with owner-visible admission snapshots without becoming required admission gates
- added a signed identity graph evidence bundle that binds the snapshot signature and the admission decision signature into one deterministic peer-admission payload for future replication handoff
- exposed the signed identity graph evidence bundle through the portal API and owner dashboard download path so the deterministic peer-admission payload can be retained outside the running cloud before replication handoff exists
- wrapped the downloadable identity graph evidence bundle in a self-describing signed envelope so retained peer-admission artifacts carry their payload boundaries, bundle hash, signature algorithm, key id, and signature without depending on the live portal JSON response
- added offline-review summary counts and blocking-evidence details to the signed identity graph evidence bundle so retained peer-admission artifacts can be triaged without rehydrating the full portal response
- added an offline identity graph evidence bundle verifier so retained peer-admission envelopes can detect payload hash/signature tampering before any replication handoff trusts them
- expanded the offline evidence bundle verifier to extract account, loop, robot, device, summary counts, and blocking-evidence fields so retained quarantine/admission artifacts can be triaged without a running portal
- expanded the signed offline evidence bundle with admission policy, reason, satisfied-evidence, and recommended-action fields so retained artifacts explain both the peer-admission decision and the next owner/operator step without a running portal
- added relationship-kind and evidence-signal-kind summaries to the signed offline evidence bundle so retained artifacts show the shape of the peer-admission snapshot without requiring the full relationship payload to be rehydrated
- expanded offline evidence bundle verification to recompute nested snapshot and admission decision signatures so retained peer-admission artifacts can detect tampering below the outer bundle envelope before replication trusts them
- carried required admission evidence into the signed offline identity graph evidence bundle so retained peer-admission artifacts explain the complete deny-by-evidence policy inputs and the offline verifier recomputes decisions from the same required-evidence set
- added signed revocation check and revocation anchor fields to the identity graph admission decision and offline evidence bundle so future peer admission can bind admit/quarantine decisions to the device, robot, certificate, and issued-identity handles used for revocation review
- added a local identity-graph revocation deny list so any matching device/robot/certificate/issued-identity anchor forces quarantine, signs the revocation match into the admission decision, and carries the blocking reason into offline evidence bundles before peer replication trusts retained artifacts
- exposed identity-graph revocation recording through the portal API and dashboard so owners/operators can quarantine a signed admission bundle by anchor, immediately regenerate the signed decision, and retain the quarantined evidence bundle before peer replication exists
- expanded offline evidence bundle verification with a local revocation deny-list input so retained bundles can remain cryptographically valid while still producing an effective quarantine decision when a receiving peer has already revoked one of the signed device, robot, certificate, or issued-identity anchors
- bound identity graph admission decisions and offline evidence bundles to a deterministic local revocation-list hash so retained peer-admission artifacts show which deny-list state was used when the admit/quarantine decision was signed
- expanded multi-Jibo identity graph evidence so additional robot loop members resolve to their registered device and add explicit loop `served-by` plus robot `runs-on` relationships before direct peer transport is introduced
- added explicit peer-transport, replication-readiness, and sync-direction fields to signed identity graph evidence bundles and the owner dashboard so retained admission artifacts state that direct peer transport is still disabled and snapshots are retention-only until admission succeeds
- expanded the signed evidence bundle handoff contract with peer admission mode, owner-retention policy, and an explicit direct-peer-transport guard so offline retained artifacts cannot be mistaken for enabled peer replication
- hardened the offline identity graph evidence bundle verifier so even a correctly signed retained artifact is rejected if it claims direct peer transport is enabled, changes the retention-only sync direction, or advertises a replication-ready transport state before peer admission is actually implemented
- exposed the offline evidence bundle verifier through the authenticated portal API so retained peer-admission artifacts can be checked against local revocation anchors without trusting direct peer transport or enabling replication
- expanded dialog parsing guardrails with Pegasus-backed dance, favorite-dance, dance-ability, and twerk phrase variants so command-vs-question behavior remains explicit while short dance commands route to the intended personality/action paths
- hardened provider-backed news selection by filtering missing-summary, blank-title, and duplicate-title items before building the spoken Nimbus payload, with skipped-headline diagnostics for capture review
- bound retained identity graph evidence bundles to the explicit `peer-admission-retention` trust purpose and made the offline verifier reject signed bundles that try to reuse the envelope for direct replication or another trust domain before peer admission is implemented
- hardened the grocery/to-do follow-up item state so blank follow-up turns retry once with the dedicated household-list listen context, repeated blank turns close cleanly, and low-signal filler such as `um` does not get stored as a list item
- bound retained peer-admission evidence bundles to an explicit local revocation review status so offline artifacts remain retention-only and verifiers reject signed bundles that try to skip the required local deny-list check before admission

## Working Order

The suggested order for early `1.0.20` execution is:

1. update / backup / restore proof
2. grocery list follow-up and add-item reliability
3. motion and personality command parity, including `twerk` and `go to sleep`
4. STT cleanup for the remaining short-utterance misses
5. continue the broader personality and presence queue once the regression gaps are understood
6. split the platform-conversion track into named backlog items and work the topmost one at a time
7. keep the cloud deployment, custom-domain, and public-site tracks in discovery until they are ready for their own proof slices
8. keep the storage and multi-Jibo architecture tracks in discovery until they are ready for their own proof slices

## Deferred Full Regression Milestone

After the current `1.0.20` build reaches the next stability checkpoint, run the named full regression bundle in [regression-test-plan.md](regression-test-plan.md) before expanding into the next platform slice.

## Closeout Note

`1.0.19` is now treated as closed history. This plan is the active queue for the next pass, and the backlog should point here for current work ordering.
