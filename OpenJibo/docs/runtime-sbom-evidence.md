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

The user designated themselves as the release-review owner and authorized
continuing this work on 2026-10-10. That designation is not a blanket approval
of unresolved third-party terms or stable promotion. Final approval should name
the exact images, evidence and remaining conditions reviewed.

`openjibo-runtime-notices-evidence.yml` collects copyright texts from the exact
image's `/usr/share/doc`, `/usr/share/common-licenses` and Whisper's shipped `LICENSE` using `docker create`
and `docker cp`. The container is never started. The bounded tar reader never
extracts archive paths onto the host or follows links; skipped links are listed
for review. A fresh ZIP records each notice's hash and the selected image.
These workflow artifacts are review inputs, not a complete notice bundle or a
corresponding-source offer. Existing releases and runtime images stay unchanged.

The first [notice collection run 38108240257](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38108240257)
passed for both profiles and retained 289 copyright/license files per image,
with 76 documentation links deliberately not followed. It predates the addition
of shared common-license texts and is not a complete notice set.

The expanded [run 38108346933](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38108346933)
passed for both profiles, retaining 303 notice files per image including shared
license texts. Each manifest lists 79 unfollowed links. Downloaded evidence is
preserved under `artifact-output/runtime-notices-38108346933/`. These unsigned
review bundles preserve extracted texts and hashes; they are not a completed
license approval or signed release-notice attachment.

The next technical review items are NuGet notice/source metadata, exact distro
corresponding-source availability, FFmpeg build configuration, and native/model
coverage. Owner sign-off will be recorded against a specific completed packet,
not inferred from authorization to gather evidence.

### Exact-version NuGet declarations

`collect-runtime-nuget-metadata.py` plans offline by default. Explicit `--fetch`
collects manifests only from NuGet.org's fixed HTTPS package-content endpoint;
redirects, arbitrary upstream URLs, XML entities and unsafe package identifiers
are refused. It validates manifest package ID/version, caps individual manifests
at 1 MiB and combined bytes at 16 MiB, and writes a fresh review ZIP without
installing packages. Original manifest bytes and hashes are retained. See the
[official package-content API](https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource)
for the endpoint contract.

The portable image's 87 scanner package records select 53 distinct name/version
pairs. Initial local collection succeeded for all 53: their manifests declare
33 MIT, 19 Apache-2.0 and one PostgreSQL license expression. Every downloaded
manifest hash was checked; raw declarations are not license clearance. The
evidence is retained in `artifact-output/runtime-nuget-20261011/portable-review.zip`.
This initial local sample predates retaining scanner SHA-512 values in the
review report; subsequent workflow bundles retain all distinct supplied hashes.

Package archive hashes have **not** been verified. Neither a matching manifest
ID/version nor a license expression proves the exact image's archive contents,
notice completeness or source availability. Framework/project/unclassified .NET
records remain outside this package-manifest lane. External license URLs and
license-file references are saved as declarations only, never followed. These
supplemental ZIPs are unsigned and do not replace the signed SPDX evidence.

### NuGet archive notices and content integrity

`collect-runtime-nuget-notices.py` has a separate explicit fetch lane for the
same package versions. It reads ZIPs only in memory: no archive extraction,
package installation or execution. Each download is capped at 128 MiB, combined
downloads at 512 MiB, and copied manifests/notice files at 32 MiB. Unsafe paths,
duplicate names, encrypted members, links and oversized selected files fail.
External license links are still not followed. Files outside the notice naming
patterns are not copied, so this is not a completeness assertion.

