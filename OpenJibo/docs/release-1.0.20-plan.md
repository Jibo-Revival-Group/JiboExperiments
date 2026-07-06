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


### Progress Update (`2026-07-05`)

- continued the Persona least-favorites parity slice with source-backed least-favorite video game, president, weather, time of day, mammal, and pizza-topping routing. These prompts now use the imported violent-games, trouble-avoiding-president, rain/thunderstorms, middle-of-the-night, hippo, and onion answer sets with focused dialog and guardrail coverage for US/UK least-favorite phrasing. Live robot playback remains the proof item for this small personality slice.

- continued the Persona favorites/music-and-sports parity slice with source-backed favorite ice cream flavor, favorite rapper, favorite rock band, favorite baseball team, favorite football team, and favorite Olympic ring routing. These prompts now use the imported mint-chocolate-chip, Snoop Dogg, AC/DC, no-favorite-baseball-team, weirdly-shaped-football, and blue-Olympic-ring answer sets with focused dialog coverage for US/UK favorite phrasing. Live robot playback remains the proof item for this small personality slice.

- continued the Persona music favorites parity slice with source-backed favorite music genre, favorite country musician, and favorite holiday/Christmas song routing. These prompts now use the imported danceable-music, Dolly Parton, and Frosty the Snowman answer sets with focused dialog coverage for US/UK favorite phrasing. Live robot playback remains the proof item for this small personality slice.

- continued the Persona favorites/team-sports parity slice with source-backed favorite hockey team, favorite basketball team, favorite pizza topping, and favorite Olympic event routing. These prompts now use the imported no-favorite-team, sliced-olives, pole-vault, and ski-jump answer sets with focused dialog and websocket guardrail coverage for US/UK favorite phrasing. Live robot playback remains the proof item for this small personality slice.

- continued the Persona favorites parity slice with source-backed favorite-joke routing. Favorite-joke prompts now use the imported all-jokes/funny-ones answer set with focused dialog and websocket guardrail coverage for US/UK favorite phrasing. Live robot playback remains the proof item for this small personality slice.

- continued the Persona favorites/opposites parity slice with source-backed least-favorite word, color/colour, and animal routing. These prompts now use the imported hate-word, all-colors, and hippo answer sets with focused dialog and guardrail coverage for US/UK least-favorite phrasing. Live robot playback remains the proof item for this small personality slice.

- continued the Persona favorites/opposites parity slice with source-backed least-favorite food and least-favorite place routing. These prompts now use the imported spilled-soup and bathtub answer sets with focused dialog and guardrail coverage for US/UK least-favorite phrasing. Live robot playback remains the proof item for this small personality slice.

- continued the Persona least-favorites parity slice with source-backed least-favorite movie, car, vegetable, number, and bird routing. These prompts now use the imported Waterworld, no-bad-cars, onion, large-number, and woodpecker answer sets with focused dialog and guardrail coverage for US/UK least-favorite phrasing. Live robot playback remains the proof item for this small personality slice.

- continued the Persona least-favorites parity slice with source-backed least-favorite artist, band, author, and celebrity routing. These prompts now use the imported makes-art, pleasant-surprise/turtle, trash-compactor, and scary-Megatron answer sets with focused dialog and websocket guardrail coverage for US/UK least-favorite phrasing. Live robot playback remains the proof item for this small personality slice.

#### Major blockers / questions

- physical-device proof is still the release blocker: the cloud can detect connection-host, DNS mapping, and freshness drift, but we still need a live converted robot run that reaches `VerifyConnection` with matching reported host and host mappings.
- safe awakening/OOBE parity remains blocked on image-specific review of owner-name replacement points plus safe body/yawn/audio asset provenance before the conversion video should claim full first-boot parity.
- self-hosted owner-facing HTTPS remains unresolved: developer HTTP smoke paths are acceptable, but a real owner path still needs a trust/certificate decision before self-hosted conversion is promoted beyond controlled tests.

### Progress Update (`2026-07-04`)

