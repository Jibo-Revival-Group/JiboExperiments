# Runtime inventory and license-review gate

Status: preview evidence tooling, 2026-10-10. No stable release approval.

The manual `openjibo-runtime-sbom-evidence.yml` workflow scans only the two
immutable images in `runtime-preview-37540893708`. It does not rebuild or run
the images, use operator credentials, alter existing release assets, publish a
new container, enroll robots or deploy. Public registry access is sufficient.

The scanner action is pinned to commit
`fffec3d851af758d56fee099cf3fbf8aa8779843` (v0.24.3), with Syft v1.54.1 selected
explicitly. Inventory uses the final, squashed image filesystem. Removed files
in earlier layers are outside this scan. This is an inventory, not a
vulnerability assessment or proof that every native library/model was detected.

Each CPU profile retains these workflow artifacts for 90 days:

- Exact runtime image reference.
- Native Syft JSON and SPDX JSON inventories.
- Review report bound to the scanner's manifest digest, Linux/amd64 platform,
  image source-commit label and CPU-profile label.
- SHA-256 checksums of both inventories and the report.
- Signed SPDX attestation bundle and workflow-local cryptographic verification.

The reviewed image pins are shared in `review-runtime-sbom.py`. Changing them
requires code review. A wrong identity, unexpected scanner version or empty
inventory fails the workflow. The report lists packages without complete SPDX
license expressions, but even a zero-gap report **never approves licenses** or
unblocks stable promotion. The SPDX attestation is issued for the existing image
digest, with registry pushing disabled; it does not change the image or its tags.
Verification fixes the repository, signer workflow, GitHub OIDC issuer, hosted
runner requirement and exact evidence-tooling commit. That tooling commit is
distinct from the older image build source commit. The signature establishes
inventory provenance, not license approval. Workflow-local verification is not
independent operator acceptance; that check is performed against downloaded
evidence separately.

## Supplemental inventory and owner review

The repository's MIT license is not a blanket license for all container contents.
Before stable distribution, review the actual shipped bytes and retain applicable
notices/terms for:

- Base OS packages and .NET runtime.
- NuGet dependencies and any scanner omissions.
- Whisper native source, linked libraries and the bundled build tree.
- The `base.en` speech model, including provenance and redistribution terms.
- FFmpeg's actual build configuration and associated obligations.

The current Dockerfile copies Whisper's entire source/build/model tree. Thus a
binary-only inventory cannot stand in for a complete notice review. Source and
model downloads also need an immutable source/checksum policy before a future
build can claim reproducibility. A digest identifies the existing image; it
does not make moving base-image tags or upstream Git tags immutable.

Do not add stock robot firmware or owner backups to the scanner artifacts or
published bundles. Robot OS/OTA redistribution remains a separate rights and
delivery lane. License findings require owner review; unknown or restrictive
terms cannot be silently classified as approved by an installer.

Scanner documentation: [Syft formats](https://oss.anchore.com/docs/guides/sbom/formats/)
and [Anchore scanner action](https://github.com/anchore/sbom-action).

## Remaining gates

Generate and inspect both inventories; supplement missing native/model evidence;
record notices and owner review; independently verify each signed SBOM
attestation. Stable promotion still also needs the
separate supported-host/robot and encrypted-data recovery evidence. Passing an
inventory check changes none of those acceptance boundaries.

## First scan, 2026-10-10

[Run 38107379753](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38107379753)
passed for both immutable images. Each inventory contains 405 package records:
294 Debian, 93 .NET, 12 binary, three Java archive and three npm records.
These are records, not necessarily unique dependencies. Each report flags 307
records without complete SPDX expressions; this includes unnormalized textual
license declarations and missing declarations, not 307 proven license violations.
Whisper appears as npm metadata in its shipped source tree; this does not certify
its compiled native library inventory or model terms. The scanner reports GPL
and LGPL declarations in FFmpeg-related copyright metadata, requiring build-
configuration and notice review rather than an automatic compatibility verdict.
Initial evidence is retained under `artifact-output/runtime-sbom-38107379753/`;
this first run predates SBOM signing and makes no signature claim.
