# Loop SyncManager contract (dump-verified)

Services-partition SyncManager sources under `/run/media/zane/891f7d4a-…/bin`
are not readable on this host (mode `750` root:group). The contract below is
locked from:

- Artifact logs (`artifact-output/jibo-test-31`, `jibo-test-32`)
- Stock API shapes `S6`/`S9` in `loop-2016-03-24.min.json`
- On-robot `jibo-kb` + `@be/introductions` on the skills mount
- Prior OpenJibo runbook observations of `_isLoopGood` failure strings

## SyncManager behavior

1. Calls JSC **`Loop#list()`** / `ListLoops` (not ListMembers) for roster sync.
2. Validates with `_isLoopGood(data)` before writing KB UserNodes.
3. Registers for **`LoopUpdated`** notifications; on receive, pauses/resumes
   loop syncing (re-runs List). Observed log pair:
   `pausing loop syncing` → `resuming loop syncing`.
4. Periodic loop sync every **7200 seconds**.
5. Observed failure strings:
   - `JSC server call Loop#list() loop has no members`
   - `JSC server call Loop#list() robot <localKbRobotId> not in loop`

## `_isLoopGood` requirements

1. Exactly **one** loop in the List array.
2. `loop.members` is a non-empty array.
3. Some `members[].accountId` equals `loop.owner`.
4. Some `members[].accountId` equals `loop.robot` (OpenJibo: `type=robot`).
5. Live people use stock status **`accepted`** (`invited|accepted|declined|removed`).

## Stock List item shape (`S6`)

Required: `owner`. Members: `id`, `name`, `owner`, `robot`, `robotFriendlyId`,
`members` (`S9` list), `isSuspended`, `created`, `updated`.

## Stock Member shape (`S9` element)

Required: `id`, `loopId`, `status`, `type`.  
Names under nested `account.firstName` / `account.lastName` (SyncManager
flattens to UserNode `data.firstName` for introductions).

## Introductions menu gate

After KB write, `@be/introductions` calls `jibo.kb.loop.loadLoop()` and shows
nodes where `data.firstName` is truthy (`!isJibo`) and status is not
`declined`/`removed`. It does **not** call cloud Loop APIs.
