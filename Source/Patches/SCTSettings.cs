using Verse;
using UnityEngine;

namespace SmartCaravanTiming
{
    /// <summary>
    /// The three gizmo modes for each caravan.
    /// </summary>
    public enum CaravanMode
    {
        Normal = 0,
        PushOn = 1,
        ArriveReady = 2
    }

    public class SCTSettings : ModSettings
    {
        // ── Push On settings ────────────────────────────────────────────
        /// <summary>Hours from destination within which Push On skips rest.</summary>
        public float pushOnHours = 4f;

        // ── Arrive Ready settings ───────────────────────────────────────
        /// <summary>Hours from destination at which Arrive Ready triggers a stop.</summary>
        public float prepareHours = 6f;

        // Need toggles and thresholds
        public bool enableSleep = true;
        public float restThreshold = 0.80f;
        public bool enableFood = true;
        public float foodThreshold = 0.60f;
        public bool enableRec = false;
        public float recThreshold = 0.50f;

        // Arrival time window
        public bool enableArrivalWindow = true;
        /// <summary>Earliest hour to arrive (0-23). Default 6 = 6 AM.</summary>
        public float arrivalWindowStart = 6f;
        /// <summary>Latest hour to arrive (0-23). Default 18 = 6 PM.</summary>
        public float arrivalWindowEnd = 18f;

        // Readiness check
        /// <summary>If true, ALL pawns must meet thresholds. If false, use percentage.</summary>
        public bool requireAllPawns = true;
        /// <summary>Percentage of pawns that must meet thresholds (0-1). Only used if requireAllPawns is false.</summary>
        public float readinessPercent = 0.75f;

