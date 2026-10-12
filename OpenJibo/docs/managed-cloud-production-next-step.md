# Managed-cloud production bootstrap — owner decision pending

Prepared from read-only inventory on 2026-10-11. This is not a deployment
manifest or authorization to create resources. The neutral Pages site and the
managed customer service are separate deployment lanes.

The owner subsequently approved the scoped bootstrap. The dedicated identity,
empty customer database, 5 GiB SMB key share/environment mount and registry-pull
grant have been provisioned. The customer app, SQL roles/migrations, production
secret access and public cloud DNS have not been deployed. Two decisions block
further execution: a separate customer-only vault is needed to avoid vault-wide
robot-secret access, and GitHub rejected required reviewers for the private
Team-plan repository. The empty environment created by that failed request was
removed after verifying it held no credentials or variables. No existing robot
resources were repurposed or reset.

The owner approved the separate customer vault and temporary explicit manual
release procedure. `kv-openjibo-cloud-prod` is now created with RBAC and remains
empty. Exact federation and secret-material writes were blocked by automatic
action review pending more specific authorization; those commands did not run.
No deployment or old-vault permission change resulted.

## Confirmed baseline

Scope: subscription `a29ded8e-51f7-4d01-9b80-2cd0be3f23cb`, resource group
`rg-openjibo-prod`, region `centralus`.

| Existing resource | Current finding |
| --- | --- |
| Container Apps environment `openjibo-managed-env` | Exists; no environment storage mappings listed |
| Robot app `openjibo-cloud` | Ready revision `openjibo-cloud--0000059`; preserve completely |
| Registry `cropenjibomanagedrmj4v5utn2` | Exists; customer candidate digest/pull grants not yet verified |
| PostgreSQL `psql-openjibo-managed-rmj4v5ut` | Contains robot state/memory and system databases, not `openjibo_cloud` |
| Storage `stopenjibomanrmj4v5utpnu` | Exists; no dedicated key share/mount verified |
| Vault `kv-openjib-manag-rmj4v5` | Exists; current operator was denied secret-metadata listing |
| User-assigned managed identities | None listed in this resource group |
| Customer app `openjibo-managed-api` | Not present in production |
| Cloud repository environments | Only `openjibo-cloud-staging` is configured |
| `cloud.openjibo.com` | No public DNS answer; leave unconfigured for now |

Staging's separate customer app is ready at `openjibo-managed-api--39e5cd41f081`,
using a commit-tagged managed image, with two replicas of 0.5 CPU / 1 GiB each.
That mutable image tag is not an approved production digest. Do not redirect
the public customer hostname to staging or the existing robot runtime.

The active cloud checkout for this work is `C:/Projects/JiboExperiments/OpenJiboCloud`.
The separate `C:/Projects/OpenJiboCloud` checkout is older; its preparation-only
workflow is not current deployment evidence.

## Proposed bounded bootstrap

Subject to approval, use the existing production group/environment/registry,
PostgreSQL server, storage account and vault. Add only the separately scoped
managed customer application and prerequisites:

1. Establish `openjibo-cloud-production` with main-only deployment and owner
   protection, plus the exact federated identity subject actually emitted by
   GitHub. Do not guess its legacy subject or copy staging credentials.
2. Create the dedicated `openjibo_cloud` customer database and runtime/deployer
   roles, apply reviewed migrations and verify least privilege. Robot databases
   and roles are not repurposed.
3. Provision a dedicated user-assigned app identity, selected secret access and
   registry pull grants, plus a dedicated Azure Files share/environment mount
   for retained encrypted Data Protection keys. Generate production-specific
   secret material privately; never print/copy it into Git or chat.
4. Resolve and verify an immutable reviewed managed-app image, its full source
   commit and production registry identity. Rerun the fail-closed inventory gate.
   Generate the exact manifest only after prerequisite identities/secret versions
   are known; never submit a placeholder manifest or relax create-only checks.
5. Bootstrap the separate app on its Azure HTTPS origin with enrollment,
   pairing, billing, runtime metering activation and robot conversion off.
   Proposed sizing matches staging: two replicas, 0.5 CPU / 1 GiB each. This
   introduces additional compute and storage charges; no dollar estimate or
   billing approval is implied by this proposal.
6. Verify the actual candidate revision, migrations, authentication, retained
   key storage and an independently provisioned rollback revision. Obtain owner
   signed-in acceptance where automation cannot prove the user journey.
7. Only after those gates pass, separately approve `cloud.openjibo.com` DNS/TLS
   and the exact production Entra callback/origin. No paid-service launch is
   implied by publishing a closed preview.

The existing cloud manifest, inventory, promotion and acceptance tools remain
the source of execution guards. This proposal does not duplicate the shared
robot runtime. No current resource, secret, role, DNS record or robot was mutated
by the read-only inventory.

## Owner involvement

Approval is needed before the new production resources, permissions and charges
are introduced. Key Vault metadata access and production Entra registration
must be handled through narrowly scoped delegated permissions or the owner;
do not grant broad secret access just to make an inventory command pass.

The neutral site's apex/`www` routing is already configured. GitHub certificate
issuance/HTTPS enforcement is still pending and must be completed independently.
