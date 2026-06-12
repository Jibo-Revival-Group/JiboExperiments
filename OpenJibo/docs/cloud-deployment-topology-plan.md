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
- `cloud.openjibo.com`: managed Open Jibo Cloud runtime
- `ai.openjibo.com` or `cloud-ai.openjibo.com`: Open Jibo AI runtime when ready
- `openjibo.com`: public site, account entry point, documentation, and owner flows

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

### Self-Hosted Isolated

Owner runs the full stack independently.

Rules:

- no dependency on Open Jibo managed services after setup
- local identities and storage remain local
- no automatic sync with the main network
- owner is responsible for backups, updates, TLS, and uptime

### Self-Hosted With Sync

Future mode.

Rules:

- opt-in only
- requires server trust/admission
- sync enrollment is one-way until reset/OOBE recovery
- cannot casually toggle back to isolated mode
- must handle bad actor servers and revocation before public use

Open question: decide whether this mode needs a visible `open-jibo-hybrid` label or can remain a self-hosted sync option.

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

- use the revived Jibo Revival Group virtual Jibo when practical
- if that is not stable enough for CI, build a small virtual-Jibo smoke client that exercises only the deployment gate
- verify robot HTTPS startup reaches the deployed API
- verify onboarding/registration operations, including `CreateRobot` or the equivalent registration flow once identified
- verify `Notification.NewRobotToken`
- verify token/session issuance enough to open the expected sockets
- verify WebSocket listen/proactive endpoints accept connections
- verify simple post-onboarding operation turns such as `hello`, `tell me a joke`, and `cloud version`
- verify backup and update metadata calls do not break startup or basic operation

The smoke gate does not need full robot parity. Its job is to prevent obviously broken deployments from being promoted to real-device testing.

Migration policy clarification:

For PostgreSQL-backed self-hosting, "migration policy" means the rule for how schema and durable data changes are applied after owners already have data. Before public self-hosted release, we need a predictable answer for:

- how database schema migrations are versioned and run
- whether migrations run automatically on startup or through an explicit command
- how owners are warned before risky migrations
- how backups are required or encouraged before migration
- how failed migrations are detected and recovered

Later pipeline should:

- build self-hosted compose bundles
- publish versioned release artifacts
- generate deployment manifests
- run migration checks
- run containerized integration tests against SQL and file storage

## `1.0.20` Exit Criteria

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
- the first auth/identity boundary is documented well enough to avoid mixing robot runtime with root identity authority
- provider-specific onboarding can hand off to signup/payment and return to robot onboarding

## Open Questions

1. Which exact protocol steps from the revived virtual Jibo are reliable enough for CI?
2. What database migration tool should we use for PostgreSQL-backed self-hosting?
3. Should PostgreSQL migrations run automatically on startup, or only through an explicit admin command?
4. Should self-hosted TLS be handled by a bundled reverse proxy/TLS helper, or by a robot patch/self-signed flow documented as part of conversion?
5. What should the provider-specific onboarding extension contract look like for paid hosted clouds, free community clouds, and self-hosted servers?
