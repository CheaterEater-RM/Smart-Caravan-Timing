using RimWorld;
using UnityEngine;
using Verse;

namespace SmartCaravanTiming
{
    public enum CaravanMode
    {
        Normal = 0,
        PushOn = 1,
        ArriveReady = 2
    }

    /// <summary>Per-caravan rest schedule mode for the second gizmo.</summary>
    public enum RestScheduleMode
    {
        Normal = 0,           // vanilla behaviour (or global window if that's set)
        AlteredSchedule = 1,  // use the altered rest window from settings
        NoResting = 2         // never stop for rest automatically
    }

    public class SCTSettings : ModSettings
    {
        // ── Push On ──────────────────────────────────────────────────────
        public float pushOnHours = 4f;

        // ── Arrive Ready ─────────────────────────────────────────────────
        public float prepareHours = 6f;

        public bool enableSleep = true;
        public float restThreshold = 0.80f;
        public bool enableFood = true;
        public float foodThreshold = 0.60f;
        public bool enableRec = false;
        public float recThreshold = 0.50f;

        // Arrival time window
        public bool enableArrivalWindow = true;
        public float arrivalWindowStart = 6f;
        public float arrivalWindowEnd = 18f;

        // Readiness check
        public bool requireAllPawns = true;
        public float readinessPercent = 0.75f;

        // Default mode for newly formed caravans
        public CaravanMode defaultMode = CaravanMode.Normal;

        // ── Global vanilla rest window override (always active) ───────────
        // Vanilla: rest starts at 22:00, wakes at 06:00.
        public float vanillaRestStart = 22f;
        public float vanillaRestEnd   = 6f;

        // ── Rest Schedule gizmo ───────────────────────────────────────────
        /// <summary>Whether the rest schedule gizmo is enabled at all.</summary>
        public bool enableRestSchedule = false;

        /// <summary>
        /// The altered rest window used by caravans in AlteredSchedule mode.
        /// Stored as start/end hour (0–23). Wraps midnight correctly.
        /// </summary>
        public float alteredRestStart = 6f;   // default: rest during day (6 AM–2 PM)
        public float alteredRestEnd   = 14f;

        /// <summary>Default rest schedule mode assigned to new caravans.</summary>
        public RestScheduleMode defaultRestSchedule = RestScheduleMode.Normal;

        // Internal: first-load flag for CR default
        private bool initialised = false;

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
            Scribe_Values.Look(ref vanillaRestStart, "vanillaRestStart", 22f);
            Scribe_Values.Look(ref vanillaRestEnd,   "vanillaRestEnd",   6f);
            Scribe_Values.Look(ref enableRestSchedule, "enableRestSchedule", false);
            Scribe_Values.Look(ref alteredRestStart, "alteredRestStart", 6f);
            Scribe_Values.Look(ref alteredRestEnd,   "alteredRestEnd",   14f);
            Scribe_Values.Look(ref defaultRestSchedule, "defaultRestSchedule", RestScheduleMode.Normal);
            Scribe_Values.Look(ref initialised, "initialised", false);
            base.ExposeData();

            if (!initialised)
            {
                initialised = true;
                if (ModsConfig.IsActive("CheaterEater.CaravanRecreation"))
                    enableRec = true;
            }
        }

        /// <summary>
        /// Returns true if the given hour falls within the vanilla rest window
        /// (as overridden by the user's global setting).
        /// </summary>
        public bool IsVanillaRestHour(float hour)
        {
            return IsRestHour(hour, vanillaRestStart, vanillaRestEnd);
        }

        /// <summary>
        /// Returns true if the given hour falls within the altered rest window.
        /// </summary>
        public bool IsAlteredRestHour(float hour)
        {
            return IsRestHour(hour, alteredRestStart, alteredRestEnd);
        }

