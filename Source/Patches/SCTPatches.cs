using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SmartCaravanTiming
{
    // ================================================================
    // PATCH 1: Block pather tick during Arrive Ready preparation
    // ================================================================

    [HarmonyPatch(typeof(Caravan_PathFollower), "PatherTickInterval")]
    internal static class Patch_PatherTick
    {
        [HarmonyPrefix]
        public static bool Prefix(Caravan_PathFollower __instance)
        {
            Caravan caravan = Traverse.Create(__instance).Field("caravan").GetValue<Caravan>();
            if (caravan == null || caravan.Faction != Faction.OfPlayer)
                return true;

            SCTTracker tracker = Find.World?.GetComponent<SCTTracker>();
            if (tracker == null) return true;

            if (tracker.IsPreparing(caravan))
                return false;

            return true;
        }
    }

    // ================================================================
    // PATCH 2: Global vanilla rest window override (WouldBeRestingAt)
    // ================================================================
    //
    // WouldBeRestingAt(PlanetTile tile, long ticksAbs) has no caravan
    // context — we use it for the always-active global window tweak.
    // Vanilla: hour < 6 || hour > 22.

    [HarmonyPatch(typeof(CaravanNightRestUtility), "WouldBeRestingAt",
        new[] { typeof(PlanetTile), typeof(long) })]
    internal static class Patch_WouldBeRestingAt
    {
        [HarmonyPostfix]
        public static void Postfix(PlanetTile tile, long ticksAbs, ref bool __result)
        {
            var settings = SCTMod.Settings;
            // If the user hasn't changed either bound from vanilla defaults, skip
            if (settings.vanillaRestStart == 22f && settings.vanillaRestEnd == 6f)
                return;

            try
            {
                float hour = GenDate.HourFloat(ticksAbs, Find.WorldGrid.LongLatOf(tile).x);
                __result = settings.IsVanillaRestHour(hour);
            }
            catch { /* leave __result unchanged on any error */ }
        }
    }

    // ================================================================
    // PATCH 3: Per-caravan rest schedule + Push On suppression
    // ================================================================
    //
    // RestingNowAt calls WouldBeRestingAt — by the time we're here the
    // global window patch has already run, so __result reflects the
    // (possibly modified) vanilla window. We then layer per-caravan
    // overrides on top.

    [HarmonyPatch(typeof(CaravanNightRestUtility), "RestingNowAt")]
    internal static class Patch_RestingNowAt
    {
        [HarmonyPostfix]
        public static void Postfix(PlanetTile tile, ref bool __result)
        {
            SCTTracker tracker = Find.World?.GetComponent<SCTTracker>();
            if (tracker == null) return;

            try
            {
                float hour = GenDate.HourFloat(
                    GenTicks.TicksAbs,
                    Find.WorldGrid.LongLatOf(tile).x);

                foreach (Caravan caravan in Find.WorldObjects.Caravans)
                {
                    if (caravan.Faction != Faction.OfPlayer) continue;
                    if (caravan.Tile.tileId != tile.tileId) continue;

                    // Push On suppression (existing behaviour)
                    if (tracker.ShouldSuppressRest(caravan))
                    {
                        __result = false;
                        return;
                    }

                    // Rest schedule gizmo suppression
                    // Note: if __result is already false (not a rest period by the
                    // global window) and the caravan is in AlteredSchedule mode that
                    // WANTS rest now, we still don't force rest — we only suppress it.
                    if (__result && tracker.ShouldSuppressRestForSchedule(caravan, hour))
                    {
                        __result = false;
                        return;
                    }
                }
            }
            catch { }
        }
    }

    // ================================================================
    // PATCH 4: Make Arrive Ready preparation act like night rest
    // ================================================================

    [HarmonyPatch(typeof(Caravan), "get_NightResting")]
    internal static class Patch_NightResting
    {
        [HarmonyPostfix]
        public static void Postfix(Caravan __instance, ref bool __result)
        {
            if (__result) return;
            if (__instance.Faction != Faction.OfPlayer) return;

            SCTTracker tracker = Find.World?.GetComponent<SCTTracker>();
            if (tracker == null) return;

            if (tracker.IsPreparing(__instance))
                __result = true;
        }
    }

    // ================================================================
    // PATCH 5: Gizmos — arrival mode + rest schedule
    // ================================================================

    [HarmonyPatch(typeof(Caravan), "GetGizmos")]
    internal static class Patch_CaravanGizmos
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
        {
            foreach (Gizmo gizmo in __result)
                yield return gizmo;

            if (__instance.Faction != Faction.OfPlayer) yield break;

            SCTTracker tracker = Find.World?.GetComponent<SCTTracker>();
            if (tracker == null) yield break;

            // ── Gizmo A: Arrival mode ──────────────────────────────────
            CaravanMode mode       = tracker.GetMode(__instance);
            bool        isPreparing = tracker.IsPreparing(__instance);

            string label, desc;
            switch (mode)
            {
                case CaravanMode.PushOn:
                    label = "SCT_Mode_PushOn".Translate();
                    desc  = "SCT_Mode_PushOn_Desc".Translate(SCTMod.Settings.pushOnHours.ToString("F1"));
                    break;
                case CaravanMode.ArriveReady:
                    label = "SCT_Mode_ArriveReady".Translate();
                    desc  = "SCT_Mode_ArriveReady_Desc".Translate();
                    break;
                default:
                    label = "SCT_Mode_Normal".Translate();
                    desc  = "SCT_Mode_Normal_Desc".Translate();
                    break;
            }

            if (isPreparing)
            {
                desc += "\n\n" + tracker.GetStatusString(__instance);
            }
            else if (mode == CaravanMode.PushOn
                && __instance.pather != null && __instance.pather.Moving)
            {
                float hours = SCTTracker.EstimateHoursToDestination(__instance);
                if (hours >= 0f && hours <= SCTMod.Settings.pushOnHours)
                    desc += "\n\n" + "SCT_Inspect_PushingOn".Translate(hours.ToString("F1"));
            }

            yield return new Command_Action
            {
                defaultLabel = label,
                defaultDesc  = desc,
                icon         = GetModeIcon(mode),
                action       = () => tracker.CycleMode(__instance)
            };

            if (isPreparing)
            {
                yield return new Command_Action
                {
                    defaultLabel = "SCT_Inspect_Ready".Translate(),
                    defaultDesc  = "SCT_Inspect_Ready_Desc".Translate(),
                    icon         = TexCommand.SquadAttack,
                    action       = () => tracker.OverridePreparing(__instance)
                };
            }

            // ── Gizmo B: Rest schedule (only when enabled in settings) ──
            if (!SCTMod.Settings.enableRestSchedule) yield break;

            RestScheduleMode schedule = tracker.GetRestSchedule(__instance);

            string schedLabel, schedDesc;
            switch (schedule)
            {
                case RestScheduleMode.AlteredSchedule:
                    schedLabel = "SCT_RestMode_Altered".Translate();
                    schedDesc  = "SCT_RestMode_Altered_Desc".Translate(
                        SCTMod.FormatHour(SCTMod.Settings.alteredRestStart),
                        SCTMod.FormatHour(SCTMod.Settings.alteredRestEnd));
                    break;
                case RestScheduleMode.NoResting:
                    schedLabel = "SCT_RestMode_NoResting".Translate();
                    schedDesc  = "SCT_RestMode_NoResting_Desc".Translate();
                    break;
                default:
                    schedLabel = "SCT_RestMode_Normal".Translate();
                    schedDesc  = "SCT_RestMode_Normal_Desc".Translate();
                    break;
            }

            yield return new Command_Action
            {
                defaultLabel = schedLabel,
                defaultDesc  = schedDesc,
                icon         = GetScheduleIcon(schedule),
                action       = () => tracker.CycleRestSchedule(__instance)
            };
        }

        private static Texture2D GetModeIcon(CaravanMode mode)
        {
            switch (mode)
            {
                case CaravanMode.PushOn:      return TexCommand.SquadAttack;
                case CaravanMode.ArriveReady: return TexCommand.PauseCaravan;
                default:                      return TexCommand.Draft;
            }
        }

        private static Texture2D GetScheduleIcon(RestScheduleMode mode)
        {
            switch (mode)
            {
                case RestScheduleMode.AlteredSchedule: return TexCommand.PauseCaravan;
                case RestScheduleMode.NoResting:        return TexCommand.SquadAttack;
                default:                                return TexCommand.Draft;
            }
        }
    }

    // ================================================================
    // PATCH 6: Inspect string
    // ================================================================

    [HarmonyPatch(typeof(Caravan), "GetInspectString")]
    internal static class Patch_InspectString
    {
        [HarmonyPostfix]
        public static void Postfix(Caravan __instance, ref string __result)
        {
            if (__instance.Faction != Faction.OfPlayer) return;

            SCTTracker tracker = Find.World?.GetComponent<SCTTracker>();
            if (tracker == null) return;

            CaravanMode mode = tracker.GetMode(__instance);

            if (mode == CaravanMode.ArriveReady && tracker.IsPreparing(__instance))
            {
                __result += "\n" + tracker.GetStatusString(__instance);
            }
            else if (mode == CaravanMode.PushOn && tracker.ShouldSuppressRest(__instance))
            {
                float hours = SCTTracker.EstimateHoursToDestination(__instance);
                if (hours >= 0f)
                    __result += "\n" + "SCT_Inspect_PushingOn".Translate(hours.ToString("F1"));
            }

            // Rest schedule status line
            if (SCTMod.Settings.enableRestSchedule)
            {
                RestScheduleMode schedule = tracker.GetRestSchedule(__instance);
                if (schedule == RestScheduleMode.NoResting)
                    __result += "\n" + "SCT_Inspect_NoResting".Translate();
                else if (schedule == RestScheduleMode.AlteredSchedule)
                    __result += "\n" + "SCT_Inspect_AlteredSchedule".Translate(
                        SCTMod.FormatHour(SCTMod.Settings.alteredRestStart),
                        SCTMod.FormatHour(SCTMod.Settings.alteredRestEnd));
            }
        }
    }
}