- continued the Persona favorites parity slice with favorite actor, actress, robot, car, weather, and time-of-day routing. These prompts now use source-backed Tom Hanks, Julie Andrews, Wally/R2-D2/Rosie, Beetle, sunny-weather, and 11:11/3:33 answer sets with focused dialog and guardrail coverage. Live robot playback remains the proof item for this small personality slice.

- continued the seasonal/persona favorites parity slice by splitting direct favorite-holiday questions away from generic holiday-season chat. Favorite holiday prompts now use the source-backed Halloween answer set, with US/UK favorite phrasing covered in focused dialog tests. Live robot playback remains the proof item for this small personality slice.

- continued the Persona favorites parity slice with source-backed favorite vegetable, favorite place, and favorite superhero routing. Vegetable questions now land on the imported artichoke / broccoli / cauliflower answer set, place questions land on the imported right-here/Mars reply, and superhero questions land on the imported Optimus Prime answer set with focused dialog and websocket guardrail coverage. Live robot playback remains the proof item for this small personality slice.

- continued the Persona favorites parity slice with source-backed favorite book and favorite candy routing. Book questions now land on the imported instruction-manual answer, and candy questions use the imported lollipop / sweet-tooth / candy-corn answer set with focused dialog and websocket guardrail coverage. Live robot playback remains the proof item for this small personality slice.

- continued the Persona favorites parity slice with source-backed favorite song routing. Song questions now land on the imported radio/dance-song reply set for US/UK favorite phrasing and `do you have a favorite song` aliases, with focused dialog guardrail coverage. Live robot playback remains the proof item for this small personality slice.
- continued the Persona favorites parity slice with source-backed favorite video game and favorite president routing. Video-game questions now land on the imported Pong replies, favorite-president questions land on the imported Abraham Lincoln / Taft / fictional-president answer set, and guardrail coverage keeps US/UK favorite phrasing away from generic chat or hero routing. Live robot playback remains the proof item for this small personality slice.
- expanded the Persona favorites surface with source-backed favorite pet and favorite mammal routing. Favorite pet now prefers the imported groundhog / water-safety replies, and favorite mammal uses the Pegasus-style people answer, with focused dialog guardrail coverage for US/UK favorite phrasing. Live robot playback remains the proof item for this small personality slice.
- continued that Persona favorites slice with favorite fruit routing. Favorite fruit now uses the imported blueberry / blue-and-roundness replies for US/UK favorite phrasing plus "what kind of fruit" aliases, with focused dialog guardrail coverage. Live robot playback remains the proof item for this small personality slice.
- continued the Persona favorites parity slice with source-backed favorite flower alias hardening plus favorite TV show, shape, and word routing. These now use the imported sunflower, TV-learning, sphere/circle, and word-play replies with focused guardrail coverage for US/UK favorite phrasing. Live robot playback remains the proof item for this small personality slice.
- continued the Persona favorites parity slice with favorite thing routing. Generic favorite-thing questions now land on the imported people/electricity answer set instead of drifting into the favorite-thing-to-do branch or generic chat, with focused guardrail coverage for US/UK favorite phrasing. Live robot playback remains the proof item for this small personality slice.
- continued the Persona favorites parity slice with favorite movie, dessert, planet, and number routing. These now land on the imported Back to the Future/Toy Story-style movie replies, blueberry-pie dessert replies, Earth/Mars planet reply, and binary/pi number replies with focused guardrail coverage for US/UK favorite phrasing. Live robot playback remains the proof item for this small personality slice.

#### Major blockers / questions

- physical-device proof is still the release blocker: the cloud can detect connection-host, DNS mapping, and freshness drift, but we still need a live converted robot run that reaches `VerifyConnection` with matching reported host and host mappings.
- safe awakening/OOBE parity remains blocked on image-specific review of owner-name replacement points plus safe body/yawn/audio asset provenance before the conversion video should claim full first-boot parity.
- self-hosted owner-facing HTTPS remains unresolved: developer HTTP smoke paths are acceptable, but a real owner path still needs a trust/certificate decision before self-hosted conversion is promoted beyond controlled tests.

