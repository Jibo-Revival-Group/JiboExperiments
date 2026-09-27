# Standalone isolated starter packaging (local preview)

This is an offline packaging tool, not an official release or a tested upgrade
installer. It prepares a ZIP for a **fresh, separate isolated installation**
using a supplied runtime image digest. It does not build, pull, verify or publish
that image, contact Azure, enroll a server, or modify a robot.

## Prepare

From the `OpenJibo` directory, with Python 3.9 or newer installed:

```text
python scripts/cloud/prepare-openjibo-starter-bundle.py --image REGISTRY/REPOSITORY@sha256:DIGEST --output PATH/TO/starter-preview.zip
```

Replace the placeholders with a reviewed image reference and 64 lowercase
hexadecimal digest characters. Tags are rejected. The destination must not
already exist. Do not distribute a bundle referencing an untested or unavailable
image. No official public image reference is supplied by this milestone.

The builder includes an explicit allowlist of starter files, not the checkout
directory. Live `.env` files, database/media files, credentials, build outputs
and robot images are not inputs. Included text uses normalized line endings;
archive metadata and file ordering are deterministic. A content manifest records
SHA-256 hashes. Those hashes detect changes only when compared to trusted
metadata: **a manifest alongside a ZIP is not a signature or proof of origin**.

The generated Compose configuration removes source-build dependencies, uses the
same fixed runtime image for API and migrations, does not publish PostgreSQL
to the host, and binds the API to loopback for local testing. These changes apply
only to the generated bundle; the source
checkout's Compose configuration is unchanged. Supporting container image tags
are not yet a complete immutable dependency lock.

The transformation is restricted to a reviewed Compose source contract. Source
Compose changes require a packaging review and updated tests rather than silently
adding new mounts or dependencies to the standalone package.

## Verify before extraction

Use the verifier from a trusted checkout before opening an acquired ZIP:

```text
python scripts/cloud/verify-openjibo-starter-bundle.py --bundle PATH/TO/starter-preview.zip --expected-sha256 TRUSTED_ARCHIVE_SHA256
```

The expected value is the **whole ZIP's** SHA-256, obtained independently from a
trusted builder or release channel. Do not calculate a hash from an untrusted
download and use that same value as evidence of authenticity. Signed release
metadata and official downloads are still pending.

The verifier checks the supplied archive hash before parsing ZIP contents, then
checks bounded sizes, the exact file inventory, regular-file types, the manifest
and each member hash. It neither extracts files nor executes their contents.
A successful result means the bytes match the supplied checksum and package
contract; it does not establish image provenance, safety of migrations, a
supported CPU/model variant, or freshness of a release.

## Try a prepared bundle

Extract into a new directory and follow its included README. Initialize `.env`
using the supplied initializer, set a strong database password, retain the
generated encryption keys, and then use the bundle's start entry point.
Docker/Compose and the relevant shell are required; Bash initialization also
requires OpenSSL. Python is needed to prepare or verify the ZIP, not to run the starter.

This configuration serves the API over HTTP on loopback. A physical robot cannot
reach it until you deliberately change the binding and configure a trusted-LAN
firewall policy. Legacy login defaults are not a public-server security policy;
do not expose this preview to the public Internet. Robot conversion, public TLS,
hybrid enrollment and managed-provider admission are separate operations.
The selected image must already contain the required speech binaries/models;
changing build-related example settings cannot rebuild a prebuilt image.

Do not extract over an existing installation or attach its volumes. This
milestone does not implement upgrade compatibility, migration rollback or
backup restoration. Never delete an existing `.env` to obtain new keys for old
encrypted data. Never use `docker compose down -v` as an upgrade shortcut.

## Remaining release gates

The [September 27 candidate inspection](starter-image-candidate-20260927.md)
records a historical deployed image, current local test prerequisites and the
next acceptance sequence. It is not an official image recommendation.

- Approve a registry, signing ownership and official image/build provenance.
- Verify the exact image's CPU architecture and speech/model variant.
- Run fresh Windows/Docker Desktop and Linux installs with retained evidence.
- Test authenticated robot sockets, restart persistence and backup/restore.
- Review supported dependency versions, licenses/SBOM and immutable locks.
- Publish only after these gates pass; keep hybrid/managed packages separate.

See [the distribution plan](official-distribution-plan.md) for the broader
release, mirror, update and hosting-profile work.
