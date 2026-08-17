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

## Required For The Paid `1.0.20` Managed Service

- normalized PostgreSQL durable state with Blob/file payload storage and bounded ephemeral memory
- customer authentication, account recovery, household/robot ownership, and credential lifecycle
- billing-provider integration, durable subscription state, entitlement enforcement, reconciliation, cancellation, and payment recovery
- customer signup/account/subscription/onboarding surface with reviewed terms, privacy, refund/cancellation, support, and status paths
- dependency-aware health, monitoring/alerts, backups, restore/rollback drills, load/soak proof, security review, and staffed support/business operations
- a capped paid pilot that passes the criteria in [release-1.0.20-paid-launch-plan.md](release-1.0.20-paid-launch-plan.md)

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
