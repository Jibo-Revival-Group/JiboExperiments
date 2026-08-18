# Legacy Skill Migration Inventory

This document records how the existing hard-coded interaction routes will become
registered skills without changing robot behavior during the migration.

## Current scale

The current .NET interaction service contains approximately:

- 300 semantic-intent routes in `JiboInteractionService.IntentRouting.cs`.
- 300 response branches in `JiboInteractionService.DecisionDispatch.cs`.
- shared decision builders spread across command, clock, memory, personality,
  report, Home Assistant, proactivity, and knowledge-search files.

These are route branches, not 300 independent skills. Many branches share the
same state, payload shape, follow-up behavior, and runtime target.

## Skill verticals

The initial built-in packages should be grouped by behavior and lifecycle:

| Package | Main responsibilities | Main execution target |
| --- | --- | --- |
| `openjibo.conversation` | chat, jokes, fun facts, scripted replies, knowledge fallback | server |
| `openjibo.personality` | robot identity, favorites, capabilities, personality questions | server |
| `openjibo.clock` | time/date/day, clock menu, timer, alarm, follow-ups | robot + server state |
| `openjibo.memory` | names, birthdays, preferences, affinity, important dates | server |
| `openjibo.reports` | weather, calendar, commute, news, personal report flows | server |
| `openjibo.media` | radio, gallery, photo capture, bad apple | robot |
| `openjibo.device-controls` | stop, sleep, wake, volume, turn/spin, robot-local controls | robot |
| `openjibo.smart-home` | Home Assistant lights and climate commands | server |
| `openjibo.proactive` | proactive greetings, pizza/fact/joke offers, seasonal behavior | server + robot |
| `openjibo.utilities` | math, spelling, definitions, countdowns, unit conversion, dice | server |

The package ID is the stable modular identity. The old semantic intent remains a
binding inside the package, not a package identity by itself.

## Compatibility boundary

The first migration must preserve the existing behavior in these areas:

- semantic intent recognition and special transcript heuristics
- NLU entities and rule payloads sent back to the robot
- native robot skill IDs such as `@be/clock` and `@be/gallery`
- server-side provider calls and persistence updates
- multi-turn follow-up state
- proactive cooldowns and context updates
- `SKILL_ACTION`, `SKILL_REDIRECT`, `LISTEN`, and `EOS` ordering

For that reason, a package should initially use a compatibility adapter rather
than reimplementing its behavior in a new runtime.

## Compatibility adapter shape

Each built-in package will expose a common adapter contract:

```csharp
public interface ILegacySkillAdapter
{
    string SkillId { get; }
    bool Handles(string semanticIntent);
    Task<JiboInteractionDecision?> ExecuteAsync(
        TurnContext turn,
        string semanticIntent,
        CancellationToken cancellationToken = default);
}
```

The adapter delegates to the existing decision builders for its vertical. The
router selects the package from its manifest, then the adapter produces the same
`JiboInteractionDecision` that the old branch produced.

This makes the first migration reversible and lets parity tests compare old and
new decisions before the old switch branches are deleted.

## Migration order

1. Utilities with simple one-shot responses: math, spelling, definitions,
   countdowns, unit conversion, and dice.
2. Personality and scripted conversation routes.
3. Memory routes, including their persistence and follow-up behavior.
4. Clock routes, because timer/alarm state and robot-local handoffs are more
   sensitive.
5. Reports and provider-backed skills.
6. Media and device controls, preserving native robot skill payloads exactly.
7. Smart-home and proactive flows, which depend on context, permissions, and
   cooldown state.

## Completion criteria for each vertical

A vertical is migrated only when:

- its package manifest is discovered by the registry;
- every old intent has a manifest binding;
- the router selects the package for the normalized NLU/semantic input;
- the compatibility adapter returns the old decision shape;
- follow-up and context behavior is covered;
- robot protocol payloads are parity-checked;
- the old branch is disabled or delegated through the adapter;
- an unmigrated route still reaches the legacy fallback.

## What this does not do yet

This inventory does not add Python or Lua execution. It also does not remove the
legacy router in one operation. Those changes happen only after the compatibility
adapters and parity checks prove that a vertical can run through the registry.
