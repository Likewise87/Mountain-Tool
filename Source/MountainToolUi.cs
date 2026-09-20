using UnityEngine;
using Verse;

namespace MountainTool
{
    public static class MountainToolUi
    {
        public static readonly Color SlateFill = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        public static readonly Color SlateHover = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        public static readonly Color SlatePress = new Color(0.12f, 0.14f, 0.17f, 0.96f);
        public static readonly Color SlateSelected = new Color(0.26f, 0.32f, 0.40f, 0.98f);
        public static readonly Color Outline = new Color(0.55f, 0.62f, 0.72f, 0.42f);
        public static readonly Color OutlineHover = new Color(0.78f, 0.84f, 0.92f, 0.72f);
        public static readonly Color OutlineSelected = new Color(0.55f, 0.85f, 1f, 0.70f);
        public static readonly Color ValueCyan = new Color(0.55f, 0.90f, 1f);

        public static bool SlateButton(Rect rect, string label, bool selected, string? tip = null)
        {
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(rect, tip);

            bool hover = Mouse.IsOver(rect);
            bool pressed = hover && Input.GetMouseButton(0);
            Color fill = selected ? SlateSelected : pressed ? SlatePress : hover ? SlateHover : SlateFill;
            Color outline = selected ? OutlineSelected : hover ? OutlineHover : Outline;

            Widgets.DrawBoxSolid(rect, fill);
            GUI.color = outline;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;

            return Widgets.ButtonInvisible(rect);
        }

        public static float StackedSlider(
            Listing_Standard listing,
            string label,
            float value,
            float min,
            float max,
            string? tip = null,
            float roundTo = 1f,
            string format = "0")
        {
            Rect labelRect = listing.GetRect(22f);
            if (!tip.NullOrEmpty())
                TooltipHandler.TipRegion(labelRect, tip);

            string valueText = value.ToString(format);
            float valueW = Text.CalcSize(valueText).x + 4f;
            Rect left = labelRect.LeftPartPixels(labelRect.width - valueW);
            Rect right = labelRect.RightPartPixels(valueW);
            Widgets.Label(left, label);
            GUI.color = ValueCyan;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(right, valueText);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            Rect sliderRect = listing.GetRect(22f);
            float next = Widgets.HorizontalSlider(sliderRect, value, min, max, middleAlignment: false, label: null, leftAlignedLabel: null, rightAlignedLabel: null, roundTo: roundTo);
            listing.Gap(4f);
            return next;
        }
    }
}
