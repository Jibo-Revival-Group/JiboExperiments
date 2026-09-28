# Isolated starter acceptance — 2026-09-27

**Result: local plumbing and synthetic backup/restore passed. Not an official
release certification or speech acceptance.** No production resources or physical
robots were modified.

## Exact candidate and host

- Windows VM with nested virtualization enabled by the operator; Docker Desktop
  engine `29.8.0`, Linux/amd64. This is not an independent native Linux-host test.
- Runtime: `cropenjibomanagedrmj4v5utn2.azurecr.io/openjibo-cloud@sha256:5cfdd613cf6a488d89b81ebad2c0b656601d1f069ad27773bd61716108771c51`.
- Source candidate: `53d7c7b6315b4400d894f017b99b6d6614ee94eb`, previously deployed
  September 23; not a claim that all subsequent runtime changes are included.
- Starter builder commit: `54335f70`; verifier commit: `b67e92c0`.
- Generated ZIP SHA-256:
  `68f533f2280b44c3f73ff9cd7baa1a20b319392e27b7340748907b5acbda9e14`.
  Verified locally before extraction; 12 members, 25,830 bytes.
- PostgreSQL tag `postgres:16-alpine` resolved during this test to
  `sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea`.
  The starter still uses that mutable tag; this observation is not a dependency lock.

## Isolation

The engine initially contained no containers or volumes. Two distinct projects
were used: `openjibo-starter-acceptance-20260927` and
`openjibo-starter-restore-20260927`. The API bound only to `127.0.0.1:8080`;
PostgreSQL had no published host port. All credentials were generated for these
local tests. Only two synthetic devices (`starter-acceptance-1` and
`starter-acceptance-2`) were exercised.

## Observed results

| Check | Result |
| --- | --- |
| ZIP preparation, external checksum verification and Compose validation | Passed |
| Fresh database initialization and migration | Migrator exited 0; PostgreSQL healthy |
| API health | HTTP 200, service version `1.0.20` |
| Two authenticated synthetic robots | Robot/hub token issuance and notification, listen and proactive sockets passed |
| API restart | Both persisted device IDs remained in PostgreSQL |
| State and memory database backups | `pg_dump -Fc` captured both databases while API was stopped |
| Database restore | Both dumps restored with `pg_restore --exit-on-error` into the second project's empty databases |
| Persisted state | Both device IDs and an explicit synthetic memory-database marker recovered |
| API volume restore | Quiesced volume archive restored into a separate named volume; marker SHA-256 matched |
| Restored stack | Migration rerun succeeded; health HTTP 200; both robots reconnected on all three socket types |
| Negative authentication check | Tokenless listen rejected with HTTP 401 |

The restored volume marker hash was
`35884fec1da312c921cc310855e46a4aebc822da6cd660fb876fae9fe66dde2c`.
The original `.env` was copied securely to the restored installation so the
matching encryption settings were retained; no key rotation was attempted.
This does not independently certify decryption of arbitrary production user data.

The volume was pre-created for restore, so Compose warned that it did not own
the volume's creation labels. The expected, separately named volume was used;
future automated restore tooling should manage those labels explicitly.

## Important limitation found

The pinned managed image contains the API and migration assemblies, but **does
not contain** `/usr/bin/whisper.cpp/build/bin/whisper-cli` or
`/usr/bin/whisper.cpp/models/ggml-base.en.bin`.

These tests explicitly set `OPENJIBO_ENABLE_LOCAL_WHISPER=false` in the launching
process. The generic starter template defaults to local Whisper enabled, so this
candidate is **not** an out-of-the-box local-speech release. A tested image/model
variant or explicitly configured external speech backend is still required.
The probe used `--skip-turn`: no ASR, TTS, conversation, proactive response
transaction, physical-robot conversion or OOBE behavior was certified here.

## Retained local state and next work

Generated files, synthetic credentials, dumps and helper scripts are retained in
`artifact-output/starter-acceptance-20260927/`, excluded from Git. Do not publish
that directory. The source test stack is stopped; the restored API/PostgreSQL
stack is left running on loopback for inspection. Both projects' volumes and
stopped helper containers are retained; nothing was deleted.

Next gates:

1. Build/select and inspect an explicit speech/model image variant; test actual
   listen/proactive transactions and ASR/TTS, not only socket connectivity.
2. Repeat fresh install/restart/restore on a native Linux host and exercise the
   Bash entrypoint against real Docker.
3. Automate the scoped acceptance/restore sequence with explicit ownership and
   retained evidence; test representative encrypted application data.
4. Finish immutable dependency locks, architecture matrix, license/SBOM,
   signing/provenance and protected publication before calling this official.

See [packaging instructions](standalone-starter-packaging.md) and the
[distribution plan](official-distribution-plan.md).
