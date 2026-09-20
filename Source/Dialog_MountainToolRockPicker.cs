using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace MountainTool
{
    public class Dialog_MountainToolRockPicker : Window
    {
        private readonly System.Action<ThingDef?> onPicked;
        private string searchTerm = "";
        private Vector2 scroll;
        private List<ThingDef>? cachedDefs;

        private const float TitleH = 32f;
        private const float SearchH = 28f;
        private const float RowH = 28f;

        public override Vector2 InitialSize => new Vector2(360f, 520f);

        public Dialog_MountainToolRockPicker(System.Action<ThingDef?> onPicked)
        {
            this.onPicked = onPicked;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            draggable = true;
            forcePause = false;
            closeOnCancel = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, TitleH), "MountainTool_RockType".Translate());
            Text.Font = GameFont.Small;

            float y = TitleH + 4f;
            Rect searchRect = new Rect(0f, y, inRect.width, SearchH);
            string old = searchTerm;
            searchTerm = Widgets.TextField(searchRect, searchTerm);
            if (string.IsNullOrEmpty(searchTerm))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;
                Widgets.Label(searchRect, "  " + "MountainTool_Search".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            if (searchTerm != old)
                scroll = Vector2.zero;
            y += SearchH + 8f;

            cachedDefs ??= MountainToolOps.GetPlaceableMountainDefs().ToList();
            string mostCommon = "MountainTool_MostCommon".Translate();
            string? filter = string.IsNullOrWhiteSpace(searchTerm)
                ? null
                : searchTerm.Trim().ToLowerInvariant();

            var rows = new List<(string label, ThingDef? def)>();
            if (filter == null
                || mostCommon.ToLowerInvariant().Contains(filter)
                || "most common".Contains(filter))
            {
                rows.Add((mostCommon, null));
            }

            for (int i = 0; i < cachedDefs.Count; i++)
            {
                ThingDef def = cachedDefs[i];
                string label = def.LabelCap;
                if (filter != null
                    && (label == null || !label.ToLowerInvariant().Contains(filter))
                    && (def.defName == null || !def.defName.ToLowerInvariant().Contains(filter)))
                {
                    continue;
                }

                rows.Add((label ?? def.defName, def));
            }

            Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, rows.Count * RowH + 4f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float rowY = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                (string label, ThingDef? def) = rows[i];
                Rect row = new Rect(0f, rowY, viewRect.width, RowH - 2f);
                if (i % 2 == 0)
                    Widgets.DrawHighlight(row);

                bool selected = def == Dialog_MountainTool.RockDef
                    || (def == null && Dialog_MountainTool.RockDef == null);
                if (selected)
                    Widgets.DrawHighlightSelected(row);

                Rect iconRect = new Rect(row.x + 4f, row.y + 2f, 24f, 24f);
                if (def != null)
                    Widgets.DefIcon(iconRect, def);
                else
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(iconRect, "★");
                    Text.Anchor = TextAnchor.UpperLeft;
                }

                Rect labelRect = new Rect(iconRect.xMax + 6f, row.y, row.width - iconRect.width - 12f, row.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, label);
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonInvisible(row))
                {
                    onPicked?.Invoke(def);
                    Close();
                }

                rowY += RowH;
            }

            Widgets.EndScrollView();
        }
    }
}
