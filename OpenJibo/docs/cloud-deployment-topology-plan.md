# Cloud Deployment And Topology Plan

## Purpose

This plan defines how Open Jibo Cloud should be packaged and deployed for `1.0.20` and beyond.

The main goal is to choose a deployment path that can serve the managed Open Jibo cloud, local/self-hosted installs, developer testing, and future AI/cloud-network modes without forcing separate architectures too early.

## Recommendation

Use containers as the shared deployment primitive.

For the first managed cloud target, prefer Azure Container Apps over Azure App Service or AKS.

For the first self-hosted target, prefer Docker Compose.

This gives the project one packaging path that works for:

- managed Open Jibo Cloud
- self-hosted Open Jibo Cloud
- developer test instances
- future hybrid sync nodes
- future AI service suites

## Managed Azure Target

### Azure Container Apps

Recommended first target.

Pros:

- container packaging matches self-hosted Docker work
- easier to run multiple mode-specific services later
- good fit for HTTP and WebSocket services without owning Kubernetes
- supports revision-based deployments and traffic splitting
- can scale down for cost control better than a fixed VM style deployment
- works naturally with managed identity, Key Vault, Azure SQL, Blob Storage, and Application Insights

Cons:

- more moving parts than a single Azure App Service deployment
- requires container registry and container build hygiene from the start
- networking, custom domains, certs, and WebSocket behavior need explicit proof with real robots
- diagnostics may take a little more setup than a familiar App Service path

### Azure App Service

Good fallback if Container Apps blocks robot compatibility.

Pros:

- very fast to set up
- familiar PaaS workflow
- simple custom domain and TLS story
- strong fit for a single .NET web app

Cons:

- less aligned with self-hosted Docker packaging
- less natural for multiple mode-specific services or sidecars
- can make the managed path drift from the self-hosted path
- future AI/service-suite expansion may need a second deployment model anyway

### AKS

Defer for now.

Pros:

- maximum control for multi-service AI and agent orchestration
- strong fit for a larger Open Jibo Cloud AI network later
- mature Kubernetes ecosystem for policy, scaling, and operators

Cons:

- too much operational weight for getting the first public cloud live
- higher setup and maintenance cost
- easy to spend energy on platform operations before robot compatibility is stable

Decision: start with Azure Container Apps, keep App Service as a fallback, and reserve AKS for later AI/network scale-out.

## Self-Hosted Target

Use Docker Compose first.

The self-hosted stack should be boring and repeatable:

- Open Jibo Cloud container
- SQL database container
- local file storage volume
- optional reverse proxy / TLS helper

The first self-hosted release does not need every storage backend. It needs clean abstractions and one well-supported default.

Recommended default:

- database: PostgreSQL
- file storage: local file system volume
- secrets: local `.env` / compose secret file for early builds, with stronger secret stores later

Decision: use PostgreSQL as the first Docker Compose database. PostgreSQL is the better first self-hosted default because it runs cleanly across more operating systems with less setup friction, while still giving us a real SQL target for durable behavior.

## Artifact Strategy

Use one source tree and one primary cloud runtime, but produce deployment-specific bundles.

Recommended shape:

- one base Open Jibo Cloud image for the standard cloud runtime
- environment/profile-specific configuration for:
  - managed Azure Container Apps
  - self-hosted Docker Compose
  - developer/debug runs
  - isolated test runs
- explicit image tags for release channels and deployment profiles where useful, such as `openjibo-cloud:managed` and `openjibo-cloud:self-hosted`
- separate images only when the runtime truly diverges, such as:
  - Open Jibo Cloud AI
  - heavy STT/media workers
  - future sync/identity services if they become independently deployable

This gives us the practical benefits of multiple preconfigured builds without forking behavior too early.

Decision: prefer explicit tags/profiles over ambiguous profile-neutral images. The base runtime can remain shared, but the published artifacts should make the intended deployment target obvious.

## Minimum Service Stack

For managed cloud:

- Open Jibo Cloud container
- identity/auth service
- SQL database
- Blob/file storage
- Key Vault or equivalent secret storage
- Application Insights or equivalent telemetry
- container registry

For self-hosted:

- Open Jibo Cloud container
- SQL database
- local file storage
- local config/secrets file
- optional reverse proxy/TLS service

For developer:

- Open Jibo Cloud container or local .NET run
- local JSON or local SQL depending on test target
- local file storage
- capture/export folders mounted as volumes

## Auth And Identity Boundary

Use the Open Jibo root domain family as the identity anchor, but keep auth deployable separately from the full robot cloud runtime.

Recommended public shape:

