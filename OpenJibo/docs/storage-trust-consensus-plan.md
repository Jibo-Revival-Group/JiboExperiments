# Storage Trust And Consensus Plan

## Purpose

This plan defines how Open Jibo should store durable state and decide which servers belong in a shared network.

The goal is to support:

- isolated self-hosted servers
- managed Open Jibo cloud servers
- later hybrid and multi-server sync
- safe handling of cloned, repaired, or previously modified robots
- revocation when a server or identity can no longer be trusted

## Core Principle

The cloud should trust its own issued identities and signed state, not whatever a robot or peer server claims by default.

That means:

- stock robot identity is a legacy claim, not a primary key
- Open Jibo-issued identity is the trust root for new cloud behavior
- server membership in a shared network must be explicit
- synchronization is opt-in and revocable

## Storage Layers

Split storage by responsibility, not by technology:

### 1. Identity And Topology Store

Responsible for:

- people
- loops
- robots
- server records
- issued robot identities
- trust state
- revocation state
- account-to-loop and loop-to-robot relationships

This is the most security-sensitive store.

### 2. Durable Behavior Store

Responsible for:

- personal memory
- preferences
- birthdays and dates
- holidays
- lists
- presence/greeting policy state when it becomes durable

### 3. Session Store

Responsible for:

- websocket tokens
- active turn/session state
- temporary prompt state

This should stay short-lived and recoverable.

### 4. Media And Backup Store

Responsible for:

- uploaded media bodies or references
- backup manifests
- restore artifacts
- capture bundles

## Canonical Envelope

Durable records should use a common envelope shape where practical:

- `AccountId`
- `LoopId`
- `DeviceId`
- `PersonId` when relevant
- `ServerId` when relevant
- `RecordType`
- `RecordKey`
- `Value`
- `CreatedUtc`
- `UpdatedUtc`
- `Revision` or `ETag`
- `Signature` when a record must be verified across servers

This supports:

- tenant scoping
- person scoping
- concurrency control
- later replication
- later auditability

## Signed At Rest Options

We do not need to decide every signed-record rule yet, but we should narrow the candidate sets now.

Candidate record groups to sign before replication:

- identity and topology records
- server admission and revocation records
- issued robot identity records
- provider onboarding results that must survive cross-server handoff
- versioned snapshots that leave one server and land on another

Candidate record groups that can stay unsigned locally for now:

- short-lived session state
- temporary prompt state
- ephemeral turn/session artifacts

Exploration path:

- start by signing the records that control trust or network membership
- then decide whether durable behavior records need signatures only when they replicate cross-server
- avoid signing everything by default unless the replication story proves it is necessary

Open question: choose which durable record classes should be signed at rest before replication versus signed only at the snapshot boundary.

Decision:

- sign identity and topology records at rest before replication
- sign server admission and revocation records at rest before replication
- sign issued robot identity records at rest before replication
- sign provider onboarding results at rest when they must survive cross-server handoff
- sign versioned snapshots before replication
- keep short-lived session state, temporary prompt state, and ephemeral turn/session artifacts unsigned for now
- keep ordinary durable behavior records unsigned locally until they are part of a replicated snapshot or another trust boundary that requires a signature

This gives us a narrow trust boundary without forcing every local write through a signing path before the sync model is proven.

## Trusted Identity Model

Observed robot fields should be treated as claims until validated.

Recommended model:

- preserve legacy robot fields for recovery and audit
- issue a new Open Jibo robot identity during onboarding or conversion
- bind legacy claims to the new identity only after validation
- persist the issued identity back to the robot when possible
- treat repeated presentation of legacy identity without the issued identity as a clone/repair scenario

Open question: decide which robot fingerprints are safe enough to use as supporting signals without making recovery impossible.

Decision:

- use supporting signals, not a single fingerprint
- preferred supporting signals are:
  - hardware-derived or hardware-stable `DeviceId`
  - issued Open Jibo robot identity/token lineage
  - certificate/public-key thumbprint when the robot presents one
  - robot build/version/distribution
  - stable configuration hashes for the specific onboarding/trust files we control
- treat robot name, loop membership, person data, media, and favorites as recovery context, not identity fingerprints
- do not use any single supporting signal as a hard lock by itself
- if the supporting signals disagree materially, route the robot into clone/repair onboarding instead of silently merging identities

This gives us a strong enough signal set to detect clones or repairs while keeping owner recovery possible.

## Server Membership Model

Every server that participates in shared sync should have:

- a stable server identity
- an issuer or trust root
- a signed admission record
- a revocation record
- a defined role such as managed, self-hosted, AI, or developer

Recommended states:

- `trusted`
- `provisioned`
- `sync-enabled`
- `revoked`
- `quarantined`
- `expired`

Recommended behavior:

- a server cannot join the network without an explicit admission event
- a server cannot sync peers until its identity is trusted
- a revoked server cannot continue to receive trusted state
- a quarantined server can be isolated for investigation without immediately deleting its data

