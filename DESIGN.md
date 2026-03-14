# Smart Caravan Timing — Design Document

## Overview

Three-mode caravan control mod. Adds a cycle gizmo to each player caravan, letting you choose between vanilla behavior, an aggressive push-to-destination mode, and a preparation-before-arrival mode. Standalone — no dependency on Almost There or any other mod.

## Core Mechanics

### Normal Mode

No intervention. Vanilla caravan behavior — rest, travel, repeat.

### Push On Mode

When the caravan is within `pushOnHours` of its destination, night rest is suppressed and the caravan travels straight through. Patches `CaravanNightRestUtility.RestingNowAt` to return `false` under those conditions.

### Arrive Ready Mode

When the caravan comes within `prepareHours` of its destination and needs are unmet, it pauses automatically. The `Caravan.get_NightResting` property is postfixed to return `true` during preparation, which causes vanilla to handle rest gain, food consumption, and recreation naturally. The `Caravan_PathFollower.PatherTickInterval` prefix returns `false` to freeze movement while preparing. Food is also actively dispensed from caravan inventory each tick cycle (every 250 ticks) to accelerate food recovery.

Once all enabled need thresholds are met (and any arrival time window is satisfied), the caravan resumes and a message is posted. The player can also force-resume via a "Proceed now" gizmo button.

## Architecture

### Source Files

| File | Purpose |
|---|---|
| `Setup.cs` | `[StaticConstructorOnStartup]` Harmony init; `SCTNeedDefOf` Def cache |
| `SCTSettings.cs` | `ModSettings` + `Mod` class; all configurable values; settings UI |
| `SCTTracker.cs` | `WorldComponent`; per-caravan state; need evaluation; tick logic |
| `SCTPatches.cs` | All 5 Harmony patches |

### Harmony Patches

| Patch Target | Type | Purpose |
|---|---|---|
| `Caravan_PathFollower.PatherTickInterval` | Prefix | Freeze caravan movement during Arrive Ready preparation |
| `CaravanNightRestUtility.RestingNowAt` | Postfix | Suppress night rest during Push On when close to destination |
| `Caravan.get_NightResting` | Postfix | Make preparation state act like night rest (enables vanilla need recovery) |
| `Caravan.GetGizmos` | Postfix (pass-through) | Inject the mode cycle button and "Proceed now" button |
| `Caravan.GetInspectString` | Postfix | Append preparation status to the inspect pane |

### Components

| Component | Type | Purpose |
|---|---|---|
| `SCTTracker` | WorldComponent | Per-caravan mode, preparing state, override state, prep start tick |

### State Persistence

`SCTTracker` holds four dictionaries keyed by `caravan.ID` (int):

- `modeMap` — `CaravanMode` per caravan
- `preparingMap` — `bool`, whether actively preparing
- `overrideMap` — `bool`, player forced resume (cleared when caravan stops moving)
- `prepStartTickMap` — `int`, tick when preparation began (used for recreation stall detection)

`modeMap`, `preparingMap`, and `overrideMap` are serialized via `Scribe_Collections`. `prepStartTickMap` is intentionally not saved — stall detection resets on load, which is acceptable.

## Settings

| Setting | Default | Description |
|---|---|---|
| `pushOnHours` | 4.0 | Hours from destination within which Push On ignores rest |
| `prepareHours` | 6.0 | Hours from destination at which Arrive Ready triggers a stop |
| `enableSleep` | true | Check rest need during preparation |
| `restThreshold` | 0.80 | Minimum rest level (0–1) to consider ready |
| `enableFood` | true | Check food need during preparation |
| `foodThreshold` | 0.60 | Minimum food level (0–1) to consider ready |
| `enableRec` | false | Check recreation (Joy) need during preparation (requires Caravan Recreation mod) |
| `recThreshold` | 0.50 | Minimum Joy level (0–1) to consider ready |
| `enableArrivalWindow` | true | Only proceed within a preferred time window |
| `arrivalWindowStart` | 6 | Earliest arrival hour (local solar time, 0–23) |
| `arrivalWindowEnd` | 18 | Latest arrival hour (local solar time, 0–23) |
| `requireAllPawns` | true | All humanlike pawns must meet thresholds (vs. percentage) |
| `readinessPercent` | 0.75 | Fraction of pawns that must be ready when `requireAllPawns` is false |
| `defaultMode` | Normal | Default mode assigned to newly formed caravans |

## Compatibility

### Hard Dependencies
- Harmony (brrainz.harmony) — runtime patching

### Soft Dependencies
- Caravan Recreation (CheaterEater.CaravanRecreation) — enables the recreation readiness check. Without this mod, recreation is hard-gated off since vanilla caravans have no meaningful way to recover recreation.

### Known Interactions
- **Vehicle Framework caravans** — untested; patching `Caravan_PathFollower` and `Caravan` should be compatible but verify.

## Performance Considerations

`WorldComponentTick` runs every 250 ticks (not every tick). The caravan loop is small (player caravans only). `EstimateHoursToDestination` calls `CaravanArrivalTimeEstimator.EstimatedTicksToArrive` which does path-cost math — this is called once per caravan per tick cycle, which is acceptable. `CleanupStaleEntries` runs every 15,000 ticks.

## Critical API Notes

### `pather.Moving` vs `pather.MovingNow`
- `caravan.pather.Moving` = `true` whenever the caravan has a destination set, **even during night rest**
- `caravan.pather.MovingNow` = `true` only when actually advancing this tick
- Push On / Arrive Ready trigger checks use `.Moving` (has destination) not `.MovingNow`

### `Caravan_PathFollower.caravan`
- This field is **private** in RimWorld 1.6. `Traverse.Create(__instance).Field("caravan").GetValue<Caravan>()` is required.

### `CaravanNightRestUtility.RestingNowAt`
- 1.6 signature: `RestingNowAt(PlanetTile tile)` — not `int` tile.

### `CaravanArrivalTimeEstimator.EstimatedTicksToArrive`
- Can throw in edge cases (invalid path, etc.) — always wrap in try-catch.
- `allowCaching: false` used for fresh estimates accounting for current terrain/weather.

### Food consumption
- `Thing.Ingested()` is **not** called — it has side effects that can interfere with map pawn food job assignment.
- Instead: item is destroyed, `foodNeed.CurLevel` is incremented directly by `CachedNutrition`.
- `item.def.ingestible.CachedNutrition` is the correct 1.6 API (not `GetStatValueAbstract`).

### Arrival time window
- Uses `Find.WorldGrid.LongLatOf(tile).x` for longitude → `GenDate.HourFloat(ticksAbs, longitude)` for local solar time.
- Handles midnight wrap correctly (e.g. `start=22, end=6` = "not between 6 AM and 10 PM").

### Recreation stall detection
- Caravans cannot always recover Joy (even with Caravan Recreation, some situations may stall). After `RecreationStallTicks` (5000 ticks ≈ 2 hours) the Joy check is skipped with a log message to prevent deadlock.
- Without Caravan Recreation mod, the recreation check is hard-gated off entirely.