- `auth.openjibo.com`: people, loops, robots, cloud/server registration, token issuance, and trust metadata
- `api.openjibo.com`: canonical robot-facing hosted API for account, loop, OOBE, media, update, and related cloud protocol traffic
- `neohub.openjibo.com`: canonical managed host for listen/proactive WebSocket traffic
- `cloud.openjibo.com`: managed Open Jibo Cloud runtime
- `members.openjibo.com` or `cloud.openjibo.com`: hosted subscription and plan-management surface for paid cloud access
- `ai.openjibo.com` or `cloud-ai.openjibo.com`: Open Jibo AI runtime when ready
- `openjibo.com`: public web app, account entry point, documentation, and owner flows

Auth is broader than user login. It must model:

- people
- loops
- robots
- clouds/servers
- issued robot identities
- server trust and revocation state
- account-to-loop and loop-to-robot relationships

Decision: use the Open Jibo domain family as the root identity authority, with auth isolated enough that other clouds/servers can integrate without running the full managed cloud runtime.

Decision: auth should start as a separate deployable service, not as a module embedded inside the robot cloud container. That avoids a later split when self-hosted, managed, hybrid, and AI modes need to share identity without all running the same robot runtime.

Decision: the first managed API surface should use `api.openjibo.com` as the canonical hostname, with `neohub.openjibo.com` as the canonical listen/proactive host.

Decision: `openjibo.com` should be treated as a real product surface, not just a marketing page. The first public site should be able to host account entry, onboarding redirects, and the project overview without requiring a separate domain for the owner-facing web app.

Decision: the hosted subscription surface should live on a separate `members.openjibo.com` or `cloud.openjibo.com` entry so `openjibo.com` can remain the showcase and account-entry site while billing and plan state stay clearly separated from the robot-facing API.

Decision: the first auth service can live in the same repository and solution as Open Jibo Cloud, but it should be its own project and deployable from day one.

Decision: the first managed container registry should be Azure Container Registry in the Azure environment that hosts the managed cloud. GitHub Container Registry can be added later for public/community images if it becomes useful.

Decision: public/community images can publish to other registries later, including GitHub Container Registry. The first managed cloud can keep Azure Container Registry as its operational registry while still allowing future open-source/community images to flow elsewhere.

## Hosting Modes

### Managed

Open Jibo operates the cloud and identity surface.

Expected default for most owners:

- easiest setup
- paid hosted access can live here
- centrally managed updates and compatibility fixes
- best path for support

Managed and community clouds may need provider-specific onboarding steps. For example, a paid hosted cloud can redirect the owner from Open Jibo onboarding into a signup/payment flow, then return to robot onboarding after the account is ready. The onboarding system should support these provider-specific steps without hard-coding one payment provider or one managed operator into the core cloud runtime.

For hosted access cancellation, the subscription surface should be able to revoke cloud access and force the robot back through the authorized validation flow before hosted access resumes. The cloud should treat that as a deliberate access change, not as a silent local preference toggle.

## Provider-Specific Onboarding Extension

The onboarding system should expose extension events around person, robot, and cloud-provider setup.

Candidate events:

- before onboarding starts
- person onboarding failed
- person onboarding completed
- cloud provider selected
- robot onboarding failed
- robot onboarding completed
- onboarding completed

For each configured provider event:

- Open Jibo sends a signed POST to the provider endpoint
- the request includes a return URI and enough signed context for the provider to trust the session
- the provider can reply with no action, a continue decision, or a URI that onboarding should send the person to
- after provider work is complete, the provider returns the person to the supplied URI with signed result data
- onboarding validates the return signature and resumes
- onboarding can display provider result information inside the robot/app/web setup flow

Example paid-cloud flow:

1. Onboarding starts.
2. Person setup completes.
3. The person sees cloud choices with payment, free/community, security, and feature notes.
4. The person selects a paid hosted cloud.
5. Open Jibo sends the provider webhook.
6. The provider returns a signup/payment URI.
7. Onboarding sends the person to that URI.
8. The provider handles payment.
9. The provider redirects back to the onboarding return URI with signed result data.
10. Onboarding displays the result, such as `Welcome to my cloud. You paid $10/month for access.`
11. Robot setup and activation continue.
12. The robot receives the selected cloud target through the onboarding/token/QR path.
13. The robot talks to the selected provider cloud.
14. On later boots, the robot should try the selected provider cloud first and use the root Open Jibo authority only as a recovery/routing fallback when needed.

Open question: define the exact event payload shape and signing scheme after the original onboarding call sequence is mapped.

Decision direction:

- use a short-lived Open Jibo onboarding session token signed by the Open Jibo authority
- bind each provider handoff to a nonce/state value plus the selected provider, person, loop, and robot context
- sign provider callbacks/returns with the provider's own key material, then verify them against the registered provider identity
- prefer standard webhook/JWS-style signed payloads over custom crypto
- allow HMAC-based provider adapters only when a provider cannot practically support asymmetric signatures
- require HTTPS for the transport and treat the signature as the trust check, not the transport alone

