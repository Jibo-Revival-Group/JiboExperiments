# Feature Backlog

## Purpose

This backlog turns discovery into implementation slices for the hosted `.NET` cloud.

Use it as the working queue when picking the next feature or bug-fix slice. The release pattern is: implement a narrow slice, test it on stock OS `1.9`, update this file with what happened, then either close the release or roll the next larger idea forward.

The live regression checklist for release closeout is [regression-test-plan.md](regression-test-plan.md).

The active `1.0.20` execution shape is tracked in [release-1.0.20-plan.md](release-1.0.20-plan.md). This file keeps the full `1.0.18` evidence trail for parity reference and the `1.0.19` closeout history alongside the new queue.

Status key:

- `implemented`: present in current source and covered by focused tests
- `polish`: implemented enough to test, but still needs live proof or small cleanup
- `ready`: grounded enough to implement now
- `discovery`: more Pegasus, JiboOS, capture, or log work needed first
- `blocked`: waiting on infrastructure, provider choice, or a risky unknown

Tags:

- `protocol`: websocket, HTTP, or stock payload shape
- `content`: provider data or response content
- `docs`: operator docs, runbooks, or capture process
- `stt`: transcript reliability
- `storage`: persistence, media, backups, or hosted export

## Historical `1.0.18` Snapshot

Historical cloud version at closeout boundary: `1.0.18`

Runtime truth:

- hosted `.NET` projects and cloud tests target `net10.0`
- version source of truth is [OpenJiboCloudBuildInfo.cs](../src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Application/Services/OpenJiboCloudBuildInfo.cs)
- `/health`, startup logging, and spoken `cloud version` are aligned with that constant
- spoken `cloud version` is now a one-shot diagnostic with speech-tail protection instead of a follow-up chat turn

Current release theme:

- alarm and photo/gallery quirks have received the main bug-fix attention
- Word of the Day cleanup, constrained yes/no routing, unknown websocket event suppression, and local state persistence are already in the current code
- radio, ESML apostrophe cleanup, and first news are implemented in source/tests; radio and basic news are live-proven as of `jibo test 23`
- `jibo test 22` validated radio, exposed backup/load interference, exposed a shared yes/no no-input gap, exposed repeated create keeper prompts after photo handoff, and showed local whisper `ffmpeg` failures on unusable buffered audio
- `jibo test 23` validated basic news, proved one alarm set/fire path at `7:43 AM`, exposed comma-separated/short alarm follow-up parsing risk, showed stock alarm replacement yes/no rules that needed cloud handling, and showed photo gallery still failing when `shared/yes_no` ASR came back empty
- personal report parity now has loop-scoped calendar and commute provider seams that merge persisted loop events, birthday/holiday dates, and commute profiles; the remaining report gap is richer travel-time data, not missing structure
- `jibo test 24` showed alarm replacement yes/no working, but exposed empty `clock/alarm_set_value` and `gallery/gallery_preview` turns falling into generic `I heard you` fallback speech; it also showed `CLIENT_NLU cancel` inside `clock/alarm_set_value` re-asking for an alarm value instead of closing the prompt
- `jibo test 25` proved a broader regression path but exposed repeated backup-in-progress/update-menu blockage, timer/alarm stale state and delete/menu disagreement, gallery `shared/yes_no` hangs under `@be/gallery`, punctuated `Never mind.` falling through to chat, volume homophone parsing (`Set Volume 2-6.`), and settings volume-control cleanup falling into `I heard you`
- `jibo test 26` live-proved punctuated stop, volume homophone parsing, gallery launch/yes/create/save, and good morning; it still exposed robot-local backup warnings, long blue-ring buffering without a fresh `LISTEN`, alarm replacement drifting into the value/manual screen, and alarm delete phrases/mishears falling to chat
- `jibo test 27` isolated early confusion: local `jibo-server-service` restarted and raised `Q4-Server_connection_lost` before testing; cloud version then self-listened into `Cloudford.` because the previous diagnostic path stayed follow-up eligible; the backup warning again came from local `@be/surprises-ota` with no `Backup_*` HTTP calls
- `jibo test 28` isolated the follow-on backup doorway: cloud-version/generic Nimbus matches had `skipSurprises` unset, then stock BE requested `@be/surprises` after Nimbus settled; VAD inhibited the offer in Test 28, while Test 27 selected `@be/surprises-ota` through the same local lifecycle path
- `jibo test 29` confirmed `skipSurprises = true` was reaching stock BE and no backup announcement repeated in the focused run, but the cloud-version answer still interrupted because the spoken diagnostic included `Jibo` and triggered local hotphrase barge-in during Nimbus TTS
- `jibo test 30` confirmed cloud-version now speaks cleanly; it still exposed a local gallery-to-`@be/surprises-ota` backup announcement, missing visible empty-gallery voice listen, and a duplicate alarm clock relaunch after `638` was parsed locally as `6:38 PM`
- `jibo test 31` showed the remaining alarm/backup wrinkle in full: startup logged a legacy `Backup_20170222.List` request before the first voice turn, `7:11 AM` collapsed into `7:00 PM` / `setting alarm for seven`, and the later clock `No` replied `that's fine` before the robot opened `@be/surprises` and ended in a blue-ring listen loop until reset
- `jibo test 32` suggests the alarm set path is improving, but the remaining regression surface is now sharper: an alarm can still leave the listen open at the end, the proactive Word of the Day `Yes` branch can miss its yes/no slot and echo back, and delete-alarm retry still falls into a second `set one?` question with a broken follow-up reply

## Immediate `1.0.18` Queue

### 1. Radio Resume And Genre Launch

- Status: `polish`
- Tags: `protocol`
- Why now: the code path is implemented and test-backed, and it is a low-risk local-skill expansion after Word of the Day.
- Current code:
  - `open the radio` maps to `@be/radio` with `intent = menu`
  - `play country music` maps to `@be/radio` with `entities.station = Country`
  - websocket output includes `LISTEN`, `EOS`, local `SKILL_REDIRECT`, and silent completion
- Evidence:
  - JiboOS `@be/radio` treats `menu` as a play launch and reads `result.nlu.entities.station`
  - `Country` is a supported station key in the inspected genre metadata
  - `jibo test 22` radio live validation passed
- Exit criteria:
  - live `open the radio` resumes or opens radio without generic chat speech
  - live `play country music` opens a country station
  - no new stock-OS unknown-event noise appears in the radio launch path
- Next action:
  - run this in the `1.0.18` live regression pass and capture both websocket payloads and robot logs

### 2. News Through Nimbus

- Status: `implemented`
- Tags: `protocol`, `content`
- Why now: the first Nimbus-compatible cloud path is implemented, test-backed, and live-proven; content can stay synthetic for `1.0.18`.
- Current code:
  - `tell me the news` maps to `IntentName = news`
  - outbound listen match includes `cloudSkill = news`
  - `SKILL_ACTION` uses skill id `news` and `mim_id = runtime-news`
- Evidence:
  - JiboOS Nimbus checks `match.cloudSkill === "news"` and waits for a cloud response
  - `jibo test 22` captured the phrase `So, play the news.` reaching the `news` intent, but live behavior was not cleanly confirmed
  - `jibo test 23` successfully played the synthetic quick brief
  - original Pegasus `report-skill` news tests cover the next expansion shape: category preferences, default categories, duplicate filtering, missing-summary filtering, child/unidentified-speaker content filtering, and headline image metadata
- Exit criteria:
  - live `tell me the news` reaches the Nimbus-shaped path
  - the robot behavior feels like a cloud skill response, not generic chat playback
- Next action:
  - keep the basic path in regression; provider-backed or category-expanded headlines can wait for `1.0.19` unless chosen as the optional feature slice

### 3. Backup / OTA / Share Yes-No Reliability

- Status: `polish`
- Tags: `protocol`, `stt`
- Why now: constrained yes/no behavior affects daily-use prompts and was tangled with the alarm/photo/gallery work.
- Current code:
  - yes/no detection reads `listenRules`, `clientRules`, and `$YESNO` hints
  - covered prompt families include `settings/download_now_later`, `surprises-ota/want_to_download_now`, `surprises-date/offer_date_fact`, `shared/yes_no`, `create/is_it_a_keeper`, `clock/alarm_timer_change`, and `clock/alarm_timer_none_set`
  - outbound replies strip global rules and keep the local rule
  - no-input fallback for constrained prompts emits local `LISTEN`/`EOS`
  - `shared/yes_no` now participates in the STT-failure no-input path instead of staying pending behind `$YESNO` hints
  - repeated empty `create/is_it_a_keeper` replies redirect to `@be/idle` after the second miss
  - hosted turn results, fallback matches, and local skill redirects include `match.skipSurprises = true` so BE does not launch end-of-skill surprises after normal replies
- Latest evidence:
  - `jibo test 22` did not show `Backup_*` HTTP traffic during the backup complaint
  - `jibo test 25` again showed backup-in-progress/update-menu blockage without `Backup_*` HTTP traffic; observed cloud traffic was log upload, ASR binary upload, and update check traffic
  - `jibo test 26` again had the robot announce backup-in-progress from `@be/surprises-ota`, with no `Backup_*` HTTP target in the capture
  - `jibo test 27` repeated that pattern in a smaller capture: the only relevant hosted startup traffic was token/update/log style traffic, while the spoken backup warning was selected by local `@be/surprises-ota`
  - Test 27 also showed local `jibo-server-service` reconnect and `Q4-Server_connection_lost` before the voice test, so startup health should be checked before blaming backup prompts on hosted cloud behavior
  - `jibo test 28` showed no hosted backup trigger in the focused cloud-version window, but did show BE opening `@be/surprises` after a Nimbus turn because the outgoing match did not carry `skipSurprises`
  - stock `@be/surprises-ota` drives the backup notification from robot-local `jibo.scheduler.backupStatus`
  - original `surprises-ota` tests make backup and OTA notifications contextual-priority prompts, with repeat suppression through last-notification timestamps
  - a spoken `take a backup` command currently routes as generic chat and is not the same as proving the local backup scheduler path
  - `jibo test 23`, `jibo test 25`, and `jibo test 26` showed backup-in-progress sluggishness or warnings while backups were active; explicit backup voice launch remains unwired
  - Test 26 suggests this should be investigated beside robot-local scheduler status and log/upload load rather than only hosted backup APIs
  - `jibo test 30` showed the backup announcement after gallery came from local `@be/surprises` -> `@be/surprises-ota`, not from a hosted `Backup_*` HTTP call; the local `@be/idle` nighttime OTA helper can also initiate backup through `jibo.scheduler.backupRobot`
  - `jibo test 31` added a startup `Backup_20170222.List` capture before the voice session, which is useful evidence that the legacy backup-status path is active even when the user did not ask for backup