Admission model:

- all servers on the network should be aware of the other servers that participate in sync
- when a server requests sync enrollment, the network performs a straw-poll consensus check
- if any server has evidence the requester is not trusted, the sync request is denied
- this is a deny-by-evidence model, not a majority-wins model
- admission only succeeds if the network does not surface a trust objection

## Revocation And Poison Pill

Revocation should be explicit and logged.

When a bad-actor or compromised server is removed:

- revoke its server identity
- revoke any sync credentials
- stop accepting future signed state from it
- mark its issued robot identities as quarantined if they were only known through that server
- mark any local data that was only known through that server as quarantined for review
- optionally re-issue identities later after an owner-controlled re-enrollment or repair flow

The "poison pill" concept should be a controlled revocation marker, not an arbitrary destructive action.

Recommended shape:

- a revocation record signed by the root trust authority
- a server-level deny state
- optional robot-level quarantine state for identities that originated only from that server
- optional re-issuance flow for robots and people that need to re-enroll elsewhere

Decision direction: quarantine first, then re-issue only when the owner or operator explicitly moves the robot back through onboarding or repair. Soft invalidation is too lossy for the first pass.

## Sync Strategy

Do not begin with full multi-master consensus.

Recommended progression:

1. Single authoritative cloud
2. Self-hosted isolated
3. One-way enrollment into shared sync
4. Explicit server admission and revocation
5. Versioned snapshots for replication
6. Later multi-server convergence or consensus only after the above is stable

This keeps the first release honest and prevents hidden distributed-state bugs.

Sync triggers:

- if a connected robot changes state on a server, that state should be pushed and pulled as needed to keep the network aligned
- if a server recently connected and the system suspects it may be out of date, a periodic poll can check for missing snapshot versions
- the periodic poll only needs to cover recently connected or recently changed entities, not the entire network
- if a snapshot version is missed, the next sync should fetch the next version in order and apply it before proceeding

Versioned snapshot behavior:

- snapshots should carry a monotonic version or revision marker
- snapshots should be applied in order
- if a snapshot is missed, the next sync should retrieve the missing version rather than assuming later state is sufficient
- snapshot application should be idempotent where possible
- snapshots should be full snapshots for the first release of the sync path
- explicit change records and incremental manifests remain later enhancements if we need finer-grained sync after the snapshot path is stable

## Data Ownership Rules

Different categories should have different ownership expectations.

### Local-only

- temporary session state
- ephemeral turn state
- short-lived onboarding state
- device-local cryptographic material such as private keys and local trust handles
- local cache or replay buffers that are only useful on the current robot or server

Decision:

- keep only transient session/onboarding artifacts and device-local secrets permanently local-only
- do not classify durable owner data as permanently local-only yet

### Owner-scoped but syncable later

- personal memory
- lists
- greetings and presence history
- account/loop/device association data

This category is intentionally not promoted to permanently local-only yet. We should keep it flexible until hybrid behavior, trust boundaries, and repair flows are better defined.

### Shared-network authoritative

- server trust
- robot issuance
- revocation state
- provider onboarding decisions

## Failure Modes

Plan for these explicitly:

- a cloned robot presents an old legacy identity
- a repaired robot returns with partial state
- a server goes offline after receiving sync data
- a server behaves maliciously or inconsistently
- a trusted server is later revoked
- a user moves from managed cloud to self-hosted and back through a reset path

## Storage Technology Guidance

Keep the application code store-agnostic.

Recommended implementation direction:

- identity/topology: SQL first
- durable behavior: SQL first where transactional, otherwise adapter-backed persistent store
- media/backup: file/blob-backed store with metadata in SQL
- session: in-memory or cache-backed initially

The plan should stay compatible with the storage abstraction already documented in [persistence-architecture.md](persistence-architecture.md).

## `1.0.20` Exit Criteria

This track is ready to build when:

- identity/topology state has a clear trusted source
- server admission and revocation states are defined
- clone/repair handling is defined for issued robot identities
- storage categories are split into local, owner-scoped, and network-authoritative groups
- the network sync story is explicitly one-way first

This track is ready to close for `1.0.20` when:

- storage abstractions can represent identity, behavior, session, media, and backup separately
- a bad-actor server can be revoked cleanly
- a cloned robot can be re-issued or quarantined without poisoning other owners
- the managed cloud can remain authoritative without forcing self-hosted servers to depend on it

## Denial Evidence

When a server is denied sync enrollment, record a signed deny-evidence packet.

- record a signed deny-evidence packet for every rejected sync enrollment
- include the requesting server identity, time, denial source, evidence type, and the signed object or audit reference that triggered the denial
- include whether the denial is temporary quarantine, hard revocation, or owner-review pending
- keep the packet append-only so later re-enrollment can explain why the previous attempt failed
