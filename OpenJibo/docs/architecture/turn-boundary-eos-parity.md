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

## Fix Plan

1. Make decisive intent/action matches close the turn earlier instead of waiting for the hard timeout.
2. Keep the hard timeout as a safety net for error states and stalled turns.
3. Preserve explicit follow-up behavior only where the response plan says `KeepMicOpen` or equivalent ownership should continue.
4. Treat yes/no and other constrained replies as prompt-owned turns, not generic open-ended speech.
5. Add focused tests around:
   - decisive command turns
   - explicit follow-up turns
   - yes/no follow-up turns
   - timeout-only fallback turns
6. Re-run the live robot capture after the change and verify:
   - the command turn closes sooner
   - the next turn is not polluted by audio bleed
   - stop and follow-up paths still behave intentionally

## Why This Matters

The recent robot failures are consistent with us holding the mic open too long and then feeding the wrong audio into the next transcription pass.

That means the fix is not just audio trimming.

We also need the turn boundary to be correct.

## Follow-Up

When the implementation changes, update this note with:

- the exact branch points in Open Jibo
- the tests that prove each branch
- the live capture that first confirms parity
