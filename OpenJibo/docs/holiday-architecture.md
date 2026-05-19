# Holiday Architecture

Pegasus exposed holidays as a loop-scoped list synchronized into `/jibo/holidays`.

In OpenJibo, the holiday path now follows the same broad model:

- system holidays come from a live holiday source
- custom holidays are loop-scoped
- suppressed holidays are represented as disabled records
- the cloud protocol returns the merged list for `PersonListHolidays`

Current behavior:

- `Person/ListHolidays` uses the loop from the request when available
- if no loop is supplied, the cloud falls back safely instead of throwing
- the merged list is built from system holidays plus any custom loop entries

Notes:

- `IsEnabled = false` can be used to suppress a holiday later
- birthdays and other personal events can be added as loop-scoped custom records
- the current system holiday source uses Nager.Date with a safe local fallback for uptime
