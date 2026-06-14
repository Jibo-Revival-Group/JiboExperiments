# Jibo.Cloud.DotNet

## Summary

`Jibo.Cloud.DotNet` is the stable hosted implementation of the OpenJibo cloud.

This is the production-oriented path for restoring device connectivity and creating a foundation for future runtime, AI, and OTA work.

Current spoken cloud version: `Cloud version 1.0.19.`

Local startup:

```powershell
.\scripts\cloud\Start-OpenJiboDotNet.ps1
```

Run that from the repo root. For the full local guide, including Node and Playground, see
[local-cloud-quickstart.md](../../../docs/local-cloud-quickstart.md).

Release hygiene reminder:

- bump [OpenJiboCloudBuildInfo.cs](/C:/Projects/JiboExperiments/OpenJibo/src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Application/Services/OpenJiboCloudBuildInfo.cs) whenever we ship a meaningful hosted-cloud update
- keep the spoken version response and `/health` version field aligned from that single source of truth
- the API startup log now prints the same version on boot, which is useful for confirming the running build during live robot tests

## Architecture

The first implementation is a modular monolith:

```text
Api -> Application -> Domain -> Infrastructure
```

This keeps deployment simple while preserving clean boundaries.

## Azure Direction

The target Azure footprint is:

- Azure App Service for HTTP and WebSocket traffic
- Azure SQL for relational persistence
- Azure Blob Storage for uploads and update artifacts
- Azure Key Vault for secrets and certificates
- Application Insights for observability

Azure SQL is the primary system of record for:

- accounts
- devices
- sessions
- update metadata
- host mappings
- bootstrap and provisioning records

## Compatibility Goal

The first compatibility milestone is `core revive`.

That means the .NET cloud should handle:

- token and session issuance
- account and robot identity flows needed for startup
- core `X-Amz-Target` dispatch
- listen and proactive WebSocket paths
- basic media and update metadata responses
- handoff into normalized `TurnContext` and `ResponsePlan` contracts

## Relationship To The Node Prototype

The Node server remains the discovery harness and fixture source.

The .NET implementation should:

- copy observed behavior where needed
- use fixtures captured from Node and real robots
- avoid speculative protocol design
- separate HTTP parity, websocket parity, and future discovery work so coverage stays honest

## Current State

This folder now contains the first hosted scaffold, not just a README.

The intent is to grow from a runnable dev monolith into the real Azure deployment target without abandoning the existing abstractions work.

Current websocket scope is still intentionally narrow:

- token-backed socket sessions
- explicit websocket turn-state tracking separate from long-lived cloud session state
- synthetic `LISTEN` result shaping for `LISTEN`, `CLIENT_NLU`, and `CLIENT_ASR`
- buffered audio state tracking behind a dedicated turn-finalization layer
- raw audio auto-finalization once `LISTEN` + `CONTEXT` + minimum buffered audio thresholds are present
- synthetic STT strategy selection for fixture-driven audio turn completion
- structured websocket telemetry and live-run fixture export
- `CONTEXT` capture and follow-up turn state
- `EOS` completion
- delayed `SKILL_ACTION` emission after `EOS` to preserve the current Node-observed turn sequence
- first skill vertical for joke/chat `SKILL_ACTION` playback
- repo-root live-run capture support for both `captures/http/` and `captures/websocket/`

Not yet covered:

- real binary audio / ASR finalization parity
- provider-backed ASR integration
- timed finalize/fallback behavior matching richer Node turn-state semantics
- upstream Nimbus or broader skill lifecycle behavior
- animation / expression command families
- ESML feature parity beyond the narrow synthetic playback payloads used in the current scaffold

## Live Capture Status

The first real `.NET` robot test has confirmed:

- startup HTTP traffic reaches the `.NET` cloud
- `Notification.NewRobotToken` is in the active startup path
- `api-socket.jibo.com` connections are being accepted live

It has not yet confirmed:

