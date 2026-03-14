# Smart Caravan Timing

Take control of how your caravans handle the final stretch of their journey.

Adds a three-mode cycle button to each caravan:

**[Normal]** — Vanilla caravan behavior. No changes.

**[Push On]** — When within a configurable number of hours from the destination, the caravan ignores rest stops and pushes straight through. No more "we're one hour from home but it's nap time."

**[Arrive Ready]** — The caravan automatically pauses before reaching its destination to fulfill needs first. Pawns will sleep, eat, and recreate until thresholds are met, and can be configured to only arrive during preferred hours (e.g. not in the middle of the night). Your colonists arrive fresh and ready to fight or explore.

All thresholds, arrival time windows, and trigger distances are configurable in mod settings. Mode is set per-caravan and persists in your save.

## Requirements

- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) (must load before Core)
- RimWorld 1.5 or 1.6

## Optional

- [Caravan Recreation](link) — enables the recreation readiness check in Arrive Ready mode. Without this mod, recreation is too limited in vanilla caravans to be useful.

## Installation

Subscribe on Steam Workshop, or place the `Smart_Caravan_Timing` folder in your RimWorld `Mods` directory.

## Building from Source

1. Ensure `RimWorld.Paths.props` exists one directory above this repo with `$(RimWorldManaged)` and `$(HarmonyAssemblies)` defined.
2. Open `Source\SmartCaravanTiming.sln` in Visual Studio.
3. Build (Ctrl+Shift+B). Output lands in `1.6\Assemblies\SmartCaravanTiming.dll`.

## License

MIT
