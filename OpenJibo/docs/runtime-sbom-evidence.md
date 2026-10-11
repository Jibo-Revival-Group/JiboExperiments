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

The reviewed image pins are shared in `review-runtime-sbom.py`. Changing them
requires code review. A wrong identity, unexpected scanner version or empty
inventory fails the workflow. The report lists packages without complete SPDX
license expressions, but even a zero-gap report **never approves licenses** or
unblocks stable promotion. These uploaded artifacts are not signed release SBOM
attestations; signing and independent verification remain separate work.

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
record notices and owner review; bind a signed SBOM attestation to each image;
independently verify that attestation. Stable promotion still also needs the
separate supported-host/robot and encrypted-data recovery evidence. Passing an
inventory check changes none of those acceptance boundaries.
