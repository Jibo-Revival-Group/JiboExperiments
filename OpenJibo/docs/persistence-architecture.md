# Persistence Architecture

## Goal

Keep OpenJibo's stateful behavior portable now and Azure-ready later.

The current in-memory stores are fine as the default implementation, but the app should depend on stable persistence contracts rather than directly on in-memory collections or file formats.

## Design Principles

- Application code talks to small, intent-specific interfaces.
- Persistence keys are always scoped by tenant and person where relevant.
- In-memory, local JSON, and hosted Azure stores are adapters, not behavior sources.
- Long-lived data should be versioned so we can add optimistic concurrency later.
- Ephemeral turn/session state should stay separate from durable user and device state.

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

This is the seam most likely to become Azure SQL or Cosmos later.

### 2. Personal memory store

Responsible for:

- names
- birthdays
- preferences
- affinities
- important dates
- household lists

This can remain in memory now and later move to a durable store keyed by account/loop/device/person.

### 3. Session and short-lived orchestration state

Responsible for:

- websocket/session tokens
- temporary skill state
- active report/list/greeting interaction state

This can stay in-process for now, but should be clearly separated from durable memory.

### 4. Media and backup store

Responsible for:

- uploaded media metadata
- backup manifests
- binary references

This is a good candidate for Azure Blob Storage plus a metadata table later.

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

### Phase 1

- keep `InMemoryPersonalMemoryStore`
- keep `InMemoryCloudStateStore`
- make sure all callers use the interfaces only
- add tests against behavior, not implementation details

### Phase 2

- introduce durable adapters behind the same interfaces
- likely split:
  - SQL or Cosmos for identity/topology
  - Blob or table-backed store for media/backup metadata
  - table/SQL-backed memory store for personal facts

### Phase 3

- add replication/sync primitives if we need multi-server state convergence
- prefer explicit change records or versioned snapshots over hidden shared state

## Non-Goals For Now

- no Azure SDK types in application logic
- no event-sourcing rewrite
- no giant generic repository
- no distributed transaction work before single-node semantics are stable

## Immediate Next Step

Before building durable adapters, tighten the store contracts around:

- tenant/person scoping
- record versioning
- explicit load/save operations for durable state

That lets us swap the backing store later without changing the personality, report, greeting, or list behaviors already built on top.
