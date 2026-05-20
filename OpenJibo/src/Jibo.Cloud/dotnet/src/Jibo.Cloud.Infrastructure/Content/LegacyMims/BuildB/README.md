# Legacy MIM Build B

This folder holds the next small import batch of legacy Jibo scripted-response MIMs.

The batch is intentionally narrow so we can keep expanding personality without widening the turn-state surface faster than we can test it.

It now includes a small emotion-response pack for `happy`, `sad`, and `angry` follow-up questions so the mood path can stay source-backed too.
It also includes a descriptor pack for questions like `are you kind`, `are you funny`, `are you helpful`, `are you curious`, `are you loyal`, and `are you mischievous`.
The newest seasonal pack adds holiday and seasonal prompts for `what holidays do you celebrate`, New Year's resolution questions, `happy holidays`, Halloween costume questions, spring suggestions, holiday gift ideas, and birthday celebration lines.

Holiday-specific note:
- `JBO_WhatHolidaysDoYouCelebrate` now lands in the holiday bucket
- `RN_HappyHolidays` now lands in the holiday greeting bucket
- `RI_USR_WhatShouldGetForHoliday` now lands in the holiday gift bucket
- `RN_HappyBirthdayToJibo` now lands in the birthday celebration bucket
- birthday memory authoring now also writes loop-scoped custom holiday records so personal dates can join the holiday list later
The newest social batch adds `welcome back`, `what are you thinking`, `what have you been doing`, and `what did you do` responses so the presence and charm lane keeps growing alongside seasonal content.
The fun-fact and joke batch adds Pegasus-style `TellAJoke`, `TellRobotFact`, and `Shuffle` excerpts so proactive fun can randomize across more than one category.
Those facts are now split into generic, robot, and human buckets so the randomizer can sound more like Pegasus while staying lightweight.
The new favorites batch adds longer authored `favorite color`, `favorite food`, and `favorite music` variants so the familiar personality responses keep more of the original cadence instead of collapsing to short placeholders.
The new motion/sleep batch adds `RA_JBO_SpinAround` plus `RI_JBO_CanSleep` so turn-around and go-to-sleep behaviors can stay source-backed and familiar.
