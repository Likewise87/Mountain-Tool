using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MountainTool
{
    public static class MountainToolOps
    {
        private static readonly string[] RockKinds =
        {
            "Marble",
            "Granite",
            "Slate",
            "Sandstone",
            "Limestone"
        };

        private static readonly IntVec3[] NeighborOffsets =
        {
            new IntVec3(-1, 0, -1), new IntVec3(0, 0, -1), new IntVec3(1, 0, -1),
            new IntVec3(-1, 0, 0),                         new IntVec3(1, 0, 0),
            new IntVec3(-1, 0, 1),  new IntVec3(0, 0, 1),  new IntVec3(1, 0, 1)
        };

        private const float NoiseAcceptMin = 0.15f;
        private const float NoiseAcceptMax = 0.85f;
        private const float PerlinScale = 0.18f;

        public static IEnumerable<ThingDef> GetPlaceableMountainDefs()
        {
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsPlaceableMountainDef)
                .OrderBy(d => d.building?.isResourceRock == true ? 1 : 0)
                .ThenBy(d => d.label);
        }

        public static void AddMountain(
            Map map,
            IReadOnlyCollection<IntVec3> cells,
            ThingDef? rockOverride,
            int fuzziness,
            float steelShare,
            int steelMinSize,
            float componentsShare,
            int componentsMinSize,
            IReadOnlyCollection<IntVec3>? solidCore = null,
            bool nukeEverything = false)
        {
            if (map == null || cells == null || cells.Count == 0) return;

            var mask = new HashSet<IntVec3>();
            foreach (IntVec3 cell in cells)
            {
                if (cell.InBounds(map))
                    mask.Add(cell);
            }

            if (mask.Count == 0) return;

            HashSet<IntVec3>? core = null;
            if (solidCore != null && solidCore.Count > 0)
            {
                core = new HashSet<IntVec3>();
                foreach (IntVec3 cell in solidCore)
                {
                    if (cell.InBounds(map))
                        core.Add(cell);
                }
            }

            fuzziness = Mathf.Clamp(fuzziness, 0, 5);
            steelShare = Mathf.Clamp01(steelShare);
            componentsShare = Mathf.Clamp01(componentsShare);
            steelMinSize = Mathf.Max(1, steelMinSize);
            componentsMinSize = Mathf.Max(1, componentsMinSize);

            ThingDef rock = rockOverride
                ?? FindDominantNaturalRock(map)
                ?? ThingDefOf.Granite;
            if (rock == null) return;

            ThingDef floorRock = ResolveFloorRock(map, rock);
            TerrainDef hewn = DefDatabase<TerrainDef>.GetNamedSilentFail(floorRock.defName + "_RoughHewn")
                ?? DefDatabase<TerrainDef>.GetNamedSilentFail(floorRock.defName + "_Rough");

            CellRect bounds = CellRect.FromCellList(mask);
            float seedX = (map.uniqueID * 0.137f) + (bounds.minX * 0.31f) + (bounds.minZ * 0.17f);
            float seedZ = (map.uniqueID * 0.271f) + (bounds.maxX * 0.19f) + (bounds.maxZ * 0.41f);

            var toPlace = new List<IntVec3>();
            foreach (IntVec3 cell in mask)
            {
                bool force = core != null && core.Contains(cell);
                if (!force && !ShouldPlaceMountainCell(cell, mask, fuzziness, seedX, seedZ)) continue;
                toPlace.Add(cell);
            }

            EvacuatePawnsFrom(map, mask);

            var placed = new List<IntVec3>();
            for (int i = 0; i < toPlace.Count; i++)
            {
                IntVec3 cell = toPlace[i];
                if (!TryPlaceMountainRock(map, cell, rock, nukeEverything)) continue;
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThick);
                if (hewn != null)
                    ApplyRoughHewnHalo(map, cell, hewn);
                placed.Add(cell);
            }

            int steelCells = 0;
            int componentCells = 0;
            if (placed.Count > 0)
            {
                ThingDef? steel = DefDatabase<ThingDef>.GetNamedSilentFail("MineableSteel");
                if (steel != null)
                    steelCells = TryPlaceOreShare(map, placed, steel, steelShare, steelMinSize);

                ThingDef? comps = DefDatabase<ThingDef>.GetNamedSilentFail("MineableComponentsIndustrial");
                if (comps != null)
                    componentCells = TryPlaceOreShare(map, placed, comps, componentsShare, componentsMinSize);
            }

            FogEnclosedRock(map, placed);

            Messages.Message(
                "MountainTool_MsgAdd".Translate(
                    placed.Count, rock.label, fuzziness, steelCells, componentCells),
                MessageTypeDefOf.TaskCompletion,
                historical: false);
        }

        public static void RemoveMountain(Map map, IReadOnlyCollection<IntVec3> cells, bool nukeEverything = false)
        {
            if (map == null || cells == null || cells.Count == 0) return;

            int cleared = 0;
            foreach (IntVec3 cell in cells)
            {
                if (!cell.InBounds(map)) continue;

                bool touched = false;
                if (nukeEverything)
                    touched |= NukeEverythingAt(map, cell);
                else
                    touched |= DestroyRockOrOreAt(map, cell);

                RoofDef? roof = map.roofGrid.RoofAt(cell);
                if (roof != null && roof.isNatural)
                {
                    map.roofGrid.SetRoof(cell, null);
                    touched = true;
                }

                if (map.fogGrid.IsFogged(cell))
                {
                    map.fogGrid.Unfog(cell);
                    touched = true;
                }

                if (TryStripRoughTerrain(map, cell))
                    touched = true;

                if (touched) cleared++;
            }

            Messages.Message(
                "MountainTool_MsgRemove".Translate(cleared),
                MessageTypeDefOf.TaskCompletion,
                historical: false);
        }

        public static HashSet<IntVec3> CellsFromRect(CellRect rect, Map map)
        {
            rect = rect.ClipInsideMap(map);
            var set = new HashSet<IntVec3>();
            foreach (IntVec3 cell in rect)
                set.Add(cell);
            return set;
        }

        public static HashSet<IntVec3> CellsFromPath(IReadOnlyList<IntVec3> points, int thickness, Map map)
        {
            var centerline = new HashSet<IntVec3>();
            if (points == null || points.Count == 0 || map == null)
                return centerline;

            thickness = Mathf.Clamp(thickness, 1, 10);
            for (int i = 0; i < points.Count; i++)
            {
                IntVec3 p = points[i];
                if (p.InBounds(map))
                    centerline.Add(p);
                if (i + 1 >= points.Count) continue;
                foreach (IntVec3 c in GenSight.PointsOnLineOfSight(points[i], points[i + 1]))
                {
                    if (c.InBounds(map))
                        centerline.Add(c);
                }
            }

            if (thickness <= 1)
                return centerline;

            int radius = thickness - 1;
            var dilated = new HashSet<IntVec3>();
            foreach (IntVec3 c in centerline)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) > radius) continue;
                        IntVec3 n = new IntVec3(c.x + dx, 0, c.z + dz);
                        if (n.InBounds(map))
                            dilated.Add(n);
                    }
                }
            }

            return dilated;
        }

        public static ThingDef? FindDominantNaturalRock(Map map)
        {
            if (map == null) return null;

            ThingDef? best = null;
            int bestN = 0;
            foreach (string kind in RockKinds)
            {
                ThingDef? rock = DefDatabase<ThingDef>.GetNamedSilentFail(kind);
                if (rock?.building == null || !rock.building.isNaturalRock) continue;
                int n = map.listerThings.ThingsOfDef(rock).Count;
                if (n > bestN)
                {
                    bestN = n;
                    best = rock;
                }
            }

            if (best != null) return best;

            foreach (ThingDef rock in Find.World.NaturalRockTypesIn(map.Tile))
            {
                if (rock?.building != null && rock.building.isNaturalRock && IsKnownRockKind(rock.defName))
                    return rock;
            }

            return null;
        }

        private static bool IsPlaceableMountainDef(ThingDef def)
        {
            if (def?.building == null) return false;
            if (def.thingClass != typeof(Mineable) && !typeof(Mineable).IsAssignableFrom(def.thingClass))
                return false;
            if (def.defName.StartsWith("Chunk")) return false;
            if (def.defName == "CollapsedRocks") return false;

            if (def.building.isNaturalRock) return true;
            if (def.building.isResourceRock) return true;
            return def.building.mineableThing != null;
        }

        private static ThingDef ResolveFloorRock(Map map, ThingDef placed)
        {
            TerrainDef direct = DefDatabase<TerrainDef>.GetNamedSilentFail(placed.defName + "_RoughHewn")
                ?? DefDatabase<TerrainDef>.GetNamedSilentFail(placed.defName + "_Rough");
            if (direct != null) return placed;

            return FindDominantNaturalRock(map) ?? ThingDefOf.Granite ?? placed;
        }

        private static bool ShouldPlaceMountainCell(
            IntVec3 cell, HashSet<IntVec3> mask, int fuzziness, float seedX, float seedZ)
        {
            if (fuzziness <= 0) return true;

            int edgeDist = MaskEdgeDistance(cell, mask, fuzziness);
            float edgeFactor = Mathf.Clamp01(edgeDist / (float)fuzziness);
            float accept = Mathf.Lerp(NoiseAcceptMin, NoiseAcceptMax, edgeFactor);
            float noise = Mathf.PerlinNoise(
                (cell.x + seedX) * PerlinScale,
                (cell.z + seedZ) * PerlinScale);
            return noise < accept;
        }

        /// <summary>
        /// Chebyshev distance to exterior minus 1 (0 on the mask boundary), matching rect-edge semantics.
        /// </summary>
        private static int MaskEdgeDistance(IntVec3 cell, HashSet<IntVec3> mask, int maxUseful)
        {
            int limit = maxUseful + 1;
            int minToOutside = int.MaxValue;
            for (int dx = -limit; dx <= limit; dx++)
            {
                for (int dz = -limit; dz <= limit; dz++)
                {
                    int cheb = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
                    if (cheb == 0 || cheb >= minToOutside) continue;
                    IntVec3 n = new IntVec3(cell.x + dx, 0, cell.z + dz);
                    if (!mask.Contains(n))
                        minToOutside = cheb;
                }
            }

            if (minToOutside == int.MaxValue)
                return maxUseful;
            return Mathf.Max(0, minToOutside - 1);
        }

        /// <summary>
        /// Converts floor(placed × share) connected mountain cells to ore, but only if that count
        /// is at least <paramref name="minSize"/> (e.g. 10% share + min 5 needs ≥50 placed cells).
        /// </summary>
        private static int TryPlaceOreShare(
            Map map, List<IntVec3> placed, ThingDef ore, float share, int minSize)
        {
            if (share <= 0f || minSize < 1 || placed.Count == 0) return 0;

            int target = Mathf.FloorToInt(placed.Count * share);
            if (target < minSize) return 0;

            return TryPlaceOreVein(map, placed, ore, target);
        }

        private static int TryPlaceOreVein(Map map, List<IntVec3> placed, ThingDef ore, int targetSize)
        {
            if (placed.Count < targetSize || targetSize < 1) return 0;

            var placedSet = new HashSet<IntVec3>(placed);
            var available = new List<IntVec3>();
            foreach (IntVec3 cell in placed)
            {
                if (IsNaturalRockMineableCell(map, cell))
                    available.Add(cell);
            }

            if (available.Count < targetSize) return 0;

            available.Shuffle();
            foreach (IntVec3 seed in available)
            {
                List<IntVec3>? cluster = GrowConnectedCluster(seed, placedSet, map, targetSize);
                if (cluster == null || cluster.Count < targetSize) continue;

                int converted = 0;
                foreach (IntVec3 cell in cluster)
                {
                    if (ConvertCellToOre(map, cell, ore))
                        converted++;
                }

                return converted;
            }

            return 0;
        }

        private static List<IntVec3>? GrowConnectedCluster(
            IntVec3 seed, HashSet<IntVec3> placedSet, Map map, int targetSize)
        {
            if (!IsNaturalRockMineableCell(map, seed)) return null;

            var result = new List<IntVec3>();
            var visited = new HashSet<IntVec3>();
            var queue = new Queue<IntVec3>();
            queue.Enqueue(seed);
            visited.Add(seed);

            while (queue.Count > 0 && result.Count < targetSize)
            {
                IntVec3 cell = queue.Dequeue();
                if (!IsNaturalRockMineableCell(map, cell)) continue;
                result.Add(cell);

                for (int i = 0; i < 4; i++)
                {
                    IntVec3 n = cell + GenAdj.CardinalDirections[i];
                    if (!visited.Add(n)) continue;
                    if (!placedSet.Contains(n)) continue;
                    if (!IsNaturalRockMineableCell(map, n)) continue;
                    queue.Enqueue(n);
                }
            }

            return result.Count >= targetSize ? result : null;
        }

        private static bool IsNaturalRockMineableCell(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return false;
            Mineable? mineable = cell.GetFirstMineable(map);
            if (mineable?.def?.building == null) return false;
            if (mineable.def.defName.StartsWith("Chunk")) return false;
            if (mineable.def.building.isResourceRock) return false;
            return mineable.def.building.isNaturalRock;
        }

        private static bool ConvertCellToOre(Map map, IntVec3 cell, ThingDef ore)
        {
            if (!cell.InBounds(map)) return false;
            DestroyRockOrOreAt(map, cell);
            GenSpawn.Spawn(ore, cell, map, WipeMode.Vanish);
            map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThick);
            return true;
        }

        private static bool TryPlaceMountainRock(Map map, IntVec3 cell, ThingDef rock, bool nukeEverything)
        {
            if (!cell.InBounds(map)) return false;

            // Pawns should already be evacuated; move any stragglers just in case.
            EvacuatePawnsFromCell(map, cell, avoid: null);

            if (nukeEverything)
            {
                NukeEverythingAt(map, cell);
            }
            else
            {
                DestroyRockOrOreAt(map, cell);
                DestroyChunksAt(map, cell);

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t == null || t.Destroyed) continue;
                    if (t is Pawn) continue;
                    if (t.def.category == ThingCategory.Filth) continue;
                    if (t.def.category == ThingCategory.Plant) continue;
                    if (IsStoneChunk(t)) continue;
                    return false;
                }
            }

            GenSpawn.Spawn(rock, cell, map, WipeMode.Vanish);
            return true;
        }

        /// <summary>
        /// Removes all non-pawn things on the cell (chunks, geysers, ship parts, buildings, items, plants, …).
        /// Non-destroyable things (e.g. SteamGeyser) are DeSpawned instead of Destroyed.
        /// </summary>
        private static bool NukeEverythingAt(Map map, IntVec3 cell)
        {
            bool removed = false;
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (t == null || t.Destroyed) continue;
                if (t is Pawn) continue;
                if (ForceRemoveThing(t))
                    removed = true;
            }

            return removed;
        }

        private static bool ForceRemoveThing(Thing t)
        {
            if (t == null || t.Destroyed) return false;

            // SteamGeyser and similar: Destroy() errors when destroyable == false.
            if (t.def != null && !t.def.destroyable)
            {
                if (t.Spawned)
                    t.DeSpawn(DestroyMode.Vanish);
                return true;
            }

            t.Destroy(DestroyMode.Vanish);
            return true;
        }

        private static void EvacuatePawnsFrom(Map map, HashSet<IntVec3> cells)
        {
            if (cells == null || cells.Count == 0) return;

            var avoid = new HashSet<IntVec3>(cells);
            var pawns = new HashSet<Pawn>();
            foreach (IntVec3 cell in cells)
            {
                if (!cell.InBounds(map)) continue;
                List<Thing> things = cell.GetThingList(map);
                for (int t = 0; t < things.Count; t++)
                {
                    if (things[t] is Pawn pawn && pawn.Spawned)
                        pawns.Add(pawn);
                }
            }

            foreach (Pawn pawn in pawns)
                TeleportPawnOut(map, pawn, avoid);
        }

        private static void EvacuatePawnsFrom(Map map, List<IntVec3> cells)
        {
            if (cells == null || cells.Count == 0) return;
            EvacuatePawnsFrom(map, new HashSet<IntVec3>(cells));
        }

        private static void EvacuatePawnsFromCell(Map map, IntVec3 cell, HashSet<IntVec3>? avoid)
        {
            HashSet<IntVec3> avoidSet = avoid ?? new HashSet<IntVec3> { cell };
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (things[i] is Pawn pawn && pawn.Spawned)
                    TeleportPawnOut(map, pawn, avoidSet);
            }
        }

        private static void TeleportPawnOut(Map map, Pawn pawn, HashSet<IntVec3> avoid)
        {
            if (pawn == null || !pawn.Spawned) return;
            if (!avoid.Contains(pawn.Position)) return;

            IntVec3 root = pawn.Position;
            IntVec3 dest = IntVec3.Invalid;
            for (int radius = 4; radius <= 40; radius += 4)
            {
                if (CellFinder.TryFindRandomCellNear(
                    root,
                    map,
                    radius,
                    c => IsValidEvacuateCell(map, c, avoid),
                    out dest))
                {
                    break;
                }
            }

            if (!dest.IsValid)
            {
                // Last resort: any standable cell on the map outside the avoid set.
                if (!CellFinder.TryFindRandomCell(map, c => IsValidEvacuateCell(map, c, avoid), out dest))
                    return;
            }

            pawn.pather?.StopDead();
            pawn.Position = dest;
            pawn.Notify_Teleported(false, true);
        }

        private static bool IsValidEvacuateCell(Map map, IntVec3 c, HashSet<IntVec3> avoid)
        {
            if (!c.InBounds(map)) return false;
            if (avoid.Contains(c)) return false;
            if (!c.Standable(map)) return false;
            if (c.GetFirstPawn(map) != null) return false;
            if (c.GetEdifice(map) != null) return false;
            return true;
        }

        private static void DestroyChunksAt(Map map, IntVec3 cell)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (!IsStoneChunk(t)) continue;
                ForceRemoveThing(t);
            }
        }

        private static bool IsStoneChunk(Thing? t)
        {
            if (t?.def == null) return false;
            if (t.def.defName != null && t.def.defName.StartsWith("Chunk"))
                return true;
            if (ThingCategoryDefOf.StoneChunks != null && t.def.IsWithinCategory(ThingCategoryDefOf.StoneChunks))
                return true;
            return false;
        }

        private static bool DestroyRockOrOreAt(Map map, IntVec3 cell)
        {
            bool destroyed = false;
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (!IsRockOrOreMineable(t)) continue;
                if (ForceRemoveThing(t))
                    destroyed = true;
            }

            return destroyed;
        }

        private static bool IsRockOrOreMineable(Thing? t)
        {
            if (t?.def?.building == null) return false;
            if (t.def.defName.StartsWith("Chunk")) return false;
            if (!(t is Mineable)) return false;
            BuildingProperties building = t.def.building;
            if (building.isNaturalRock) return true;
            return building.mineableThing != null;
        }

        private static void ApplyRoughHewnHalo(Map map, IntVec3 center, TerrainDef hewn)
        {
            PaintRoughHewnIfFree(map, center, hewn);
            for (int i = 0; i < 8; i++)
                PaintRoughHewnIfFree(map, center + GenAdj.AdjacentCellsAround[i], hewn);
        }

        private static void PaintRoughHewnIfFree(Map map, IntVec3 cell, TerrainDef hewn)
        {
            if (!cell.InBounds(map)) return;
            TerrainDef current = cell.GetTerrain(map);
            if (current == null || current.IsWater) return;
            if (current.layerable) return;
            if (current != hewn)
                map.terrainGrid.SetTerrain(cell, hewn);
        }

        private static void FogEnclosedRock(Map map, List<IntVec3> placed)
        {
            if (map.fogGrid == null || placed == null) return;

            for (int i = 0; i < placed.Count; i++)
            {
                IntVec3 cell = placed[i];
                if (!cell.InBounds(map)) continue;
                if (!IsInteriorFogMineableCell(map, cell)) continue;
                if (!AllEightNeighborsAreRockOrOre(map, cell)) continue;
                if (map.fogGrid.IsFogged(cell)) continue;
                map.fogGrid.Refog(CellRect.SingleCell(cell));
            }
        }

        private static bool AllEightNeighborsAreRockOrOre(Map map, IntVec3 cell)
        {
            for (int i = 0; i < NeighborOffsets.Length; i++)
            {
                if (!IsInteriorFogMineableCell(map, cell + NeighborOffsets[i]))
                    return false;
            }

            return true;
        }

        private static bool IsInteriorFogMineableCell(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return false;
            Mineable? mineable = cell.GetFirstMineable(map);
            if (mineable?.def == null) return false;
            if (mineable.def.defName.StartsWith("Chunk")) return false;
            BuildingProperties? building = mineable.def.building;
            if (building == null) return false;
            if (building.isNaturalRock) return true;
            return building.mineableThing != null;
        }

        private static bool TryStripRoughTerrain(Map map, IntVec3 cell)
        {
            TerrainDef current = cell.GetTerrain(map);
            if (current == null) return false;
            if (!IsRoughStoneTerrain(current)) return false;
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            return true;
        }

        private static bool IsRoughStoneTerrain(TerrainDef terrain)
        {
            string name = terrain.defName;
            if (name.EndsWith("_RoughHewn") || name.EndsWith("_Rough"))
            {
                foreach (string kind in RockKinds)
                {
                    if (name.StartsWith(kind))
                        return true;
                }
            }

            return false;
        }

        private static bool IsKnownRockKind(string defName)
        {
            foreach (string kind in RockKinds)
            {
                if (defName == kind) return true;
            }

            return false;
        }
    }
}