- Parity plan:
  - keep the current cloud `GetUpdateFrom` no-content compatibility only as a short-term updater bridge
  - add robot-local scheduler/status behavior that matches the original contracts instead of asking the cloud manifest to model menu state
  - model the original paths separately: `backupStatus` as boolean, `downloadStatus` as null-or-progress object, and `checkForUpdates` as an `updates[]` response
  - let the update menu decide among `backup`, `downloading`, `updates`, and `none` using those local status calls
  - keep the robot updater path compatible with `jibo-get-update` / `jibo-download-update` until the robot-side caller is fixed
- Exit criteria:
  - spoken `yes` and `no` work on update, backup, share/offer, and gallery/create prompts
  - empty or missed short replies retry locally instead of relaunching Nimbus or generic chat
  - ordinary Nimbus/chat/cloud-version turns settle without `@be/surprises` / `@be/surprises-ota` opening afterward
- Next action:
  - re-run these prompt families in the `1.0.18` live regression pass after the shared yes/no, alarm yes/no, and create no-input fixes
  - verify websocket `match.skipSurprises = true` on cloud-version, generic chat, fallback/no-input, and at least one local redirect
  - keep explicit backup creation as part of the update/backup/restore proof slice, not as an assumed yes/no prompt test

### 4. Alarm And Photo Gallery Release Regression

- Status: `polish`
- Tags: `protocol`, `stt`
- Why now: this is the main bug-fix theme for `1.0.18`.
- Current code:
  - alarm values parse explicit, compact, spaced, comma-separated, hyphenated, and local-context ambiguous times
  - short alarm/timer value replies are accepted during clock value follow-up rules instead of being filtered out before parsing
  - local clock value follow-up rules now return only `LISTEN`/`EOS`, avoiding the Test 30 duplicate delayed `@be/clock` relaunch after stock clock already consumed a short time reply
  - empty alarm/timer value turns complete locally as no-input instead of falling through to generic Nimbus speech
  - missing alarm times stay in local `@be/clock` clarification
  - alarm cancel can reuse the last active clock domain
  - cancel inside a clock value prompt maps to local clock `cancel`
  - stock alarm replacement/no-alarm prompts use the constrained yes/no path
  - gallery opens as `@be/gallery`; snapshot and photobooth open through `@be/create`
  - empty `gallery/gallery_preview` turns complete locally as no-input instead of relaunching Nimbus fallback speech
  - passive gallery/create/settings context no longer reopens stale cloud turns
  - active local prompts under gallery/settings contexts are preserved so real short replies are not suppressed as passive context
  - context-only or post-skill binary audio tails are ignored until a fresh `LISTEN`, preventing no-`LISTEN` blue-ring buffering loops
  - fresh no-transcript hotphrase launch `LISTEN` setup packets are ignored during diagnostic speech-tail cleanup, preventing the Test 27 `Cloudford.` self-listen path
  - blank-audio hotphrase turns clear pending listen state and install a short late-audio ignore window
  - `shared/yes_no` no-input fallback and repeated create keeper cleanup were added after `jibo test 22`
- Latest evidence:
  - gallery opened and handed into create, but repeated `create/is_it_a_keeper` prompts could leave the blue ring/listening state
  - alarm recognition collapsed several attempts before a complete alarm value could be set
  - `ffmpeg` failures were present during the same test window, so alarm/gallery retest should separate transcript quality from payload shape
  - `jibo test 23` set and fired a `7:43 AM` alarm, then failed a later clarify/replacement path when the robot heard `- Time. - 7, 14.` and stock NLU converted that to `7:00 PM`
  - `jibo test 23` photo gallery got stuck on `shared/yes_no` turns with empty ASR, not on a transcript-bearing `yes` that the cloud mapped incorrectly
  - `jibo test 24` recognized `Yes.` for `clock/alarm_timer_change`, but empty `clock/alarm_set_value` produced `I heard you`; current source now keeps that as local no-input
  - `jibo test 24` showed photo/gallery blue-ring cleanup improved and create keeper completion working, but empty `gallery/gallery_preview` produced `I heard you`; current source now keeps that as local no-input
  - `jibo test 25` showed gallery launching from the observed phrase `open the photogal`, but active `shared/yes_no` prompts under `@be/gallery` could hang; current source now recognizes the alias and preserves active gallery prompts even while ignoring passive gallery tails
  - `jibo test 25` showed timer/alarm still needs live follow-up for stale timer state, alarm replacement/PM ambiguity, and voice delete versus robot menu agreement
  - `jibo test 26` showed gallery success through empty-gallery yes, create, keep, save, and reopen, but also showed a post-gallery blue-ring/fallback tail now addressed by the no-`LISTEN` binary guard
  - `jibo test 26` showed alarm replacement still drifting into value/manual-screen behavior and alarm delete phrases/mishears falling to chat; current source now maps `delete the alarm`, `delete along`, and `delete the along` to local clock delete without keeping follow-up open
  - `jibo test 27` showed the no-`LISTEN` guard worked for same-transID binary tails, but a new hotphrase launch `LISTEN` could still capture diagnostic speech tail; current source now blocks that diagnostic-tail shape
  - `jibo test 30` showed cloud-version fixed, but the empty-gallery prompt did not visibly light the blue ring for a voice `yes`; treat the next gallery pass as a proof of local `shared/yes_no` listen ownership, not just cloud payload shape
  - `jibo test 30` showed `638` was processed at 6:38:13 AM and stock clock resolved it to `6:38 PM`; the duplicate replacement prompt matched our extra delayed clock relaunch, now suppressed for local clock follow-up rules
  - `jibo test 31` showed `7:11 AM` collapsing to `7:00 PM` / `setting alarm for seven`, then a clock `No` producing `that's fine` before the robot opened `@be/surprises`; the later retry sat in a continuous blue-ring/listen loop until reset
  - original clock tests confirm cancel inside the alarm value prompt must close without scheduling, existing-alarm `keep` must preserve KB/scheduler state, and existing-alarm `delete` or `cancel` must clear it
  - original gallery tests confirm empty-gallery `yes` redirects to `@be/create`, empty-gallery `no` exits, media-load failure exits, and delete confirmation only deletes on a positive `yes`
- Exit criteria:
  - gallery opens, offers to take a picture if empty, accepts `yes`, and hands into create
  - alarm set, clarify, replacement yes/no, cancel from value prompt, and cancel/delete flows behave locally and agree with the menu state
  - alarm replacement and deletion regression checks verify both websocket payload shape and persistent robot menu state where possible
  - short alarm/timer follow-up values do not produce a second `@be/clock` relaunch after the local skill consumes the answer
  - failures caused by collapsed STT transcripts are logged as STT issues rather than misdiagnosed as payload bugs
- Next action:
  - re-run a stock OS `1.9` regression bundle before declaring `1.0.18` complete

### 5. Optional Small Feature Before `1.0.18` Freeze

- Status: `implemented`
- Tags: `protocol`
- Why now: the user wants one or two features before `1.0.18` is called complete, but the release should not take on a risky subsystem.
- Selected slices:
  - Stop command
  - Volume up / volume down / set-to-value voice control
- Current code:
  - `stop`, `stop that`, and `never mind` map to stock `global_commands` `stop` NLU plus local `@be/idle` redirect/completion
  - `turn it up` and `turn it down` emit stock `global_commands` `volumeUp` / `volumeDown` with `volumeLevel = null` and no cloud speech
  - `set volume to six` emits stock `global_commands` `volumeToValue` with `volumeLevel = 6` and no cloud speech
  - `show volume controls` redirects into `@be/settings` with `volumeQuery`
  - stop/cancel matching now normalizes stock ASR punctuation, so `Never mind.` is still a stop command
  - absolute volume parsing now treats the observed homophone shape `Set Volume 2-6.` as level `6`
  - passive settings context and `settings/volume_control` no-input cleanup now avoid post-panel `I heard you` fallback speech
  - local clock delete/cancel commands now settle without a generic follow-up mic
- Evidence:
  - Pegasus `globals/global_commands_launch.rule` defines `stop`, `volumeUp`, `volumeDown`, and `volumeToValue`
  - stock Jibo `VolumePlugin` subscribes to global volume events and uses the same intent/entity names
  - stock `@be/settings` exposes `volumeQuery` and opens the volume panel
  - `jibo test 26` live-proved punctuated `Never mind.` and the `Set Volume 2-6.` homophone path
- Exit criteria:
  - live stop settles the robot without a generic chat reply
  - live volume up/down audibly changes volume or logs a local volume event
  - live volume-to-value changes the setting to the requested value or logs the expected stock local handling
  - live volume controls opens the settings volume panel
  - live volume controls settles after the panel opens without a trailing `I heard you`

## Implemented In Current Source

### ESML Apostrophe Encoding Bug

- Status: `implemented`
- Tags: `polish`
- Result:
  - apostrophes remain natural in spoken ESML
  - `&`, `<`, `>`, and `"` are still escaped
  - covered by `ResponsePlanMapper_EscapesSpeechWithoutEncodingApostrophes`
