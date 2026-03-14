using HarmonyLib;
using RimWorld;
using Verse;

namespace SmartCaravanTiming
{
    /// <summary>
    /// Caches NeedDefs that may not be present in all game configurations.
    /// Populated after Defs are loaded, safe to access from [StaticConstructorOnStartup].
    /// </summary>
    internal static class SCTNeedDefOf
    {
        public static readonly NeedDef Joy = DefDatabase<NeedDef>.GetNamed("Joy", false);
    }

    /// <summary>
    /// Harmony patch entry point. Fires after all Defs are loaded.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class SmartCaravanTiming_Init
    {
        static SmartCaravanTiming_Init()
        {
            var harmony = new Harmony("com.cheatereater.smartcaravantiming");
            harmony.PatchAll();
            Log.Message("[Smart Caravan Timing] Harmony patches applied.");
        }
    }
}
