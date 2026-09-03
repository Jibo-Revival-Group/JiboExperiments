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
