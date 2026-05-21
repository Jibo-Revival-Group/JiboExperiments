# Commute Architecture

## Purpose

Commute is part of personal report parity and household-aware personality.

The original Jibo report-skill had a commute section that could speak about getting to work, leaving soon, or being too early or too late. In OpenJibo, that behavior now starts with a loop-scoped commute profile so we can stay faithful to stock behavior first and add richer routing later.

## Current Shape

The cloud now models commute as persisted loop data instead of a hardcoded reply.

The current pieces are:

- `CommuteProfileRecord`
- `ICommuteReportProvider`
- `CloudStateCommuteReportProvider`
- `Person/ListCommute`
- `Person/UpsertCommute`

## Data Model

The commute profile is stored per loop and can optionally be tied to a person.

Typical fields include:

- `LoopId`
- `MemberId`
- `Mode`
- `WorkHour`
- `WorkMinute`
- `OriginName`
- `DestinationName`
- `TypicalDurationMinutes`
- `IsEnabled`
- `IsComplete`

The provider uses the loop-scoped profile to decide whether commute is ready, missing setup, or ready to render a spoken answer.

## Runtime Behavior

At runtime, the commute provider:

- reads the current loop from the request context
- loads the loop commute profile from cloud state
- uses the profile plus current time to compute minutes until work
- merges in same-day calendar pressure when a calendar event exists before the commute window
- returns a safe setup response when the commute profile is missing or incomplete

## Personal Report Integration

Personal report uses the commute provider as a section in the broader household report.

That means the report can now speak in the familiar Jibo shape:

- weather
- calendar
- commute
- news

## Next Gaps

The current commute provider is intentionally conservative.

Next steps can include:

- a richer travel-time source
- map or transit integration
- better depart-time commentary
- preference-based commute suppression or reminders

For now the goal is to keep the interface stable and the behavior stock-like.
