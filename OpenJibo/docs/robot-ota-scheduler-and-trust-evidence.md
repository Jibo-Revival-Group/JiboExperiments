# Stock OTA scheduler and trust boundaries

Read-only follow-up, 2026-09-27. This extends the
[updater 1.3.10 evidence](robot-ota-stock-1.3.10-evidence.md), not a release gate.
No robot service, extracted program, cloud request or update offer was executed.

## Scheduler boundary

The supplied services partition contains an embedded TypeScript source map at
`3.services/bin/jibo-ssm/lib/skills-service-manager.js.map`. Its
`sources` entry `src/utils/OTAUpdater.ts` is used for the source line references
below. The adjacent `lib/docs/utils/OTAUpdater.js` contains documentation, not
the implementation. SHA-256 of the map:
`c6717f3d0355a0f6e7ff4f6e70af23c1bd18dd3fed32e32b73d71dd4239ccca1`.

- Line 9 imports `systemManager` from `jibo-service-clients`.
- Lines 30–38 describe an OTA record including opaque `dependencies` and a
  `downloaded` flag. A field declaration is not dependency validation.
- Lines 201–202 delegate `checkForUpdates` and the filter to system manager.
- Lines 561–617 re-query available updates, collect downloaded IDs, and choose
  the first unfinished record for `downloadUpdates`. Completion re-enters the
  loop. This is serial download orchestration, not a dependency graph sort.
- Line 304 delegates installation to `systemManager.installUpdates({ids}, ...)`.

The extracted native `3.services/bin/jibo-system-manager` and
`3.services/lib/libJiboSystemManager.so` exist, but this inspection has not
established their dependency comparison, cycle handling, version constraints,
or install ordering. No such rules should be invented from the array order.
Source-map text also needs correlation with the deployed firmware before
claiming behavior for other robot releases.

## Metadata authentication boundary

The updater-bundled SDK is at
`0.rootfsA/usr/lib/node_modules/@jibo/jibo-ota-updater/node_modules/@jibo/jibo-server-client`.
The references below are relative to that root. Only SDK source was inspected;
the robot's credentials and runtime configuration were not read.

| Evidence | Finding |
| --- | --- |
| `clients/update.js:6` and `apis/update-2016-03-01.min.json` | The registered API is 2016-03-01, target prefix `Update_20160301`, JSON 1.1 and signature version v4. |
| `lib/service.js:283`, `lib/event_listeners.js:112` and `:165` | Request setup selects the signer, hashes content and obtains credentials for signing. |
| `lib/signers/v4.js:34` and `:98` | Authorization uses AWS4-HMAC-SHA256 with date and optional session token. |
| `lib/config.js:453`, `lib/http/node.js:25` and `:110` | Defaults enable TLS and certificate validation; the actual protocol follows the resolved endpoint. |
| `lib/service.js:45` and `:400`, `lib/region_config.js:38` | Configuration and endpoint rules can alter those defaults. Effective runtime settings were not verified. |

This establishes a signed metadata **request**, not a publisher-signed response
or firmware package. The updater's separate downloader still follows the
metadata URL and permits HTTP. Neither a SHA-1 checksum nor client request
signing substitutes for an approved publisher-trust and freshness design.
Reuse the shared runtime's existing SigV4 infrastructure where appropriate;
do not introduce a second robot protocol in the commercial control plane.

## Release implications

Preserve dependency metadata as implemented, but do not claim that round-trip
preservation proves install eligibility. Before release, obtain native source
or authorized, isolated contract traces for missing dependencies, unavailable
versions, cycles, incompatible components and stable ordering across retries.
Do not use an actual install as the first method of discovering that behavior.

Archive structure, metadata authentication, dependency selection, hardware
compatibility and recovery are independent gates. Even an archive that passes
all offline structural checks must remain ineligible for OTA offers until the
other gates have explicit evidence.