        /// <summary>Midnight-wrapping rest window check.</summary>
        public static bool IsRestHour(float hour, float start, float end)
        {
            // Window wraps midnight (e.g. 22–6): rest if hour >= start OR hour < end
            if (start > end)
                return hour >= start || hour < end;
            // Window within same day (e.g. 6–14): rest if start <= hour < end
            return hour >= start && hour < end;
        }
    }

    public class SCTMod : Mod
    {
        public static SCTSettings Settings;

        private string _pushOnBuf;
        private string _prepareBuf;
        private string _restBuf;
        private string _foodBuf;
        private string _recBuf;
        private string _windowStartBuf;
        private string _windowEndBuf;
        private string _readinessBuf;
        private string _vanillaRestStartBuf;
        private string _vanillaRestEndBuf;
        private string _alteredRestStartBuf;
        private string _alteredRestEndBuf;

        public SCTMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SCTSettings>();
        }

        public override string SettingsCategory() => "SCT_SettingsTitle".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            _pushOnBuf           ??= Settings.pushOnHours.ToString("F1");
            _prepareBuf          ??= Settings.prepareHours.ToString("F1");
            _restBuf             ??= Mathf.RoundToInt(Settings.restThreshold * 100f).ToString();
            _foodBuf             ??= Mathf.RoundToInt(Settings.foodThreshold * 100f).ToString();
            _recBuf              ??= Mathf.RoundToInt(Settings.recThreshold * 100f).ToString();
            _windowStartBuf      ??= Settings.arrivalWindowStart.ToString("F0");
            _windowEndBuf        ??= Settings.arrivalWindowEnd.ToString("F0");
            _readinessBuf        ??= Mathf.RoundToInt(Settings.readinessPercent * 100f).ToString();
            _vanillaRestStartBuf ??= Settings.vanillaRestStart.ToString("F0");
            _vanillaRestEndBuf   ??= Settings.vanillaRestEnd.ToString("F0");
            _alteredRestStartBuf ??= Settings.alteredRestStart.ToString("F0");
            _alteredRestEndBuf   ??= Settings.alteredRestEnd.ToString("F0");

            bool hasCaravanRec = ModsConfig.IsActive("CheaterEater.CaravanRecreation");

            float gutter   = 16f;
            float colWidth = (inRect.width - gutter) / 2f;
            Rect leftCol   = new Rect(inRect.x, inRect.y, colWidth, inRect.height);
            Rect rightCol  = new Rect(inRect.x + colWidth + gutter, inRect.y, colWidth, inRect.height);

            // ── LEFT COLUMN ────────────────────────────────────────────
            var left = new Listing_Standard();
            left.Begin(leftCol);

            SectionHeader(left, "SCT_Settings_Header_PushOn".Translate());
            FloatEntryRow(left, "SCT_Settings_PushOnHours".Translate(), "h",
                ref Settings.pushOnHours, ref _pushOnBuf, 0.5f, 48f);

            left.GapLine();

            SectionHeader(left, "SCT_Settings_Header_ArriveReady".Translate());
            FloatEntryRow(left, "SCT_Settings_PrepareHours".Translate(), "h",
                ref Settings.prepareHours, ref _prepareBuf, 0.5f, 48f);

            left.Gap();

            left.CheckboxLabeled("SCT_Settings_EnableSleep".Translate(), ref Settings.enableSleep);
            using (new DisabledBlock(!Settings.enableSleep))
                PercentEntryRow(left, "SCT_Settings_RestThreshold".Translate(),
                    ref Settings.restThreshold, ref _restBuf, 10, 100);

            left.Gap();

            left.CheckboxLabeled("SCT_Settings_EnableFood".Translate(), ref Settings.enableFood);
            using (new DisabledBlock(!Settings.enableFood))
                PercentEntryRow(left, "SCT_Settings_FoodThreshold".Translate(),
                    ref Settings.foodThreshold, ref _foodBuf, 10, 100);

            left.Gap();

            if (hasCaravanRec)
            {
                left.CheckboxLabeled("SCT_Settings_EnableRec".Translate(), ref Settings.enableRec);
                using (new DisabledBlock(!Settings.enableRec))
                    PercentEntryRow(left, "SCT_Settings_RecThreshold".Translate(),
                        ref Settings.recThreshold, ref _recBuf, 10, 100);
            }
            else
            {
                GUI.color = Color.gray;
                left.Label("SCT_Settings_RecRequiresMod".Translate());
                GUI.color = Color.white;
                Settings.enableRec = false;
            }

            left.End();

            // ── RIGHT COLUMN ───────────────────────────────────────────
            var right = new Listing_Standard();
            right.Begin(rightCol);

            // Arrival window
            SectionHeader(right, "SCT_Settings_Header_ArrivalWindow".Translate());
            right.CheckboxLabeled("SCT_Settings_EnableArrivalWindow".Translate(), ref Settings.enableArrivalWindow);
            using (new DisabledBlock(!Settings.enableArrivalWindow))
            {
                HourEntryRow(right, "SCT_Settings_ArrivalWindowStart".Translate(),
                    ref Settings.arrivalWindowStart, ref _windowStartBuf);
                HourEntryRow(right, "SCT_Settings_ArrivalWindowEnd".Translate(),
                    ref Settings.arrivalWindowEnd, ref _windowEndBuf);
            }

            right.GapLine();

            // Readiness check
            SectionHeader(right, "SCT_Settings_Header_Readiness".Translate());
            right.CheckboxLabeled("SCT_Settings_RequireAllPawns".Translate(), ref Settings.requireAllPawns);
            using (new DisabledBlock(Settings.requireAllPawns))
                PercentEntryRow(right, "SCT_Settings_ReadinessPercent".Translate(),
                    ref Settings.readinessPercent, ref _readinessBuf, 25, 100);

            right.GapLine();

            // Default arrival mode cycle button
            string modeLabel = Settings.defaultMode switch
            {
                CaravanMode.PushOn      => "SCT_Mode_PushOn".Translate(),
                CaravanMode.ArriveReady => "SCT_Mode_ArriveReady".Translate(),
                _                       => "SCT_Mode_Normal".Translate()
            };
            if (right.ButtonTextLabeled("SCT_Settings_DefaultMode".Translate(), modeLabel))
                Settings.defaultMode = (CaravanMode)(((int)Settings.defaultMode + 1) % 3);

            right.GapLine();

            // ── Rest schedule section ──────────────────────────────────
            SectionHeader(right, "SCT_Settings_Header_RestSchedule".Translate());

            // Global vanilla window override — always active
            right.Label("SCT_Settings_VanillaRestWindow".Translate());
            HourEntryRow(right, "SCT_Settings_VanillaRestStart".Translate(),
                ref Settings.vanillaRestStart, ref _vanillaRestStartBuf);
            HourEntryRow(right, "SCT_Settings_VanillaRestEnd".Translate(),
                ref Settings.vanillaRestEnd, ref _vanillaRestEndBuf);

            right.Gap();

            // Per-caravan gizmo toggle
            right.CheckboxLabeled("SCT_Settings_EnableRestSchedule".Translate(), ref Settings.enableRestSchedule);

            using (new DisabledBlock(!Settings.enableRestSchedule))
            {
                // Altered schedule window
                HourEntryRow(right, "SCT_Settings_AlteredRestStart".Translate(),
                    ref Settings.alteredRestStart, ref _alteredRestStartBuf);
                HourEntryRow(right, "SCT_Settings_AlteredRestEnd".Translate(),
                    ref Settings.alteredRestEnd, ref _alteredRestEndBuf);

                right.Gap();

                // Default rest schedule mode
                string schedLabel = Settings.defaultRestSchedule switch
                {
                    RestScheduleMode.AlteredSchedule => "SCT_RestMode_Altered".Translate(),
                    RestScheduleMode.NoResting        => "SCT_RestMode_NoResting".Translate(),
                    _                                 => "SCT_RestMode_Normal".Translate()
                };
                if (right.ButtonTextLabeled("SCT_Settings_DefaultRestSchedule".Translate(), schedLabel))
                    Settings.defaultRestSchedule = (RestScheduleMode)(((int)Settings.defaultRestSchedule + 1) % 3);
            }

            right.End();
        }

        // ── UI helpers ────────────────────────────────────────────────

        private static void SectionHeader(Listing_Standard listing, string text)
        {
            GUI.color = new Color(0.8f, 0.85f, 1f);
            listing.Label(text);
            GUI.color = Color.white;
        }

        private static void FloatEntryRow(Listing_Standard listing, string label, string unit,
            ref float value, ref string buffer, float min, float max)
        {
            Rect row      = listing.GetRect(Text.LineHeight);
            float boxW    = 52f;
            Rect labelR   = new Rect(row.x, row.y, row.width - boxW - 4f, row.height);
            Rect boxR     = new Rect(row.xMax - boxW, row.y, boxW, row.height);
            Rect unitR    = new Rect(boxR.xMax + 2f, row.y, 20f, row.height);

            Widgets.Label(labelR, label);
            buffer = Widgets.TextField(boxR, buffer);
            if (float.TryParse(buffer, out float parsed))
                value = Mathf.Clamp(parsed, min, max);

            GUI.color = Color.gray;
            Widgets.Label(unitR, unit);
            GUI.color = Color.white;
            listing.Gap(2f);
        }

        private static void PercentEntryRow(Listing_Standard listing, string label,
            ref float value, ref string buffer, int minPct, int maxPct)
        {
            Rect row    = listing.GetRect(Text.LineHeight);
            float boxW  = 44f;
            Rect labelR = new Rect(row.x, row.y, row.width - boxW - 14f, row.height);
            Rect boxR   = new Rect(row.xMax - boxW - 12f, row.y, boxW, row.height);
            Rect pctR   = new Rect(boxR.xMax + 2f, row.y, 12f, row.height);

            Widgets.Label(labelR, label);
            buffer = Widgets.TextField(boxR, buffer);
            if (int.TryParse(buffer, out int parsed))
                value = Mathf.Clamp(parsed, minPct, maxPct) / 100f;

            GUI.color = Color.gray;
            Widgets.Label(pctR, "%");
            GUI.color = Color.white;
            listing.Gap(2f);
        }

        private static void HourEntryRow(Listing_Standard listing, string label,
            ref float value, ref string buffer)
        {
            Rect row       = listing.GetRect(Text.LineHeight);
            float boxW     = 36f;
            float ampmW    = 52f;
            Rect labelR    = new Rect(row.x, row.y, row.width - boxW - ampmW - 8f, row.height);
            Rect boxR      = new Rect(row.xMax - boxW - ampmW - 4f, row.y, boxW, row.height);
            Rect ampmR     = new Rect(boxR.xMax + 4f, row.y, ampmW, row.height);

            Widgets.Label(labelR, label);
            buffer = Widgets.TextField(boxR, buffer);
            if (int.TryParse(buffer, out int parsed))
                value = Mathf.Clamp(parsed, 0, 23);

            GUI.color = Color.gray;
            Widgets.Label(ampmR, FormatHour(value));
            GUI.color = Color.white;
            listing.Gap(2f);
        }

        public static string FormatHour(float hour)
        {
            int h = Mathf.FloorToInt(hour);
            if (h == 0)  return "12 AM";
            if (h < 12)  return h + " AM";
            if (h == 12) return "12 PM";
            return (h - 12) + " PM";
        }

        private readonly struct DisabledBlock : System.IDisposable
        {
            private readonly bool _prev;
            public DisabledBlock(bool disable) { _prev = GUI.enabled; if (disable) GUI.enabled = false; }
            public void Dispose() { GUI.enabled = _prev; }
        }
    }
}