### Progress Update (`2026-07-02`)

- expanded the Persona favorites surface with source-backed favorite drink and favorite sport routing. These now use the imported Pegasus-style liquid-safety and mini-golf replies with focused regression coverage, leaving live robot playback as the remaining proof item for this small persona slice.

- tightened the live connection proof freshness contract so `OOBE VerifyConnection` now reports the accepted proof age window, accepted future clock skew, and computed `freshUntil` timestamp in `reportedConnectionProof`. Conversion clients can also send `connectionProofMaxAgeSeconds` / `proofMaxAgeSeconds` / `freshnessMaxAgeSeconds` when a release/video gate needs a stricter live-capture window; the cloud clamps that policy to a safe 60-second to 1-hour range and returns `stale-proof-observed-at` when the observed proof falls outside it.

- expanded the identity graph relationship model for the `1.0.20` platform track: non-robot loop members now carry explicit owner-scoped relationship edges for family, friend, caregiver/guardian, or generic loopmate roles, with reciprocal owner edges. This keeps the signed snapshot useful for family/friend recognition and multiple-member planning without enabling direct Jibo-to-Jibo transport yet.

- added operator-facing `connectionProofGuidance` to robot-facing `OOBE VerifyConnection` / `ConnectionProof` responses. Each active blocker now includes a severity (`release-gate`, `setup-gate`, or `needs-review`) and a concrete owner action, so the conversion video/runbook can fail closed while telling the operator whether to refresh setup, complete robot setup, capture live reported host evidence, fix DNS/static host rewrites, or gather a fresh proof timestamp.

- added a fresh live-proof option to robot-facing `OOBE VerifyConnection`: conversion clients can now set `requireFreshConnectionProof` / `requireFreshLiveRobotProof` and include `connectionProofObservedAt` plus optional `connectionProofSource` and `connectionProofId`. The proof response now returns a `reportedConnectionProof` envelope with freshness, observation age, source, and capture id, and blocks with `missing-proof-observed-at` or `stale-proof-observed-at` when the release/video gate asks for fresh robot-observed evidence.

- added an explicit live-robot proof gate to `OOBE VerifyConnection`: conversion clients can now set `requireReportedConnectionProof`, `requirePhysicalConnectionProof`, or `requireLiveRobotProof` to require both a robot-reported connected host and robot-observed legacy host mappings before the cloud marks the proof as connected. Missing evidence returns `missing-reported-connection-host` and/or `missing-reported-host-mappings`, keeping scripted smoke compatibility while giving the physical conversion run a fail-closed gate.

- tightened the shared managed/self-hosted cloud smoke clients so `VerifyConnection` now sends robot-reported legacy host mapping evidence for `api.jibo.com`, `api-socket.jibo.com`, and `neo-hub.jibo.com`, then fails if the proof does not echo those mappings or mark them as matching the selected target host. This moves the conversion gate from a single connected-host assertion to the same DNS-retargeting evidence expected from the physical robot run.

- expanded the robot-facing `OOBE VerifyConnection` proof so a conversion client can send robot-observed legacy DNS/host mappings via `reportedHostMappings`, `reportedDnsMappings`, or `resolvedHostMappings`; the cloud normalizes URL/host:port values, echoes the reported mapping evidence, and reports `reported-host-mapping-mismatch` when the robot-observed `api.jibo.com`, `api-socket.jibo.com`, or `neo-hub.jibo.com` target does not match the selected Open Jibo cloud. This gives the conversion video a stronger DNS retargeting proof before physical writes.

- strengthened the robot-facing `OOBE VerifyConnection` proof again so a conversion client can send `reportedConnectionHost` / `connectedHost` / `resolvedHost` / `currentHost`; the cloud normalizes URLs or host:port values, echoes the normalized reported host, and reports `reported-connection-host-mismatch` when the robot says it reached a different host than the selected Open Jibo target. This keeps the final conversion proof from relying only on stored setup mappings.

