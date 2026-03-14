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
        public bool enableRec = false;          // set to true at first load if CR active
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

        // Internal: tracks whether we've ever saved, so we can set CR default on first load
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
            Scribe_Values.Look(ref initialised, "initialised", false);
            base.ExposeData();

            // On the very first load (no saved config yet), enable recreation if CR is active
            if (!initialised)
            {
                initialised = true;
                if (ModsConfig.IsActive("CheaterEater.CaravanRecreation"))
                    enableRec = true;
            }
        }
    }

    public class SCTMod : Mod
    {
        public static SCTSettings Settings;

        // Per-field buffer strings for text entry boxes
        private string _pushOnBuf;
        private string _prepareBuf;
        private string _restBuf;
        private string _foodBuf;
        private string _recBuf;
        private string _windowStartBuf;
        private string _windowEndBuf;
        private string _readinessBuf;

        public SCTMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<SCTSettings>();
        }

        public override string SettingsCategory() => "SCT_SettingsTitle".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Initialise buffers from current values on first draw
            _pushOnBuf      ??= Settings.pushOnHours.ToString("F1");
            _prepareBuf     ??= Settings.prepareHours.ToString("F1");
            _restBuf        ??= Mathf.RoundToInt(Settings.restThreshold * 100f).ToString();
            _foodBuf        ??= Mathf.RoundToInt(Settings.foodThreshold * 100f).ToString();
            _recBuf         ??= Mathf.RoundToInt(Settings.recThreshold * 100f).ToString();
            _windowStartBuf ??= Settings.arrivalWindowStart.ToString("F0");
            _windowEndBuf   ??= Settings.arrivalWindowEnd.ToString("F0");
            _readinessBuf   ??= Mathf.RoundToInt(Settings.readinessPercent * 100f).ToString();

            bool hasCaravanRec = ModsConfig.IsActive("CheaterEater.CaravanRecreation");

            // Split into two columns with a small gutter
            float gutter = 16f;
            float colWidth = (inRect.width - gutter) / 2f;
            Rect leftCol  = new Rect(inRect.x, inRect.y, colWidth, inRect.height);
            Rect rightCol = new Rect(inRect.x + colWidth + gutter, inRect.y, colWidth, inRect.height);

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

            // Sleep
            left.CheckboxLabeled("SCT_Settings_EnableSleep".Translate(), ref Settings.enableSleep);
            using (new DisabledBlock(!Settings.enableSleep))
                PercentEntryRow(left, "SCT_Settings_RestThreshold".Translate(),
                    ref Settings.restThreshold, ref _restBuf, 10, 100);

            left.Gap();

            // Food
            left.CheckboxLabeled("SCT_Settings_EnableFood".Translate(), ref Settings.enableFood);
            using (new DisabledBlock(!Settings.enableFood))
                PercentEntryRow(left, "SCT_Settings_FoodThreshold".Translate(),
                    ref Settings.foodThreshold, ref _foodBuf, 10, 100);

            left.Gap();

            // Recreation
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

            SectionHeader(right, "SCT_Settings_Header_Readiness".Translate());
            right.CheckboxLabeled("SCT_Settings_RequireAllPawns".Translate(), ref Settings.requireAllPawns);

            using (new DisabledBlock(Settings.requireAllPawns))
                PercentEntryRow(right, "SCT_Settings_ReadinessPercent".Translate(),
                    ref Settings.readinessPercent, ref _readinessBuf, 25, 100);

            right.GapLine();

            // Default mode cycle button
            string modeLabel = Settings.defaultMode switch
            {
                CaravanMode.PushOn      => "SCT_Mode_PushOn".Translate(),
                CaravanMode.ArriveReady => "SCT_Mode_ArriveReady".Translate(),
                _                       => "SCT_Mode_Normal".Translate()
            };
            if (right.ButtonTextLabeled("SCT_Settings_DefaultMode".Translate(), modeLabel))
                Settings.defaultMode = (CaravanMode)(((int)Settings.defaultMode + 1) % 3);

            right.End();
        }

        // ── UI helpers ────────────────────────────────────────────────

        private static void SectionHeader(Listing_Standard listing, string text)
        {
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.8f, 0.85f, 1f);
            listing.Label(text);
            GUI.color = Color.white;
        }

        /// <summary>One row: label on left, value+unit text box on right.</summary>
        private static void FloatEntryRow(Listing_Standard listing, string label, string unit,
            ref float value, ref string buffer, float min, float max)
        {
            Rect row = listing.GetRect(Text.LineHeight);
            float boxWidth = 52f;
            Rect labelRect = new Rect(row.x, row.y, row.width - boxWidth - 4f, row.height);
            Rect boxRect   = new Rect(row.xMax - boxWidth, row.y, boxWidth, row.height);

            Widgets.Label(labelRect, label);
            buffer = Widgets.TextField(boxRect, buffer);
            if (float.TryParse(buffer, out float parsed))
                value = Mathf.Clamp(parsed, min, max);

            // Append unit hint in grey to the right of the box — just nudge label
            // (unit shown inside the buffer string is fine; label suffix is cleaner)
            Rect unitRect = new Rect(boxRect.xMax + 2f, row.y, 20f, row.height);
            GUI.color = Color.gray;
            Widgets.Label(unitRect, unit);
            GUI.color = Color.white;

            listing.Gap(2f);
        }

        /// <summary>Percent row: value stored as 0–1 float, displayed/entered as 0–100 int.</summary>
        private static void PercentEntryRow(Listing_Standard listing, string label,
            ref float value, ref string buffer, int minPct, int maxPct)
        {
            Rect row = listing.GetRect(Text.LineHeight);
            float boxWidth = 44f;
            Rect labelRect = new Rect(row.x, row.y, row.width - boxWidth - 14f, row.height);
            Rect boxRect   = new Rect(row.xMax - boxWidth - 12f, row.y, boxWidth, row.height);
            Rect pctRect   = new Rect(boxRect.xMax + 2f, row.y, 12f, row.height);

            Widgets.Label(labelRect, label);
            buffer = Widgets.TextField(boxRect, buffer);
            if (int.TryParse(buffer, out int parsed))
                value = Mathf.Clamp(parsed, minPct, maxPct) / 100f;

            GUI.color = Color.gray;
            Widgets.Label(pctRect, "%");
            GUI.color = Color.white;

            listing.Gap(2f);
        }

        /// <summary>Hour row: 0–23, displays formatted AM/PM label alongside entry.</summary>
        private static void HourEntryRow(Listing_Standard listing, string label,
            ref float value, ref string buffer)
        {
            Rect row = listing.GetRect(Text.LineHeight);
            float boxWidth  = 36f;
            float ampmWidth = 52f;
            Rect labelRect = new Rect(row.x, row.y, row.width - boxWidth - ampmWidth - 8f, row.height);
            Rect boxRect   = new Rect(row.xMax - boxWidth - ampmWidth - 4f, row.y, boxWidth, row.height);
            Rect ampmRect  = new Rect(boxRect.xMax + 4f, row.y, ampmWidth, row.height);

            Widgets.Label(labelRect, label);
            buffer = Widgets.TextField(boxRect, buffer);
            if (int.TryParse(buffer, out int parsed))
                value = Mathf.Clamp(parsed, 0, 23);

            GUI.color = Color.gray;
            Widgets.Label(ampmRect, FormatHour(value));
            GUI.color = Color.white;

            listing.Gap(2f);
        }

        private static string FormatHour(float hour)
        {
            int h = Mathf.FloorToInt(hour);
            if (h == 0)  return "12 AM";
            if (h < 12)  return h + " AM";
            if (h == 12) return "12 PM";
            return (h - 12) + " PM";
        }

        /// <summary>RAII helper that sets GUI.enabled for a block and restores it.</summary>
        private readonly struct DisabledBlock : System.IDisposable
        {
            private readonly bool _prev;
            public DisabledBlock(bool disable) { _prev = GUI.enabled; if (disable) GUI.enabled = false; }
            public void Dispose() { GUI.enabled = _prev; }
        }
    }
}
