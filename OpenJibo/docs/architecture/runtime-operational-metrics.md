# Runtime Operational Metrics

Status date: `2026-08-26`

OpenJibo emits privacy-safe aggregate measurements through the .NET meter `OpenJibo.Transport`. These
measurements are intended to establish a concurrency and cost envelope; they are not a customer activity log.
Robot IDs, session IDs, transcripts, audio, credentials, connection strings, and free-form error text must never
be added as metric attributes.

## Application Instruments

| Instrument | Type | Unit | Attributes |
| --- | --- | --- | --- |
| `openjibo.turn.active` | up/down counter | turns | none |
| `openjibo.turn.phase.duration` | histogram | ms | `phase`, `outcome` |
| `openjibo.turn.phase.operations` | counter | operations | `phase`, `outcome` |
| `openjibo.turn.finalization_suppressions` | counter | suppressions | `reason` |
| `openjibo.turn.reply_batches` | counter | batches | `has_eos` |
| `openjibo.turn.reply_count` | histogram | replies | `has_eos` |
| `openjibo.audio.current_buffered_bytes` | observable gauge | bytes | none |
| `openjibo.audio.buffered_high_water_bytes` | observable gauge | bytes | none |
| `openjibo.audio.accepted_bytes` | counter | bytes | none |
| `openjibo.audio.buffer_limit_rejections` | counter | rejections | none |
| `openjibo.audio.rejected_bytes` | counter | bytes | none |
| `openjibo.persistence.cache.accesses` | counter | accesses | `store`, `result` |
| `openjibo.persistence.postgresql.configured_max_connections` | observable gauge | connections | `store` |

The transport HTTP, WebSocket, connection, and active-session instruments remain in the same meter. Turn phases
are limited to `stt`, `plan`, `finalize`, and `other`. Outcomes are limited to `success`, `bypassed`,
`unavailable`, `failure`, `canceled`, and `other`. Persistence store, cache result, suppression reason, socket,
payload, message, endpoint, method, and status attributes have fixed allowlists in `TransportMetrics`; unknown
values collapse to `other`.

`finalize` is the current end-to-end server-side finalization interval. `plan` covers conversation routing and
plan creation. `stt` covers selection and transcription, or records a bypass when the robot supplied a usable
transcript or no audio required transcription. Robot acknowledgement latency is not observable in the current
wire protocol.

The audio high-water value is monotonic for the life of one process and resets when that replica restarts. The
configured PostgreSQL gauge is a ceiling, not live pool use.

## Collection And Provider Metrics

Managed Azure deployments provision a workspace-backed Application Insights resource and register the Azure
Monitor OpenTelemetry metrics exporter when `APPLICATIONINSIGHTS_CONNECTION_STRING` is present. The exporter
subscribes to:

- `OpenJibo.Transport` for the application instruments above;
- the .NET runtime metrics for working set, managed heap, allocation rate, GC collections, and pause duration;
- Npgsql's native metrics for pool connections, pending requests/waits, command duration, and failures;
- Azure Container Apps and Azure Database for PostgreSQL platform metrics for replica, CPU, memory, restart,
  database CPU/storage/connection, and network evidence.

Only the metrics signal is exported by the application; request traces and Serilog events continue through the
existing Container Apps Log Analytics path so enabling operational measurements does not duplicate those data.
Npgsql data sources use the bounded names `cloud_state` and `personal_memory`; never allow a connection string
to become the pool-name attribute.

Do not duplicate Npgsql internals with a second application-side pool tracker. Reconcile the provider's live pool
measurements against `openjibo.persistence.postgresql.configured_max_connections` instead.

## Capacity Worksheet

For each load-test tier (`6`, `10`, `15`, and `20` connected fake robots), retain the same time window and record:

1. connected sockets, active turns, finalize throughput, and success/failure/cancellation counts;
2. STT, plan, and finalize P50/P95/P99 durations;
3. current and high-water audio bytes plus rejection count;
4. cache hit ratio by bounded store;
5. configured and observed PostgreSQL connections, pool waits, command duration, and errors;
6. per-replica CPU, working set, managed heap, GC pauses, restarts, and revision identity.

Use `hits / (hits + misses)` for cache ratio. Treat any pool wait, audio rejection, restart, OOM, rising overnight
memory slope, or cross-replica inconsistency as a failed tier even when average latency looks acceptable. A tier
is not the enrollment cap until its highest simultaneous-turn case retains at least 25% measured headroom.
