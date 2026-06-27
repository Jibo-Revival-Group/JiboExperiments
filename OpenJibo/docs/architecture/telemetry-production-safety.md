# Telemetry Production Safety

This note defines what "production safe" means for Open Jibo Cloud telemetry after the managed deploy proved that the current file-based capture sinks can break request handling.

## Short Answer

Production-safe telemetry means:

1. Request handling never fails because telemetry failed.
2. Diagnostic logs are centralized and durable.
3. Rich capture data is written to an external store, not the container filesystem.
4. Capture can be disabled without affecting API behavior.

## Recommended Split

### 1. Application Logs

Use Serilog for normal service logging.

Recommended production target:

- stdout/stderr in the container
- Azure Monitor or Application Insights at the platform level

What belongs here:

- startup/shutdown
- warnings and errors
- auth and protocol failures
- dependency failures
- telemetry write failures, when they occur

What does not belong here:

- full HTTP bodies
- websocket frame payloads
- bulky fixture exports

### 2. Capture Telemetry

Keep the existing request/response and websocket capture concept, but move it away from local files in managed hosting.

Recommended production target:

- Azure Blob Storage for raw capture artifacts and exported fixtures
- optional queue-backed ingestion if capture volume grows

What belongs here:

- HTTP request/response NDJSON
- websocket event NDJSON
- session fixture exports
- capture index manifests

### 3. Failure Policy

Telemetry must be best-effort.

Rules:

- if capture storage is unavailable, drop the capture and log a warning
- never bubble capture exceptions back into the protocol dispatch path
- never depend on writable local container disk for managed production
- keep telemetry toggles so capture can be disabled during incidents

## Recommended Managed Setup

For the managed Container Apps deployment:

- keep Serilog enabled
- send logs to the platform log pipeline
- leave the file-based protocol/websocket/turn capture sinks disabled by default
- if hosted capture is needed later, add a Blob-backed sink and enable it explicitly

That matches the fix we just used to unblock deploy: request flow stays healthy even when capture storage is not available.

## Practical Shape

The production-safe implementation should probably end up as two separate paths:

1. **Logging path**
   - Serilog
   - platform log export
   - low-friction, always on

2. **Capture path**
   - explicit capture sink
   - Azure Blob Storage or similar durable backing
   - optional, rate-limited, and redacted
   - safe to disable

## Why Not Container Files

The managed deploy showed the core risk:

- container filesystem semantics vary by host
- permissions and paths can differ between dev and production
- write failures become request failures unless every call site is hardened
- multiple replicas can fragment capture data

Local files are fine for dev and ad hoc replay, but not as the durability boundary for production capture.

## Next Implementation Step

If we want to re-enable managed capture later, the next slice should be:

1. add a blob-backed `ISnapshotStore` or capture sink
2. keep request handling isolated from storage errors
3. add tests for storage outage behavior
4. enable it only in explicit managed environments

