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
    //
    // 1.6 API: PatherTickInterval(int delta), not PatherTick.
    // Caravan_PathFollower.caravan is private — Traverse required.

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
    // PATCH 2: Suppress night rest during Push On mode
    // ================================================================
    //
    // 1.6 API: RestingNowAt(PlanetTile tile).

    [HarmonyPatch(typeof(CaravanNightRestUtility), "RestingNowAt")]
    internal static class Patch_RestingNowAt
    {
        [HarmonyPostfix]
        public static void Postfix(PlanetTile tile, ref bool __result)
        {
            if (!__result) return;

            SCTTracker tracker = Find.World?.GetComponent<SCTTracker>();
            if (tracker == null) return;

            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.Faction != Faction.OfPlayer) continue;
                if (caravan.Tile.tileId != tile.tileId) continue;

                if (tracker.ShouldSuppressRest(caravan))
                {
                    __result = false;
                    return;
                }
            }
        }
    }

    // ================================================================
    // PATCH 3: Make Arrive Ready preparation act like night rest
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
    // PATCH 4: Cycle gizmo button on caravans
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

            CaravanMode mode = tracker.GetMode(__instance);
            bool isPreparing = tracker.IsPreparing(__instance);

            // -- Main cycle button --
            string label;
            string desc;
            switch (mode)
            {
                case CaravanMode.PushOn:
                    label = "SCT_Mode_PushOn".Translate();
                    desc = "SCT_Mode_PushOn_Desc".Translate(
                        SCTMod.Settings.pushOnHours.ToString("F1"));
                    break;
                case CaravanMode.ArriveReady:
                    label = "SCT_Mode_ArriveReady".Translate();
                    desc = "SCT_Mode_ArriveReady_Desc".Translate();
                    break;
                default:
                    label = "SCT_Mode_Normal".Translate();
                    desc = "SCT_Mode_Normal_Desc".Translate();
                    break;
            }

            if (isPreparing)
            {
                desc += "\n\n" + tracker.GetStatusString(__instance);
            }
            else if (mode == CaravanMode.PushOn && __instance.pather != null && __instance.pather.Moving)
            {
                float hours = SCTTracker.EstimateHoursToDestination(__instance);
                if (hours >= 0f && hours <= SCTMod.Settings.pushOnHours)
                {
                    desc += "\n\n" + "SCT_Inspect_PushingOn".Translate(hours.ToString("F1"));
                }
            }

            Command_Action cycleCmd = new Command_Action
            {
                defaultLabel = label,
                defaultDesc = desc,
                icon = GetModeIcon(mode),
                action = () =>
                {
                    tracker.CycleMode(__instance);
                }
            };

            yield return cycleCmd;

            // -- "Proceed now" button when preparing --
            if (isPreparing)
            {
                Command_Action proceedCmd = new Command_Action
                {
                    defaultLabel = "SCT_Inspect_Ready".Translate(),
                    defaultDesc = "Force the caravan to proceed immediately, even if needs aren't fully met.",
                    icon = TexCommand.SquadAttack,
                    action = () =>
                    {
                        tracker.OverridePreparing(__instance);
                    }
                };
                yield return proceedCmd;
            }
        }

        private static Texture2D GetModeIcon(CaravanMode mode)
        {
            switch (mode)
            {
                case CaravanMode.PushOn:
                    return TexCommand.SquadAttack;
                case CaravanMode.ArriveReady:
                    return TexCommand.PauseCaravan;
                default:
                    return TexCommand.Draft;
            }
        }
    }

    // ================================================================
    // PATCH 5: Inspect string showing current status
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
        }
    }
}
