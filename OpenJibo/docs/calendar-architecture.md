# Calendar Architecture

Pegasus treated calendar as a loop-scoped report surface, with report output fed by the
household context instead of an isolated generic calendar service.

In OpenJibo, the current calendar path follows the same broad shape:

- calendar report output is loop-scoped
- the report provider can read persisted loop calendar events
- birthday and other personal dates already live in the loop-scoped holiday list
- the personal report merges the report provider output into the spoken flow

Current behavior:

- if loop calendar events exist, the provider surfaces the next matching items
- if no loop calendar events exist, the provider falls back to the merged holiday list
- birthdays and custom holiday entries can therefore appear in the calendar section
- the personal report still degrades safely when no calendar data is available

Notes:

- the current provider is intentionally lightweight and in-process
- this gives us a swappable seam for later Azure-backed calendar sync
- commute remains a separate report gap for the next pass