- Follow-up:
  - none unless a live capture proves another ESML escaping edge case

### Radio First Pass

- Status: `implemented`
- Tags: `protocol`
- Result:
  - phrase routing and websocket redirect/completion are implemented for radio resume/open and genre launch
- Follow-up:
  - live validation remains in the immediate queue

### News First Pass

- Status: `implemented`
- Tags: `protocol`, `content`
- Result:
  - Nimbus-shaped `news` cloud-skill lane is implemented with synthetic briefing content
- Follow-up:
  - basic live validation passed in `jibo test 23`
  - provider-backed headlines belong in `1.0.19` or later

### Clock / Alarm Family

- Status: `implemented`
- Tags: `protocol`
- Result:
  - time/date/day and clock open route through local `@be/clock`
  - timer/alarm menu, value, clarify, and delete are implemented
  - compact, spoken, comma-separated, and local-context alarm parsing has focused tests
  - short clock value replies under `clock/alarm_set_value` and `clock/timer_set_value` are not filtered out by websocket finalization
  - empty clock value turns produce local no-input instead of generic Nimbus fallback speech
  - `CLIENT_NLU cancel` inside a clock value prompt maps to local clock `cancel`
  - alarm replacement/no-alarm yes/no prompts are mapped as constrained local prompts
  - client NLU alarm clarify/cancel cases from `jibo test 20`, `jibo test 21`, and `jibo test 24` are reflected in source
- Follow-up:
  - live regression remains in the immediate queue
  - add fixture coverage for original clock-test branches that are not yet mirrored in `.NET`: no-alarm query `yes`/`no`, existing-alarm `keep` versus `delete`, and cross-domain `OtherSet` behavior
  - Test 26 still requires a focused live check for alarm replacement, voice delete versus menu state, and whether the no-`LISTEN` guard removes the long blue-ring loop

### Photo / Gallery / Create Family

- Status: `implemented`
- Tags: `protocol`, `storage`
- Result:
  - gallery, snapshot, and photobooth voice paths route to the correct local skills
  - the observed `open photogal` transcript routes to gallery
  - media metadata persists locally
  - `/media/{path}` serves the current text-body placeholder payload
  - empty `gallery/gallery_preview` turns produce local no-input instead of generic Nimbus fallback speech
  - active `shared/yes_no` prompts under `@be/gallery` stay active instead of being suppressed as passive local context
  - repeated empty `create/is_it_a_keeper` turns redirect to `@be/idle` after the second miss
- Follow-up:
  - live regression remains in the immediate queue
  - add fixture coverage for original gallery-test branches that are not yet mirrored in `.NET`: empty-gallery `yes` redirect to create, empty-gallery `no` exit, media-load failure exit, and delete confirmation `yes`/`no`
  - binary-safe media storage remains future work

### Constrained Yes-No Cleanup

- Status: `implemented`
- Tags: `protocol`, `stt`
- Result:
  - `shared/yes_no` is included in yes/no STT-failure detection
  - local no-input replies strip global rules and keep the active constrained rule
  - update, OTA, share/date-offer, gallery shared yes/no, alarm replacement/no-alarm, and create keeper rules share the same no-input fallback machinery
- Follow-up:
  - live update/backup/share/gallery/alarm replacement prompts still need another clean pass

### Cloud Version Tail Cleanup

- Status: `implemented`
- Tags: `protocol`
- Result:
  - `cloud_version` no longer keeps the generic follow-up mic open
  - diagnostic speech receives an eight-second late-audio ignore window
  - no-transcript hotphrase launch `LISTEN` setup packets inside that cleanup window are ignored before they can reopen a stale turn
  - spoken diagnostic wording is now `Cloud version ...` rather than `Open Jibo Cloud version ...`, avoiding the self-hotphrase phrase found in Test 29
  - focused websocket coverage reproduces the Test 27 `Cloudford.` shape: cloud-version speech, tail `LISTEN`, and binary speech tail
- Follow-up:
  - live smoke should confirm `cloud version` speaks `1.0.18`, carries `match.skipSurprises = true`, does not stop itself on the word `Jibo`, and settles without a generic `I heard...` reply or a local surprise handoff

### GLSM Listener Flow Capture And Recovery

- Status: `implemented`
- Tags: `protocol`, `docs`
- Result:
  - the legacy listener state machine source (`sdk ... glsm.png`) is now captured in current planning docs
  - runtime now emits GLSM-aligned phase snapshots (`HJ_LISTENING`, `LISTENING`, `WAIT_LISTEN_FINISHED`, `DISPATCH_DIALOG`, `PROCESS_LISTENER_QUEUE`)
  - turn diagnostics now include `glsm_phase_transition` for phase changes
  - websocket telemetry now records `glsmPhase` on binary/context/turn events
  - stale pending-listen recovery is now in source so a long-open no-context/no-audio listen can be cleared when the next hotphrase listen arrives
- Follow-up:
  - live-capture proof is still required against the recurring blue-ring/stuck-listening sequence
  - deeper GLSM parity (`Interrupt Listeners`, launch/global parse branches) should be tackled after this first capture slice is validated on-device

### End-Of-Skill Surprise Suppression

- Status: `implemented`
- Tags: `protocol`
- Result:
  - hosted `LISTEN` matches, fallback `LISTEN` matches, and local `SKILL_REDIRECT` matches emit `skipSurprises = true`
  - focused websocket assertions cover generic chat, cloud version, no-transcript fallback, and a local clock redirect
  - Test 28 evidence ties the repeated backup warning to the local `@be/surprises` lifecycle path after Nimbus, with no corresponding hosted `Backup_*` traffic
  - Test 29 showed the deployed payload reached stock BE and did not repeat the backup announcement in the focused run
- Follow-up:
  - live regression should confirm normal Nimbus/cloud/local turns no longer open `@be/surprises` or `@be/surprises-ota` after completion

### Word Of The Day Cleanup

- Status: `implemented`
- Tags: `protocol`
- Result:
  - voice launch uses menu-shaped local payload plus redirect/completion
  - structured and spoken guesses complete correctly
  - line-number guesses use hint order
  - close hint matching handles near misses
  - `right_word` cleanup can no-input close and redirect to `@be/idle`
  - late same-turn audio is ignored during cleanup
- Follow-up:
  - keep this in regression coverage because it shares turn-state machinery with gallery and alarm flows

### Stop And Volume First Pass

- Status: `implemented`
- Tags: `protocol`
- Result:
  - global stop commands emit stock `global_commands` `stop` and redirect to `@be/idle`
  - stop/cancel command matching tolerates punctuation from stock ASR
  - relative volume commands emit stock `global_commands` `volumeUp` / `volumeDown`
  - absolute volume commands emit `volumeToValue` with a `volumeLevel` entity, including the observed `Set Volume 2-6.` shape
  - volume controls launch redirects to `@be/settings` `volumeQuery`
  - passive settings context and `settings/volume_control` no-input cleanup avoid stale generic speech after the settings panel opens
  - websocket responses avoid generic chat speech for these local/global command paths
- Follow-up:
  - live validation remains in the immediate queue because volume depends on stock robot local global-command handling

### Unknown OpenJibo Event Noise

- Status: `implemented`
- Tags: `protocol`
- Result:
  - current websocket service drops unknown inbound message types silently
  - synthetic `OPENJIBO_TURN_PENDING`, `OPENJIBO_CONTEXT_ACK`, and fallback `OPENJIBO_ACK` should no longer be emitted by current source
- Follow-up:
  - `jibo test 22` still captured those event types from the deployed run, so the next deployment must verify the artifact/build as well as source

### Update Phantom Manifest Fix

- Status: `implemented`
- Tags: `protocol`, `storage`
- Result:
  - `GetUpdateFrom` returns no content when no update is staged
  - staged updates can still be created explicitly
- Follow-up:
  - end-to-end update delivery and restore proof remains future work

## `1.0.20` Launch Priorities

These are the carryover items that need a clean proof pass first:

1. Update / backup / restore parity
   - finish the update-menu investigation
   - prove whether the robot is fabricating an update path when none exists
   - keep backup and update state aligned with the robot-local behavior
   - verify the `ListUpdates`, `ListUpdatesFrom`, `GetUpdateFrom`, `CreateUpdate`, and `RemoveUpdate` shapes against the robot capture
   - separate menu-state truth from the compatibility bridge so `GetUpdateFrom` is not the source of truth
   - the current false-positive points at robot-side OTA KB state, especially `updatesAvailable`, not the cloud `GetUpdateFrom` placeholder
   - confirm update prompt state, backup prompt state, and restore rehydration are each driven by the right local status source
   - capture the minimum live or replayable path that shows update, backup, and restore without a phantom update announcement
   - hold the cloud compatibility bridge only for the updater helper until robot-local state is fully proven
   - exit criteria: the robot does not invent an update path when none exists, backup state is reported correctly, and restore is understood as persisted-state rehydration
2. Grocery list follow-up and add-item reliability
  - grocery follow-up listen is now emitted from the cloud path; finish hardware verification and any robot-side parity gaps rather than inventing a new capture flow
   - keep the list interaction listening for the follow-up item instead of dropping back to a passive state
   - verify long add-item phrases still reach the list engine cleanly
   - cloud parser coverage now includes polite long-form inline adds such as `can you add ... for my grocery list`,
     `could you please add ... in my shopping list`, and `would you add ... for my to do list`
3. Motion and personality command parity
- keep `go to sleep` from drifting into the wrong visible state; the legacy path is a real sleep global, the ASLEEP state is event-driven rather than timer-driven, wake is driven by `dayStarts`, `headTouch`, or `hjHeard`, and the legacy sleep behavior tree includes a sleeping-idle loop that we need to preserve so the robot stays visibly asleep
   - keep `turn around` and other motion verbs source-backed; the legacy snapshot backs the lane through `spin around` / `twirl`
   - separate bare `twerk` from the greeting-looking fallback while preserving `can you twerk`; the intent is source-backed and the remaining gap is robot-side STT mishearing
