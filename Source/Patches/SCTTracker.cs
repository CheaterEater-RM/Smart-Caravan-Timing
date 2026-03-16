using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SmartCaravanTiming
{
    public class SCTTracker : WorldComponent
    {
        private Dictionary<int, CaravanMode> modeMap = new Dictionary<int, CaravanMode>();
        private Dictionary<int, bool> preparingMap = new Dictionary<int, bool>();
        private Dictionary<int, bool> overrideMap = new Dictionary<int, bool>();
        private Dictionary<int, int> prepStartTickMap = new Dictionary<int, int>();
        private Dictionary<int, RestScheduleMode> restScheduleMap = new Dictionary<int, RestScheduleMode>();

        private List<int> modeKeys = new List<int>();
        private List<CaravanMode> modeValues = new List<CaravanMode>();
        private List<int> preparingKeys = new List<int>();
        private List<bool> preparingValues = new List<bool>();
        private List<int> overrideKeys = new List<int>();
        private List<bool> overrideValues = new List<bool>();
        private List<int> restScheduleKeys = new List<int>();
        private List<RestScheduleMode> restScheduleValues = new List<RestScheduleMode>();

        /// <summary>Ticks before recreation is considered stalled. 5000 = 2 game-hours.</summary>
        private const int RecreationStallTicks = 5000;

        public SCTTracker(World world) : base(world) { }

        public CaravanMode GetMode(Caravan caravan)
        {
            if (modeMap.TryGetValue(caravan.ID, out CaravanMode mode))
                return mode;
            return SCTMod.Settings.defaultMode;
        }

        public void SetMode(Caravan caravan, CaravanMode mode)
        {
            modeMap[caravan.ID] = mode;
            if (mode != CaravanMode.ArriveReady)
            {
                preparingMap.Remove(caravan.ID);
                overrideMap.Remove(caravan.ID);
                prepStartTickMap.Remove(caravan.ID);
            }
        }

        public void CycleMode(Caravan caravan)
        {
            CaravanMode current = GetMode(caravan);
            CaravanMode next = (CaravanMode)(((int)current + 1) % 3);
            SetMode(caravan, next);
        }

        public bool IsPreparing(Caravan caravan)
        {
            return preparingMap.TryGetValue(caravan.ID, out bool val) && val;
        }

        public void BeginPreparing(Caravan caravan)
        {
            preparingMap[caravan.ID] = true;
            prepStartTickMap[caravan.ID] = Find.TickManager.TicksGame;
        }

        public void EndPreparing(Caravan caravan)
        {
            preparingMap.Remove(caravan.ID);
            prepStartTickMap.Remove(caravan.ID);
        }

        public void OverridePreparing(Caravan caravan)
        {
            preparingMap.Remove(caravan.ID);
            prepStartTickMap.Remove(caravan.ID);
            overrideMap[caravan.ID] = true;
        }

        public bool IsOverridden(Caravan caravan)
        {
            return overrideMap.TryGetValue(caravan.ID, out bool val) && val;
        }

        public RestScheduleMode GetRestSchedule(Caravan caravan)
        {
            // If the feature is disabled globally, always report Normal (vanilla fallback)
            if (!SCTMod.Settings.enableRestSchedule)
                return RestScheduleMode.Normal;
            if (restScheduleMap.TryGetValue(caravan.ID, out RestScheduleMode mode))
                return mode;
            return SCTMod.Settings.defaultRestSchedule;
        }

        public void SetRestSchedule(Caravan caravan, RestScheduleMode mode)
        {
            restScheduleMap[caravan.ID] = mode;
        }

        public void CycleRestSchedule(Caravan caravan)
        {
            RestScheduleMode current = GetRestSchedule(caravan);
            RestScheduleMode next = (RestScheduleMode)(((int)current + 1) % 3);
            SetRestSchedule(caravan, next);
        }

        /// <summary>
        /// Returns true if this caravan should NOT rest right now based on its
        /// rest schedule mode. Arrive Ready preparation always overrides this.
        /// </summary>
        public bool ShouldSuppressRestForSchedule(Caravan caravan, float hour)
        {
            // Arrive Ready preparation takes priority — never suppress during prep
            if (IsPreparing(caravan)) return false;

            RestScheduleMode schedule = GetRestSchedule(caravan);
            switch (schedule)
            {
                case RestScheduleMode.NoResting:
                    return true;
                case RestScheduleMode.AlteredSchedule:
                    // Suppress rest if we are NOT in the altered rest window
                    return !SCTSettings.IsRestHour(hour, SCTMod.Settings.alteredRestStart, SCTMod.Settings.alteredRestEnd);
                default:
                    return false;
            }
        }

        public bool ShouldSuppressRest(Caravan caravan)
        {
            if (GetMode(caravan) != CaravanMode.PushOn) return false;
            if (caravan.pather == null || !caravan.pather.Moving) return false;
            float hours = EstimateHoursToDestination(caravan);
            return hours >= 0f && hours <= SCTMod.Settings.pushOnHours;
        }

        public ReadinessStatus GetReadinessStatus(Caravan caravan)
        {
            var settings = SCTMod.Settings;
            List<Pawn> pawns = GetAblePawns(caravan);
            if (pawns.Count == 0) return ReadinessStatus.Ready;

            if (settings.enableSleep && !NeedMet(pawns, NeedDefOf.Rest, settings.restThreshold))
                return ReadinessStatus.NeedsSleep;

            if (settings.enableFood && !NeedMet(pawns, NeedDefOf.Food, settings.foodThreshold))
                return ReadinessStatus.NeedsFood;

            if (settings.enableRec && SCTNeedDefOf.Joy != null
                && ModsConfig.IsActive("CheaterEater.CaravanRecreation")
                && !NeedMet(pawns, SCTNeedDefOf.Joy, settings.recThreshold))
            {
                if (prepStartTickMap.TryGetValue(caravan.ID, out int startTick))
                {
                    int elapsed = Find.TickManager.TicksGame - startTick;
                    if (elapsed > RecreationStallTicks)
                    {
                        Log.Message("[Smart Caravan Timing] Recreation stalled for "
                            + caravan.LabelCap + " after "
                            + (elapsed / 2500f).ToString("F1")
                            + "h - skipping recreation check.");
                    }
                    else
                    {
                        return ReadinessStatus.NeedsRecreation;
                    }
                }
                else
                {
                    return ReadinessStatus.NeedsRecreation;
                }
            }

            if (settings.enableArrivalWindow && !IsWithinArrivalWindow(caravan.Tile))
                return ReadinessStatus.WaitingForArrivalWindow;

            return ReadinessStatus.Ready;
        }

        public string GetStatusString(Caravan caravan)
        {
            var settings = SCTMod.Settings;
            List<Pawn> pawns = GetAblePawns(caravan);

            if (settings.enableSleep)
            {
                int below = CountPawnsBelowNeed(pawns, NeedDefOf.Rest, settings.restThreshold);
                if (!NeedMetByCount(below, pawns.Count))
                    return "SCT_Inspect_Sleeping".Translate(below.ToString());
            }
            if (settings.enableFood)
            {
                int below = CountPawnsBelowNeed(pawns, NeedDefOf.Food, settings.foodThreshold);
                if (!NeedMetByCount(below, pawns.Count))
                    return "SCT_Inspect_Eating".Translate(below.ToString());
            }
            if (settings.enableRec && SCTNeedDefOf.Joy != null
                && ModsConfig.IsActive("CheaterEater.CaravanRecreation"))
            {
                int below = CountPawnsBelowNeed(pawns, SCTNeedDefOf.Joy, settings.recThreshold);
                if (!NeedMetByCount(below, pawns.Count))
                    return "SCT_Inspect_Recreating".Translate(below.ToString());
            }
            if (settings.enableArrivalWindow && !IsWithinArrivalWindow(caravan.Tile))
                return "SCT_Inspect_WaitingArrivalWindow".Translate();

            return "SCT_Inspect_Ready".Translate();
        }

        public bool ShouldTriggerPreparation(Caravan caravan)
        {
            if (GetMode(caravan) != CaravanMode.ArriveReady) return false;
            if (IsPreparing(caravan)) return false;
            if (IsOverridden(caravan)) return false;
            if (caravan.pather == null || !caravan.pather.Moving) return false;
            float hours = EstimateHoursToDestination(caravan);
            if (hours < 0f || hours > SCTMod.Settings.prepareHours) return false;
            return GetReadinessStatus(caravan) != ReadinessStatus.Ready;
        }

        public bool IsReadyToResume(Caravan caravan)
        {
            if (!IsPreparing(caravan)) return false;
            return GetReadinessStatus(caravan) == ReadinessStatus.Ready;
        }

        /// <summary>
        /// Actively feed pawns below the food threshold during preparation.
        /// Respects each pawn's food policy. Eating is instant in caravans.
        /// </summary>
        private void ActivelyFeedPawns(Caravan caravan)
        {
            if (!SCTMod.Settings.enableFood) return;
            float threshold = SCTMod.Settings.foodThreshold;

            List<Pawn> pawns = GetAblePawns(caravan);
            foreach (Pawn pawn in pawns)
            {
                Need_Food foodNeed = pawn.needs?.food;
                if (foodNeed == null) continue;
                if (foodNeed.CurLevel >= threshold) continue;

                TryFeedPawnFromCaravan(pawn, caravan, threshold);
            }
        }

        /// <summary>
        /// Feed a pawn from caravan inventory, respecting food policy.
        /// Tries up to 3 items to reach threshold. Uses CachedNutrition (1.6 API).
        /// </summary>
        private void TryFeedPawnFromCaravan(Pawn pawn, Caravan caravan, float threshold)
        {
            Need_Food foodNeed = pawn.needs?.food;
            if (foodNeed == null) return;

            FoodPolicy policy = pawn.foodRestriction?.CurrentFoodPolicy;

            for (int i = 0; i < 3 && foodNeed.CurLevel < threshold; i++)
            {
                Thing bestFood = null;
                float bestNutrition = 0f;

                foreach (Thing item in CaravanInventoryUtility.AllInventoryItems(caravan))
                {
                    if (!item.def.IsNutritionGivingIngestible) continue;
                    if (item.def.IsDrug) continue;
                    if (!pawn.WillEat(item)) continue;

                    if (policy != null && !policy.Allows(item))
                        continue;

                    float nutrition = item.def.ingestible.CachedNutrition;
                    if (bestFood == null || nutrition > bestNutrition)
                    {
                        bestFood = item;
                        bestNutrition = nutrition;
                    }
                }

                if (bestFood == null) break;

                // Safe consumption: manually remove item and add nutrition.
                // Do NOT call Thing.Ingested() as it has side effects that
                // can interfere with map pawn food job assignment.
                float gained = bestFood.def.ingestible.CachedNutrition;
                Pawn owner = CaravanInventoryUtility.GetOwnerOf(caravan, bestFood);
                if (bestFood.stackCount > 1)
                {
                    bestFood.SplitOff(1).Destroy(DestroyMode.Vanish);
                }
                else
                {
                    bestFood.Destroy(DestroyMode.Vanish);
                    if (owner != null)
                    {
                        owner.inventory.innerContainer.Remove(bestFood);
                    }
                }

                foodNeed.CurLevel += gained;
            }
        }

        private bool NeedMet(List<Pawn> pawns, NeedDef needDef, float threshold)
        {
            if (needDef == null) return true;
            int belowCount = CountPawnsBelowNeed(pawns, needDef, threshold);
            return NeedMetByCount(belowCount, pawns.Count);
        }

        private bool NeedMetByCount(int belowCount, int totalCount)
        {
            if (totalCount == 0) return true;
            if (belowCount == 0) return true;
            if (SCTMod.Settings.requireAllPawns) return false;
            float aboveFraction = (float)(totalCount - belowCount) / totalCount;
            return aboveFraction >= SCTMod.Settings.readinessPercent;
        }

        private int CountPawnsBelowNeed(List<Pawn> pawns, NeedDef needDef, float threshold)
        {
            if (needDef == null) return 0;
            int count = 0;
            foreach (Pawn pawn in pawns)
            {
                Need need = pawn.needs?.TryGetNeed(needDef);
                if (need == null) continue;
                if (need.CurLevel < threshold) count++;
            }
            return count;
        }

        public static bool IsWithinArrivalWindow(PlanetTile tile)
        {
            if (!tile.Valid) return true;
            try
            {
                Vector2 lonLat = Find.WorldGrid.LongLatOf(tile);
                float hour = GenDate.HourFloat(Find.TickManager.TicksAbs, lonLat.x);
                float start = SCTMod.Settings.arrivalWindowStart;
                float end = SCTMod.Settings.arrivalWindowEnd;
                if (start <= end)
                    return hour >= start && hour < end;
                else
                    return hour >= start || hour < end;
            }
            catch { return true; }
        }

        public static List<Pawn> GetAblePawns(Caravan caravan)
        {
            List<Pawn> result = new List<Pawn>();
            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                if (pawn.Dead || pawn.Downed) continue;
                if (!pawn.RaceProps.Humanlike) continue;
                result.Add(pawn);
            }
            return result;
        }

        /// <summary>
        /// Fresh estimate (allowCaching=false) so winter/terrain penalties are current.
        /// 2500 ticks = 1 game-hour.
        /// </summary>
        public static float EstimateHoursToDestination(Caravan caravan)
        {
            if (caravan.pather == null || !caravan.pather.Moving) return -1f;
            if (!caravan.pather.Destination.Valid) return -1f;
            try
            {
                int ticksToArrive = CaravanArrivalTimeEstimator.EstimatedTicksToArrive(caravan, false);
                if (ticksToArrive <= 0) return 0f;
                return ticksToArrive / 2500f;
            }
            catch { return -1f; }
        }

        private void CleanupStaleEntries()
        {
            HashSet<int> activeIds = new HashSet<int>();
            foreach (Caravan c in Find.WorldObjects.Caravans) activeIds.Add(c.ID);
            List<int> toRemove = new List<int>();
            foreach (int id in modeMap.Keys)
            {
                if (!activeIds.Contains(id)) toRemove.Add(id);
            }
            foreach (int id in toRemove)
            {
                modeMap.Remove(id);
                preparingMap.Remove(id);
                overrideMap.Remove(id);
                prepStartTickMap.Remove(id);
                restScheduleMap.Remove(id);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref modeMap, "sctModeMap",
                LookMode.Value, LookMode.Value, ref modeKeys, ref modeValues);
            Scribe_Collections.Look(ref preparingMap, "sctPreparingMap",
                LookMode.Value, LookMode.Value, ref preparingKeys, ref preparingValues);
            Scribe_Collections.Look(ref overrideMap, "sctOverrideMap",
                LookMode.Value, LookMode.Value, ref overrideKeys, ref overrideValues);
            Scribe_Collections.Look(ref restScheduleMap, "sctRestScheduleMap",
                LookMode.Value, LookMode.Value, ref restScheduleKeys, ref restScheduleValues);

            if (modeMap == null) modeMap = new Dictionary<int, CaravanMode>();
            if (preparingMap == null) preparingMap = new Dictionary<int, bool>();
            if (overrideMap == null) overrideMap = new Dictionary<int, bool>();
            if (prepStartTickMap == null) prepStartTickMap = new Dictionary<int, int>();
            if (restScheduleMap == null) restScheduleMap = new Dictionary<int, RestScheduleMode>();
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (Find.TickManager.TicksGame % 250 != 0) return;

            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.Faction != Faction.OfPlayer) continue;

                CaravanMode mode = GetMode(caravan);

                // Clear override when caravan stops moving (arrived/cancelled)
                if (IsOverridden(caravan)
                    && (caravan.pather == null || !caravan.pather.Moving))
                {
                    overrideMap.Remove(caravan.ID);
                }

                if (mode == CaravanMode.ArriveReady)
                {
                    if (IsPreparing(caravan))
                    {
                        ActivelyFeedPawns(caravan);

                        if (IsReadyToResume(caravan))
                        {
                            EndPreparing(caravan);
                            Messages.Message(
                                "SCT_Msg_Resuming".Translate(caravan.LabelCap),
                                caravan, MessageTypeDefOf.PositiveEvent, false);
                        }
                    }
                    else if (ShouldTriggerPreparation(caravan))
                    {
                        BeginPreparing(caravan);
                        Messages.Message(
                            "SCT_Msg_Stopping".Translate(caravan.LabelCap),
                            caravan, MessageTypeDefOf.NeutralEvent, false);
                    }
                }
            }

            if (Find.TickManager.TicksGame % 15000 == 0) CleanupStaleEntries();
        }
    }

    public enum ReadinessStatus
    {
        Ready,
        NeedsSleep,
        NeedsFood,
        NeedsRecreation,
        WaitingForArrivalWindow
    }
}
