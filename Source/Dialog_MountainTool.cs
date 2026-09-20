using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MountainTool
{
    public class Dialog_MountainTool : Window
    {
        private Vector2 scrollPosition;

        private const float ScrollbarWidth = 16f;
        private const float ToolButtonHeight = 32f;

        // Session-persistent settings
        public static MountainToolEditMode EditMode = MountainToolEditMode.Add;
        public static ThingDef? RockDef;
        public static int Fuzziness = 3;
        public static float SteelShare = 0.10f;
        public static int SteelMinSize = 6;
        public static float ComponentsShare = 0.02f;
        public static int ComponentsMinSize = 3;
        public static MountainToolDrawKind DrawKind = MountainToolDrawKind.Rect;
        public static int PathThickness = 3;
        public static bool NukeEverything;

        public override Vector2 InitialSize
        {
            get
            {
                float h = Mathf.Min(720f, UI.screenHeight - 48f);
                return new Vector2(360f, h);
            }
        }

        public Dialog_MountainTool()
        {
            doCloseButton = true;
            doCloseX = true;
            draggable = true;
            absorbInputAroundWindow = false;
            forcePause = false;
            closeOnClickedOutside = false;
            preventCameraMotion = false;
            closeOnCancel = false;
            optionalTitle = null;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            MountainToolDraw.Arm(DrawKind);
        }

        public static void Open()
        {
            for (int i = Find.WindowStack.Windows.Count - 1; i >= 0; i--)
            {
                if (Find.WindowStack.Windows[i] is Dialog_MountainTool)
                    return;
            }

            Find.WindowStack.Add(new Dialog_MountainTool());
        }

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            float x = 12f;
            float y = 12f;
            if (y + size.y > UI.screenHeight - 8f)
                y = Mathf.Max(8f, UI.screenHeight - size.y - 8f);
            windowRect = new Rect(x, y, size.x, size.y).Rounded();
        }

        public override void PreClose()
        {
            MountainToolDraw.Disarm();
            base.PreClose();
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            Map? map = Find.CurrentMap;
            if (map != null && map.GetComponent<MountainToolMapComponent>() == null)
                map.components.Add(new MountainToolMapComponent(map));

            if (!MountainToolDraw.Armed || MountainToolDraw.Kind != DrawKind)
                MountainToolDraw.Arm(DrawKind);
            else
                MountainToolDraw.WindowUpdate();
        }

        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            MountainToolDraw.OnGUI();
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 32f), "MountainTool_Title".Translate());
            y += 36f;
            Text.Font = GameFont.Small;

            float bottomReserve = 44f;
            Rect scrollOut = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - bottomReserve);
            float viewH = EstimateHeight();
            Rect view = new Rect(0f, 0f, scrollOut.width - ScrollbarWidth - 4f, viewH);
            Widgets.BeginScrollView(scrollOut, ref scrollPosition, view);

            var listing = new Listing_Standard();
            listing.Begin(view);

            listing.Label("MountainTool_Mode".Translate());
            listing.Gap(2f);
            Rect modeRow = listing.GetRect(ToolButtonHeight);
            float half = (modeRow.width - 6f) / 2f;
            Rect addRect = new Rect(modeRow.x, modeRow.y, half, modeRow.height);
            Rect remRect = new Rect(modeRow.x + half + 6f, modeRow.y, half, modeRow.height);
            if (MountainToolUi.SlateButton(addRect, "MountainTool_Add".Translate(), EditMode == MountainToolEditMode.Add))
            {
                EditMode = MountainToolEditMode.Add;
                SoundDefOf.Click.PlayOneShotOnCamera();
                MountainToolDraw.Arm(DrawKind);
            }

            if (MountainToolUi.SlateButton(remRect, "MountainTool_Remove".Translate(), EditMode == MountainToolEditMode.Remove))
            {
                EditMode = MountainToolEditMode.Remove;
                SoundDefOf.Click.PlayOneShotOnCamera();
                MountainToolDraw.Arm(DrawKind);
            }

            listing.Gap(10f);

            if (EditMode == MountainToolEditMode.Add)
            {
                listing.Label("MountainTool_RockType".Translate());
                listing.Gap(2f);
                Rect rockRect = listing.GetRect(ToolButtonHeight);
                string rockLabel = RockDef?.LabelCap ?? "MountainTool_MostCommon".Translate();
                if (MountainToolUi.SlateButton(rockRect, rockLabel, selected: false, tip: "MountainTool_RockTip".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_MountainToolRockPicker(def => RockDef = def));
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                listing.Gap(8f);
                Fuzziness = Mathf.RoundToInt(MountainToolUi.StackedSlider(
                    listing, "MountainTool_Fuzziness".Translate(), Fuzziness, 0f, 5f,
                    "MountainTool_FuzzinessTip".Translate(),
                    roundTo: 1f, format: "0"));

                listing.Gap(4f);
                float steelPct = MountainToolUi.StackedSlider(
                    listing, "MountainTool_SteelShare".Translate(), SteelShare * 100f, 0f, 100f,
                    "MountainTool_SteelShareTip".Translate(),
                    roundTo: 1f, format: "0");
                SteelShare = steelPct / 100f;

                SteelMinSize = Mathf.RoundToInt(MountainToolUi.StackedSlider(
                    listing, "MountainTool_SteelMin".Translate(), SteelMinSize, 1f, 50f,
                    "MountainTool_SteelMinTip".Translate(),
                    roundTo: 1f, format: "0"));

                float compPct = MountainToolUi.StackedSlider(
                    listing, "MountainTool_ComponentsShare".Translate(), ComponentsShare * 100f, 0f, 100f,
                    "MountainTool_ComponentsShareTip".Translate(),
                    roundTo: 1f, format: "0");
                ComponentsShare = compPct / 100f;

                ComponentsMinSize = Mathf.RoundToInt(MountainToolUi.StackedSlider(
                    listing, "MountainTool_ComponentsMin".Translate(), ComponentsMinSize, 1f, 50f,
                    "MountainTool_ComponentsMinTip".Translate(),
                    roundTo: 1f, format: "0"));
            }

            listing.Gap(6f);
            bool nuke = NukeEverything;
            listing.CheckboxLabeled(
                "MountainTool_Nuke".Translate(),
                ref nuke,
                "MountainTool_NukeTip".Translate());
            NukeEverything = nuke;

            listing.Gap(6f);
            listing.Label("MountainTool_DrawTool".Translate());
            listing.Gap(2f);
            Rect toolRow = listing.GetRect(ToolButtonHeight);
            float toolHalf = (toolRow.width - 6f) / 2f;
            Rect rectBtn = new Rect(toolRow.x, toolRow.y, toolHalf, toolRow.height);
            Rect pathBtn = new Rect(toolRow.x + toolHalf + 6f, toolRow.y, toolHalf, toolRow.height);

            bool rectSelected = DrawKind == MountainToolDrawKind.Rect;
            bool pathSelected = DrawKind == MountainToolDrawKind.Path;

            if (MountainToolUi.SlateButton(rectBtn, "MountainTool_Rect".Translate(), rectSelected,
                "MountainTool_RectTip".Translate()))
            {
                DrawKind = MountainToolDrawKind.Rect;
                MountainToolDraw.Arm(MountainToolDrawKind.Rect);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (MountainToolUi.SlateButton(pathBtn, "MountainTool_Path".Translate(), pathSelected,
                "MountainTool_PathTip".Translate()))
            {
                DrawKind = MountainToolDrawKind.Path;
                MountainToolDraw.Arm(MountainToolDrawKind.Path);
                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            if (DrawKind == MountainToolDrawKind.Path)
            {
                listing.Gap(8f);
                PathThickness = Mathf.RoundToInt(MountainToolUi.StackedSlider(
                    listing, "MountainTool_PathThickness".Translate(), PathThickness, 1f, 10f,
                    "MountainTool_PathThicknessTip".Translate(),
                    roundTo: 1f, format: "0"));
            }

            listing.Gap(10f);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            if (DrawKind == MountainToolDrawKind.Rect)
                listing.Label("MountainTool_HelpRect".Translate());
            else
                listing.Label("MountainTool_HelpPath".Translate());
            listing.Label("MountainTool_HelpArmed".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.End();
            Widgets.EndScrollView();
        }

        private float EstimateHeight()
        {
            float h = 80f;
            if (EditMode == MountainToolEditMode.Add)
                h += 320f;
            h += 40f;
            h += 120f;
            if (DrawKind == MountainToolDrawKind.Path)
                h += 55f;
            return h;
        }
    }
}