        // Default mode for newly formed caravans
        public CaravanMode defaultMode = CaravanMode.Normal;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref pushOnHours, "pushOnHours", 4f);
            Scribe_Values.Look(ref prepareHours, "prepareHours", 6f);
            Scribe_Values.Look(ref enableSleep, "enableSleep", true);
            Scribe_Values.Look(ref restThreshold, "restThreshold", 0.80f);
            Scribe_Values.Look(ref enableFood, "enableFood", true);
            Scribe_Values.Look(ref foodThreshold, "foodThreshold", 0.60f);
            Scribe_Values.Look(ref enableRec, "enableRec", false);
            Scribe_Values.Look(ref recThreshold, "recThreshold", 0.50f);
            Scribe_Values.Look(ref enableArrivalWindow, "enableArrivalWindow", true);
            Scribe_Values.Look(ref arrivalWindowStart, "arrivalWindowStart", 6f);
            Scribe_Values.Look(ref arrivalWindowEnd, "arrivalWindowEnd", 18f);
            Scribe_Values.Look(ref requireAllPawns, "requireAllPawns", true);
            Scribe_Values.Look(ref readinessPercent, "readinessPercent", 0.75f);
            Scribe_Values.Look(ref defaultMode, "defaultMode", CaravanMode.Normal);
            base.ExposeData();
        }
    }

    public class SCTMod : Mod
    {
        public static SCTSettings Settings;

        public SCTMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SCTSettings>();
        }

        public override string SettingsCategory()
        {
            return "SCT_SettingsTitle".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            // ── Push On ─────────────────────────────────────────────────
            listing.Label("SCT_Settings_Header_PushOn".Translate());
            listing.Label("SCT_Settings_PushOnHours".Translate() + ": " + Settings.pushOnHours.ToString("F1") + "h");
            Settings.pushOnHours = listing.Slider(Settings.pushOnHours, 1f, 24f);

            listing.GapLine();

            // ── Arrive Ready ────────────────────────────────────────────
            listing.Label("SCT_Settings_Header_ArriveReady".Translate());
            listing.Label("SCT_Settings_PrepareHours".Translate() + ": " + Settings.prepareHours.ToString("F1") + "h");
            Settings.prepareHours = listing.Slider(Settings.prepareHours, 1f, 24f);

            listing.Gap();

            // Sleep
            listing.CheckboxLabeled("SCT_Settings_EnableSleep".Translate(), ref Settings.enableSleep);
            if (Settings.enableSleep)
            {
                listing.Label("  " + "SCT_Settings_RestThreshold".Translate() + ": " + Settings.restThreshold.ToStringPercent());
                Settings.restThreshold = listing.Slider(Settings.restThreshold, 0.3f, 1f);
            }

            listing.Gap();

            // Food
            listing.CheckboxLabeled("SCT_Settings_EnableFood".Translate(), ref Settings.enableFood);
            if (Settings.enableFood)
            {
                listing.Label("  " + "SCT_Settings_FoodThreshold".Translate() + ": " + Settings.foodThreshold.ToStringPercent());
                Settings.foodThreshold = listing.Slider(Settings.foodThreshold, 0.2f, 1f);
            }

            listing.Gap();

            // Recreation (requires Caravan Recreation mod)
            bool hasCaravanRec = ModsConfig.IsActive("CheaterEater.CaravanRecreation");
            if (hasCaravanRec)
            {
                listing.CheckboxLabeled("SCT_Settings_EnableRec".Translate(), ref Settings.enableRec);
                if (Settings.enableRec)
                {
                    listing.Label("  " + "SCT_Settings_RecThreshold".Translate() + ": " + Settings.recThreshold.ToStringPercent());
                    Settings.recThreshold = listing.Slider(Settings.recThreshold, 0.2f, 1f);
                }
            }
            else
            {
                Settings.enableRec = false;
                listing.Label("SCT_Settings_RecRequiresMod".Translate());
            }

            listing.GapLine();

            // Arrival time window
            listing.CheckboxLabeled("SCT_Settings_EnableArrivalWindow".Translate(), ref Settings.enableArrivalWindow);
            if (Settings.enableArrivalWindow)
            {
                listing.Label("  " + "SCT_Settings_ArrivalWindowStart".Translate() + ": " + FormatHour(Settings.arrivalWindowStart));
                Settings.arrivalWindowStart = Mathf.Round(listing.Slider(Settings.arrivalWindowStart, 0f, 23f));
                listing.Label("  " + "SCT_Settings_ArrivalWindowEnd".Translate() + ": " + FormatHour(Settings.arrivalWindowEnd));
                Settings.arrivalWindowEnd = Mathf.Round(listing.Slider(Settings.arrivalWindowEnd, 0f, 23f));
            }

            listing.GapLine();

            // Readiness check
            listing.Label("SCT_Settings_Header_Readiness".Translate());
            listing.CheckboxLabeled("SCT_Settings_RequireAllPawns".Translate(), ref Settings.requireAllPawns);
            if (!Settings.requireAllPawns)
            {
                listing.Label("  " + "SCT_Settings_ReadinessPercent".Translate() + ": " + Settings.readinessPercent.ToStringPercent());
                Settings.readinessPercent = listing.Slider(Settings.readinessPercent, 0.25f, 1f);
            }

            listing.GapLine();

            // Default mode
            string modeLabel;
            switch (Settings.defaultMode)
            {
                case CaravanMode.PushOn: modeLabel = "SCT_Mode_PushOn".Translate(); break;
                case CaravanMode.ArriveReady: modeLabel = "SCT_Mode_ArriveReady".Translate(); break;
                default: modeLabel = "SCT_Mode_Normal".Translate(); break;
            }
            if (listing.ButtonTextLabeled("SCT_Settings_DefaultMode".Translate(), modeLabel))
            {
                Settings.defaultMode = (CaravanMode)(((int)Settings.defaultMode + 1) % 3);
            }

            listing.End();
        }

        private static string FormatHour(float hour)
        {
            int h = Mathf.FloorToInt(hour);
            if (h == 0) return "12 AM";
            if (h < 12) return h + " AM";
            if (h == 12) return "12 PM";
            return (h - 12) + " PM";
        }
    }
}