Confirmed app-side onboarding sequence from the Open_Jibo_APP tree and the archived app research:

- `ScreenWelcome`
- `ScreenTip`
- `ScreenAuth`
- `ScreenWifi`
- `ScreenQR`
- `ScreenSetup`
- `ScreenSuccess`

Confirmed app-side service calls:

- `Account_20151111.Create`
- `Account_20151111.Login`
- `Loop_20160324.List`
- `Loop_20160324.ListMembers`
- `Loop_20160324.InviteMember`
- `Loop_20160324.UpdateMember`
- `Media_20160725.List`
- `OOBE_20161026.PrepareRobot`
- `OOBE_20161026.GetStatus`

Confirmed QR/token shape:

- SSID + password + optional static IP block + access token
- XOR obfuscation with the classic Jibo key phrase
- chunked QR codes when the payload is long
- static fallback token `JiboLivesSo` when the app cannot reach the server

This is now enough to build a server-side replay smoke test and a much more exact app/server parity target.

Current implementation:

- `scripts/cloud/Invoke-CloudSmoke.ps1` now walks the onboarding spine in order
- `tests/Jibo.Cloud.Tests/Protocol/OnboardingReplaySmokeTests.cs` replays the same account/loop/OOBE sequence against the protocol service
- the managed and self-hosted deployment contract scripts now verify the replay smoke markers instead of the older bootstrap-only markers

Minimum replayable onboarding sequence for CI:

- create or confirm the person/account record
- create or confirm the loop record
- issue the onboarding token with `OOBE_20161026.PrepareRobot`
- display and consume the QR payload
- confirm `OOBE_20161026.GetStatus` completes
- confirm the robot reconnects or reaches the post-onboarding socket/session path
- verify a small post-onboarding turn set such as `hello`, `tell me a joke`, and `cloud version`

If the robot-side app or firmware later proves the original sequence includes extra calls, the CI replay should expand, but this is now the smallest useful gate for the first parity slice.

### Self-Hosted Isolated

Owner runs the full stack independently.

Rules:

- no dependency on Open Jibo managed services after setup
- local identities and storage remain local
- no automatic sync with the main network
- owner is responsible for backups, updates, TLS, and uptime

TLS/self-hosted decision:

- self-hosted v1 should primarily be handled by robot mode/config patching
- the conversion path controls the domain/IP Jibo talks to and can control the certificate/trust behavior for that mode
- self-signed certificate support can remain viable if the conversion disables or redirects the relevant robot-side verification checks
- local HTTP is acceptable for developer/smoke-only paths where we fully control both ends and no owner robot trust needs to persist
- real owner-facing self-hosted robot paths should default to HTTPS/self-signed or equivalent trust-patched behavior until a safe robot-side HTTP mode is proven
- once a robot enters this self-hosted trust mode, returning to a normal trust posture should require reset/OOBE-style recovery

### Self-Hosted With Sync

Future mode.

Rules:

- opt-in only
- requires server trust/admission
- sync enrollment is one-way until reset/OOBE recovery
- cannot casually toggle back to isolated mode
- must handle bad actor servers and revocation before public use

Open question: decide whether this mode needs a visible `open-jibo-hybrid` label or can remain a self-hosted sync option.

Decision:

- on later boots, try the selected provider cloud first
- if it is unavailable, enter an explicit recovery flow instead of silently switching clouds
- use the root Open Jibo authority as a recovery/routing broker for the first recovery step
- let the owner retry the selected provider, switch to a different provider if the onboarding policy allows it, or fall back to isolated/self-hosted recovery where appropriate
- do not silently migrate a paid hosted cloud identity to a different provider without owner action

### Developer

Used for local testing, captures, and debugging.

Rules:

- easy to point a robot at a specific development cloud
- capture paths are mounted and easy to export
- unsafe test settings are explicit
- not marketed as an owner path

## CI/CD Shape

First pipeline should:

- restore and build the .NET solution
- run focused test suites
- build the Open Jibo Cloud container image
- scan or inspect the image enough to catch obvious packaging failures
- push to a container registry
- deploy to Azure Container Apps staging
- run `/health`
- run a protocol smoke test before the deployment can touch a real robot
- promote to production manually after live robot validation

Minimum protocol smoke gate:

- prefer recorded onboarding/session replay from the server perspective as the first CI-friendly gate
- use the revived Jibo Revival Group virtual Jibo when practical
- if virtual Jibo is not stable enough for CI, build a small virtual-Jibo smoke client that exercises only the deployment gate
- verify robot HTTPS startup reaches the deployed API
- verify onboarding/registration operations, likely including create person, `CreateRobot` or the equivalent registration flow, activation, and OOBE calls once identified
- verify `Notification.NewRobotToken`
- verify token/session issuance enough to open the expected sockets
- verify WebSocket listen/proactive endpoints accept connections
- verify simple post-onboarding operation turns such as `hello`, `tell me a joke`, and `cloud version`
- verify backup and update metadata calls do not break startup or basic operation