- tightened the robot-facing `OOBE VerifyConnection` proof so it now compares the expected Open Jibo host mappings against the persisted robot identity graph, returns the stored mappings beside the expected mappings, and reports `host-mapping-mismatch` in `connectionBlockers` instead of claiming the robot is connected when DNS/API mapping state has drifted. This keeps the conversion-video and deployment gates focused on a real robot-to-cloud connection proof, not just a completed setup token.

#### Major blockers / questions

- physical-device proof is still the release blocker: the cloud can now detect mapping drift and reported-host drift, but we still need a live converted robot run that shows the robot resolving the selected target host and reaching `VerifyConnection` after setup with a matching reported connection host.
- safe awakening/OOBE parity remains blocked on image-specific review of owner-name replacement points plus safe body/yawn/audio asset provenance before the conversion video should claim full first-boot parity.
- self-hosted owner-facing HTTPS remains unresolved: developer HTTP smoke paths are acceptable, but a real owner path still needs a trust/certificate decision before we promote self-hosted conversion beyond controlled tests.


### Progress Update (`2026-06-30`)

- tightened the conversion smoke clients so `VerifyConnection` now sends a robot-reported connection host and fails if the proof does not echo a matching normalized host. This moves the managed/self-hosted gate closer to the physical converted-robot requirement: the cloud must prove both stored DNS mappings and the host the robot says it reached.

- tightened the self-hosted smoke path so Bash and PowerShell smoke clients can carry an explicit conversion target mode/host through `PlanConversion`, `PrepareRobot`, `SetupRobot`, and `VerifyConnection`, defaulting managed runs to `api.openjibo.com` while making self-hosted runs prove their own host mappings.
- expanded the self-hosted deployment contract to require `VerifyConnection`, `targetMode`, and `targetHost` evidence in the shared smoke client so Docker Compose packaging now gates the same robot-to-cloud connection proof as managed Azure smoke.

