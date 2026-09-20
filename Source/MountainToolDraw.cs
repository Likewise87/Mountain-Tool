using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MountainTool
{
    public enum MountainToolDrawKind
    {
        Rect,
        Path
    }

    public enum MountainToolEditMode
    {
        Add,
        Remove
    }

    /// <summary>
    /// Map painting tool owned by <see cref="Dialog_MountainTool"/>.
    /// Left clicks go through a vanilla <see cref="DebugTool"/> label; RMB/Esc/preview are handled here.
    /// </summary>
    public static class MountainToolDraw
    {
        private static readonly Color PreviewEdge = new Color(0.45f, 0.85f, 1f, 0.85f);

        public static bool Armed { get; private set; }
        public static MountainToolDrawKind Kind { get; private set; }

        private static IntVec3? rectFirst;
        private static readonly List<IntVec3> pathPoints = new List<IntVec3>();
        private static DebugTool? installedTool;

        public static void Arm(MountainToolDrawKind kind)
        {
            bool keepPath = Armed && Kind == kind && kind == MountainToolDrawKind.Path;
            bool keepRect = Armed && Kind == kind && kind == MountainToolDrawKind.Rect && rectFirst.HasValue;
            IntVec3? keptRect = keepRect ? rectFirst : null;
            List<IntVec3>? keptPath = keepPath ? new List<IntVec3>(pathPoints) : null;

            Kind = kind;
            rectFirst = keptRect;
            pathPoints.Clear();
            if (keptPath != null)
                pathPoints.AddRange(keptPath);

            Armed = true;
            InstallDebugTool();
        }

        public static void Disarm()
        {
            Armed = false;
            rectFirst = null;
            pathPoints.Clear();
            if (DebugTools.curTool != null && ReferenceEquals(DebugTools.curTool, installedTool))
                DebugTools.curTool = null;
            installedTool = null;
        }

        public static void CancelTransient()
        {
            rectFirst = null;
            pathPoints.Clear();
            if (Armed)
                InstallDebugTool();
        }

        private static bool HasTransientSelection()
        {
            if (Kind == MountainToolDrawKind.Path)
                return pathPoints.Count > 0;
            return rectFirst.HasValue;
        }

        private static void PromptCloseMountainTool()
        {
            // Avoid stacking duplicate confirms.
            for (int i = 0; i < Find.WindowStack.Windows.Count; i++)
            {
                if (Find.WindowStack.Windows[i] is Dialog_MessageBox)
                    return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "MountainTool_CloseConfirm".Translate(),
                CloseMountainDialog,
                destructive: false));
        }

        private static void CloseMountainDialog()
        {
            for (int i = Find.WindowStack.Windows.Count - 1; i >= 0; i--)
            {
                if (Find.WindowStack.Windows[i] is Dialog_MountainTool dialog)
                {
                    dialog.Close(doCloseSound: true);
                    return;
                }
            }
        }

        public static void OnGUI()
        {
            if (!Armed) return;

            EnsureToolInstalled();
            if (!Armed) return;
            HandleRightClickAndEsc();
        }

        public static void WindowUpdate()
        {
            if (!Armed) return;
            EnsureToolInstalled();
        }

        /// <summary>Called from <see cref="MountainToolMapComponent"/> so overlays render in map space.</summary>
        public static void DrawWorldOverlays()
        {
            if (!Armed) return;
            if (Kind != MountainToolDrawKind.Path || pathPoints.Count == 0) return;

            Map? map = Find.CurrentMap;
            if (map == null) return;

            List<IntVec3> previewPts = new List<IntVec3>(pathPoints);
            IntVec3 mouse = UI.MouseCell();
            bool mousePreview = mouse.InBounds(map)
                && (previewPts.Count == 0 || previewPts[previewPts.Count - 1] != mouse);
            if (mousePreview)
                previewPts.Add(mouse);

            HashSet<IntVec3> cells = MountainToolOps.CellsFromPath(
                previewPts, Dialog_MountainTool.PathThickness, map);
            DrawCellSet(cells);
            DrawPathPolyline(previewPts, mousePreview);
        }

        private static void EnsureToolInstalled()
        {
            if (!Armed) return;

            // While the dialog owns us, reclaim if vanilla cleared us or another debug tool stole focus.
            if (DebugTools.curTool == null
                || installedTool == null
                || !ReferenceEquals(DebugTools.curTool, installedTool))
            {
                InstallDebugTool();
            }
        }

        private static void InstallDebugTool()
        {
            if (!Armed) return;

            DebugTool tool;
            if (Kind == MountainToolDrawKind.Rect && rectFirst.HasValue)
                tool = new DebugTool(BuildLabel(), OnLeftClick, rectFirst.Value);
            else
                tool = new DebugTool(BuildLabel(), OnLeftClick);

            installedTool = tool;
            DebugTools.curTool = tool;
        }

        private static string BuildLabel()
        {
            string mode = Dialog_MountainTool.EditMode == MountainToolEditMode.Add
                ? "MountainTool_Add".Translate()
                : "MountainTool_Remove".Translate();
            if (Kind == MountainToolDrawKind.Rect)
            {
                return rectFirst.HasValue
                    ? "MountainTool_ToolRectSecond".Translate(mode)
                    : "MountainTool_ToolRectFirst".Translate(mode);
            }

            return "MountainTool_ToolPath".Translate(mode, pathPoints.Count);
        }

        private static void OnLeftClick()
        {
            if (!Armed) return;
            if (MouseIsOverUi()) return;

            Map? map = Find.CurrentMap;
            if (map == null) return;

            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map)) return;

            if (Kind == MountainToolDrawKind.Rect)
                HandleRectClick(map, cell);
            else
                HandlePathClick(map, cell);
        }

        private static void HandleRectClick(Map map, IntVec3 cell)
        {
            if (!rectFirst.HasValue)
            {
                rectFirst = cell;
                SoundDefOf.Click.PlayOneShotOnCamera();
                InstallDebugTool();
                return;
            }

            CellRect rect = CellRect.FromLimits(rectFirst.Value, cell).ClipInsideMap(map);
            rectFirst = null;
            ApplyCells(map, MountainToolOps.CellsFromRect(rect, map), solidCore: null);
            InstallDebugTool();
        }

        private static void HandlePathClick(Map map, IntVec3 cell)
        {
            bool isDouble = Event.current != null && Event.current.clickCount >= 2;

            bool added = false;
            if (pathPoints.Count == 0 || pathPoints[pathPoints.Count - 1] != cell)
            {
                pathPoints.Add(cell);
                added = true;
            }

            if (added && !isDouble)
                SoundDefOf.Click.PlayOneShotOnCamera();

            if (isDouble)
            {
                if (pathPoints.Count >= 2)
                {
                    HashSet<IntVec3> centerline = MountainToolOps.CellsFromPath(pathPoints, 1, map);
                    HashSet<IntVec3> cells = MountainToolOps.CellsFromPath(
                        pathPoints, Dialog_MountainTool.PathThickness, map);
                    pathPoints.Clear();
                    ApplyCells(map, cells, centerline);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                InstallDebugTool();
                return;
            }

            InstallDebugTool();
        }

        private static void HandleRightClickAndEsc()
        {
            Event e = Event.current;
            if (e == null) return;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (HasTransientSelection())
                {
                    CancelTransient();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                else
                {
                    PromptCloseMountainTool();
                }

                e.Use();
                return;
            }

            if (e.type != EventType.MouseDown || e.button != 1) return;
            if (MouseIsOverUi()) return;

            if (Kind == MountainToolDrawKind.Rect)
            {
                if (rectFirst.HasValue)
                {
                    rectFirst = null;
                    InstallDebugTool();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                else
                {
                    PromptCloseMountainTool();
                }

                e.Use();
                return;
            }

            if (pathPoints.Count > 0)
            {
                pathPoints.RemoveAt(pathPoints.Count - 1);
                SoundDefOf.Click.PlayOneShotOnCamera();
                InstallDebugTool();
            }
            else
            {
                PromptCloseMountainTool();
            }

            e.Use();
        }

        private static void ApplyCells(Map map, HashSet<IntVec3> cells, HashSet<IntVec3>? solidCore)
        {
            if (cells == null || cells.Count == 0) return;

            if (Dialog_MountainTool.EditMode == MountainToolEditMode.Remove)
            {
                MountainToolOps.RemoveMountain(map, cells, Dialog_MountainTool.NukeEverything);
                return;
            }

            MountainToolOps.AddMountain(
                map,
                cells,
                Dialog_MountainTool.RockDef,
                Dialog_MountainTool.Fuzziness,
                Dialog_MountainTool.SteelShare,
                Dialog_MountainTool.SteelMinSize,
                Dialog_MountainTool.ComponentsShare,
                Dialog_MountainTool.ComponentsMinSize,
                solidCore,
                Dialog_MountainTool.NukeEverything);
        }

        private static void DrawPathPolyline(List<IntVec3> pts, bool lastSegmentIsGhost)
        {
            float alt = AltitudeLayer.MetaOverlays.AltitudeFor();

            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p = pts[i].ToVector3Shifted();
                p.y = alt;
                GenDraw.DrawCircleOutline(p, 0.45f, SimpleColor.Yellow);
                GenDraw.DrawCircleOutline(p, 0.2f, SimpleColor.Yellow);

                if (i + 1 >= pts.Count) continue;

                Vector3 a = pts[i].ToVector3Shifted();
                Vector3 b = pts[i + 1].ToVector3Shifted();
                a.y = b.y = alt;

                bool ghost = lastSegmentIsGhost && i + 1 == pts.Count - 1;
                GenDraw.DrawLineBetween(a, b, ghost ? SimpleColor.White : SimpleColor.Yellow);
            }
        }

        private static void DrawCellSet(HashSet<IntVec3> cells)
        {
            if (cells.Count == 0) return;
            GenDraw.DrawFieldEdges(new List<IntVec3>(cells), PreviewEdge);
        }

        private static bool MouseIsOverUi()
        {
            if (Find.WindowStack == null) return false;
            Window? under = Find.WindowStack.GetWindowAt(UI.MousePositionOnUIInverted);
            return under != null;
        }
    }
}