The smoke gate does not need full robot parity. Its job is to prevent obviously broken deployments from being promoted to real-device testing.

Custom-domain readiness should be part of that gate once the public hostnames are wired up. At minimum, the deployment should prove:

- the managed API answers correctly behind `api.openjibo.com`
- the public site answers correctly behind `openjibo.com`
- any `neohub.openjibo.com` routing decision is either explicit or intentionally collapsed onto the API host
- the same deployment can still satisfy the robot-facing protocol smoke checks without depending on a hardcoded legacy hostname

Research target: record a new-robot onboarding session and reduce it to the smallest replayable sequence that proves the deployment can onboard, issue tokens, accept sockets, and complete basic operation turns.

Migration policy clarification:

For PostgreSQL-backed self-hosting, "migration policy" means the rule for how schema and durable data changes are applied after owners already have data. Before public self-hosted release, we need a predictable answer for:

- how database schema migrations are versioned and run
- whether migrations run automatically on startup or through an explicit command
- how owners are warned before risky migrations
- how backups are required or encouraged before migration
- how failed migrations are detected and recovered

Recommendation:

- use versioned SQL migration scripts as the source of truth
- run migrations through an explicit Open Jibo migration command in CI/CD and managed deployments
- for local/self-hosted startup, provide a script or entrypoint switch that can run migrations intentionally
- keep the self-hosted/container migration launcher shell-native so Linux CI and Docker Compose do not depend on PowerShell
- default to not applying destructive or risky migrations silently
- require a dry-run/report mode before public self-hosted release
- use a DbUp-style SQL script runner as the first implementation path
- wrap it with an Open Jibo migration command that can provide apply, preview, dry-run/report, and container-entrypoint modes
- revisit FluentMigrator only if we later need a richer code-first or rollback-oriented model than SQL scripts can reasonably provide

Later pipeline should:

- build self-hosted compose bundles
- publish versioned release artifacts
- generate deployment manifests
- run migration checks
- run the Linux self-hosted contract check alongside the PowerShell contract check
- run the managed deployment workflow through the bash wrappers so the Azure job stays Linux-native for its orchestration layer
- run containerized integration tests against SQL and file storage

## `1.0.20` Exit Criteria

Status clarification (`2026-08-18`): the container, migration, and smoke contracts prove the neutral deployment foundation. Provider-specific membership, price, billing, entitlement policy, managed backup promise, support operations, and paid pilot belong in that provider's repository. Transcendent Software's implementation is tracked in [OpenJiboCloud](https://github.com/Transcendent-Software-LLC/OpenJiboCloud).

This track is ready to build when:

- Azure Container Apps is accepted as the first managed target
- Docker Compose is accepted as the first self-hosted target
- PostgreSQL is accepted as the first self-hosted database
- storage abstraction expectations are documented
- separate auth deployable and subdomain boundary are documented
- explicit image/profile strategy is documented
- Azure Container Registry is accepted as the first managed registry
- virtual-Jibo or smoke-client gate shape is documented

This track is ready to close for `1.0.20` when:

- the cloud has a Dockerfile
- Docker Compose can run the self-hosted stack locally
- Azure Container Apps deployment steps are scripted or documented
- Azure Container Registry publish steps are scripted or documented
- the managed cloud can answer `/health`
- the version endpoint and `cloud version` speech identify the deployed build
- a virtual-Jibo or purpose-built smoke client gates real-robot deployment
- recorded onboarding replay or equivalent smoke coverage proves basic onboarding and operation calls
- the first auth/identity boundary is documented well enough to avoid mixing robot runtime with root identity authority
- provider-specific onboarding can hand off to signup/payment and return to robot onboarding

The final item above is a provider-extension contract criterion. The neutral runtime should support a signed handoff/return without implementing or depending on a particular provider's checkout system.

## Open Questions

1. What is the exact new-robot onboarding call sequence, including person creation, robot creation, activation, OOBE, token issuance, and first socket connection? App code is still needed to confirm parity beyond the current helper-tool hypothesis.
2. Which migration runner best fits our SQL-script, dry-run/report, PostgreSQL, and container requirements?
3. Which self-hosted paths can use HTTP locally, and which robot-side checks must be patched for HTTPS/self-signed operation? Developer/smoke-only paths can use HTTP; owner-facing robot paths should stay on HTTPS/self-signed unless proven safe otherwise.
4. What signing mechanism should provider-specific onboarding events and returns use?
5. What recovery behavior should happen if a selected provider cloud is unavailable on later robot boots?
