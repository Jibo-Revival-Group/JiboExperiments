# Greetings And Presence Plan (`1.0.19`)

## Purpose

Recreate the original Jibo greeting charm with modern cloud architecture:

- person-aware greetings when someone is detected
- proactive offers tied to presence, time of day, and memory
- safe cooldown rules so proactivity feels alive, not noisy

This plan is source-anchored to Pegasus and scoped to shippable slices.

## Pegasus Behavior Baseline

Primary source artifacts:

- `C:\Projects\jibo\pegasus\packages\hub\be-skills\greetings_manifest.json`
- `C:\Projects\jibo\sdk\skills\greetings\src\GreetingsSkill.ts`
- `C:\Projects\jibo\sdk\skills\greetings\src\GreetingsSM.ts`
- `C:\Projects\jibo\sdk\skills\greetings\src\states\IntentSplit.ts`
- `C:\Projects\jibo\sdk\skills\greetings\src\states\ProactiveGreetingState.ts`
- `C:\Projects\jibo\sdk\skills\greetings\src\states\ProactiveProbabilityState.ts`
- `C:\Projects\jibo\sdk\skills\greetings\src\states\ShouldDoMorningGreetingState.ts`
- `C:\Projects\jibo\sdk\skills\greetings\src\states\ShouldDoBirthdayState.ts`
- `C:\Projects\jibo\sdk\skills\greetings\src\states\ShouldDoHolidayState.ts`
- `C:\Projects\jibo\pegasus\packages\hub\src\proactive\ProactiveTransactionHandler.ts`
- `C:\Projects\jibo\pegasus\packages\hub\src\proactive\tools\ContextTools.ts`

Key behaviors to port:

- explicit reactive/proactive greeting split
- identity source split:
  - reactive path uses active speaker
  - proactive path uses present identified persons
- hub-level proactive gating:
  - block greetings when trigger source is `SURPRISE`
  - throttle by interaction history (`GreetingsLaunchLast2Hours < 1`)
- morning/birthday/holiday gates with per-user recency checks
- optional follow-up response flow after proactive greetings

## Current OpenJibo Baseline

Current implementation anchor:

- `C:\Projects\JiboExperiments\OpenJibo\src\Jibo.Cloud\dotnet\src\Jibo.Cloud.Application\Services\JiboInteractionService.cs`
- `C:\Projects\JiboExperiments\OpenJibo\src\Jibo.Cloud\dotnet\src\Jibo.Cloud.Application\Services\ProtocolToTurnContextMapper.cs`
- `C:\Projects\JiboExperiments\OpenJibo\src\Jibo.Cloud\dotnet\src\Jibo.Cloud.Application\Services\WebSocketTurnFinalizationService.cs`
- `C:\Projects\JiboExperiments\OpenJibo\src\Jibo.Cloud\dotnet\src\Jibo.Cloud.Application\Services\ChitchatStateMachine.cs`
- `C:\Projects\JiboExperiments\OpenJibo\src\Jibo.Cloud\dotnet\src\Jibo.Cloud.Infrastructure\Persistence\InMemoryPersonalMemoryStore.cs`

What we already have:

- tenant-scoped memory primitives (name, birthday, preferences, affinity)
- proactivity baseline with pending-offer follow-up handling
- state-machine style chitchat split (`ScriptedResponse`, `EmotionQuery`, `EmotionCommand`, `ErrorResponse`)
- GLSM-aware websocket lifecycle and stuck-listen recovery

Main gap:

- no first-class presence/identity perception extraction from runtime context for greeting policy decisions

Current implementation progress:

- runtime presence parsing now extracts speaker, people-present ids, and loop user first names
- reactive and proactive greeting turns now write durable greeting-presence history into cloud state
- proactive greeting gating now consults stored greeting history first, then falls back to the current turn metadata
- birthday-aware proactive greetings now use the loop/person birthday memory when the current date matches
- holiday-aware proactive greetings now use the loop holiday calendar when the current date matches
- morning proactive greetings now stay distinct from return-visit greetings so a fresh start of day still sounds like a morning greeting
- the remaining work is to broaden the presence policy surface so it can grow into richer day-part and return-visit variations without reworking the storage seam again

## Implementation Slices

### Slice G1: Presence Context Extraction And Session Snapshot

Goal:

- extract presence/identity fields from websocket context payload into normalized metadata for routing

