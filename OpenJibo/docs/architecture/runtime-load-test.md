# Runtime Connection And Turn Load Test

Status date: `2026-08-26`

The release smoke can now hold a configurable population of fake notification sockets while a rotating subset
opens NeoHub listen sockets and completes simultaneous `CLIENT_ASR` joke turns. Every turn must return
`LISTEN -> EOS -> SKILL_ACTION` with `@be/joke`; quiet notification sockets must remain open throughout the
rounds. The result reports completed turns and client-observed min/P50/P95/max latency.

The default release gate remains intentionally short: six connected robots, 25% active turns, one round, and a
500 ms quiet hold. The browser harness exposes the same controls. The managed staging workflow exposes the four
capacity-tier controls while preserving these defaults.

## CLI Controls

Run `node ./scripts/cloud/invoke-release-smoke.mjs` with `BASE_URL` and these optional environment variables:

| Variable | Default | Bound | Meaning |
| --- | ---: | ---: | --- |
| `RELEASE_SMOKE_CONCURRENCY` | `6` | `1-100` | Connected fake notification sockets |
| `RELEASE_SMOKE_BOOTSTRAP_CONCURRENCY` | `4` | `1-100` | Maximum simultaneous token-issuance and initial socket operations |
| `RELEASE_SMOKE_TURN_PERCENT` | `25` | `0-100` | Simultaneous turn share per round |
| `RELEASE_SMOKE_TURN_ROUNDS` | `1` | `1-1000` | Number of rotating turn rounds |
| `RELEASE_SMOKE_HOLD_MS` | `500` | `0-86400000` | Quiet hold after all sockets connect |
| `RELEASE_SMOKE_ROUND_INTERVAL_MS` | `0` | `0-3600000` | Delay between turn rounds |
| `RELEASE_SMOKE_TIMEOUT_MS` | `6000` | `100-120000` | Per-connect and per-turn timeout |

Use a stable `TEST_ROBOT_ID` prefix for repeated staging experiments so the generated device identities are easy
to identify. Do not target production for a capacity run.

## Managed Staging Sweep

Run the `openjibo-staging-capacity-sweep` workflow to exercise the current staging image without publishing a
new image, running migrations, cloning databases, or creating a production promotion gate. It:

1. verifies the exact `rg-openjibo-staging` resource group and rejects production ingress hostnames;
2. records the current image, ready revision, and replica scale;
3. creates a temporary release-smoke secret, pins two replicas, and requires the resulting configuration revision
   to serve from the unchanged image;
4. runs the 6, 10, 15, and 20 connected-robot tiers serially with the selected turn percentage, round count, and
   interval;
5. waits briefly for aggregate telemetry, captures an exact-revision one-day capacity report, and uploads the
   manifest plus each tier's JSON result;
6. always disables release-smoke authorization, removes its secret after a healthy disabled revision exists,
   restores the original scale, and verifies the image and cleanup invariants.

The fixed `open-jibo-smoke-staging` namespace reuses the same bounded identities on later runs. A complete sweep
can create at most 21 staging-only synthetic registrations: one primary control identity plus 20 concurrent-tier
identities. Those records remain hidden deployment-smoke data rather than visible robot inventory.

The temporary authorization and cleanup operations create configuration revisions, so this workflow intentionally
resets any passive staging exact-revision observation window. It does not affect the passive production baseline.

## Certification Matrix

Run connected-robot tiers `6`, `10`, `15`, and `20`. At each tier, run `10%`, `25%`, and `50%` simultaneous
turns. Begin with 10 rounds at five-second intervals, then run the 60-minute step and overnight soak only after
the shorter tiers pass. Keep the telemetry window aligned with the application, .NET runtime, Npgsql, Container
Apps, and PostgreSQL measurements in [runtime-operational-metrics.md](runtime-operational-metrics.md).

This driver uses transcript-bearing `CLIENT_ASR`; it exercises WebSocket ingress, session/turn concurrency,
routing, persistence interactions, response mapping, and egress without consuming or certifying STT provider
capacity. A separate captured-audio scenario is required before making an Azure Speech throughput or cost claim.

Stop a tier on any incorrect/missing reply, socket loss, timeout, restart, OOM, audio/session growth, pool wait,
database error, cross-replica inconsistency, or rising memory slope. The highest passing tier is still not the
enrollment cap until the production-shaped run retains at least 25% measured headroom.

## Initial Driver Proof

On `2026-08-26`, the local driver ran against the existing `rg-openjibo-staging` Container App with six connected
fake robots, 25% simultaneous turns, and one round. All six quiet notification sockets remained open; both active
turns returned the expected joke sequence. Client-observed turn latency was 218 ms and 247 ms (P95 247 ms).
Notification reconnect, malformed-frame recovery, missing-token rejection, and post-session robot persistence
also passed. This proves the driver and current staging protocol path, not a capacity tier: the run was brief,
used `CLIENT_ASR`, and preceded deployment/export of the new runtime measurements.

## Short Matrix Evidence (`2026-09-07`)

Runs `34121034373`, `34122033998`, and `34123116268` exercised the 25%, 50%, and 10% simultaneous-turn cases,
respectively, against the unchanged image `sha-3773955b69c6`. Each run observed two serving replicas, proved a
different replica could read a committed registration, restored the original one-to-two scale, disabled release
smoke, and removed the temporary authorization secret. Across all runs, 460 of 460 requested turns completed with
the expected reply order and no socket timeout or incorrect response.

