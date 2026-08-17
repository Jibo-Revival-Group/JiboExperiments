# Persistence Architecture

## Goal

Keep OpenJibo's stateful behavior portable while making durable production state database-backed and bounded.

In-memory stores are appropriate for tests, local development, and explicitly bounded active-session state. They are not an acceptable production source of truth for durable cloud or personal-memory data. The `2026-08-16/17` production OOM incident demonstrated that placing a PostgreSQL snapshot adapter behind `InMemoryCloudStateStore` still hydrates and rewrites the entire cloud state in process memory.

The tracked replacement work and incident evidence live in [feature-backlog.md](feature-backlog.md#13-replace-snapshot-backed-in-memory-cloud-state).

## Design Principles

- Application code talks to small, intent-specific interfaces.
- Persistence keys are always scoped by tenant and person where relevant.
- PostgreSQL is the managed and self-hosted source of truth for durable structured state.
- Blob/file storage holds binary media and backup payloads; PostgreSQL holds their manifests and integrity metadata.
- In-memory adapters are limited to tests/local development and bounded ephemeral connection/turn state.
- Long-lived data should be versioned so we can add optimistic concurrency later.
- Ephemeral turn/session state should stay separate from durable user and device state.
- A scoped mutation must not serialize or rewrite unrelated records or tenants.

## Current Seams

These are the contracts we should preserve:

- `IPersonalMemoryStore`
  - personal facts: names, birthdays, preferences, affinities, important dates, household lists
  - scope: account + loop + device + optional person
- `ICloudStateStore`
  - account, robot, loops, people, sessions, updates, media, backups, holidays, keys
  - scope: system-level state with loop/device/person records inside it
- `IJiboExperienceContentRepository`
  - catalog/content layer only

## Recommended Storage Split

### 1. Identity and topology store

Responsible for:

- account profile
- robot/device registration
- loop membership
- person records
- greeting/proactive presence metadata when it becomes durable

This belongs in normalized PostgreSQL tables with transactional writes and revision checks.

### 2. Personal memory store

Responsible for:

- names
- birthdays
- preferences
- affinities
- important dates
- household lists

This belongs in PostgreSQL keyed by account/loop/device/person. An in-memory implementation remains useful for tests only.

### 3. Session and short-lived orchestration state

Responsible for:

- websocket/session tokens
- temporary skill state
- active report/list/greeting interaction state

Active connection and turn state can stay in process only with TTL cleanup, per-session audio limits, total memory bounds, and disconnect cleanup. Durable issued-token material must be separated from live connection state and persisted safely when restart survival is required.

### 4. Media and backup store

Responsible for:

- uploaded media metadata
- backup manifests
- binary references

Payload bytes belong in Azure Blob Storage (or the self-hosted blob/file adapter) and manifests belong in PostgreSQL.

## Record Shape Guidance

For durable records, prefer a small shared envelope:

- `AccountId`
- `LoopId`
- `DeviceId`
- `PersonId` when relevant
- `RecordType`
- `RecordKey`
- `Value`
- `CreatedUtc`
- `UpdatedUtc`
- `Revision` or `ETag`

That gives us:

- easy partitioning later
- clear tenant boundaries
- room for concurrency checks
- a path to Azure Table, Cosmos, or SQL without changing behavior code

## Adapter Plan

### Phase 1: Incident Mitigation (Complete)

- prevent backups from recursively embedding the backup catalog
- compact legacy recursive backup payloads on load
- retain behavior tests around backup creation, persistence, and restore

### Phase 2: Production Store Replacement

- split durable state, backup/media, authentication-token, and ephemeral-session contracts
- add normalized PostgreSQL tables and scoped repository operations
- move backup/media payload bytes to Blob Storage and retain manifests in PostgreSQL
- import and verify the existing snapshot through an idempotent migration
- keep a compatibility adapter until each record family passes protocol and migration tests

### Phase 3: Scale And Remove Snapshot Fallback

- add replication/sync primitives if we need multi-server state convergence
- add multi-replica concurrency and cache-invalidation proof
- remove production snapshot fallback and alert if it is ever selected
- retain export snapshots only as operational recovery artifacts, never runtime truth

## Non-Goals For Now

- no Azure SDK types in application logic
- no event-sourcing rewrite
- no giant generic repository
- no distributed transaction work before single-node semantics are stable
- no attempt to make active audio buffers durable

## Immediate Next Step

Start the backlog item by tightening and splitting store contracts around:

- tenant/person scoping
- record versioning
- scoped query/upsert operations for durable state
- bounded ephemeral session ownership
- backup/media manifest versus payload ownership

Then implement the PostgreSQL schema and snapshot-import migration one cohesive record family at a time, preserving the personality, report, greeting, list, and stock backup/restore behaviors behind compatibility tests.
