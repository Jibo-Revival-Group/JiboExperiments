# Turn Boundary And EOS Parity

## Purpose

This note captures the turn boundary decision for the current Open Jibo cloud work.

The goal is to match the original Pegasus behavior closely enough that the robot stops waiting at the right time, not merely when a safety timeout eventually fires.

## Decision

The long hard timeout is a safety net, not the normal end-of-turn mechanism.

For normal command turns, Open Jibo should:

- recognize when the current transcript is already sufficient to match an intent or action
- close the listen turn immediately at that boundary
- hand off to the next branch of logic instead of waiting for the hard timeout

The turn context then decides what happens next:

- if this is a fresh intent, finalize the turn and route
- if a skill expects a follow-up, keep the conversational turn open only for that explicit continuation
- if the path is a yes/no or other constrained reply, keep that reply bound to the active prompt instead of letting it drift into the next generic listen cycle
- if the robot is not in one of those explicit follow-up states, a new utterance should be treated as a new intent turn

## Pegasus Mapping

Pegasus provides the behavioral reference:

- `GoogleASRSession` emits `SOS` and `EOS` as speech boundaries, and can also force an early `EOS` from incremental ASR when `FastEOS` matches a decisive phrase
- after `EOS`, `ListenTransactionHandler` immediately performs NLU and routing
- the routing result decides whether the listen result is final or whether the robot is entering a skill continuation
- yes/no handling is not a generic timeout branch; it is a constrained ASR vocabulary plus prompt-specific turn ownership

Source references:

- `C:\Projects\jibo\pegasus\packages\hub\src\asr\google\GoogleASRSession.ts`
- `C:\Projects\jibo\pegasus\packages\hub\src\listen\ListenTransactionHandler.ts`
- `C:\Projects\jibo\pegasus\packages\hub\src\asr\ASRUtils.ts`
- `C:\Projects\jibo\pegasus\packages\hub\tests\listen\ListenHandler.test.js`
- `C:\Projects\jibo\pegasus\packages\hub\tests\listen\ListenHandlerRedirect.test.ts`

## Open Jibo Mapping

The current Open Jibo turn-finalization path still has a safety-oriented auto-finalize loop that watches buffered audio, silence, and a hard timeout.

That is useful as a backstop, but it is too late to be the common path.

The important runtime state in Open Jibo is:

- `AwaitingTurnCompletion`
- `FollowUpOpen`
- `LastIntent`
- `LastListenType`
- `IgnoreAdditionalAudioUntilUtc`
- `IgnoreLateListenSetupUntilUtc`
- `FinalizeAttemptCount`

Those fields should decide whether we are:

- closing a normal command turn
- waiting on an explicit follow-up
- waiting on a constrained yes/no answer
- or safely timing out an error path

## Current Decision

We are keeping the buffered turn finalizer, but we are no longer treating prompt echo as a valid answer.

For decisive command turns, Open Jibo now has two early-close paths before the hard timeout:

- a decisive `audioTranscriptHint`, when the context provides one
- an early buffered OGG ASR probe, when the live robot provides no transcript hint but has sent enough real Opus audio after `LISTEN` and `CONTEXT`

For constrained yes/no prompts, Open Jibo now distinguishes three cases:

- clean yes/no replies, which can resolve immediately
- mixed replies such as `no yes`, which should clarify instead of forcing a `no`
- prompt echo or robot self-audio, which should stay open until a real answer arrives or the hard timeout is reached

The important practical consequence is that `audioTranscriptHint` is a routing hint, not a transcript substitute, and it is not required on live robot turns:

- if the hint already identifies a command like cloud version, stop, or word of the day, the turn can close early once the buffered audio is large enough
- if the hint is generic or incomplete, the normal buffered-audio and timeout guards still apply
- if the turn is still too small or too early, the hint should not force a close
- if there is no hint, hotphrase OGG launch turns should still probe ASR once enough audio pages and bytes are present

That matches the Pegasus shape more closely:

- Pegasus attaches the yes/no constraint to the prompt itself and can stop ASR early with `earlyEOS`
- Open Jibo does not yet have the same streaming ASR boundary, so we approximate it from buffered OGG pages, bounded ASR probes, and transcript heuristics
- the prompt-echo guard is the missing piece that keeps the robot from finalizing on its own question or self-audio

The live robot capture that drove this decision did not include `audioTranscriptHint` in the `CONTEXT` payload. Waiting for OGG EOS or the hard timeout caused the cloud-version reply to arrive several seconds late, then Jibo's spoken reply bled into the next captured WAV. The parity behavior is to probe buffered ASR around the first meaningful speech window and emit `LISTEN` plus `EOS` plus the action as soon as the transcript maps to a usable command.

One additional boundary rule matters for the cloud-version path:

- `IgnoreAdditionalAudioUntilUtc` is for trailing binary audio only
- `IgnoreLateListenSetupUntilUtc` is for a brand-new hotphrase/listen setup arriving too soon
- the audio suppression window must not veto a legitimate next listen setup after a spoken diagnostic reply

## Fix Plan

1. Make decisive intent/action matches close the turn earlier instead of waiting for the hard timeout.
2. Keep the hard timeout as a safety net for error states and stalled turns.
3. Use `audioTranscriptHint` as a turn-boundary accelerator only when the hint is already decisive and the buffered audio is substantial enough to be meaningful.
4. When no hint exists, let hotphrase OGG launch turns early-probe ASR after context once buffered audio reaches the Node-oracle-sized speech window.
5. Preserve explicit follow-up behavior only where the response plan says `KeepMicOpen` or equivalent ownership should continue.
6. Treat yes/no and other constrained replies as prompt-owned turns, not generic open-ended speech.
7. Add focused tests around:
   - decisive command turns
   - no-hint live OGG hotphrase turns
   - explicit follow-up turns
   - yes/no follow-up turns
   - timeout-only fallback turns
8. Re-run the live robot capture after the change and verify:
   - the command turn closes sooner
   - the next turn is not polluted by audio bleed
   - a fresh listen setup after cloud-version is not discarded just because the prior audio suppression window is still active
   - stop and follow-up paths still behave intentionally
9. Keep prompt echo and robot self-audio from counting as a yes/no answer until the user actually gives one.

## Why This Matters

The recent robot failures are consistent with us holding the mic open too long and then feeding the wrong audio into the next transcription pass.

That means the fix is not just audio trimming.

We also need the turn boundary to be correct.

This implementation now adds early no-hint OGG probing for live hotphrase launch turns and keeps the prompt-echo guard on top of the existing yes/no clarification logic so we do not turn the robot's own prompt back into a false close.

## Follow-Up

When the implementation changes, update this note with:

- the exact branch points in Open Jibo
- the tests that prove each branch
- the live capture that first confirms parity