4. STT and turn-finalization cleanup
   - treat the bare `twerk` miss as an STT/parsing proof item until a robot capture proves the cloud path is at fault
   - `turn around` has been verified as working on the robot, so it no longer belongs in the STT cleanup bucket
   - keep short constrained replies and local prompts stable while the new regression items are retested
5. Broader personality and presence continuation
   - continue the source-backed favorites, presence, and seasonal ladder once the regression gaps are understood

## Near-Term `1.0.20` Queue

### 6. Stop Command

- Status: `polish`
- Tags: `protocol`
- User goals:
  - `stop`
  - `stop that`
  - `never mind`
- Evidence:
  - `@be/idle` exists and is already used as a cleanup redirect target
  - current `1.0.18` source emits stock `global_commands` `stop` plus local `@be/idle` redirect
- Questions:
  - whether live stock OS treats the combined global stop plus idle redirect as cleanly as expected during active local skills
- Exit criteria:
  - a spoken stop command settles the robot locally without a generic chat reply

### 7. Volume Up / Volume Down Voice Control

- Status: `polish`
- Tags: `protocol`
- User goals:
  - `turn it up`
  - `turn it down`
  - `increase the volume`
  - `decrease the volume`
- Evidence:
  - Pegasus global commands define `volumeUp`, `volumeDown`, and `volumeToValue`
  - stock Jibo `VolumePlugin` listens for those global intents and `volumeLevel`
  - current `1.0.18` source emits those stock NLU shapes and opens `@be/settings` `volumeQuery`
- Questions:
  - whether live stock OS applies the global volume event from the hosted cloud response without any additional local event payload
- Exit criteria:
  - relative voice volume commands adjust volume without generic cloud speech

### 8. Update, Backup, And Restore End-To-End Proof

- Status: `ready`
- Tags: `protocol`, `storage`, `docs`
- Why next:
  - prompt routing is improved, but lifecycle proof is still missing
- Current evidence:
  - `@be/settings` contains update and backup flows
  - `@be/restore` waits for a UGC key, runs restore, and reboots
  - original OTA surprise tests treat backup/download status as robot-local scheduler state, not as a direct cloud backup command path
  - no-op update fabrication has been removed from `.NET`
  - Test 25 still showed repeated backup-in-progress/update-menu blockage without `Backup_*` HTTP traffic
  - Test 26 repeated the backup-in-progress warning from robot-local `@be/surprises-ota` without `Backup_*` HTTP traffic
  - Test 27 repeated the same no-`Backup_*` finding and added evidence of local startup reconnect / `Q4-Server_connection_lost` before the test
  - Test 28 showed the same class of surprise handoff beginning at `@be/surprises` after Nimbus, before VAD inhibited the offer
- Progress update (`2026-06-25`):
  - added a local scheduler `/apply-update` proof endpoint so a staged update can advance the stored robot firmware version, require reboot, and disappear from subsequent scheduler update checks instead of remaining as a phantom pending update
  - added focused protocol coverage for staging a controlled robot update, applying it, and verifying the robot profile platform plus empty follow-up `/check-updates` response
- Progress update (`2026-06-29`):
  - restore now accepts a mapped backup `location.url` object or location URL string in addition to `backupId`, `id`, and `etag`, matching the shape returned by `Backup_20170222.List` / `Create` so stock restore callers can round-trip the response without a manual ID transform
  - added focused protocol coverage proving both location restore shapes rehydrate the saved update snapshot and remove post-backup stray updates
- Exit criteria:
  - no phantom "always has updates" behavior
  - one controlled update can be staged and delivered
  - one controlled backup can be taken
  - restore behavior is documented well enough to recover a test robot intentionally

### 9. STT Upgrade And Noise Screening

- Status: `in progress`
- Tags: `stt`
- Why next:
  - feature paths are now often correct when a transcript exists, but short replies and low-quality audio still block otherwise-correct flows
- Current evidence:
  - `jibo test 22` showed `ffmpeg` and `whisper.cpp` failures
  - `jibo test 23` did not show the same decode failure pattern, but gallery yes/no turns still produced empty ASR
  - `jibo test 24` still had collapsed or empty transcripts in alarm/gallery paths, including `Sudden alarm.`, `I'm setting alarm for seven.`, empty clock value input, and empty gallery preview input
  - `jibo test 25` still had short-answer failures, but several were cloud turn-state issues now patched rather than pure STT failures
  - `jibo test 26` had long no-`LISTEN` binary buffering and alarm-delete mishears now patched; remaining short-answer failures still need STT/noise work
  - current source now skips local whisper when buffered audio does not contain an Opus identification header
  - yes/no and alarm flows are especially sensitive to short or collapsed transcripts
- Progress update (`2026-05-21`):
  - added a small local whisper noise floor so obviously tiny buffered audio can be screened before ffmpeg/whisper work runs
  - short/noisy buffered turns now fail fast instead of wasting a transcription cycle
  - focused tests now cover the new low-audio rejection behavior
- Implementation notes:
  - add lightweight waveform or energy screening before transcription
  - compare managed STT against the local toolchain
  - keep synthetic transcript hints for fixture replay

### 10. Hosted Capture And Storage Plan

- Status: `in progress`
- Tags: `docs`, `storage`
- Why next:
  - repo-local captures work for single-operator testing, but group testing needs a cleaner archival/export boundary
- Implementation notes:
  - define local capture sinks versus hosted retention
  - decide how testers submit noteworthy sessions
  - keep a lightweight `capture-index.ndjson` manifest beside raw captures so testers can quickly find sessions, operations, and fixture exports
  - preserve sanitized fixtures as the durable parity artifact
- Architecture note:
  - [telemetry-production-safety.md](architecture/telemetry-production-safety.md) now records the production-safe split between Serilog logs and hosted capture storage, plus the rule that capture failures must never break request handling
- Progress update (`2026-06-24`):
  - websocket turn diagnostics now append to `capture-index.ndjson`, not only the raw daily event log, so short-answer probes, turn-boundary decisions, and STT guardrail events are findable in the same manifest as connections, messages, HTTP protocol captures, and exported fixtures
  - this keeps the local capture shape closer to the eventual hosted-retention boundary while the group-submission workflow is still being designed

### 11. Binary-Safe Media Storage

- Status: `in progress`
- Tags: `storage`, `protocol`
- Why next:
  - the first gallery bridge stores metadata and text-body placeholders, but final gallery support needs originals and thumbnails
- Questions:
  - whether stock gallery expects originals, thumbnails, or both
  - what upload metadata must survive for gallery refresh
  - how to map this cleanly to Blob Storage
- Implementation notes:
  - media content now flows through a storage seam with file and Azure Blob adapters
  - the protocol still serves the legacy text-body contract, but the original payload is now persisted separately and can be swapped to binary-native storage later

### Next Up (`2026-05-06`): Dialog Parsing Expansion And Ambiguity Guardrails

- Status: `polish`
- Tags: `protocol`, `content`, `stt`, `docs`
- Why now:
  - this is the next queued `1.0.20` implementation slice after weather provider bring-up
  - recent live runs showed phrases where trigger detection can interrupt full-utterance understanding
  - phrase import work from Pegasus has already started for chitchat and should now expand to broader parsing boundaries
- Scope:
  - expand Pegasus-backed phrase coverage for question/command/assertion patterns
  - add ambiguity guardrails for overlapping intents (date vs birthday, generic chat vs memory set/lookup, weather variants)
  - preserve command-vs-question personality behavior and stock skill launch compatibility
  - add focused tests for new phrase families and negative boundary cases
- Progress update (`2026-05-07`):
  - implemented date/time guardrails so birthday phrasing is not misrouted to date
  - expanded phrase coverage for:
    - birthday alias set/recall (`bday` variants)
    - shorthand favorites (`my favorite sport football`)
    - weather phrasing (`what's today's weather look like`, `will it be sunny tomorrow`)
  - updated continuation deferral so complete shorthand favorites finalize instead of waiting for missing continuation
- Progress update (`2026-05-21`):
  - expanded friendship parsing for Pegasus-style `do you have friends`, `are we friends`, and `are we best friends` phrasing
  - added named-person guardrails so forms like `are you friends with Siri` and `is Dr. Breazeal your best friend` stay on the friendship route instead of falling into generic chat
- Progress update (`2026-06-25`):
  - expanded user birthday memory parsing with Pegasus-style `birth date`, `birthdate`, possessive birthday, and `falls on` set/recall aliases so user-date memory stays ahead of generic date and robot-birthday routes
  - added targeted parser coverage for the new birthday alias families
- Progress update (`2026-06-25`, preference recall alias slice):
  - expanded personal preference recall parsing for `do you know my favorite ...`, `tell me my favorite ...`, and `tell me what my favorite ... is` variants so owner-memory lookup stays ahead of generic chat
  - added focused guardrail coverage for the new favorite/favourite recall alias families
- Progress update (`2026-06-29`, preference shorthand guardrail slice):
  - expanded personal preference parsing for `fave` shorthand plus embedded recall forms like `do you know what my favorite ... is` and `do you remember what my favourite ... is` so Pegasus-style owner-memory phrases do not fall through to generic chat
  - added focused guardrail coverage for the new shorthand and embedded recall families
- Progress update (`2026-06-29`, preference recall helper slice):
  - expanded owner preference recall parsing for helper-style aliases such as `do you recall my favorite ...`, `can you tell me my favourite ...`, and embedded `can you tell me what my fave ... is` forms
  - added targeted guardrail coverage so these recall prompts stay on the memory lookup route instead of being mistaken for incomplete preference-setting attempts