Initial fields:

- focused speaker id
- identified person ids present
- total people present
- trigger source if present
- time-of-day helper signals

Notes:

- no facial-recognition implementation is needed in cloud; cloud consumes robot perception signals

### Slice G2: Greeting Intent Families And Parser Guardrails

Goal:

- add explicit greeting intent families with question/command guardrails

Initial families:

- `hello`, `hey jibo`, `what's up`
- `good morning`, `good afternoon`, `good evening`, `good night`
- `i'm home`, `i'm back`
- identity question (`who am i`) as a future-compatible hook

Guardrails:

- avoid stealing non-greeting domains
- keep existing date/time and birthday disambiguation intact

### Slice G3: Greeting State-Machine Port (OpenJibo Style)

Goal:

- add a greeting state-machine module with explicit route metadata like chitchat

Planned routes:

- `ReactiveGreeting`
- `ProactiveGreeting`
- `MorningGreeting`
- `SpecialDayGreeting`
- `OptionalResponse`
- `ErrorResponse`

Output shape:

- keep stock-compatible skill payload patterns
- preserve MIM/ESML hook points for charm content

### Slice G4: Proactive Gating And Cooldowns

Goal:

- port the critical Pegasus policy behavior to prevent spam

Phase-1 rules:

- skip proactive greetings when trigger source is surprise
- enforce per-tenant/person cooldown (target parity: 2-hour greeting window)
- suppress proactive launch when session is unstable (pending listen/follow-up conflict)

### Slice G5: Person Queue And Memory Extensions

Goal:

- introduce lightweight person queue/history for greeting relevance

Phase-1 storage additions:

- last-seen timestamp per person key
- last-greeted timestamp per person key
- optional preferred-name alias for spoken greeting personalization

### Slice G6: Rollout, Logging, And Live Validation

Goal:

- ship safely with observability and test confidence

Required coverage:

- unit tests for context extraction and intent routing
- websocket tests for presence-triggered greeting eligibility and cooldown behavior
- live captures validating:
  - no stuck listening regressions
  - no runaway proactive loops
  - stable fallback when identity is unknown

## Suggested Build Order

1. G1 context extraction + diagnostics
2. G2 greeting parser families + guardrails
3. G3 greeting state machine (reactive first)
4. G4 proactive gating + cooldowns
5. G5 person queue memory extensions
6. G6 live validation and polish

## Firmware Follow-up for Missed Relaxed-Mode Arrivals

On November 24, 2026, the same stock 1.9.2 robot missed a quiet walk-in after
15 minutes away and greeted the enrolled person on a later quiet approach.
The last logged circadian state before both approaches was `RELAXED`.

| Walk-in (Central time) | Robot evidence | Cloud evidence |
| --- | --- | --- |
| Missed, around 3:41–3:44 p.m. | System logs continued. The robot logged no new face track, identity match, arrival evaluation, or camera fault. | No `NEW_ARRIVAL` request. |
| Successful, 3:45:57 p.m. | Passive vision matched the enrolled household member. The robot passed its local checks and sent `NEW_ARRIVAL` without a wake word and before the on-demand camera check. | The deployed server returned `proactive_greeting`, then a separate `SURPRISE` returned `proactive_joke`. |

The earliest observable difference was before the arrival tracker. Stock logs
omit per-frame detector activity, the active perception profile, and candidate
rejection reasons. The installed
`normal` and `focused` perception profiles enable face detection and tracking.
The `minimal` profile omits both. The attention warning about unavailable head
or body controls appeared in both tests.

### Diagnostics for future firmware

- Log perception profile changes, camera frame age and drop counts, detector
  runs and candidate rejection reasons, and face-track start and loss.
- Log arrival tracker state changes and suppression reasons so a detected face
  can be traced through to a greeting or a decision to stay quiet.
- Reproduce with repeated quiet entries after the tracker resets. Start with
  the same position and lighting in Alert and Relaxed, then vary approach
  angle and lighting one at a time. Record the time from entry to detection
  and the outcome of each stage. Keep diagnostic output to event timings,
  counters, and reason codes without retaining frames or face templates.

## Definition Of Done For This Track

- presence-aware greeting behavior works with and without identified users
- proactive greeting frequency is policy-bounded and observable
- no regressions in existing `1.0.19` memory/weather/proactivity flows
- release docs and backlog are updated with shipped scope and next slice