The SDK-backed `NugetContentVerifier` checks signed-package archive integrity and
compares NuGet's content hash with the scanner-supplied SHA-512 value. This
uses SDK NuGet libraries without adding package references; the library version
is recorded per check. NuGet content hashes and raw ZIP hashes are distinct for
signed packages; see [NuGet's archive-reader implementation](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageArchiveReader.cs).
Certificate/signer trust is **not** verified by this lane. Missing scanner hashes
are explicitly reported rather than guessed. Integrity/hash failures stop
collection; they never enable approval or downgrade to unsigned acceptance.

Initial portable collection on 2026-10-11 downloaded 83,546,721 bytes for 53
packages, preserved 80 manifest/notice files and verified 51 content hashes.
Two framework package records have no scanner hash, and 36 packages have no
selected notice files. These gaps remain open even when a manifest declares a
license expression. Local evidence is retained under
`artifact-output/runtime-nuget-notices-20261011/`; the initial `portable.zip`
predates content verification, while `portable-content-verified.zip` includes it.
The supplemental bundles remain unsigned. Positive, modified-content and forged
signature tests exercise the SDK verifier; no runtime image or release changed.

Both Linux profile jobs passed in
[run 38114995887](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38114995887).
Downloaded bundles were checked independently: each contains 53 manifests and
27 selected notice files, with 51 content-hash checks passed. The two missing
scanner hashes are for `Microsoft.AspNetCore.App.Runtime.linux-x64` and
`Microsoft.NETCore.App.Runtime.linux-x64`, both version `10.0.12`. These bundles
do not certify that every scanner record represents a runtime-loaded assembly.

### Exact Ubuntu FFmpeg source candidates

`collect-ffmpeg-source-evidence.py` is tied to the same reviewed inventories and
requires their FFmpeg Debian package to be exactly `7:6.1.1-3ubuntu5`. Explicit
fetch preserves the [Ubuntu source descriptor](https://archive.ubuntu.com/ubuntu/pool/universe/f/ffmpeg/ffmpeg_6.1.1-3ubuntu5.dsc),
its listed original archive and upstream signature, and Ubuntu's Debian
packaging/patch archive. Only these fixed-version filenames are allowed; sizes
and SHA-256 values must match the descriptor. No tar extraction, source build or
image execution occurs. Existing outputs cannot be replaced.

Initial portable collection on 2026-10-11 passed all three descriptor-listed
archive checks and retained four files plus its review report in
`artifact-output/runtime-nuget-notices-20261011/ffmpeg-source-portable.zip`.
This establishes source-candidate availability and consistency with an HTTPS
descriptor. Neither the descriptor's PGP signature nor the upstream detached
signature has been verified, and matching the actual image binary/build
configuration remains open. It is not a complete corresponding-source offer
for FFmpeg's linked dependencies or a legal approval.

Both profile jobs passed in
[run 38115159426](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38115159426).
Each downloaded source bundle was independently checked against its three
recorded SHA-256 values and exact file sizes. Evidence is preserved under
`artifact-output/runtime-notices-38115159426/`; source signatures and actual
image-binary linkage remain unverified.

### Upstream review inputs

The [original Whisper project](https://github.com/openai/whisper#license) states
that its code and model weights use MIT terms. The image uses the converted
GGML `base.en` model fetched through
[Whisper.cpp's model downloader](https://github.com/ggml-org/whisper.cpp/blob/v1.9.3/models/download-ggml-model.sh),
which selects a Hugging Face `resolve/main` URL. The publisher checked model
SHA-256 `a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002`.
Upstream licensing information is useful but does not replace retaining the
original model notice and the exact converted-file/source linkage.

[FFmpeg's licensing guidance](https://ffmpeg.org/legal.html) distinguishes the
default licensing from optional GPL/nonfree build configurations and discusses
distribution/source requirements. The inventory identifies Ubuntu FFmpeg
`7:6.1.1-3ubuntu5`; do not substitute an unpatched upstream archive for the
corresponding Ubuntu build source. Copyright files and package metadata alone
do not prove the actual executable's configuration or that all required source
material is available. These are review inputs, not a legal compliance opinion.

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

The notice-review bundle now includes an offline `dependency-review.json`,
bound to the exact image and inventory hash and hashed in its manifest. It
groups identical identity/license/source evidence while retaining versions,
different declarations and original record counts. Debian source names and
versions explicitly distinguish scanner metadata from inferred binary-package
fallbacks; neither is marked verified corresponding source. A copied copyright
path is an inspection aid, not proof of complete notices. .NET records are only
classified as NuGet package metadata when the scanner supplies both package
type and a NuGet package identifier; other assemblies remain unclassified.
All entries start unreviewed. This checklist does not fetch links, execute image
code, approve terms or enable stable promotion.

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

## Signed inventory acceptance

### Nonexecuting FFmpeg configuration collection

`collect-ffmpeg-build-evidence.py` reads the concrete `libavutil.so.58.29.100`
file copied from the stopped, digest-pinned runtime container. It checks the
reviewed inventory identity, exact Ubuntu package version, ELF64/x86-64 header,
and the package metadata's MD5 before preserving a SHA-256 and one unambiguous,
NUL-delimited embedded ASCII configuration. MD5 provides metadata consistency
only, not cryptographic authenticity. The collector never executes or loads the
library, interprets configuration as shell commands, or extracts the TAR to disk.

The evidence workflow collects this separately for portable and AVX2 images.
Observed `--enable-gpl`, `--enable-version3`, `--enable-nonfree`, and
`--enable-shared` switches are literal configuration evidence, not a legal
classification or proof of complete corresponding source. A missing switch is
not evidence that every linked dependency is permissively licensed. Actual-image
results must be reviewed before this gap is considered narrowed; stable promotion
and license approval remain false.

[Run 38116012218](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38116012218)
passed for both images. Independently checked downloaded report hashes and
inventory bindings agree. Both libraries have SHA-256
`11b0836441b899e1b2183fc10eda5a5f637c03547fdfe5b97774e2cbd5aeb72c`,
and the same configuration at byte offset 899408. The literal configuration
contains `--enable-gpl` and `--enable-shared`, but not `--enable-version3` or
`--enable-nonfree`. This is a concrete review input; do not assume this Ubuntu
build is an LGPL-only build. No legal approval or complete source offer follows
from these results.

### Speech model byte identity

`collect-runtime-model-evidence.py` streams SHA-256 over the concrete shipped
`ggml-base.en.bin` copied from a stopped container. The reviewed expected size
is 147,964,211 bytes and SHA-256 is
`a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002`.
These values were observed on 2026-10-11 in the model publisher's
[HTTPS LFS metadata](https://huggingface.co/api/models/ggerganov/whisper.cpp/tree/main?recursive=false).
The metadata URL is mutable; the expected size/hash are pinned in tooling.
The report is bound to the reviewed image inventory. No model is loaded,
executed, installed or uploaded as an artifact. Passing proves byte equality
to that reviewed digest, not publisher-signature trust, conversion
reproducibility, accuracy across robots, or redistribution approval. Those
separate checks remain open.

[Run 38107600594](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38107600594)
passed scanning, signing and workflow-local verification for both profiles.
The trusted evidence-tooling commit is
`67eccca5959cf263ef9e4fab2227bfe8f4d9a4b3`; the image build source remains
`fd32678bb29ade975c71a9bcd7dc7a89babaf541`. This does not rebuild the images.
Downloaded artifacts are retained in `artifact-output/runtime-sbom-38107600594/`.

Independent local verification with `verify-runtime-sbom.py` passed for both
profiles. The verifier snapshots the proof before invoking GitHub CLI, checks
the exact SPDX JSON matches the signed predicate, requires the reviewed image
subject and SPDX 2.3 predicate, then enforces signer/source/main-ref/issuer/hosted-
runner policy cryptographically. It never accepts the publisher's saved
verification report as proof. A real negative check using the wrong evidence
commit (`51404e7eedf5eb98784ec3b5cf6aa4bc8555d407`) was rejected.

Verified SPDX file hashes:

- Portable: `be941d9305911684b5209b8964c24c21fd3684df2235d8414cd22bc9720b72c3`.
- AVX2: `dd45b6edfc3323fc45eac8cf5a9eb035ab00b23155e55d02bfc17ace4d124bb6`.

To repeat acceptance of this exact evidence run after downloading its artifacts,
run the following from the repository root (with a capable `gh` CLI):

```bash
python3 -B OpenJibo/scripts/cloud/verify-runtime-sbom.py \
  --spdx artifact-output/runtime-sbom-38107600594/runtime-sbom-portable-38107600594/runtime.spdx.json \
  --attestation artifact-output/runtime-sbom-38107600594/runtime-sbom-portable-38107600594/sbom-attestation.json \
  --cpu-profile portable \
  --evidence-commit 67eccca5959cf263ef9e4fab2227bfe8f4d9a4b3
```

Select a newly reviewed evidence commit explicitly for a later run; do not read
the expected trust anchor from the unverified downloaded report. This command
does not install containers or certify licenses. Raw Syft JSON and gap reports
remain supplemental unsigned artifacts; the signature covers the SPDX predicate.