- Progress update (`2026-06-29`, polite preference helper slice):
  - expanded owner preference recall parsing for polite `could you tell me ...` and `would you tell me ...` helper forms, including embedded `what my fave ... is` variants
  - added focused guardrail coverage so these polite helper prompts stay on the memory lookup route instead of falling into generic chat or incomplete preference-setting prompts
- Progress update (`2026-07-01`, polite please preference helper slice):
  - expanded owner preference recall parsing for polite `can/could/would you please tell me ...` helper forms, including embedded `what my favorite/favourite/fave ... is` variants
  - added focused guardrail coverage so please-prefixed helper prompts stay on the memory lookup route instead of being treated as incomplete preference-setting attempts
- Progress update (`2026-07-03`, preference reminder / past-tense recall slice):
  - expanded owner preference recall parsing for direct `please tell me ...`, reminder-style `could/would you remind me ...`, and past-tense `what was my favorite/favourite/fave ...` aliases so natural memory checks keep landing on the lookup route
  - added focused guardrail coverage for the new reminder and past-tense recall families
- Progress update (`2026-07-04`, preference reminder helper expansion):
  - expanded owner preference recall parsing for direct `remind me ...` and `can you remind me ...` aliases, including embedded `what my favorite/favourite/fave ... is` forms, so reminder-style memory checks keep routing to owner-memory lookup instead of generic reminders
  - added focused guardrail coverage for the new direct and can-you reminder families
- Progress update (`2026-07-04`, preference recall confirmation slice):
  - expanded owner preference recall parsing for `please remind me ...` and `do you still remember ...` aliases, including embedded `what my favorite/favourite/fave ... is` forms, so polite reminder and confirmation-style memory checks keep routing to owner-memory lookup
  - added focused guardrail coverage for the new polite reminder and still-remember families
- Progress update (`2026-07-04`, still-remember helper expansion):
  - consolidated embedded owner preference recall helper parsing so supported helper leads share the same favorite/favourite/fave extraction path
  - expanded confirmation-style aliases for `can/could/would you still remember what my favorite/favourite/fave ... is` so assistant-style memory checks stay on owner-memory lookup
  - added focused guardrail coverage for the new modal still-remember helper family
- Progress update (`2026-07-04`, remember helper expansion):
  - expanded owner preference recall parsing for `can/could/would you remember ...` and `would you happen to remember ...` forms, including embedded `what my favorite/favourite/fave ... is` variants, so natural memory-check prompts stay on owner-memory lookup
  - added focused guardrail coverage for the new remember-helper and happen-to-remember families
- Progress update (`2026-07-05`, happen-to-know / recall expansion):
  - expanded owner preference recall parsing for `do you happen to know ...` and `do/can/could/would you happen to recall ...` forms, including embedded `what my favorite/favourite/fave ... is` variants, so hesitant memory-check prompts keep routing to owner-memory lookup instead of generic chat
  - added focused guardrail coverage for the new happen-to-know and happen-to-recall families
- Progress update (`2026-07-06`, modal happen-to-know expansion):
  - expanded owner preference recall parsing for `can/could/would you happen to know ...` forms, including embedded `what my favorite/favourite/fave ... is` variants, so polite hesitant memory-check prompts stay on owner-memory lookup
  - added focused guardrail coverage for the new modal happen-to-know family
- Exit criteria:
  - ambiguous phrase handling is improved without regressions in existing `1.0.20` features
  - phrase imports are documented and traceable to Pegasus parser sources
  - test suite stays green and includes targeted parser-guardrail coverage
- Tracking:
  - [release-1.0.20-plan.md](release-1.0.20-plan.md)
  - [system-diagram-alignment.md](system-diagram-alignment.md)

## Discovery Queue

### 12. Weather As Cloud Report Plus Local Presentation

- Status: `implemented`
- Tags: `protocol`, `content`
- Evidence:
  - Nimbus and Pegasus contain personal-report weather assets and Lasso provider hooks
  - no standalone `@be/weather` package has been confirmed in the inspected Be skill inventory
- Questions:
  - whether weather is a dedicated cloud skill, a personal-report branch, or both
  - what payload shape triggers local animation and weather presentation

### 13. Provider-Backed News

- Status: `in progress`
- Tags: `content`
- Why later:
  - first protocol path is implemented, but content is synthetic
- Questions:
  - which source should provide headlines for hosted OpenJibo
  - whether news belongs under a broader Lasso-style aggregation service
  - how to keep content short and Jibo-native
- Source-backed implementation notes:
  - original report-skill news tests expect default general, technology, sports, and business headlines for unidentified users
  - category counts are preference-dependent: one active category gets multiple headlines, two categories get two each, and three or more get one each
  - filter items without summaries, corrections, duplicate headlines, banned words, and adult headlines for children or unidentified speakers
  - the first provider-hardening slice now filters blank-title, missing-summary, and duplicate-title headlines before speech or payload emission, and reports skipped headline diagnostics for capture review
  - the second provider-hardening slice now also rejects correction/update headlines plus family-unsafe violent or adult terms before speech or payload emission, keeping the hosted briefing safe for unidentified or child listeners while provider preference controls are still pending
  - the provider ingestion hardening now applies the same correction/update and family-safety screen before fallback decisions, so unsafe category results are not cached as usable source headlines before application-level formatting gets a chance to filter them
  - include image view metadata with unique IDs, category labels, source image URLs, and sane scaling

### 14. Proactivity Selector And Surprise Offers

- Status: `discovery`
- Tags: `protocol`, `content`, `docs`
- Evidence:
  - original architecture materials show cloud-side `Proactivity Selector`, `Proactivity Catalog`, and robot-side proactive trigger plumbing
  - live captures include a proactive-style `I have something to share with you` offer and later proactive `TRIGGER` traffic
  - `@be/surprises`, `@be/surprises-date`, and `@be/surprises-ota` exist as local robot-side building blocks
- Questions:
  - minimum hosted selector for stock-OS-compatible surprise offers
  - how proactive `TRIGGER` traffic maps into OpenJibo
  - whether `surprises-date/offer_date_fact` should be the first intentional proactive offer

### 15. Surprises Routing

- Status: `discovery`
- Tags: `protocol`, `content`
- Evidence:
  - `@be/surprises` is a router rather than one experience
  - `surprises-date` and `surprises-ota` show category-specific branches
- Questions:
  - whether `surprise me` should enter the top-level surprise router
  - which categories depend on cloud services
  - whether stock OS `1.9` differs from the `x.x` source snapshot

### 16. History / Memory Layer

- Status: `discovery`
- Tags: `content`, `storage`, `docs`
- Evidence:
  - Pegasus includes a `history` package
  - original architecture materials call out cloud-side history
  - stock behavior historically included names, birthdays, holidays, and personal dates
- Questions:
  - what belongs in memory versus account/profile versus skill-specific storage
  - first safe OpenJibo memory slice
  - privacy and hosted-data boundaries

### 17. Lasso / Knowledge And Event Aggregation

- Status: `discovery`
- Tags: `content`
- Evidence:
  - Pegasus `packages/lasso` is a provider credential and data aggregation service
  - original architecture connected Lasso to AP News, Dark Sky, Google Calendar, Wolfram, and other providers
- Questions:
  - recreate Lasso as one aggregation service or several focused providers
  - which parts are needed for news, weather, calendar, commute, holidays, and special dates

### 18. Personal Report, Calendar, And Commute

- Status: `discovery`
- Tags: `protocol`, `content`
- Evidence:
  - current `.NET` catalog has placeholder replies
  - Nimbus has personal-report hooks and assets
- Questions:
  - whether calendar and commute are independent feature paths or personal-report sections
  - minimum provider data shape for natural Jibo presentation

### 19. Who Am I / Identity Management

- Status: `discovery`
- Tags: `protocol`, `content`, `storage`
- Evidence:
  - `@be/who-am-i` exists
  - source references `jibo.kb.loop`, owner/member lookup, enrollment, and name collection
- Questions:
  - recognition, enrollment, rename, and profile-correction boundaries
  - split between local state and hosted cloud state
  - first useful hosted identity slice
  - live QA has shown person-identification collisions in the same loop (for example, a parent and child both getting normalized to the same remembered name)
  - person-identification correction likely needs its own repair pass before we can trust greetings, reports, and presence triggers in mixed-household scenarios

### 20. Onboarding, Loop Management, And Fresh Start

- Status: `discovery`
- Tags: `protocol`, `docs`, `storage`
- Evidence:
  - `@be/first-contact`, `@be/introductions`, `@be/tutorial`, `@be/restore`, and `@be/who-am-i` exist
  - current `.NET` loop/account state is still mostly scaffolded
- Questions:
  - how to provision an owner without the original mobile app
  - how to add, remove, and re-enroll loop members
  - whether the first replacement is operator-only, a lightweight web app, or both

### 21. How Old Are You / Robot Age Persona

- Status: `implemented`
- Tags: `protocol`, `content`
- Result:
  - `how old are you`
  - `when's your birthday`
  - `do you have a personality`
  - `make a pizza` now ports the original scripted-response path through `chitchat-skill` with `mim_id = RA_JBO_MakePizza` and pizza-making animation ESML
  - `can you order pizza` now ports the original scripted-response path through `chitchat-skill` with `mim_id = RA_JBO_OrderPizza`
  - current source answers these with a `1.0.19` rule-based persona baseline, backed by `OpenJiboCloudBuildInfo.PersonaBirthday`
  - `how old are you` now also uses the imported Build B age prompts so the first-powered-up and birthday phrasing stays source-backed
- Follow-up:
  - wire persona age to first-powered-up or durable first-cloud-seen metadata when available
  - add command-vs-question variants so expressive prompts can answer conversationally before launching actions