- tightened the Wi-Fi QR conversion path so a server-backed QR now collects target mode, optional target host, and required rollback snapshot evidence, runs non-writing `OOBE PlanConversion`, and refuses to mint a robot token when readiness is blocked instead of falling back to an unsafe static token.
- expanded the managed cloud smoke gate to exercise `OOBE PlanConversion` before `PrepareRobot`, asserting that the plan is non-writing, write-safe, and resolves the managed target host to `api.openjibo.com` before the robot setup token is issued.
- tightened the managed cloud smoke gate so both Bash and PowerShell smoke clients now call robot-facing `OOBE VerifyConnection` after `SetupRobot` and fail if the prepared robot is not connected, complete, mapped to `api.openjibo.com`, or write-safe. This makes the deployment gate prove the exact post-conversion robot-to-cloud connection target instead of stopping at setup/status responses.
- added a robot-facing OOBE `VerifyConnection` / `ConnectionProof` operation for the conversion-video path: after `SetupRobot`, a prepared token can now return connected/complete state, cloud version, robot/device/loop ids, target mode/host, rollback snapshot, host mappings, baseline evidence, and conversion readiness in one proof payload.
- added signed onboarding-session evidence to the OOBE conversion plan/prepare/status/setup responses: each prepared token now carries a nonce, provider-return state, canonical target host, expiry, signature payload, and HMAC-SHA256 signature so account/provider onboarding can be bound to the exact robot conversion target before the robot writes identity.
- added a planning-only OOBE OTA bootstrap manifest helper so the static-DNS/NTP/HTTPS update lane can be reviewed without repository-bundled historical certificate material; strict mode now keeps certificate provenance and missing trace captures as explicit blockers before any physical robot attempt.
- recorded the Jibo Revival Group OOBE OTA bootstrap discovery from Maaarcna: QR-provided static DNS can steer a wiped/OOBE robot to a LAN bootstrap server, a 2017 NTP response can make the historical `*.jibo.com` certificate validate, and stock `GetUpdateFrom` flows can request per-subsystem OTA tarballs before Open Jibo conversion. This alters the plan by adding an OOBE OTA lane beside ShofEL/firewall/SSH, while keeping ShofEL as the rescue, rollback, non-wipe, and older/variant path until traces and package provenance are proven.
- added a non-destructive OOBE `PlanConversion` / `AuditConversion` protocol path so conversion tooling can preview target mode, target host mappings, rollback evidence, and machine-readable readiness blockers before issuing a prepared token or writing robot identity. This gives the RCM/video flow an audit-first cloud contract for proving whether a robot is safe to prepare for Open Jibo.
- tightened self-hosted conversion targeting so prepared `open-jibo-self-hosted` tokens now require an explicit owner-supplied target host before robot writes, while managed and AI modes retain deterministic hosted defaults. Successful self-hosted setup now writes the supplied host into the `api.jibo.com`, `api-socket.jibo.com`, and `neo-hub.jibo.com` mappings that the converted robot will use to connect to the cloud.
- hardened the prepared-token conversion write gate so `SetupRobot` and `ReconnectRobot` now fail closed with machine-readable readiness blockers when a prepared conversion token lacks rollback evidence or names an unsupported Open Jibo mode, while preserving the legacy implicit setup path for stock compatibility.
- expanded conversion readiness responses with the supported target-mode list and `supported-target-mode` required evidence so the robot/app can distinguish a missing snapshot from an invalid mode before any robot identity write occurs.
- added rollback-snapshot-aware OOBE conversion readiness reporting to `GetStatus`, including the selected Open Jibo target mode, rollback snapshot id, required evidence, and machine-readable blockers before any robot write is considered safe. This keeps the conversion path aligned with the audit-first/rollback-safe release priority and makes the current major blocker explicit when a prepared token lacks rollback evidence.
- tightened OOBE conversion status semantics so `GetStatus` now reports prepared/accepted/expired state plus the exact Open Jibo target host mappings, and `SetupRobot`/`ReconnectRobot` refuse expired prepared tokens before mutating robot identity. This keeps the robot-to-cloud connection proof explicit and safer for conversion-video runs.
- tightened OOBE conversion connection proof by making `GetStatus` return the prepared device/loop/expiry metadata and by promoting `SetupRobot`/`ReconnectRobot` into the active robot identity graph with Open Jibo host mappings, so the cloud can prove the converted robot identity and canonical host target immediately after setup
- added a robot-facing `Loop_*.ListRecognitionObservations` conversion-smoke operation and wired the cloud smoke script to assert that the seeded recognition observation can be read back before OOBE `PrepareRobot`/`SetupRobot`, making the video path prove recognition evidence is queryable instead of only write-accepted
- fixed the cloud smoke `SetupRobot` 409 regression by making both Bash and PowerShell smoke clients carry explicit rollback snapshot evidence into `PrepareRobot`; the deployment contract now checks for that marker so future smoke edits stay aligned with the rollback-safe conversion gate.
- tightened the conversion-video persistence proof by extending the cloud-state round-trip regression to include loop member creation, face/voice enrollment flags, and a recognition observation with source evidence, so a smoke run can survive process restart instead of only proving in-memory state