- full startup parity with the successful Node run cadence
- consistent eye-open / wake completion on the robot
- the later health/log upload sequence currently seen in the working Node run

Current raw-audio behavior is still a compatibility bridge:

- if buffered audio has a synthetic transcript hint, the server now auto-finalizes the turn and emits `LISTEN` + `EOS` + `SKILL_ACTION`
- if buffered audio crosses the finalize threshold without a usable transcript, the server now emits a Node-style fallback completion with `EOS` instead of hanging the turn forever
- this is intentionally not a claim of real ASR parity
- follow-up turns now preserve enough constraint state to distinguish yes/no-style replies from ordinary free-form chat
- create-flow yes/no turns now preserve `create/is_it_a_keeper` and `domain=create` in the outbound synthetic `LISTEN` payload
- structured word-of-the-day guesses now complete as `CLIENT_NLU` turns instead of falling back to pending/blank-audio behavior
- spoken word-of-the-day launch phrases now route into the same cloud intent as the on-screen menu path
- spoken word-of-the-day puzzle answers now emit menu-compatible `guess` turns, including line-number picks resolved through the observed hint order
- voice-triggered word-of-the-day launches now emit the same `loadMenu + destination=word-of-the-day` shape the robot already uses successfully from the menu
- hotphrase `[BLANK_AUDIO]` cleanup turns are ignored instead of reopening the cloud into a stale blank-audio comment path after word-of-the-day completion
- phrase matching has been widened slightly for known test prompts such as joke, dance, surprise, weather, calendar, commute, and news variants
- time replies now use the natural hour format without a leading zero
- plain time/date/day questions now travel through stock-shaped local `@be/clock` handoffs, and `open the clock` uses the direct clock-view path instead of the menu path
- timer/alarm voice launches now accept compact alarm forms like `830` and `8 30`, and malformed timer/alarm requests stay on a clarification reply instead of generic cloud chat
- media and update metadata now persist to a local state file so gallery/update behavior is not lost on every process restart

## Buffered Audio STT

The current `.NET` websocket stack now preserves buffered Ogg/Opus websocket frames in memory for each in-flight turn.

That enables two distinct STT paths:

- fixture-oriented synthetic transcript hints for replay and parity tests
- an opt-in local tool-based path that can normalize the buffered Ogg pages, call `ffmpeg`, and then call `whisper.cpp`

The local tool path is intentionally off by default. It exists to help map real robot audio behavior while the stable hosted cloud remains the primary goal.

The checked-in API host config enables that path by default, but no longer pins
Linux-only tool paths. At startup OpenJibo resolves `ffmpeg`, `whisper-cli`, and
the model from explicit config, environment variables, common Linux/macOS
locations, and finally command names on `PATH`.

Useful macOS overrides:

- `OPENJIBO_STT_FFMPEG_PATH`
- `OPENJIBO_STT_WHISPER_CLI_PATH`
- `OPENJIBO_STT_WHISPER_MODEL_PATH`

Common macOS candidates include Homebrew paths such as
`/opt/homebrew/bin/ffmpeg`, `/opt/homebrew/bin/whisper-cli`, and
`~/whisper.cpp/models/ggml-base.en.bin`, plus
`~/Library/Application Support/openjibo/whisper/ggml-base.en.bin` for a
user-local model install. Temp audio still defaults to `/tmp/openjibo-stt` in
the local API config.

On the current macOS development machine this path has been verified with
Homebrew `ffmpeg`, Homebrew `whisper-cli`, and a local
`ggml-base.en.bin` model under `~/Library/Application Support/openjibo/whisper`.
The CLI transcribes test WAV audio and writes the transcript to `stdout`, which
is the stream parsed by `LocalWhisperCppBufferedAudioSttStrategy`. The remaining
validation is an end-to-end turn with real Jibo WebSocket audio reaching the
running server.

Configuration lives under `OpenJibo:Stt`:

- `EnableLocalWhisperCpp`
- `FfmpegPath`
- `WhisperCliPath`
- `WhisperModelPath`
- `WhisperLanguage`
- `TempDirectory`

This is not yet a claim of production-ready onboard ASR. It is a `.NET` discovery seam that keeps us compatible with the Node oracle while we evaluate longer-term options such as Azure-hosted STT or a managed decode/transcribe stack.

Latest live-capture guidance after the `2026-04-18` round:

- prefer synthetic transcript hints when they are present in the observed turn
- only use local `whisper.cpp` when the configured tool paths are real and the decode chain is behaving
- treat `ffmpeg` decode failures on normalized Ogg captures as evidence that the local audio path still needs more hardening before it can be the default live-test expectation
- keep the Node implementation as the oracle for yes/no turn semantics and audio preprocessing details until the `.NET` port catches up

Capture-storage guidance while moving toward hosted group testing:

- repo-local file captures remain the default for laptop-based reverse engineering
- hosted deployments should keep runtime request handling decoupled from long-term capture retention
- sanitized fixtures remain the preferred durable artifact for parity work and bug reproduction

Current local state persistence:

- default path: `App_Data/cloud-state.json` under the running API directory
- current contents: media metadata, backup metadata, and staged update metadata
- current limitation: media bodies are only preserved through the existing text-based HTTP body capture seam, so this is a hosted-gallery bridge, not final binary-safe media storage

## Recent Protocol Fixes

### Tutorial yes/no flow (`tutorial/yes_no`)

The tutorial dance sequence ends with "did you like my dance?" — a yes/no question handled entirely by the robot-local tutorial skill. This was previously broken because the cloud was sending a competing `SKILL_ACTION` and returning the wrong `outboundRules` in the `LISTEN` reply.

Two changes in `ResponsePlanToSocketMessagesMapper` fixed this:

**1. Suppress the chitchat `SKILL_ACTION` for `tutorial/yes_no` turns.**

The tutorial skill handles the yes/no response locally. Sending a competing `SKILL_ACTION` with `final:true` caused the GLSM to double-dispatch and the dance question would repeat forever. A new `IsYesNoListenTurn()` helper checks for the `tutorial/yes_no` rule specifically (not any yes/no rule) and suppresses the `SKILL_ACTION` only for that case. Cloud-side yes/no flows (`shared/yes_no`, `surprises-ota/want_to_download_now`, etc.) are unaffected.

**2. Return a single `outboundRule` in the `LISTEN` reply for `tutorial/yes_no` turns.**

The `outboundRules` field tells Jibo which rules to match the response against. For yes/no turns it must be a single rule (e.g. `["tutorial/yes_no"]`), not the full global rules list. `tutorial/yes_no` was missing from `ReadYesNoRule()`, so `isYesNoTurn` was `false` and the full list was sent instead. Adding `tutorial/yes_no` to that method fixes the `outboundRules` to the correct single-rule form.

### Family loop member filtering

`EnsureRobotLoopMember()` seeds an internal `type="robot"` loop member used by the SSM to prevent `Q4-Server_connection_lost` errors. This member must not appear in API responses — it has no display name and confuses the family list UI.

Filtered in `JiboCloudProtocolService` at two points:
- `ListMembers` / `ListLoopMembers` operations
- `MapLoopRecord()` — the loop detail returned during session startup

The internal seeding is unchanged; only API-facing responses are filtered.

## Current Interaction Paths

The working cloud model currently looks like three main paths:

1. Jibo reports what already happened locally and the cloud tracks or lightly completes the turn.
2. Jibo reports what happened locally and the cloud responds with a different synthetic completion path.
3. Jibo streams raw audio and the cloud interprets the turn before sending ESML back.

That framing matches the repo evidence so far and is a good operating model for current discovery. There may still be smaller side paths around proactive traffic, direct skill-to-service communication, or future on-robot extensions, but those are not the main cloud revive loop yet.
