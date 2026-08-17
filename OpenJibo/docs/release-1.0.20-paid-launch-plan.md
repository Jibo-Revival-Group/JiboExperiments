# Open Jibo Cloud `1.0.20` Paid Service Launch Plan

## Purpose

This is the launch-control document for completing Open Jibo Cloud `1.0.20` and opening the managed hosted service to paying customers.

Use [release-1.0.20-plan.md](release-1.0.20-plan.md) for feature history, [feature-backlog.md](feature-backlog.md) for the complete work queue, and this document for launch ordering and go/no-go decisions.

## Current Decision

As of `2026-08-17`, the paid service is **NO-GO**.

The repository has a strong compatibility core, more than two thousand automated cloud tests, PostgreSQL and Azure Blob adapters, managed deployment scripts, onboarding replay smoke coverage, and a working admin surface. It does not yet have the minimum product and operational controls required to accept recurring payments:

1. Durable cloud state is still owned by a singleton `InMemoryCloudStateStore` and persisted as a whole JSON snapshot. The `2026-08-16/17` memory incident proved this is a production reliability blocker.
2. There is no separate customer authentication/account service for the proposed `auth.openjibo.com` boundary.
3. There is no subscription, billing-provider, webhook, entitlement, or cancellation implementation.
4. The public site is a placeholder and does not provide signup, account management, legal terms, support, or service-status flows.
5. Production readiness does not yet include dependency-aware readiness probes, paid-service monitoring and alerts, restore/rollback drills, security review, or a repeatable paid-customer support process.

Passing unit tests or returning `200` from `/health` is not sufficient to change this decision.

## Launch Definition

`1.0.20` is complete for paid launch only when all P0 gates below are closed with recorded evidence and a paid pilot has completed successfully. Optional personality expansion, direct Jibo-to-Jibo messaging, easy-button conversion, hybrid-cloud sync, and broader device variants do not block the first paid pilot unless testing shows they affect the supported launch cohort.

The first supported paid offer should be deliberately narrow:

- one managed hosted-cloud plan
- one owner account with one household/loop
- a documented robot limit per subscription
- the currently proven enthusiast-assisted conversion/onboarding path
- supported device/firmware rows named explicitly before checkout
- self-hosted use remains separate and does not require a paid entitlement
- manual production promotion after staging and real-robot proof

## Decisions Required Before Billing Work

Record these as product decisions before creating live products or prices:

- plan name, monthly price, billing currency, included robot count, and whether an introductory trial exists
- initial sales countries/regions and minimum customer eligibility
- supported Jibo hardware/firmware/conversion matrix for the paid offer
- payment processor and merchant account owner
- cancellation effective date, grace period, failed-payment retry behavior, refund policy, and dispute handling
- support channel, support hours, response target, and who owns incidents
- launch cohort size, pilot duration, success thresholds, and refund/rollback authority
- service SLO, maintenance-window policy, and target RPO/RTO
- data retention defaults for audio, diagnostic captures, logs, media, backups, and deleted accounts

These are owner decisions. Engineering may provide options, but should not silently invent commercial or legal policy.

## P0 Launch Gates

### Gate 1: Release Scope And Compatibility Freeze

Status: `in progress`

Required work:

- freeze `1.0.20` to paid-launch blockers and regressions; move unrelated feature growth to `1.0.21+`
- name the supported device, firmware, and conversion rows for the initial cohort
- finish the live robot regression bundle for startup, onboarding, reconnect, conversation, STT, update/backup/restore, media, stop/volume, sleep/wake, and identity merge behavior
- close or explicitly waive each remaining live-only `polish` item with evidence and a recorded risk owner
- produce a release candidate changelog and known-issues list

Exit evidence:

- full automated suite passes from a clean checkout
- managed deployment smoke passes against staging
- named real-device matrix passes the release regression plan
- no open P0 compatibility defect
- release scope, known issues, and supported cohort are published

### Gate 2: Replace Snapshot-Backed In-Memory Production State

Status: `ready` — **hard blocker**