| Turn share | Connected robots | Active turns/round | Completed turns | Client P95 |
| ---: | ---: | ---: | ---: | ---: |
| 10% | 6 | 1 | 10 | 2,133 ms |
| 10% | 10 | 1 | 10 | 792 ms |
| 10% | 15 | 2 | 20 | 2,003 ms |
| 10% | 20 | 2 | 20 | 1,773 ms |
| 25% | 6 | 2 | 20 | 1,227 ms |
| 25% | 10 | 3 | 30 | 1,906 ms |
| 25% | 15 | 4 | 40 | 3,143 ms |
| 25% | 20 | 5 | 50 | 1,973 ms |
| 50% | 6 | 3 | 30 | 2,073 ms |
| 50% | 10 | 5 | 50 | 1,168 ms |
| 50% | 15 | 8 | 80 | 3,132 ms |
| 50% | 20 | 10 | 100 | 2,324 ms |

The curve is non-monotonic, so these brief samples do not show a linear saturation point. More importantly, the
10% run captured one `cloud_state` pool sample during the 20-robot tier with all eight per-replica connections in
use and two requests pending. The next sample returned to zero and all turns completed, but any pool wait fails the
certification rule. The 10% workflow run predated enforcement of that report result; the workflow now preserves the
artifact, performs cleanup, and fails when aggregate telemetry reports `reliability-signal-detected`.

The original driver issued every tier's tokens and initial notification sockets at once. The guarded repeat now
limits that bootstrap phase to four operations while leaving the selected 10%, 25%, or 50% simultaneous turn load
unchanged. This separates steady connected-robot/turn evidence from a 20-way enrollment burst; a reconnect or
enrollment storm remains a separate scenario to test deliberately.

The repeat workflow also writes `tier-database-evidence.json`. It queries only the exact probe revision and the
recorded tier windows, retains bounded `cloud_state`/`personal_memory` pool and replica dimensions, and summarizes
used connections, executing commands, and pending requests for each tier. Active connection gauges corroborate a
zero when Npgsql does not create a pending-request series. A tier with no usable connection samples, aggregate
telemetry that never becomes visible, or any observed pending request fails the evidence step; cleanup and artifact
upload still run.

Do not begin the 60-minute step yet. First repeat the short matrix with the new guard and retain per-tier pool
samples so a wait can be attributed to connection/token ramp or turn processing. The aggregate reports' value of
three maximum replicas also needs correlation with revision lifecycle events because every point-in-time probe saw
the required two replicas. Physical Ogg/Opus audio, STT-provider load, reconnect storms, and sustained memory slope
remain outside this matrix.

## Guarded Short Matrix Repeat (`2026-09-10`)

Runs [`34477940754`](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/34477940754),
[`34479120393`](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/34479120393), and
[`34480066458`](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/34480066458) repeated the
10%, 25%, and 50% cases on commit `c517f97c23ab460ea7d4b771185797edc2e074df`. All used unchanged image
`sha-3773955b69c6`, observed exactly two serving replicas, proved a cross-replica committed read, retained complete
per-tier connection/executing-command samples, inferred `pendingRequestMax: 0`, and passed cleanup restoration.

| Turn share | Robots | Active turns/round | Completed | P50 | P95 | Maximum |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 10% | 6 | 1 | 10 | 381 ms | 2,483 ms | 2,483 ms |
| 10% | 10 | 1 | 10 | 381 ms | 1,952 ms | 1,952 ms |
| 10% | 15 | 2 | 20 | 375 ms | 2,995 ms | 3,033 ms |
| 10% | 20 | 2 | 20 | 372 ms | 1,244 ms | 1,259 ms |
| 25% | 6 | 2 | 20 | 381 ms | 3,166 ms | 3,201 ms |
| 25% | 10 | 3 | 30 | 348 ms | 1,238 ms | 1,239 ms |
| 25% | 15 | 4 | 40 | 375 ms | 3,959 ms | 4,006 ms |
| 25% | 20 | 5 | 50 | 316 ms | 520 ms | 1,972 ms |
| 50% | 6 | 3 | 30 | 314 ms | 2,477 ms | 2,480 ms |
| 50% | 10 | 5 | 50 | 349 ms | 3,856 ms | 3,883 ms |
| 50% | 15 | 8 | 80 | 311 ms | 4,956 ms | 4,999 ms |
| 50% | 20 | 10 | 100 | 322 ms | 4,241 ms | 5,884 ms |

The guarded matrix completed all `460/460` requested turns without an incorrect reply, socket loss, or timeout.
Across the three exact probe revisions, application memory maxima were 201-217 MiB of 2 GiB, platform memory
maxima were 167-179 MiB, highest hourly-average CPU was 7.2% of one core, PostgreSQL connections peaked at 9 of
50, and aggregate cache hit ratios remained 95.31-95.69%. The previous two-pending-request observation did not
recur.

This closes the guarded short-matrix repeat, but does not yet certify a sustained tier. Latency remains strongly
non-monotonic: the 15-robot tail recurred and the 20-robot/50% maximum reached 5,884 ms against the 6,000 ms client
timeout. The aggregate windows remain only about five minutes and cannot establish memory slope, physical
Ogg/Opus behavior, Azure Speech capacity, or a 25% headroom claim. Before a 60-minute step, investigate the tail
and define the pilot latency/headroom threshold; use a lower bounded tier if 20 robots cannot retain that margin.