- live QA has shown motion/sleep quirks too: `turn around` can become a no-op and `go to sleep` can fail at the last step before the sleep animation fully completes; the legacy sleep behavior tree includes sleeping-idle phases that should remain active in the parity path, and the Open Jibo cloud sleep replay now has regression coverage for the legacy `@be/idle` redirect plus follow-up acknowledgement speech
  - the legacy snapshot backs the motion lane through `spin around` / `twirl`, not a literal `turn around` text prompt
  - reply-selection polish still needs attention on a couple of identity prompts where short variants are over-selected (`how are you`, `what is your favorite flower`)

### 22. Command Vs Question Reply Style

- Status: `implemented`
- Tags: `content`, `polish`
- Result:
  - `dance` still launches the dance animation path
  - `do you like to dance` now responds conversationally as a personality question instead of launching the action
  - birthday phrasing now takes precedence over an `askForDate` client-intent misclassification
- Follow-up:
  - expand command-vs-question splits to more expressive intents (pizza, surprise, photo prompts)
  - add Pegasus phrase and MIM-backed variants for richer style coverage

### 23. First Memory-Backed Personal Facts

- Status: `implemented`
- Tags: `storage`, `content`
- Result:
  - tenant-scoped memory store abstraction is in place for personal facts
  - birthday set/recall works (`my birthday is ...` / `when is my birthday`)
  - preference set/recall works (`my favorite X is Y` / `what is my favorite X`)
  - account/loop/device scoped lookup prevents cross-tenant leakage
- Follow-up:
  - add durable persistence path for personal facts
  - broaden fact categories further (multi-person household memory, relationship cues, and corrective updates)
  - add explicit person-scoped state so future interactions can distinguish household members inside the same loop
  - define the first server-to-server sync envelope for durable state before we need it in production

### 24. Memory-Triggered Proactivity Baseline

- Status: `implemented`
- Tags: `content`, `storage`, `protocol`
- Result:
  - `surprise me` now uses weighted candidate selection instead of only generic fallback text
  - candidate weighting uses tenant-scoped memory signals and date triggers
  - February 9 (`National Pizza Day`) can proactively launch the legacy pizza animation path
  - proactive pizza fact offer flow stores pending offer state in session metadata and resolves direct short `yes/no` turns
  - memory parsing now includes names, anniversary-style important dates, likes/dislikes variants, and reverse favorite phrasing (`pizza is my favorite food`)
- Follow-up:
  - expand proactivity beyond pizza to additional Pegasus-backed categories
  - add cooldown/throttle policy and observability around proactive offer frequency
  - connect memory store to durable multi-tenant persistence
  - keep the sync story visible so stateful offers can survive a multi-server deployment later

### 25. Weather Report-Skill Launch Compatibility

- Status: `implemented`
- Tags: `protocol`, `content`
- Result:
  - weather requests now launch `report-skill` using Pegasus-aligned intent `requestWeatherPR`
  - weather phrase coverage includes baseline forecast and condition-style questions (`will it rain`, `is it snowing`, tomorrow variants)
  - weather launches emit `SKILL_REDIRECT` + completion and now also include cloud weather speech so weather turns remain useful even when local report providers are incomplete
  - weather entity hints are carried in outbound NLU (`date = tomorrow`, `Weather = rain/snow/...`) for report-skill consumption
  - OpenWeather provider integration is in place with configurable API key, default location, unit preference, and environment-variable fallback (`OPENWEATHER_API_KEY`)
  - cloud weather speech now uses live provider summaries for current conditions and tomorrow high/low forecast when available
- Follow-up:
  - connect weather units and location directly to user/report-skill settings parity instead of config defaults
  - add richer condition-change commentary and view parity with original report-skill weather behaviors

### 26. Presence-Aware Greetings And Identity Proactivity

- Status: `in_progress`
- Tags: `protocol`, `content`, `storage`, `docs`
- Why now:
  - this is the next personality-charm expansion after parser guardrail and weather bring-up
  - Pegasus greetings behavior is strongly tied to presence/identity signals and proactive cooldown policy
  - current OpenJibo has memory/proactivity foundations but no first-class presence extraction path yet
- Pegasus source anchors:
  - `C:\Projects\jibo\pegasus\packages\hub\be-skills\greetings_manifest.json`
  - `C:\Projects\jibo\sdk\skills\greetings\src\GreetingsSkill.ts`
  - `C:\Projects\jibo\sdk\skills\greetings\src\GreetingsSM.ts`
  - `C:\Projects\jibo\pegasus\packages\hub\src\proactive\ProactiveTransactionHandler.ts`
  - `C:\Projects\jibo\pegasus\packages\hub\src\proactive\tools\ContextTools.ts`
- Scope:
  - extract presence/identity context (`speaker`, `peoplePresent`, focused person) from runtime context payload
  - add greeting intent families and state-machine split for reactive vs proactive greeting routes
  - add cooldown and trigger-source guardrails for proactive greetings
  - start person-aware greeting hooks (name-aware greeting, morning greeting policy, return greeting policy)
- Shipped so far:
  - durable greeting-presence records now persist last-seen and last-greeted per person/loop
  - proactive greeting gating now consults cloud greeting history when available
  - reactive and proactive greeting turns write back greeting-history records for later cooldown checks
  - birthday-aware proactive greetings now use stored birthday memory on matching dates
  - holiday-aware proactive greetings now use loop holiday records on matching dates
  - morning proactive greetings now stay distinct from return-visit greetings
- Exit criteria:
  - presence-aware greetings are routed deterministically with tests
  - proactive greetings are frequency-bounded and do not trigger from surprise source when blocked by policy
  - fallback behavior remains stable when identity is unknown or context is incomplete
  - docs and release tracking are updated with shipped scope and residual gaps
- Tracking:
  - [greetings-presence-plan.md](greetings-presence-plan.md)
  - [release-1.0.20-plan.md](release-1.0.20-plan.md)

### 27. Personal Report Parity Track (Weather/News/Commute/Calendar)

- Status: `in_progress`
- Tags: `protocol`, `content`, `storage`, `docs`
- Why now:
  - personal report is a core Jibo charm surface and currently split between implemented weather speech and placeholder calendar/commute/news content
  - Pegasus weather used explicit condition animations and weather views; current OpenJibo weather is functional but visually lighter
- Scope:
  - weather icon/animation parity and view support
  - broader non-local weather query handling and short-range date coverage
  - provider-backed news ingestion and filtering
  - commute provider path, settings schema, and loop-scoped commute profile storage
  - coverage matrix for personal report parity gaps and test/capture exit criteria
- Progress update (`2026-05-10`):
  - added provider-ready news briefing lane with Nimbus-compatible `news` skill payload continuity
  - added memory/transcript category hint plumbing for provider requests (sports/technology/business/general)
  - fallback synthetic news behavior remains active when no provider key is configured
  - added TTL caching for weather/news provider calls to reduce repeated external requests
  - vendored Pegasus `report-skill` templates for weather and personal-report phrasing so the next pass can focus on renderer coverage for calendar, commute, and news templates instead of rediscovering source text
  - commute now has a loop-scoped provider seam plus persisted commute profiles, so the next pass can focus on richer travel-time data instead of basic storage shape
- Progress update (`2026-05-21`):
  - weather payloads now distinguish current-vs-weekly view modes so renderer parity can key off the payload shape more cleanly
  - news provider now skips summaryless correction headlines before falling back to broader sources
- Source anchors:
  - `C:\Projects\jibo\pegasus\packages\report-skill\src\subskills\weather\WeatherMimLogic.ts`
  - `C:\Projects\jibo\pegasus\packages\report-skill\resources\views\weatherHiLo.json`
  - `C:\Projects\jibo\pegasus\packages\report-skill\src\subskills\news\NewsMimLogic.ts`
  - `C:\Projects\jibo\pegasus\packages\report-skill\src\subskills\commute\CommuteMimLogic.ts`
  - `C:\Projects\jibo\pegasus\packages\hub\pegasus-skills\report_skill_manifest.json`
- Tracking:
  - [personal-report-parity-plan.md](personal-report-parity-plan.md)
  - [release-1.0.20-plan.md](release-1.0.20-plan.md)

### 28. Grocery List Capability (Requested Feature)

- Status: `in_progress`
- Tags: `content`, `docs`, `storage`
- Why now:
  - directly requested by Jibo owners and fits memory + household utility roadmap
- Source findings:
  - Pegasus has scripted responses for shopping/to-do list requests but no standalone grocery-list skill or add-item capture flow in this snapshot
  - examples:
    - `C:\Projects\jibo\pegasus\packages\chitchat-skill\mims\scripted-responses\RA_JBO_ShoppingList.mim`
    - `C:\Projects\jibo\pegasus\packages\chitchat-skill\mims\scripted-responses\RA_JBO_ManageToDoList.mim`
- MVP decision:
  - use the existing household list engine as the native lightweight grocery MVP, but only after we add a dedicated capture state
  - keep grocery as a first-class spoken alias over the shopping list storage path once the capture path exists
  - reserve integration-backed list orchestration for a later discovery pass
- Exit criteria:
  - grocery prompts, add/recall/done flows, and list follow-ups consistently speak grocery wording
  - the robot stays in a live listen/capture state long enough to accept an item phrase
  - existing shopping/to-do flows remain unchanged
  - future integration-backed list work remains a separate backlog item

### 29. Legacy MIM Personality Import Ladder

- Status: `in_progress`
- Tags: `content`, `protocol`, `docs`
- Why now:
  - we already have a chitchat/content scaffold that can render stock-compatible personality replies
  - the legacy `chitchat-mims` tree is mostly declarative content, so a phased import can add visible charm fast
  - this is the best near-term path to get Jibo feeling more interactive without needing a full Pegasus runtime clone
- What is possible today:
  - direct scripted replies through the existing content catalog
  - stock-compatible payloads with `skillId`, `mim_id`, `mim_type`, `prompt_id`, and ESML
  - current examples already prove the shape for pizza, dance, weather, news, and generic chat