Canonical backlog item: [Replace Snapshot-Backed In-Memory Cloud State](feature-backlog.md#13-replace-snapshot-backed-in-memory-cloud-state).

Required work:

- split durable repositories from live connection/turn state
- normalize accounts, loops, people, robots, credentials, durable tokens, updates, media metadata, backup manifests, integrations, memory, and trust records into PostgreSQL tables
- move backup payloads and media bytes to Azure Blob Storage; store bounded metadata and hashes in PostgreSQL
- replace whole-cloud serialization with scoped transactional queries and optimistic concurrency
- keep only bounded, expiring connection and turn data in memory
- add per-session audio limits, global memory bounds, disconnect cleanup, and overload behavior
- migrate the existing snapshot idempotently, validate record counts/identity links, and retain a rollback export
- prove two replicas do not diverge or overwrite one another
- add persistence latency/failure, working-set/GC, active-session, buffered-audio, and payload-size telemetry

Exit evidence:

- production DI contains no in-memory durable source of truth
- no routine mutation rewrites the whole cloud
- migration dry-run/apply/verify/rollback rehearsal succeeds against a production-shaped copy
- backup growth is linear and payloads are outside relational metadata rows
- reconnect/audio soak remains within the `2 GiB` limit with cleanup after abnormal disconnects
- two-replica concurrency and restart tests pass

### Gate 3: Customer Identity, Ownership, And Access Service

Status: `not started` — **hard blocker**

Required work:

- create the separately deployable auth/account service described by the topology plan
- implement customer signup, verified email, login, logout, password reset, session/token rotation, and account recovery
- define owner/admin/support roles and require stronger authentication for operator access
- bind customer accounts to households/loops and robots without trusting wire-reported identity as ownership proof
- implement robot claim, transfer, removal, and duplicate/merge audit flows
- separate durable robot credentials from WebSocket connection state and store reusable secrets safely
- provide customer data export and account deletion workflows
- replace the single shared admin password as the long-term production operator identity boundary

Exit evidence:

- a customer can create and recover an account, claim a supported robot, and see only their household data
- cross-account access tests fail closed across HTTP, WebSocket, media, backup, and portal surfaces
- support impersonation or override, if retained, is time-bounded and audited
- credential rotation and revocation work without orphaning the robot

### Gate 4: Billing, Subscription State, And Entitlements

Status: `not started` — **hard blocker**

Required work:

- add a provider-neutral billing seam and select the first payment provider
- create versioned product/price configuration outside application source
- implement hosted checkout and a customer billing-management portal
- persist customer, subscription, price, invoice, and entitlement references without storing card data
- verify webhook signatures and implement idempotency, ordering tolerance, replay tooling, retry/dead-letter handling, and audit history
- model at least `pending`, `trialing`, `active`, `past_due`, `grace`, `canceled`, `suspended`, and `refunded/disputed` outcomes
- make subscription state produce a durable managed-cloud entitlement with an explicit robot allowance
- enforce entitlement at onboarding, token issuance/renewal, HTTP protocol entry, and WebSocket connection/reconnect
- define safe cancellation behavior: do not corrupt or delete robot data, clearly revoke managed access, and provide authorized recovery/resubscribe/self-hosted paths
- add periodic provider reconciliation so missed webhooks cannot leave access permanently wrong
- test checkout success/failure, duplicate/out-of-order events, payment failure/recovery, plan change, cancellation, refund, dispute, and resubscription in the provider sandbox

Exit evidence:

- no managed robot remains active without a valid entitlement except an explicitly audited grace/support override
- duplicate or delayed webhooks cannot duplicate accounts, robots, subscriptions, or entitlements
- cancellation and payment recovery behave according to the published policy
- billing reconciliation reports and repairs drift safely
- test and live provider environments cannot be mixed accidentally

### Gate 5: Customer Site, Onboarding, And Legal Surface

Status: `not started` — **hard blocker**

Required work:

- turn `openjibo.com` from the placeholder scaffold into the product overview and account entry surface
- implement the `auth.openjibo.com` and `members.openjibo.com`/`cloud.openjibo.com` handoff with signed, expiring state/nonce binding
- show price, billing interval, included robots, supported-device requirements, conversion risk, renewal/cancellation terms, and service limitations before purchase
- implement signup, checkout return, onboarding resume, account, robot, subscription, cancellation, and recovery screens
- publish Terms of Service, Privacy Policy, refund/cancellation policy, acceptable-use rules, support contact, and service-status link after appropriate business/legal review
- make consent/version acceptance auditable
- add accessible error states for failed payment, unsupported robot, entitlement loss, and service outage

Exit evidence:

- a new pilot customer can move from public site to payment, account, robot claim, onboarding, first successful turn, and account management without operator database edits
- failed or abandoned checkout resumes safely
- policy versions accepted at purchase are recorded
- accessibility, mobile, and major-browser smoke checks pass

### Gate 6: Security And Privacy Review

Status: `not started` — **hard blocker**

Required work:

- threat-model public site, auth, billing callbacks, robot APIs/WebSockets, admin portal, provider handoffs, media, backups, and fleet sync
- remove committed/default production credentials and rotate any value that may have been exposed
- replace permissive production CORS with an explicit origin policy
- add endpoint-specific rate limits, abuse controls, request/body limits, and lockout protection
- verify authorization on every customer, robot, media, backup, admin, and billing route
- define encryption, key rotation, secret ownership, log redaction, and audit retention
- implement data retention/deletion for logs, audio, media, backups, billing references, and account data
- add dependency, secret, container, and source scanning to CI; produce an SBOM for the release image
- perform an external or independent security review before general availability

Exit evidence:

- no unresolved critical/high finding without a written, time-bounded exception
- secrets, webhook signing keys, and operator credentials can be rotated without redeploying source changes
- privacy export/deletion and retention jobs are tested
- production logs do not expose reusable credentials, payment payload secrets, or unnecessary customer audio/content

### Gate 7: Production Operations, Recovery, And Cost Controls

Status: `in progress` — **hard blocker**

Required work:

- isolate dev, staging, and production resources, identities, billing environments, databases, storage, and secrets
- add dependency-aware readiness plus liveness/startup probes; `/health` must not be the only promotion signal
- define SLOs and instrument request success/latency, WebSocket connection health, robot turn completion, STT/provider failures, billing webhook lag/failures, database/Blob health, memory/GC, replica count, and queue/backlog depth
- configure actionable alerts, an on-call owner, escalation contacts, and a public status/incident communication path
- write runbooks for memory pressure, database/Blob outage, provider outage, webhook backlog, stuck entitlement, credential compromise, bad deployment, and regional failure
- configure database backups/PITR and Blob protection; run restore drills and record RPO/RTO evidence
- prove deployment rollback and database-forward-compatibility behavior
- add capacity/load/soak tests and set autoscaling limits that WebSocket behavior can tolerate
- add budgets and alerts for Container Apps, PostgreSQL, Blob, STT, search/news/weather providers, email, and payment fees; verify the plan has positive unit economics at expected usage

Exit evidence:

- staging-to-production promotion and rollback are rehearsed
- one restore drill and one incident exercise complete successfully
- alerts page a named owner and link to a working runbook
- sustained pilot-shaped load meets the SLO and cost envelope
- production has no single undocumented operator dependency

### Gate 8: Release Engineering And Supply Chain

Status: `in progress`

Required work:

- make pull-request CI, not only pushes to `main`, run the release test and deployment-contract gates
- pin/approve third-party workflow actions and enforce protected production approval
- publish immutable semantic image tags and record the source commit, migrations, configuration version, and dependency inventory
- add image vulnerability scanning, SBOM generation, provenance/signing, and release artifact retention
- add billing/auth integration suites, migration tests, authorization tests, and production-configuration validation
- prevent production deployment when required secrets, domains, certificates, probes, migrations, or provider settings are absent

Exit evidence:

- a release candidate is reproducible from a clean checkout
- the deployed revision can be traced to source, image digest, migration set, and configuration
- production promotion requires successful automated gates plus explicit approval
- rollback uses an immutable prior image and a rehearsed data-compatibility path

### Gate 9: Customer Support And Business Operations

Status: `not started` — **hard blocker for taking money**

Required work:

- establish the merchant/business owner, payout account, bookkeeping flow, tax handling, and invoice/receipt ownership
- have qualified reviewers approve the commercial terms, privacy policy, refund/cancellation policy, and required customer disclosures
- create a support inbox/ticket path, customer identity-verification procedure, response templates, and escalation rules
- document refund, cancellation, chargeback, service-credit, account recovery, robot transfer, and bereavement/ownership-transfer handling
- prepare launch communications, known issues, setup requirements, maintenance messaging, and outage templates
- define pilot and launch metrics: checkout conversion, activated subscriptions, first-turn success, daily robot success, support contacts, churn, refunds, payment failure, incident rate, and cost per active robot

Exit evidence:

- a real customer can receive a receipt, request help, cancel, obtain any policy-compliant refund, and recover access through documented processes
- financial reconciliation has a named owner and repeatable cadence
- support and incident coverage exists for every period in which paid customers can use the service

## Launch Sequence

### Phase A: Scope And Architecture Lock

1. Make the commercial/support decisions.
2. Freeze the `1.0.20` launch scope and supported cohort.
3. Lock auth, billing-provider, entitlement, data-retention, and normalized-storage contracts.
4. Create acceptance tests before implementation begins.

### Phase B: Reliability Foundation

1. Complete normalized PostgreSQL/Blob persistence and migrate production-shaped data.
2. Bound all ephemeral memory and prove multi-replica behavior.
3. Add readiness, telemetry, alerts, backups, restore, and rollback controls.
4. Run the load/soak test that reproduces the class of the August memory incident.

No billing implementation should be promoted to production before this phase passes.

### Phase C: Customer And Revenue Path

1. Deliver customer auth and household/robot ownership.
2. Deliver provider-neutral billing and the first provider adapter.
3. Deliver durable entitlements and enforcement at every access path.
4. Deliver the customer site, account area, onboarding return, and cancellation/recovery flows.
5. Complete security, privacy, and business/legal review.

### Phase D: Release Candidate

1. Freeze code except P0 fixes.
2. Run clean automated, migration, billing sandbox, authorization, load, deployment, and rollback suites.
3. Run the supported real-robot regression matrix.
4. Publish RC notes, setup requirements, supported devices, limitations, policies, and support contacts.
5. Conduct an operational game day.

### Phase E: Paid Pilot

1. Start with a small named cohort and a hard subscription/robot cap.
2. Manually review every activation and failed onboarding.
3. Reconcile billing and entitlements daily.
4. Review reliability, support volume, cost, churn/refunds, and security signals at a fixed cadence.
5. Pause enrollment automatically or operationally when a stop condition is reached.

Pilot exit criteria:

- pilot duration and minimum active-robot days meet the predeclared target
- activation and first-turn success meet the target
- no unresolved P0/P1 incident or billing/ownership data-loss defect
- restore and rollback remain proven against the release candidate
- support load and cost per active robot fit the published offer
- every cancellation/refund/payment-failure case in the cohort reconciles correctly

### Phase F: General Availability

1. Record a signed go/no-go review covering every P0 gate.
2. Promote the immutable release image and migrations.
3. Open enrollment gradually with capacity and budget caps.
4. Monitor the launch dashboard and support queue continuously during the launch window.
5. Publish the final changelog, known issues, status link, and support path.

## Launch Stop Conditions

Stop new enrollment and assess rollback when any of these occurs:

- customer or robot ownership crosses account boundaries
- payment state and managed entitlement materially disagree
- cancellation cannot revoke access according to policy or revokes the wrong robot/account
- durable state is lost, corrupted, or overwritten across replicas
- memory growth is unbounded or turn completion degrades under pilot load
- secrets or customer data are exposed
- restore/rollback cannot meet the declared recovery target
- support coverage is unavailable for an active paid cohort

## Post-Launch, Not `1.0.20` Blockers

- easy-button/zero-touch conversion and unproven device variants
- direct Jibo-to-Jibo transport and messaging
- public hybrid-cloud synchronization
- multiple paid tiers, annual billing, coupons, gifts, referrals, or usage billing
- mobile apps beyond the minimum supported onboarding surface
- broader personality catalog expansion that does not fix a launch regression
- advanced calendar, rideshare, delivery, smart-home, and tiered-brain integrations

These remain valuable, but they must not obscure the paid-service reliability, identity, billing, entitlement, and support gates.

## Go/No-Go Record

The final launch review must record:

- release commit and image digest
- database migration version and rollback/export location
- supported device/firmware matrix
- completed regression, load, security, restore, billing, and pilot evidence
- open exceptions with owner and expiration
- operational, support, finance, and product approvers
- launch time, enrollment cap, monitoring owner, and rollback authority