- added a video-ready conversion evidence bundle script that records the exact harness, first-contact inspection, and cloud-smoke commands, emits a manifest with loop/member recognition smoke evidence, and surfaces unresolved physical-device, awakening-asset, and face-recognition blockers before a real robot is written
- extended the robot-facing loop protocol with a conversion-smoke-safe recognition observation operation so scripts can seed a loop member, mark face/voice enrollment, and record a face-recognition event before checking OOBE setup against the cloud
- expanded the cloud smoke script to exercise loop/member identity persistence and recognition observation capture before `PrepareRobot`/`SetupRobot`, giving the conversion video a single repeatable path for cloud connection plus identity evidence instead of a separate manual portal step
- added a non-destructive first-contact/OOBE inspection helper for conversion-video prep so restored/OOBE and stock `1.9.2` images can be scanned for `@be/first-contact`, awakening scene manifests, `name_learning`, `pronoun_`, `WhoAmI`, and recognition hook candidates before any asset or behavior is ported into Open Jibo onboarding
- connected the first-contact filesystem report to the existing websocket recognition-candidate inspector so loop/member identity persistence can be demonstrated as seeded or speaker-to-loop-member evidence until a live capture proves a stable face/person identifier
- kept the major blocker explicit: safe body/yawn/audio awakening assets and exact owner-name replacement points still require image-specific review of the reported skill roots plus live regression logs before the conversion video claims full OOBE parity
- made `PrepareRobot` return the same conversion readiness, target-mode, target-host, rollback snapshot, and host-mapping evidence that `GetStatus` exposes, so the app/video path can surface blockers immediately after token issuance instead of waiting until the robot attempts `SetupRobot`.
- made successful `SetupRobot` and `ReconnectRobot` responses echo the accepted robot id, target mode, target host, rollback snapshot, host mappings, and conversion readiness so conversion-video tooling can prove the robot identity write and cloud connection target in the same response that hands back access credentials.
- carried baseline-audit evidence into the OOBE conversion audit/prepare/status/setup path so conversion-video tooling can prove firmware/application version, source distribution, and stock/source mode alongside rollback snapshot and host mappings; when a caller explicitly requires baseline evidence, readiness now fails closed with `missing-baseline-audit` before any robot write.
- hardened the prepared-token setup gate against unsafe setup-time overrides: `SetupRobot` / `ReconnectRobot` now evaluate the final requested target mode, host, rollback, and baseline evidence before mutating robot identity, so a prepared managed token cannot be turned into an incomplete self-hosted write during the robot handoff.

### Progress Update (`2026-06-24`)

- kept loop/identity recognition advancement in the platform track by persisting robot recognition observations, projecting them into signed identity graph relationships, and carrying recognition evidence summaries into offline retention bundles so conversion smoke runs can prove enrollment plus recognition state survives a cloud restart before peer replication exists
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
- bound retained peer-admission evidence bundles to explicit Open Jibo Cloud exporter metadata so offline verifiers can reject correctly signed bundles that claim another service exported the retention artifact before direct peer replication exists
- expanded household-list inline item parsing for natural short follow-up phrasing such as `we need apples for my grocery list` and `need to call the vet on my to-do list`, keeping grocery/to-do reliability moving without changing the hosted API shape
- hardened NewsAPI provider ingestion so correction/update and family-unsafe headlines are rejected before category fallback and caching, preventing unsafe provider batches from being treated as usable source snapshots before runtime formatting filters run
- tightened `who am I` identity recall so a multi-person presence context no longer borrows a loop-level remembered name when no person-scoped name exists, preserving the `1.0.20` regression expectation that Jibo should not guess from the wrong loop member
- expanded owner preference recall guardrails for modal still-remember helper forms (`can/could/would you still remember what my favorite/favourite/fave ... is`) and consolidated embedded favorite/favourite/fave helper extraction so future alias additions do not duplicate every spelling/lead combination.
- expanded owner preference recall parsing for `please remind me ...` and `do you still remember ...` aliases, including embedded `what my favorite/favourite/fave ... is` forms, so polite reminder and confirmation-style checks stay on owner-memory lookup
- expanded owner preference recall parsing for `can/could/would you remember ...` and `would you happen to remember ...` aliases, including embedded `what my favorite/favourite/fave ... is` forms, so natural memory-check prompts keep routing to owner-memory lookup
- expanded owner preference recall parsing for `do you happen to know ...` and `do/can/could/would you happen to recall ...` aliases, including embedded `what my favorite/favourite/fave ... is` forms, so hesitant memory-check prompts keep routing to owner-memory lookup instead of generic chat
- added a websocket recognition-candidate scanner plus a concrete conversion demo checklist so the next robot session can prove whether ASR websocket captures contain live person/face/voice identity metadata or whether the first conversion video must label the recognition observation as smoke-seeded until a richer robot-local source is found

- expanded the recognition-candidate scanner to derive redacted speaker-to-loop-member and peoplePresent matches with timestamps, added the Jake/Erin recognition probe to the regression plan, and locked the first conversion video direction to managed Azure with safe staged credentials before the explicit cloud cutover