- What we need to build:
  1. a MIM inventory importer that can scan the legacy tree and normalize `skill_id`, `mim_id`, prompt text, and metadata
  2. a prompt-selection layer that can choose by category and condition metadata
  3. a safe ESML/prompt renderer for imported content
- What can be ported with each build:
  - Build A: declarative prompt packs
    - `core-responses`
    - `deflector`
    - the simplest `emotion-responses`
    - direct `scripted-responses` that are just prompt lists
  - Build B: conditioned prompt packs
    - `gqa-responses`
    - structured emotion prompts with `condition` gates
    - any response families that only need simple state or Jibo-emotion checks
  - Build C: conversation families
    - richer `scripted-responses` that need follow-up state
    - holiday / special-date personality sets
    - more nuanced chitchat branches that depend on context-aware routing
  - Build D: full parity cleanup
    - larger cross-skill collections
    - any MIMs that depend on Pegasus-only parser assumptions
    - any files that need dedicated runtime abstraction instead of catalog lookup
- Low-hanging fruit for tonight:
  - import the smallest declarative packs first so we can test something tomorrow
  - prioritize anything that is pure prompt text with no complex branching
  - keep the first pass limited to content that maps cleanly onto the current catalog shape
  - Progress update (`2026-05-13`):
    - added the first Build A importer scaffold in the cloud content repository
    - checked in a small seed bundle under `Content/LegacyMims/BuildA`
    - added focused importer tests for prompt stripping, bucketing, and merge behavior
    - expanded Build A with additional easy scripted-response packs for identity and persona replies
    - started Build B with source-backed scripted-response packs for work, food, home, birthplace, language, hobby, and material questions
- Tomorrow test target:
  - verify imported personality replies show up through the existing chitchat route
  - confirm the emitted payload still looks like a stock skill response
  - confirm the imported content does not disturb existing weather/news/pizza flows
- Exit criteria:
  - a first importer path exists for the simplest legacy MIM files
  - at least one legacy prompt pack is running through OpenJibo content instead of hand-authored fallback text
  - we have a clear second-wave list for the more conditional MIM families

### 30. Original Personalized Function Inventory

- Status: `discovery`
- Tags: `content`, `docs`, `protocol`
- Why now:
  - we are actively porting persona and memory slices, so we need a bounded checklist of the original Jibo charm surfaces
  - the goal is to keep the next few passes focused on personality-rich wins instead of letting the work sprawl
- Known sources:
  - legacy Jibo OS/Pegasus chitchat and MIM response families
  - current OpenJibo persona, memory, and greeting work as the implementation target
- Inventory to track:
  - identity and origin questions
  - personality and capability questions
  - favorite-style prompts like `what is your favorite color`
  - identity charm prompts like `what's your name`, `do you have a nickname`, `do you like being Jibo`, `are there others like you`, and `what is your favorite name`
  - attraction and preference prompts like `what is your favorite flower`, `do you like R2D2`, `do you like the sun`, `do you like space`, and `do you like kids`
  - longer authored variants for the same prompt family when Pegasus shows richer phrasing
- charm/capability prompts like `can you laugh`, `can you dance`, `can you sing`, and `will you sing`
  - mood / affect questions
  - recognition follow-ups like `do you know me`
  - follow-up state prompts that should stay warm and locally grounded
- Next pass targets:
  - document the remaining persona inventory so we keep a clean checklist for the next passes
  - keep the favorites family moving with source-backed imports where available, and temporary runtime replies only when the source is missing
  - keep adding small sourced personality batches, especially the legacy `R2D2`, `sun`, `space`, `kids`, and charm prompts
  - keep adding 1-3 persona prompts per pass with tests
  - prefer source-backed MIM imports when the legacy text is available, and use a temporary runtime reply only when needed to unblock user value
  - keep a separate note for longer authored variants so we do not lose the multi-clause Peggy-style phrasing while importing the short-form packs
- Mood follow-up work in flight:
  - source-backed happy/sad/angry response packs are now part of Build B
  - small-talk aliases like `what are you up to` and `how are things` now stay on the emotion-query path
- Descriptor charm work in flight:
  - source-backed `are you kind`, `are you funny`, `are you helpful`, `are you curious`, `are you loyal`, `are you mischievous`, and `are you likable` prompts are now in Build B
  - these keep the self-description lane warm while we build toward seasonal and holiday charm
- Seasonal charm work in flight:
  - source-backed holiday, New Year's, Halloween, spring, summer, favorite-season, and gift prompts are now part of Build B
  - `RN_` holiday greeting files are now bucketed as greetings so seasonal replies stay visible in the catalog
  - birthday celebration lines are now bucketed separately, and birthday memory writes a loop-scoped holiday record so personal dates can join the holiday list later
  - holiday extras now include `show santa tracker` so the Christmas-time launcher keeps its source-backed animation line
  - the remaining seasonal polish now includes `do you like halloween`, `do you like holiday music`, `do you like holiday parties`, `are you looking forward to christmas`, `what are you doing for christmas`, and `what are you thankful for`
  - the Black History Month family is now a source-backed seasonal batch with `celebrate`, `like`, `looking forward`, `plans`, `what should I do`, and `fact` replies, so the history lane can keep growing in small, testable slices
- Stop-style command work in flight:
  - `stop moving`, `stop making that noise`, `stop ignoring me`, and `stop staring` now have source-backed Build B replies alongside the generic stop lane
  - the broader stop lane now also catches `stop talking`, `be quiet`, `be silent`, `shut up`, `silence`, `quiet down`, `no more music`, and `no more dancing`
- Favorite-animal work in flight:
  - the favorites family now includes `what is your favorite animal`, `what is your favorite bird`, `do you like penguins`, and `do you like animals` so the penguin-centric replies stay easy to find
  - these favorites prompts are already source-backed in the cloud path; any remaining mismatch is live playback or robot-side launch-state handling
- Presence and thought follow-ups in flight:
  - `welcome back`, `what are you thinking`, `what have you been doing`, and `what did you do` are now part of Build B
  - these keep the social surface lively while the memory and multitenant tracks keep advancing in parallel
- Next queued persona surfaces:
  - richer identity follow-ups like `who is this`, `do you know me`, `do you remember me`, and `can you recognize me`
  - mood and affect prompts like `how are you`, `are you happy`, `are you sad`, and `are you angry`
  - self-description charm like `what's your name`, `do you have a nickname`, `do you like being Jibo`, and `what is your favorite name`
  - deeper personality follow-ups like `what do you dream about`, `what are you afraid of`, `what do you want to talk about`, `what is your best book`, `what is your best exercise`, `what is your dream vacation`, `who is your hero`, `who do you love`, and `what is your religion`; `what is your sign` stays deferred until templated placeholder rendering exists
  - the next identity / knowledge wave adds `are you god`, `are you here`, `do you have super powers`, `how much do you know`, `what does jibo mean`, `where do you get info`, `what are you forbidden to do`, `what color are you`, and `what do you do when alone`
- additional legacy source-backed `RI_USR` prompts where the text is short and the behavior is easy to verify
- the new `Can...` batch adds dream, exercise, fly, learn, laugh, read, hear, talk, see, and wink prompts so the capability lane keeps getting more of Pegasus's playful personality
- the second `Can...` batch adds move, work, breathe, get tired, have emotions, whistle, cook, make coffee, make breakfast, and jump prompts so the broader capability lane keeps filling out in small, testable chunks
  - templated edge cases like `what is your sign`, `how many people do you know`, and `what is the loop` where live birthday and loop state are part of the line instead of a plain canned response
- Exit criteria:
  - a stable checklist exists for the original persona surface
  - each pass can be scoped to a small batch of prompts
  - the backlog makes it obvious what is still missing without losing momentum

### 31. Longer Authored Persona Variants

- Status: `ready`
- Tags: `content`, `docs`, `protocol`
- Why now:
  - Pegasus often used longer, multi-clause authored alternatives for the same personality question
  - we already have the short-path import working, so this is a low-risk way to add richer phrasing without inventing a new dialog engine
  - it gives us a straightforward next pass that stays familiar to the original robot
- Scope:
  - import the longer authored variants already present in the legacy MIMs
  - prefer richer phrasing for favorite-style, identity, and charm prompts when the source text provides it
  - keep the runtime behavior rule-based and deterministic
- Next step:
  - add a small batch of longer variants to the current Build B content packs and prove them with a smoke test

### 32. Dialog Joining And Composition

- Status: `discovery`
- Tags: `content`, `docs`, `protocol`
- Why now:
  - the videos and source files suggest Jibo sometimes felt like he was joining thoughts together, even when the source text was still authored
  - we have not found evidence of a general runtime joiner yet, so this remains a post-release enhancement instead of a 1.0.19 dependency
  - keeping it separate lets us preserve familiar Jibo phrasing now and experiment with composition later
- Scope:
  - design a post-release dialog composition layer that can stitch authored fragments together when appropriate
  - keep the first version conservative and familiar, not LLM-driven
  - make sure any future joining feature is opt-in and does not replace the current authored prompt path
- Follow-up:
  - revisit after 1.0.19 personality import and report-skill parity stabilize
  - decide whether the composition layer should sit above the prompt catalog or beside it as a dedicated response post-processor
  - keep this separate from the authored-variant backlog item so we do not blur prompt richness with runtime composition

### 33. Singing And Musical Personality

- Status: `discovery`
- Tags: `content`, `docs`, `protocol`
- Why now:
  - Jibo’s charm surface includes musical and sing-along behavior, and it fits naturally after the current personality and holiday batches
  - the first pass should stay familiar and rule-based, not LLM-driven
- Scope:
  - inventory the legacy song / sing / musical prompt families
  - keep the first implementation source-backed if Pegasus has usable authored lines
  - preserve room for a later sing-along launcher if we want one
