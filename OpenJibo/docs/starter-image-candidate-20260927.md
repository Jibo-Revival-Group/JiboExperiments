# Standalone starter candidate inspection — 2026-09-27

Status: candidate identified; **not install/restore certified**. No image was
pulled, no containers started, and no Azure resources changed during inspection.

## Candidate identity

```text
cropenjibomanagedrmj4v5utn2.azurecr.io/openjibo-cloud@sha256:5cfdd613cf6a488d89b81ebad2c0b656601d1f069ad27773bd61716108771c51
```

The successful [September 23 deployment run](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/35860160054)
recorded this pushed digest for source commit
`53d7c7b6315b4400d894f017b99b6d6614ee94eb`, tag `sha-53d7c7b6315b`,
and ready runtime revision `openjibo-cloud--0000059`.

A read-only Azure registry lookup on September 27 confirmed the exact digest
still exists, with creation/update time `2026-09-23T12:34:43.5539958Z` and media
type `application/vnd.docker.distribution.manifest.v2+json`.
This is historical deployed runtime evidence, not proof that this image is the
latest release or supports all isolated-host settings. It also does not prove
compatibility with the new starter package.

## Local prerequisites and unknowns

- Docker CLI is installed, but the selected `desktop-linux` engine pipe was
  unavailable and no Docker Desktop/backend process was running.
- Direct Docker manifest inspection returned registry authentication required.
  Existing Azure CLI authorization could read registry metadata successfully;
  signing into Azure again is not currently necessary for that lookup.
- CPU architecture and included Whisper/model variant still need inspection.
- This is a private Azure registry image, not a public official download.
- Dependency locks, signing/provenance and third-party notices/SBOM are not
  established by this candidate lookup.

## Next validation sequence

1. Start Docker Desktop's Linux engine. Inventory existing containers, volumes,
   ports and image cache before creating a separately named test project.
2. Establish local registry access using the existing Azure session; inspect
   platform/model identity before using the immutable digest.
3. Prepare and verify a preview ZIP with the trusted local builder. Record its
   whole-archive checksum independently. Use only a new test directory, unique
   Compose project and synthetic credentials/data; never reuse production or
   existing self-hosted volumes.
4. Validate configuration, start migrations/API/PostgreSQL, and retain health
   plus authenticated fake-robot protocol results. Keep API loopback-only.
5. Verify restart persistence and a backup restored into another empty test
   project. An old image alone is not a safe migration rollback.
6. Repeat on Linux and retain the exact image/package/platform evidence. Only
   then decide whether this candidate can underpin an official isolated release.

Starting Docker Desktop may also resume the user's other configured containers;
the user should start it interactively. No production deployment, robot
conversion, public port exposure, release publication or automated deletion is
authorized by this validation record.
