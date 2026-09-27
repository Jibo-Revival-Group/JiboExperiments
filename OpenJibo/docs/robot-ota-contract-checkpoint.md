# Robot OTA contract checkpoint

Date: 2026-09-26. Source inspection and local regression work only; no robot,
Azure resource or live update catalog was modified by this investigation.

## What the current runtime actually implements

The .NET `JiboCloudProtocolService.HandleUpdate` handles the `Update_*`
metadata family, including listing, lookup, creation and removal. No staged
records produces HTTP 204 for `GetUpdateFrom`; list operations return arrays.
`MapUpdate` returns version, subsystem/filter, size/hash, URL and dependency
fields. This is a compatibility contract, not proof of installable packages.

`InMemoryCloudStateStore` starts with an empty update list. PostgreSQL persists
update metadata and dependencies in the existing artifact repository. Creation
still synthesizes a legacy URL and can supply placeholder hash/size values.
These records must not be promoted as official robot releases. No package
builder, publisher trust validation or hardware recovery proof is supplied by
those methods.

The local scheduler proof routes model status/version transitions; they do not
flash a physical robot. In particular, their success is not an OTA acceptance
test. The Node development oracle also has update scaffolding; this checkpoint
does not certify its administration boundary or endorse it as an OTA server.

## Repairs in this development slice

- Preserve opaque JSON dependency metadata through the domain model, memory
  snapshot, PostgreSQL facade and protocol response. Previously the memory
  path discarded input and the protocol mapper always emitted an empty object,
  including for dependencies stored separately in PostgreSQL.
- Deny public HTTP `Update_*` `CreateUpdate` and `RemoveUpdate` requests in the
  .NET API. There is no reviewed OTA release-administration identity or route
  yet. Robot polling must not double as release administration; a robot token,
  harness header or existing portal credential does not establish that authority.
  Internal fixtures/store methods remain available for local tests.

These changes do not seed records, enable offers, enforce dependency semantics,
change version selection, replace URLs/hashes or install anything. Existing
catalog records are not deleted. Until deployed, the local HTTP denial is not a
claim that an already-running server has been patched.

## Evidence still needed

Research in [robot internals](jibo-internals.md) names stock metadata operations,
A/B partitions and `S72jibo-apply-update`. It does not establish a full build or
signing chain. We still need version-specific source or redacted lab traces for
package layout, actual hash/signature algorithms, trust anchors, download/resume,
dependency meaning, partition writes, boot selection and recovery/commit logic.
Preserving a dependencies object is not validating its contents or compatibility.

### Local source inventory follow-up

A bounded source/file inventory of `C:/Projects/jibo/pegasus` and the runtime
checkout (excluding generated `artifact-output`) did not locate the stock
apply/download/updater implementation or an installable firmware package.
Conversion scripts reference updater SDK paths but do not implement their
verification logic. The Pegasus `surprises_ota_manifest.json` describes skill
intents, not an OS/package installation manifest. This search does not establish
that no other owner-held image contains the needed files.

The OOBE conversion plan also records community-reported tarball/SHA-1 behavior.
Keep those reports distinct from verified, version-specific package fixtures.
The older planning helper previously treated the existence of a trace path as
sufficient to clear its blockers. Its readiness signal now stays blocked
regardless of that path until a reviewed evidence-validation mechanism exists.

The next input needed is a legally available, owner-supplied stock updater source
or mounted image with its firmware version and provenance. Inspect only updater
code and public trust material first; do not copy credentials, private keys,
personal data or whole image contents into the repository. A representative
redistributable package and redacted positive/negative traces can then support
format-specific offline tests. Until then, do not invent archive/signature rules
or treat a generic hash check as an OTA validator.

Next: collect those fixtures without changing a robot, define a separate
fail-closed release-administration/promotion design, and add offline package
validation before any update can be offered. Follow the
[robot build and OTA plan](robot-build-and-ota-plan.md); its licensing, signing,
hardware recovery and opt-in rollout gates remain open.
