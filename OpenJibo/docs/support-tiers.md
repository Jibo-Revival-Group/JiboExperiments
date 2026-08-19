# Support Tiers

## Purpose

This document keeps the revival effort honest about what must work first, what can wait for parity, and what belongs to later modernization.

## Required For Core Revive

- hosted replacement cloud reachable through legacy Jibo host routing
- token, account, robot, and session bootstrap flows needed for startup
- basic WebSocket connectivity for listen and proactive channels
- minimal turn handling and a normalized `ResponsePlan` path
- Azure deployment foundation
- durable PostgreSQL state plus Blob/file payload-storage design
- bootstrap documentation for router, DNS, RCM, TLS patching, and smoke tests

## Required For Any Managed Hosting Provider

- normalized durable state with Blob/file payload storage and bounded ephemeral memory
- provider-neutral account/robot ownership, credential lifecycle, onboarding handoff/return, revocation, export, and recovery contracts
- dependency-aware health, monitoring, backups, restore/rollback, load/soak proof, security review, and a published support/status path
- explicit operator identity, service terms, privacy/retention, backup behavior, and exit path

Provider-specific price, billing, entitlement, customer policy, and support operations belong to the provider. Transcendent Software's implementation is private and represented publicly by [cloud.openjibo.com](https://cloud.openjibo.com).

## Optional For Parity

- broader `X-Amz-Target` family coverage
- richer media management
- more complete key and sharing flows
- higher-fidelity update metadata behavior
- more native-skill bridging and expression parity
- more complete per-version device behavior mapping

## Future Modernization

- OTA-first recovery for non-technical owners
- additional hosted tiers, annual/usage billing, coupons, gifts, referrals, and donation flows beyond the first paid plan
- deeper on-device modernization
- richer runtime orchestration and AI providers
- community plugin or skill ecosystem
- OS, bridge, and firmware modernization beyond hosted-cloud recovery