### Progress Update (`2026-07-02`)

- expanded the identity graph relationship model for the `1.0.20` platform track: non-robot loop members now carry explicit owner-scoped relationship edges for family, friend, caregiver/guardian, or generic loopmate roles, with reciprocal owner edges. This keeps the signed snapshot useful for family/friend recognition and multiple-member planning without enabling direct Jibo-to-Jibo transport yet.

- tightened the shared managed/self-hosted cloud smoke clients so `VerifyConnection` now sends robot-reported legacy host mapping evidence for `api.jibo.com`, `api-socket.jibo.com`, and `neo-hub.jibo.com`, then fails if the proof does not echo those mappings or mark them as matching the selected target host. This moves the conversion gate from a single connected-host assertion to the same DNS-retargeting evidence expected from the physical robot run.

- strengthened the robot-facing `OOBE VerifyConnection` proof again so a conversion client can send `reportedConnectionHost` / `connectedHost` / `resolvedHost` / `currentHost`; the cloud normalizes URLs or host:port values, echoes the normalized reported host, and reports `reported-connection-host-mismatch` when the robot says it reached a different host than the selected Open Jibo target. This keeps the final conversion proof from relying only on stored setup mappings.

- tightened the conversion-video evidence recorder so its cloud smoke now uses the same selected Open Jibo target mode and target host that the filesystem harness stages on the robot overlay; this prevents a self-hosted/developer conversion demo from accidentally proving only the default managed `open-jibo` cloud path.
- expanded the conversion-video manifest with the robot-facing `VerifyConnection` proof from smoke output, including connected/complete state, accepted target mode/host, legacy host mappings, and write-safe readiness, so the filmed conversion bundle can show that the converted robot is pointed at the intended cloud before any physical write.
- kept the remaining physical-device blockers unchanged: first real-device variant selection, backup confirmation, safe awakening asset reuse, and live face/person identifier capture still need operator review before moving from overlay proof to a physical robot.
- added an authenticated admin summary API and dashboard panel so operators can review cloud version, persistence state, robot host mappings, identity/admission counts, Home Assistant connectivity, suggested smoke operations, and the current conversion blockers/questions without digging through raw snapshots.
- added a browser-based fake Jibo robot harness at `/harness` that can issue `X-Amz-Target` robot protocol calls with a selectable simulated host, starting with conversion audit/status/connection proof and robot profile presets so cloud endpoints can be exercised while waiting on physical robot proof.
- added a harness-only host override header for local browser smoke runs so self-hosted/developer conversion targets can be verified without needing DNS or browser-forbidden `Host` header mutation.
- expanded the browser fake robot harness into a guided conversion smoke surface: operators can now edit target mode/host, rollback snapshot, baseline evidence, and device identity once, load each OOBE request shape, cache the prepared token, or run an audit → prepare → setup → live connection proof sequence that requires reported host and legacy DNS mapping evidence before moving to physical robot writes.
- expanded the dialog parsing guardrail slice with polite modal Pegasus-style dance/twerk requests (`can/could/would/will you please ...`) so ability-style questions stay on source-backed personality replies and specific twerk commands keep their motion route instead of falling through to generic chat.
- tightened live `VerifyConnection` proof completeness so a physical/harness run that requires reported evidence must report all three legacy host mappings (`api.jibo.com`, `api-socket.jibo.com`, and `neo-hub.jibo.com`); partial DNS evidence now returns `incomplete-reported-host-mappings` plus a machine-readable missing-host list instead of looking like a complete conversion proof.
- aligned the authenticated admin summary and dashboard with that three-host conversion gate by publishing the required legacy DNS proof set, machine-readable missing host mappings, and per-host blocker labels so operators can see incomplete `neo-hub`/socket/API evidence before relying on the browser harness or physical conversion run.
- expanded dialog parsing guardrails for modal `can/could/would you happen to know ...` owner-preference recall prompts, including embedded `what my favorite/favourite/fave ... is` forms, so polite hesitant memory checks keep routing to memory lookup instead of generic chat.

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
