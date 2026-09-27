# Stock updater 1.3.10: source evidence

Inspected 2026-09-26, read-only, from the owner-supplied extracted eMMC tree.
Package root: `0.rootfsA/usr/lib/node_modules/@jibo/jibo-ota-updater`.
`package.json` identifies updater **1.3.10**; this is not the robot's firmware
version and does not establish compatibility with every OOBE/v1/v2 image.
No stock code, credentials, image contents or private keys are copied here.
No updater command was executed and no robot was contacted.

The extracted rootfsB package also reports 1.3.10; its `apply_os.js` and
`download-update.js` hashes match rootfsA. This is only a two-file comparison,
not a complete slot equivalence check. RootfsA's `etc/os-release` reports
Buildroot 2015.11, which likewise does not identify the robot product release.

## Verified source behavior

Paths below are relative to that package root unless explicitly stated.

| Boundary | Evidence | Consequence |
| --- | --- | --- |
| Metadata | `src/get-update.js:49` calls `getUpdateFrom` with source version, subsystem and optional filter. | We have a concrete client contract for this package version, not fleet-wide proof. |
| Download | `src/download-update.js:59` computes SHA-1; lines 75–129 select HTTP/HTTPS, require 200, read Content-Length for progress and compare the result to `--shasum`. | SHA-1 is legacy byte-integrity compatibility, not publisher authentication. No signature verification, byte-count equality check or Range/resume path was found in this downloader. |
| Retry download | `src/apply_common.js:347` passes URL, ID and `shaHash` to the downloader, not metadata length or dependencies. | Do not infer dependency enforcement or size enforcement from metadata presence. The higher-level dependency scheduler remains to be traced. |
| Envelope | `README.md:54` and `src/package-update.js:39` describe/build an uncompressed tar with optional `filesystem.tar.bz2`, `preinstall`, `postinstall`. | Hooks are executable code. An empty/no-op package can be accepted by stock code; our inspector should reject it. |
| Extraction | `src/apply_common.js:267` uses external tar extraction; `:286` runs preinstall, filesystem application, then postinstall. | Do not run stock extraction on the workstation. Inspect paths/types and bound resources offline first. |
| Filesystem replacement | `src/apply_common.js:193` and `:206` unmount, format ext4, remount and unpack the filesystem payload. | The implementation is more destructive than the README's illustrative file deletion commands. A valid archive is not an installation safety proof. |
| OS | `src/apply_os.js:37` changes boot environment; `:127` requires bootcount 1; `:144` clears the upgrade flag; `:111` copies the running root to the other root. The order is explicit at `:161`. | The previous root is overwritten before the later global service verification. A/B must not be advertised as a retained rollback copy. Actual bootloader/power-loss recovery is untested. |
| Services | `src/apply_services.js:27` applies to the services partition mounted at `/usr/local`, then `:29` invokes bodyboard firmware update. | Services updates are not just replaceable user-space files; bodyboard behavior needs separate evidence. |
| Skills | `src/apply_skill.js`, `doSkillInstall` / `doFilesystem`, clears the supplied destination then unpacks the nested filesystem. | Destination authorization, preservation and subsystem allowlisting need validation upstream. |
| Bootloader | `src/apply_bootloader.js`, `copyImg` / `doFlash`, writes `u-boot.img` to the boot device. | Exclude this subsystem from the first inspector and release scope. |
| Final check | `src/apply-update.js:209` waits up to 60 seconds for a system-manager registry record; failure at `:241` marks all work for retry. | This is not a full robot health test or proof of rollback. |

`0.rootfsA/etc/init.d/S72jibo-apply-update` runs the updater with `--continue`
when mode is `ota` or `/var/jibo/ota.json` exists. The extracted `/usr/bin`
launcher is zero-length, so this directory is evidence, not a runnable image.
The package source is present even though the launcher is not usable here.

Absence of a signature check in the inspected downloader is a bounded finding:
it does not prove that every layer or firmware version lacks authentication.
Inspect the SDK transport, metadata authorization and higher-level scheduler
before selecting a compatible publisher-trust design. Do not disable checks.

## Reproducibility fingerprints

SHA-256 of inspected source files (inventory identity only, not signatures):

| File | SHA-256 |
| --- | --- |
| `package.json` | `0bada40a4743885e97b8ddfb6838c66c28e787b5ad75c6a54e44ff6ffe12d13b` |
| `src/apply_os.js` | `0ed80c3aa979ac1f48bca58ee4322ef36da62f729f873704edf6354ccf0a1846` |
| `src/apply_common.js` | `44a0d69a9690ee66d3e810942364e752a5723adc292da24a04cee228515d5724` |
| `src/apply-update.js` | `e348ec59be7bd5352016e073fc632ade0e08065b5d8095107427800fa37dc836` |
| `src/download-update.js` | `33f6db1496baa3abd506a2ba9dad9b5cdf7567341e3e292e42cb3ed6f016003c` |

## Next implementation boundary

The original offline outer-envelope inspector is
`scripts/bootstrap/inspect-robot-ota-package.py`, with synthetic fixtures in
its accompanying test. It is deliberately not the stock updater.
Read headers and hash bytes; never extract payloads or execute hooks. Restrict
the initial format to known ordinary envelope members, reject links/special
entries and unsafe paths, and flag hooks for manual review. This conservative
subset may reject stock packages; it is not a general compatibility validator.

Every report must keep update offers disabled. Inner filesystem safety, ABI,
partition sizes, publisher trust, dependency ordering, owner-data preservation,
licensing and hardware recovery remain separate gates. No production catalog
records or downloadable firmware should be created from these findings.

Run locally with Python 3 (substitute your installed Python executable):

```text
python3 scripts/bootstrap/inspect-robot-ota-package.py /path/to/package.tar
```

Optional `--sha1-compat` reports legacy compatibility hashes; neither SHA-1 nor
SHA-256 proves publisher identity. Exit 0 means only that the outer envelope
passed this inspector's structural subset; `CanOfferUpdates` remains false.
Exit 2 rejects the input. No extraction directory is created. Use a stable local
copy of an untrusted package in an isolated analysis environment; this tool is
not a sandbox or a substitute for inner-filesystem inspection.