- Current note:
  - `can you sing` and the Christmas-song variant are already source-backed in the cloud path, so the open work is live robot playback verification and any missing song families
- Holiday tracker follow-up:
  - `show santa tracker` now emits a tracker presentation payload in the cloud path
  - the live robot still needs verification for the winter visuals and jingle-bell style audio described in the legacy video
  - source discovery suggests this is an animation/presentation payload rather than a dedicated skill launch or robot-local handoff
- Exit criteria:
  - a small song backlog exists with candidate phrases listed
  - the release plan has a clear place for musical personality without crowding out weather/news/report work
  - the current source-backed singing slice is implemented and test-covered

## Suggested Order

Before closing `1.0.18`:

1. Radio live validation
2. Basic news regression, with provider-backed expansion deferred
3. Backup / OTA / share yes-no regression
4. Alarm and photo/gallery regression
5. Stop and volume first-pass validation

Use [regression-test-plan.md](regression-test-plan.md) as the detailed checklist for this sequence.

For `1.0.19`:

1. Command-vs-question personality split (`dance` command vs `do you like to dance` question style; expand this pattern) - implemented
2. Expand memory-backed personal facts with tenant-scoped storage (beyond the first birthday/preferences foundation) - implemented
3. Proactivity selector baseline with source-backed first offers - implemented
4. Weather report-skill launch compatibility - implemented
5. Dialog parsing expansion and ambiguity guardrails - in progress (`2026-05-09` third guardrail slice implemented; Pegasus affinity phrase families + continuation guardrails expanded)
6. Presence-aware greetings and identity-triggered proactivity - implemented (trigger path, identity-aware reactive/proactive replies, cooldown metadata wiring, focused websocket coverage)
7. Personal report parity track (weather visuals, live news path, commute path, calendar parity matrix) - in progress (`2026-05-10` first live-news provider slice implemented; commute now has a loop-scoped provider seam)
8. Holidays and seasonal personality behavior built on the new memory/proactivity foundation
   - system holidays should come from an up-to-date provider and merge with loop-scoped custom holiday records
   - allow disabled holiday records to suppress reminders for people who do not celebrate a holiday
   - birthdays and other personal dates should flow into the same loop-scoped holiday list once authoring is wired up
9. Durable memory persistence path (multi-tenant backing store)
  - reference design captured in `docs/persistence-architecture.md`
  - store contracts are now tightened around account/loop/device/person scoping, revision tracking, and explicit load/save boundaries
  - the backend seam is now selectable, with file-backed local persistence as default and an Azure Blob Storage slot wired for future deployment when a storage account connection string is available
  - next implementation pass should supply the real Azure Storage connection string / deployment wiring and validate the live round-trip in the storage account smoke test
10. Update, backup, and restore proof - implemented (update creation and backup creation now survive persisted reloads; restore is the persisted-state rehydration proof path, not a new cloud API)
11. STT upgrade and noise screening
  - progress update (`2026-05-21`): added a low-signal short-turn screen in websocket finalization so filler-only fragments and stray single-token leftovers like `so command` get rejected before they can become bad turns, while preserving the existing yes/no and word-of-the-day short-turn flows
12. Hosted capture/storage plan / indexing for group testing
  - progress update (`2026-05-21`): added a bundle helper so group testers can package raw capture trees, `capture-index.ndjson`, and exported fixtures into one zip handoff artifact
13. Binary-safe media storage / sync to cloud drive: OneDrive, Google Drive, Box, etc.
14. Provider-backed news and weather parity polish
15. Grocery list capability discovery and MVP selection
16. Lasso, identity, and onboarding as larger discovery-driven tracks
17. Legacy MIM personality import ladder and first declarative prompt packs
18. Longer authored persona variants for the same prompt families
   - progress: favorite drink and favorite sport now route through source-backed persona replies with focused tests; live robot playback is still needed before marking the broader persona ladder closed.
19. Dialog joining/composition as a post-release enhancement, kept separate from the 1.0.19 ladder

For `1.0.20` and beyond:

1. Open Jibo mode conversion package
   - add explicit `open-jibo`, `open-jibo-ai`, `open-jibo-self-hosted`, and `open-jibo-developer` modes
   - install an Open Jibo onboarding/config skill that can enable or disable the converted mode while staying available in the menu
   - include first-boot/OOBE behavior so a converted robot can finish setup on the first launch after conversion
   - issue Open Jibo identities instead of blindly trusting stock robot identity values, especially for cloned or previously modified robots
   - planning anchor: [open-jibo-mode-conversion-plan.md](open-jibo-mode-conversion-plan.md)
   - status: `ready`
   - note: the app-side onboarding sequence is now mapped, and the existing bootstrap / cloud contract scripts give us enough structure to start implementation rather than only discovery
   - current progress: required physical/harness `VerifyConnection` proof now distinguishes missing, partial, mismatched, and complete legacy DNS mapping evidence for the three stock Jibo hosts before the conversion path is considered connected
2. Device compatibility matrix
   - prove the conversion path on the newest OOBE-capable devices
   - prove it on older stock devices such as the `1.9.2` baseline
   - test pre-`1.9.2` installs and alternate distributions such as NTT or MIT-special versions where available
   - status: `discovery`
3. Hardware-assisted "easy button" conversion
   - work with the Jibo Revival Group on a USB/RCM-based helper path
   - keep the file-system modification flow repeatable and safe for owners or testers
   - add Maaarcna's OOBE OTA bootstrap as a parallel candidate mode: QR-provided DNS, controlled LAN DNS/NTP/HTTPS, and stock OTA metadata may convert wiped/OOBE robots without SSH, while ShofEL/firewall/SSH remains required for rescue, rollback, non-wipe, and unproven variants
   - open blockers: certificate/key provenance and safe handling, repeatable request traces, legal stock package sourcing, first Open Jibo OTA package shape, multi-robot session handling, and whether normal-boot OOBE can run without wiping owner data
   - status: `discovery`
4. Cloud deployment and CI/CD
   - set up the hosted cloud for deployment into the Azure environment
   - make the release path reproducible from source to deployed service
   - first target is Azure Container Apps, with App Service kept as fallback and AKS deferred for later AI/network scale-out
   - first registry target is Azure Container Registry
   - deployment promotion must pass a virtual-Jibo or purpose-built protocol smoke gate
   - recorded onboarding/session replay is the preferred first CI-friendly smoke gate
   - PostgreSQL migrations should use a DbUp-style SQL runner with an Open Jibo wrapper for apply, preview, dry-run/report, and container-entrypoint modes
   - planning anchor: [cloud-deployment-topology-plan.md](cloud-deployment-topology-plan.md)
   - status: `ready`
5. Hosting modes and service topology
   - support self-hosted operation with no external cloud dependency
   - support hybrid operation where non-self-hosted servers sync to a main cloud service
   - support a managed cloud service for paid hosted access
   - treat self-hosted sync enrollment as a one-way setup choice until reset/OOBE recovery is performed
   - first self-hosted target is Docker Compose
   - first Docker Compose database is PostgreSQL
   - PostgreSQL migrations should run through explicit CI/CD or admin commands, with self-hosted startup migration behind an intentional switch
   - status: `ready`
6. Storage abstraction and sync
   - abstract storage so the rest of the system does not care which server implementation is backing it
   - keep only transient session/onboarding artifacts and device-local secrets permanently local-only for now
   - keep identity and storage synchronized across the network for participating servers
   - define trust, admission, and revocation rules for bad-actor servers, including what happens to user data they already held
   - issue Open Jibo robot identity from the new cloud rather than trusting legacy stock robot identifiers as primary keys
   - use deny-by-evidence admission and full versioned snapshots as the first sync model
   - sign trust-boundary records before replication, not every local write
   - use hardware-stable `DeviceId`, cert thumbprint, issued-identity lineage, and build/config hashes as corroborating clone-detection signals only
   - current progress: signed identity graph admission decisions and offline evidence bundles now carry a deterministic local revocation-list hash, so retained artifacts can prove which deny-list state was used for the signed admit/quarantine decision
   - planning anchor: [storage-trust-consensus-plan.md](storage-trust-consensus-plan.md)
   - status: `discovery`
7. OpenJibo.com web UI and account surface
   - provide a web UI for openjibo.com
   - support paid access on the hosted side while leaving room for free or self-hosted options elsewhere
   - auth starts as a separate deployable under the Open Jibo domain family
   - auth can live in the shared repo/solution initially, but must be its own project
   - onboarding needs provider-specific extension points for signup/payment, free community clouds, and self-hosted servers
   - onboarding should expose a data-driven trusted-server registry API backed by cloud state so the app can present approved managed options, distinguish self-hosted hybrid servers that stay synced but private, support admission/revocation/reactivation, and let the user enter a separate custom self-hosted server name/IP for validation
   - provider-specific onboarding must use signed event callbacks and signed returns
   - provider onboarding should use short-lived signed session tokens plus provider-signed callbacks/returns with nonce/state binding
   - later boots should prefer the selected provider cloud first and enter explicit recovery instead of silently switching clouds
   - developer/smoke-only self-hosted paths can use HTTP locally; owner-facing robot paths should default to HTTPS/self-signed or equivalent patched trust behavior until safe HTTP is proven
   - status: `discovery`
8. Loop advancement and multi-Jibo support
   - support family/friend advancement, multiple user recognition, and multiple Jibo interaction
   - keep the identity model ready for Jibo-to-Jibo communication and shared household use
   - scope `1.0.20` to the identity graph and relationship model first, not direct robot-to-robot transport
   - model loops as households that can hold multiple people and multiple robots without assuming a single robot per loop forever
   - status: `discovery`
9. Next-tier features after the platform is stable
   - advanced integrations such as pizza delivery, Uber/Lyft, calendar management, and smart home control
   - longer-term LLM integration for more natural dialog and content generation
   - tiered brain/orchestration planning from the README, added gradually without losing Jibo's charm
