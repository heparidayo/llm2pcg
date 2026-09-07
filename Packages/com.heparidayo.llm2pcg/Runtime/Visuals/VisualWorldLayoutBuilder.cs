using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Rendering;
using Llm2Pcg.Generators.Nature;
using UnityEngine;

namespace Llm2Pcg.Visuals
{
    public readonly struct ForestTreeCollision
    {
        public readonly float X;
        public readonly float Z;
        public readonly float Radius;

        public ForestTreeCollision(float x, float z, float radius) { X = x; Z = z; Radius = radius; }
    }

    /// <summary>Builds visual-only placements from immutable generated world data.</summary>
    public static class VisualWorldLayoutBuilder
    {
        private const int ForestStartExitClearanceSquared = 4;
        private const int ForestDecorationSafetyRadius = 3;
        private const int ForestTreeClearanceRadius = 2;
        private const int ForestRockMaximum = 96;
        private const int ForestBushMaximum = 128;
        private const int ForestGroundDetailMaximum = 384;
        private const int CaveRockDetailMaximum = 192;
        private const int CaveCrystalMaximum = 64;
        // Real stalagmite clusters are now available. Keep them sparse and near solid cave walls
        // so they decorate rather than visually fill the traversable tunnel.
        private const int CaveGroundDetailMaximum = 48;

        public static List<ResolvedVisualPlacement> BuildForestTrees(BiomeWorldData world, BiomeVisualProfile profile)
        {
            return BuildForestTrees(world, profile, null);
        }

        public static List<ResolvedVisualPlacement> BuildForestTrees(BiomeWorldData world, BiomeVisualProfile profile, VisualSettings visualSettings)
        {
            List<ResolvedVisualPlacement> result = new List<ResolvedVisualPlacement>();
            if (world == null || profile == null || !string.Equals(world.WorldType, PCGRequest.ForestWorldType, StringComparison.Ordinal)) return result;
            VisualCategorySettings settings = visualSettings?.trees;
            for (int index = 0; index < world.Props.Count; index++)
            {
                Int2 tree = world.Props[index];
                if (!world.IsInBounds(tree.X, tree.Y) || world.Tiles[tree.Y * world.Width + tree.X] != BiomeTile.Ground) continue;
                if (SquaredDistance(tree, world.StartPosition) <= ForestStartExitClearanceSquared || SquaredDistance(tree, world.ExitPosition) <= ForestStartExitClearanceSquared) continue;
                if (ReachedLimit(settings, result.Count) || !PassesDensity(settings, world.Seed, tree.X, tree.Y, 3440)) continue;
                Vector3 position = new Vector3(tree.X, world.GetSurfaceHeight(tree.X, tree.Y), tree.Y);
                if (VisualVariantResolver.TryResolve(profile, VisualCategoryIds.ForestTrees, world.Seed, tree.X, tree.Y, index, position, Quaternion.identity, Vector3.one, AllowedTypes(settings), out ResolvedVisualPlacement placement))
                    result.Add(placement);
            }
            return result;
        }

        /// <summary>
        /// Builds a bounded, visual-only decoration layer. Stable row-major traversal and stage-derived
        /// cell hashes make this independent from collection ordering and leave BiomeWorldData untouched.
        /// </summary>
        public static List<ResolvedVisualPlacement> BuildForestDecorations(BiomeWorldData world, BiomeVisualProfile profile)
        {
            return BuildForestDecorations(world, profile, null);
        }

        public static List<ResolvedVisualPlacement> BuildForestDecorations(BiomeWorldData world, BiomeVisualProfile profile, VisualSettings visualSettings)
        {
            List<ResolvedVisualPlacement> result = new List<ResolvedVisualPlacement>();
            if (world == null || profile == null || !string.Equals(world.WorldType, PCGRequest.ForestWorldType, StringComparison.Ordinal)) return result;

            bool[] treeClearance = BuildForestTreeClearance(world, ForestTreeClearanceRadius);
            bool[] occupied = new bool[world.Tiles.Length];
            int rockCount = 0, bushCount = 0, detailCount = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                int tileIndex = y * world.Width + x;
                if (world.Tiles[tileIndex] != BiomeTile.Ground || treeClearance[tileIndex]) continue;
                if (IsNear(x, y, world.StartPosition, ForestDecorationSafetyRadius) || IsNear(x, y, world.ExitPosition, ForestDecorationSafetyRadius)) continue;
                if (HasPathOrWaterNeighbour(world, x, y)) continue;

                float slope = MaximumNeighbourHeightDelta(world, x, y);
                string categoryId = null;
                Vector3 targetSize = Vector3.one;
                int stage = 0;
                if (rockCount < ForestRockMaximum && slope <= .75f && StableCell(world.Seed, x, y, 3410) % 127u == 0u)
                {
                    categoryId = VisualCategoryIds.ForestRocks; targetSize = Vector3.one * .72f; stage = 3410;
                }
                else if (bushCount < ForestBushMaximum && slope <= 1f && StableCell(world.Seed, x, y, 3420) % 79u == 0u)
                {
                    categoryId = VisualCategoryIds.ForestBushes; targetSize = Vector3.one * .68f; stage = 3420;
                }
                else if (detailCount < ForestGroundDetailMaximum && slope <= .75f && StableCell(world.Seed, x, y, 3430) % 23u == 0u)
                {
                    categoryId = VisualCategoryIds.ForestGroundDetails; targetSize = Vector3.one * .58f; stage = 3430;
                }
                if (categoryId == null || occupied[tileIndex]) continue;

                VisualCategorySettings settings = ForestDecorationSettings(visualSettings, categoryId);
                int categoryCount = categoryId == VisualCategoryIds.ForestRocks ? rockCount : categoryId == VisualCategoryIds.ForestBushes ? bushCount : detailCount;
                if (ReachedLimit(settings, categoryCount) || !PassesDensity(settings, world.Seed, x, y, stage + 900)) continue;

                Vector3 position = new Vector3(x, world.GetSurfaceHeight(x, y), y);
                if (!VisualVariantResolver.TryResolve(profile, categoryId, world.Seed, x, y, tileIndex + stage, position, Quaternion.identity, targetSize, AllowedTypes(settings), out ResolvedVisualPlacement placement)) continue;
                result.Add(placement);
                occupied[tileIndex] = true;
                if (categoryId == VisualCategoryIds.ForestRocks) rockCount++;
                else if (categoryId == VisualCategoryIds.ForestBushes) bushCount++;
                else detailCount++;
            }
            return result;
        }

        public static List<ResolvedVisualPlacement> BuildForestWater(BiomeWorldData world, BiomeVisualProfile profile)
        {
            List<ResolvedVisualPlacement> result = new List<ResolvedVisualPlacement>();
            if (world == null || profile == null || !string.Equals(world.WorldType, PCGRequest.ForestWorldType, StringComparison.Ordinal)) return result;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                int tileIndex = y * world.Width + x;
                if (world.Tiles[tileIndex] != BiomeTile.Water) continue;
                Vector3 position = new Vector3(x, .08f, y);
                if (VisualVariantResolver.TryResolve(profile, VisualCategoryIds.ForestWater, world.Seed, x, y, tileIndex, position, Quaternion.identity, new Vector3(1f, .02f, 1f), out ResolvedVisualPlacement placement))
                    result.Add(placement);
            }
            return result;
        }

        public static List<ForestTreeCollision> BuildForestTreeCollisions(BiomeWorldData world, BiomeVisualProfile profile, VisualSettings visualSettings = null)
        {
            List<ForestTreeCollision> result = new List<ForestTreeCollision>();
            List<ResolvedVisualPlacement> trees = BuildForestTrees(world, profile, visualSettings);
            for (int index = 0; index < trees.Count; index++)
            {
                ResolvedVisualPlacement tree = trees[index];
                Vector3 position = tree.WorldMatrix.GetColumn(3);
                Vector3 scale = tree.WorldMatrix.lossyScale;
                float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float radius = Mathf.Max(.05f, tree.Variant.EffectiveCollisionRadius * horizontalScale);
                result.Add(new ForestTreeCollision(position.x, position.z, radius));
            }
            return result;
        }

        public static string NaturePrimaryCategory(string worldType)
        {
            if (worldType == PCGRequest.ForestWorldType) return VisualCategoryIds.ForestTrees;
            if (worldType == PCGRequest.SwampWorldType) return VisualCategoryIds.SwampTrees;
            if (worldType == PCGRequest.SnowfieldWorldType) return VisualCategoryIds.SnowfieldTrees;
            if (worldType == PCGRequest.DesertWorldType) return VisualCategoryIds.DesertCacti;
            return null;
        }

        public static string NatureWaterCategory(string worldType)
        {
            if (worldType == PCGRequest.ForestWorldType) return VisualCategoryIds.ForestWater;
            if (worldType == PCGRequest.SwampWorldType) return VisualCategoryIds.SwampWater;
            if (worldType == PCGRequest.SnowfieldWorldType) return VisualCategoryIds.SnowfieldIce;
            if (worldType == PCGRequest.DesertWorldType) return VisualCategoryIds.DesertWater;
            return null;
        }

        public static List<ResolvedVisualPlacement> BuildNaturePrimaryVegetation(BiomeWorldData world, BiomeVisualProfile profile)
        {
            return BuildNaturePrimaryVegetation(world, profile, null);
        }

        public static List<ResolvedVisualPlacement> BuildNaturePrimaryVegetation(BiomeWorldData world, BiomeVisualProfile profile, VisualSettings visualSettings)
        {
            List<ResolvedVisualPlacement> result = new List<ResolvedVisualPlacement>();
            if (world == null || profile == null || !NatureWorldTypes.IsNature(world.WorldType)) return result;
            string category = NaturePrimaryCategory(world.WorldType);
            int stage = 1160 + NatureWorldTypes.GetRequired(world.WorldType).StageOffset;
            VisualCategorySettings settings = visualSettings?.trees;
            for (int index = 0; index < world.Props.Count; index++)
            {
                Int2 point = world.Props[index];
                if (!world.IsInBounds(point.X, point.Y) || world.Tiles[point.Y * world.Width + point.X] != BiomeTile.Ground) continue;
                if (SquaredDistance(point, world.StartPosition) <= ForestStartExitClearanceSquared || SquaredDistance(point, world.ExitPosition) <= ForestStartExitClearanceSquared) continue;
                if (ReachedLimit(settings, result.Count) || !PassesDensity(settings, world.Seed, point.X, point.Y, stage + 900)) continue;
                Vector3 size = world.WorldType == PCGRequest.DesertWorldType ? Vector3.one * .82f : Vector3.one;
                Vector3 position = new Vector3(point.X, world.GetSurfaceHeight(point.X, point.Y), point.Y);
                if (VisualVariantResolver.TryResolve(profile, category, world.Seed, point.X, point.Y, index + stage, position, Quaternion.identity, size, AllowedTypes(settings), out ResolvedVisualPlacement placement)) result.Add(placement);
            }
            return result;
        }

        public static List<ResolvedVisualPlacement> BuildNatureDecorations(BiomeWorldData world, BiomeVisualProfile profile)
        {
            return BuildNatureDecorations(world, profile, null);
        }

        public static List<ResolvedVisualPlacement> BuildNatureDecorations(BiomeWorldData world, BiomeVisualProfile profile, VisualSettings visualSettings)
        {
            List<ResolvedVisualPlacement> result = new List<ResolvedVisualPlacement>();
            if (world == null || profile == null || !NatureWorldTypes.IsNature(world.WorldType)) return result;
            int offset = NatureWorldTypes.GetRequired(world.WorldType).StageOffset;
            bool[] primaryClearance = BuildForestTreeClearance(world, ForestTreeClearanceRadius);
            bool[] occupied = new bool[world.Tiles.Length];
            int firstCount = 0, secondCount = 0, detailCount = 0, waterPropCount = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                int tileIndex = y * world.Width + x;
                BiomeTile tile = world.Tiles[tileIndex];
                if (IsNear(x, y, world.StartPosition, ForestDecorationSafetyRadius) || IsNear(x, y, world.ExitPosition, ForestDecorationSafetyRadius)) continue;
                string category = null; Vector3 targetSize = Vector3.one; int stage = 0;
                if (tile == BiomeTile.Water)
                {
                    if (waterPropCount >= 96 || StableCell(world.Seed, x, y, 1190 + offset) % 109u != 0u) continue;
                    category = NatureWaterPropCategory(world.WorldType); targetSize = Vector3.one * .65f; stage = 1190 + offset;
                }
                else
                {
                    if (tile != BiomeTile.Ground || primaryClearance[tileIndex] || HasPathOrWaterNeighbour(world, x, y)) continue;
                    float slope = MaximumNeighbourHeightDelta(world, x, y);
                    if (firstCount < 96 && slope <= 1f && StableCell(world.Seed, x, y, 1170 + offset) % 113u == 0u) { category = NatureFirstDecorationCategory(world.WorldType); targetSize = Vector3.one * .7f; stage = 1170 + offset; }
                    else if (secondCount < 128 && slope <= 1f && StableCell(world.Seed, x, y, 1180 + offset) % 83u == 0u) { category = NatureSecondDecorationCategory(world.WorldType); targetSize = Vector3.one * .65f; stage = 1180 + offset; }
                    else if (detailCount < 384 && slope <= .75f && StableCell(world.Seed, x, y, 1185 + offset) % 29u == 0u) { category = NatureGroundDetailCategory(world.WorldType); targetSize = Vector3.one * .55f; stage = 1185 + offset; }
                }
                if (category == null || occupied[tileIndex]) continue;
                VisualCategorySettings settings = NatureDecorationSettings(visualSettings, category);
                int categoryCount = tile == BiomeTile.Water ? waterPropCount : category == NatureFirstDecorationCategory(world.WorldType) ? firstCount : category == NatureSecondDecorationCategory(world.WorldType) ? secondCount : detailCount;
                if (ReachedLimit(settings, categoryCount) || !PassesDensity(settings, world.Seed, x, y, stage + 900)) continue;
                float surface = tile == BiomeTile.Water ? .09f : world.GetSurfaceHeight(x, y);
                if (!VisualVariantResolver.TryResolve(profile, category, world.Seed, x, y, tileIndex + stage, new Vector3(x, surface, y), Quaternion.identity, targetSize, AllowedTypes(settings), out ResolvedVisualPlacement placement)) continue;
                result.Add(placement); occupied[tileIndex] = true;
                if (tile == BiomeTile.Water) waterPropCount++;
                else if (category == NatureFirstDecorationCategory(world.WorldType)) firstCount++;
                else if (category == NatureSecondDecorationCategory(world.WorldType)) secondCount++;
                else detailCount++;
            }
            return result;
        }

        public static List<ResolvedVisualPlacement> BuildNatureWater(BiomeWorldData world, BiomeVisualProfile profile)
        {
            List<ResolvedVisualPlacement> result = new List<ResolvedVisualPlacement>();
            if (world == null || profile == null || !NatureWorldTypes.IsNature(world.WorldType)) return result;
            string category = NatureWaterCategory(world.WorldType);
            int stage = 1160 + NatureWorldTypes.GetRequired(world.WorldType).StageOffset;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                int tileIndex = y * world.Width + x; if (world.Tiles[tileIndex] != BiomeTile.Water) continue;
                if (VisualVariantResolver.TryResolve(profile, category, world.Seed, x, y, tileIndex + stage, new Vector3(x, .08f, y), Quaternion.identity, new Vector3(1f,.02f,1f), out ResolvedVisualPlacement placement)) result.Add(placement);
            }
            return result;
        }

        public static List<ForestTreeCollision> BuildNatureTreeCollisions(BiomeWorldData world, BiomeVisualProfile profile, VisualSettings visualSettings = null)
        {
            List<ForestTreeCollision> result = new List<ForestTreeCollision>();
            List<ResolvedVisualPlacement> placements = world != null && world.WorldType == PCGRequest.ForestWorldType ? BuildForestTrees(world, profile, visualSettings) : BuildNaturePrimaryVegetation(world, profile, visualSettings);
            for (int index = 0; index < placements.Count; index++)
            {
                ResolvedVisualPlacement item = placements[index]; Vector3 position = item.WorldMatrix.GetColumn(3); Vector3 scale = item.WorldMatrix.lossyScale;
                result.Add(new ForestTreeCollision(position.x, position.z, Mathf.Max(.05f, item.Variant.EffectiveCollisionRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)))));
            }
            return result;
        }

        private static string NatureFirstDecorationCategory(string worldType) => worldType == PCGRequest.SwampWorldType ? VisualCategoryIds.SwampReeds : worldType == PCGRequest.SnowfieldWorldType ? VisualCategoryIds.SnowfieldRocks : VisualCategoryIds.DesertRocks;
        private static string NatureSecondDecorationCategory(string worldType) => worldType == PCGRequest.SwampWorldType ? VisualCategoryIds.SwampRocks : worldType == PCGRequest.SnowfieldWorldType ? VisualCategoryIds.SnowfieldIceProps : VisualCategoryIds.DesertRocks;
        private static string NatureGroundDetailCategory(string worldType) => worldType == PCGRequest.SwampWorldType ? VisualCategoryIds.SwampGroundDetails : worldType == PCGRequest.SnowfieldWorldType ? VisualCategoryIds.SnowfieldGroundDetails : VisualCategoryIds.DesertGroundDetails;
        private static string NatureWaterPropCategory(string worldType) => worldType == PCGRequest.SwampWorldType ? VisualCategoryIds.SwampWaterProps : worldType == PCGRequest.SnowfieldWorldType ? VisualCategoryIds.SnowfieldIceProps : VisualCategoryIds.DesertOasisProps;

        private static VisualCategorySettings ForestDecorationSettings(VisualSettings settings, string category)
        {
            if (settings == null) return null;
            if (category == VisualCategoryIds.ForestRocks) return settings.rocks;
            if (category == VisualCategoryIds.ForestBushes) return settings.bushes;
            return settings.groundDetails;
        }

        private static VisualCategorySettings NatureDecorationSettings(VisualSettings settings, string category)
        {
            if (settings == null) return null;
            if (category != null && (category.Contains("WaterProps") || category.Contains("OasisProps"))) return settings.waterProps;
            if (category != null && (category.Contains("Rocks") || category.Contains("IceProps"))) return settings.rocks;
            if (category != null && category.Contains("Reeds")) return settings.bushes;
            return settings.groundDetails;
        }

        private static bool ReachedLimit(VisualCategorySettings settings, int count) => settings != null && settings.maxCount > 0 && count >= settings.maxCount;
        private static string[] AllowedTypes(VisualCategorySettings settings) => settings == null ? null : settings.allowedTypes;
        private static bool PassesDensity(VisualCategorySettings settings, int seed, int x, int y, int stage)
        {
            if (settings == null || settings.density >= 1f) return true;
            if (settings.density <= 0f) return false;
            uint threshold = (uint)Math.Round(settings.density * uint.MaxValue);
            return StableCell(seed, x, y, stage) <= threshold;
        }

        public static List<ResolvedVisualPlacement> BuildDungeonProps(DungeonWorldData world, BiomeVisualProfile profile)
        {
            List<ResolvedVisualPlacement> result = new List<ResolvedVisualPlacement>();
            if (world == null || profile == null) return result;
            for (int index = 0; index < world.Props.Count; index++)
            {
                DungeonPropPlacement prop = world.Props[index];
                string categoryId = DungeonCategory(prop.Type);
                GetDungeonPropTransform(prop, out Vector3 position, out Quaternion rotation, out Vector3 scale);
                if (VisualVariantResolver.TryResolve(profile, categoryId, world.Seed, prop.Position.X, prop.Position.Y, index, position, rotation, scale, out ResolvedVisualPlacement placement))
                    result.Add(placement);
            }
            return result;
        }

        public static List<ResolvedVisualPlacement> BuildCaveDetails(CaveWorldData world, BiomeVisualProfile profile, float tileSize = 1f)
        {
            List<ResolvedVisualPlacement> result = new List<ResolvedVisualPlacement>();
            if (world == null || profile == null) return result;
            AddCaveEntrance(result, world, profile, world.StartPosition, 3230, tileSize);
            AddCaveEntrance(result, world, profile, world.ExitPosition, 3231, tileSize);
            int rockCount = 0, crystalCount = 0, groundDetailCount = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                bool walkable = world.IsWalkable(x, y);
                string categoryId = null;
                int stage = 0;
                Vector3 position = Vector3.zero, targetSize = Vector3.one;
                if (!walkable && rockCount < CaveRockDetailMaximum && HasWalkableNeighbour(world, x, y) && StableCell(world.Seed, x, y, 3200) % 7u == 0u)
                {
                    categoryId = VisualCategoryIds.CaveRocks;
                    stage = 0;
                    position = new Vector3(x * tileSize, CaveSurfaceMeshBuilder.SampleRockSurfaceHeight(world, x, y) - .12f, y * tileSize);
                    targetSize = Vector3.one * .85f * tileSize;
                }
                else if (walkable && crystalCount < CaveCrystalMaximum && !IsNear(x, y, world.StartPosition, 2) && !IsNear(x, y, world.ExitPosition, 2) && HasSolidNeighbour(world, x, y) && StableCell(world.Seed, x, y, 3210) % 9u == 0u)
                {
                    categoryId = VisualCategoryIds.CaveCrystals;
                    stage = 1;
                    position = new Vector3(x * tileSize, CaveSurfaceMeshBuilder.SampleSurfaceHeight(world, x, y) + .05f, y * tileSize);
                    // Root crystals in the recessed wall, away from corridor centerlines.
                    Vector3 wallDirection = !world.IsWalkable(x + 1, y) ? Vector3.right :
                        !world.IsWalkable(x - 1, y) ? Vector3.left : !world.IsWalkable(x, y + 1) ? Vector3.forward : Vector3.back;
                    position += wallDirection * (.54f * tileSize);
                    targetSize = new Vector3(.55f, 1.1f, .55f) * tileSize;
                }
                else if (walkable && groundDetailCount < CaveGroundDetailMaximum && !IsNear(x, y, world.StartPosition, 2) && !IsNear(x, y, world.ExitPosition, 2) && HasSolidNeighbour(world, x, y) && StableCell(world.Seed, x, y, 3220) % 17u == 0u)
                {
                    categoryId = VisualCategoryIds.CaveGroundDetails;
                    stage = 2;
                    position = new Vector3(x * tileSize, CaveSurfaceMeshBuilder.SampleSurfaceHeight(world, x, y), y * tileSize);
                    targetSize = Vector3.one * tileSize;
                }
                if (categoryId == null || !VisualVariantResolver.TryResolve(profile, categoryId, world.Seed, x, y, stage, position, Quaternion.identity, targetSize, out ResolvedVisualPlacement placement)) continue;
                result.Add(placement);
                if (categoryId == VisualCategoryIds.CaveRocks) rockCount++;
                else if (categoryId == VisualCategoryIds.CaveCrystals) crystalCount++;
                else groundDetailCount++;
            }
            return result;
        }

        private static void AddCaveEntrance(List<ResolvedVisualPlacement> result, CaveWorldData world, BiomeVisualProfile profile, Int2 point, int stage, float tileSize)
        {
            Vector3 forward = CaveWalkableDirection(world, point);
            Quaternion rotation = forward.sqrMagnitude < .5f ? Quaternion.identity : Quaternion.LookRotation(forward, Vector3.up);
            Vector3 position = new Vector3(point.X * tileSize, CaveSurfaceMeshBuilder.SampleSurfaceHeight(world, point.X, point.Y), point.Y * tileSize) + forward * (.65f * tileSize);
            if (VisualVariantResolver.TryResolve(profile, VisualCategoryIds.CaveEntrances, world.Seed, point.X, point.Y, stage, position, rotation, Vector3.one, out ResolvedVisualPlacement entrance))
                result.Add(entrance);
        }

        private static Vector3 CaveWalkableDirection(CaveWorldData world, Int2 point)
        {
            float centerX = (world.Width - 1) * .5f;
            float centerY = (world.Height - 1) * .5f;
            float toCenterX = centerX - point.X;
            float toCenterY = centerY - point.Y;
            float bestScore = float.NegativeInfinity;
            Vector3 best = Vector3.forward;
            if (world.IsWalkable(point.X + 1, point.Y) && toCenterX > bestScore) { bestScore = toCenterX; best = Vector3.right; }
            if (world.IsWalkable(point.X - 1, point.Y) && -toCenterX > bestScore) { bestScore = -toCenterX; best = Vector3.left; }
            if (world.IsWalkable(point.X, point.Y + 1) && toCenterY > bestScore) { bestScore = toCenterY; best = Vector3.forward; }
            if (world.IsWalkable(point.X, point.Y - 1) && -toCenterY > bestScore) best = Vector3.back;
            return best;
        }

        public static List<ResolvedVisualPlacement> BuildCityVisuals(BiomeWorldData world, BiomeVisualProfile profile, IReadOnlyList<CityStructurePlacement> structures)
        {
            List<ResolvedVisualPlacement> result = new List<ResolvedVisualPlacement>();
            if (world == null || profile == null || !string.Equals(world.WorldType, PCGRequest.CityWorldType, StringComparison.Ordinal)) return result;
            if (structures != null)
            {
                for (int index = 0; index < structures.Count; index++)
                {
                    CityStructurePlacement structure = structures[index];
                    float x = structure.Footprint.X + (structure.Footprint.Width - 1) * .5f;
                    float y = CitySurfaceMeshBuilder.SidewalkHeight;
                    float z = structure.Footprint.Y + (structure.Footprint.Height - 1) * .5f;
                    Vector3 targetSize = new Vector3(structure.Footprint.Width * .9f, structure.Height, structure.Footprint.Height * .9f);
                    if (VisualVariantResolver.TryResolve(profile, CityStructureExtractor.Category(structure.StructureClass), world.Seed, structure.Center.X, structure.Center.Y, index, new Vector3(x, y, z), Quaternion.identity, targetSize, out ResolvedVisualPlacement placement)) result.Add(placement);
                }
            }

            int semantic = structures == null ? 0 : structures.Count;
            bool district = profile.FindCategory(VisualCategoryIds.CityTrees) != null;
            int decorations = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                int tileIndex = y * world.Width + x;
                BiomeTile tile = world.Tiles[tileIndex];
                string categoryId = null;
                if (district)
                {
                    if (decorations >= 768 || IsNear(x, y, world.StartPosition, 3) || IsNear(x, y, world.ExitPosition, 3)) continue;
                    // A regular planting rhythm reads as designed streets, while the world seed still selects the buildings.
                    // A one-cell free neighbourhood keeps planters out of building footprints and narrow alleys.
                    if (tile == BiomeTile.Park && x % 4 == 2 && y % 4 == 2 && ClearCityPlanting(world, x, y, true))
                        categoryId = VisualCategoryIds.CityTrees;
                    else if (tile == BiomeTile.Ground && x % 6 == 1 && y % 6 == 1 && ClearCityPlanting(world, x, y, false))
                        categoryId = VisualCategoryIds.CityTrees;
                    else if (tile == BiomeTile.Ground && HasRoadNeighbour(world, x, y) && (x + y) % 7 == 0)
                        categoryId = VisualCategoryIds.CityStreetProps;
                    else if (tile == BiomeTile.Park && x % 4 == 0 && y % 4 == 0 && ClearCityPlanting(world, x, y, true))
                        categoryId = VisualCategoryIds.CityParkProps;
                    if (categoryId != null && VisualVariantResolver.TryResolve(profile, categoryId, world.Seed, x, y, tileIndex,
                        new Vector3(x, CitySurfaceMeshBuilder.HeightFor(tile), y), CityPropRotation(world, x, y),
                        Vector3.one * (categoryId == VisualCategoryIds.CityTrees ? 1.05f : .8f), out var districtProp))
                    { result.Add(districtProp); decorations++; }
                    continue;
                }
                if (tile == BiomeTile.Park && !IsNear(x, y, world.StartPosition, 2) && !IsNear(x, y, world.ExitPosition, 2) && StableCell(world.Seed, x, y, 3310) % 41u == 0u)
                    categoryId = VisualCategoryIds.CityParkProps;
                else if (tile == BiomeTile.Ground && HasRoadNeighbour(world, x, y) && StableCell(world.Seed, x, y, 3320) % 127u == 0u)
                    categoryId = VisualCategoryIds.CityStreetProps;
                if (categoryId != null && VisualVariantResolver.TryResolve(profile, categoryId, world.Seed, x, y, semantic, new Vector3(x, world.GetSurfaceHeight(x, y), y), Quaternion.identity, Vector3.one * .8f, out ResolvedVisualPlacement decoration))
                { result.Add(decoration); semantic++; }
            }
            return result;
        }

        private static bool ClearCityPlanting(BiomeWorldData world, int x, int y, bool park)
        {
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!world.IsInBounds(x + dx, y + dy)) return false;
                var tile = world.Tiles[(y + dy) * world.Width + x + dx];
                if (tile == BiomeTile.Building || tile == BiomeTile.Road || (park && tile != BiomeTile.Park)) return false;
            }
            return true;
        }

        private static Quaternion CityPropRotation(BiomeWorldData world, int x, int y)
        {
            // Authored lamp heads face -Z; turn them towards the adjacent road.
            if (IsTile(world, x + 1, y, BiomeTile.Road)) return Quaternion.Euler(0, -90, 0);
            if (IsTile(world, x - 1, y, BiomeTile.Road)) return Quaternion.Euler(0, 90, 0);
            if (IsTile(world, x, y + 1, BiomeTile.Road)) return Quaternion.Euler(0, 180, 0);
            return Quaternion.identity;
        }

        public static List<ForestTreeCollision> BuildCityPropCollisions(BiomeWorldData world, BiomeVisualProfile profile)
        {
            var collisions = new List<ForestTreeCollision>();
            // Match the full layout's semantic indices, including legacy catalogs.
            foreach (var prop in BuildCityVisuals(world, profile, CityStructureExtractor.Extract(world)))
            {
                if (prop.CategoryId.StartsWith("City/Buildings/", StringComparison.Ordinal)) continue;
                Vector3 p = prop.WorldMatrix.GetColumn(3);
                Vector3 scale = prop.WorldMatrix.lossyScale;
                collisions.Add(new ForestTreeCollision(p.x, p.z, prop.Variant.EffectiveCollisionRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))));
            }
            return collisions;
        }

        public static string DungeonCategory(DungeonPropType type)
        {
            if (type == DungeonPropType.Pillar) return VisualCategoryIds.DungeonPillars;
            if (type == DungeonPropType.Crate) return VisualCategoryIds.DungeonCrates;
            if (type == DungeonPropType.Crystal) return VisualCategoryIds.DungeonCrystals;
            return VisualCategoryIds.DungeonTorches;
        }

        public static void GetDungeonPropTransform(DungeonPropPlacement prop, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            float variation = StableVariation(prop.Position, prop.Type);
            position = new Vector3(prop.Position.X, .5f, prop.Position.Y);
            if (prop.Type == DungeonPropType.Pillar)
            {
                position.y = .9f; scale = new Vector3(.24f, 1.6f + variation * .45f, .24f); rotation = Quaternion.Euler(0f, variation * 360f, 0f);
            }
            else if (prop.Type == DungeonPropType.Crate)
            {
                position.y = .42f; float size = .58f + variation * .20f; scale = new Vector3(size, .64f + variation * .28f, size); rotation = Quaternion.Euler(0f, variation * 360f, 0f);
            }
            else if (prop.Type == DungeonPropType.Crystal)
            {
                position.y = .65f; float size = .30f + variation * .16f; scale = new Vector3(size, .9f + variation * .7f, size); rotation = Quaternion.Euler(0f, variation * 360f, 0f);
            }
            else
            {
                position = new Vector3(prop.Position.X + prop.WallNormal.X * .36f, 1.05f, prop.Position.Y + prop.WallNormal.Y * .36f);
                scale = new Vector3(.16f, .72f + variation * .24f, .16f);
                Vector3 forward = new Vector3(-prop.WallNormal.X, 0f, -prop.WallNormal.Y);
                rotation = forward.sqrMagnitude < .5f ? Quaternion.identity : Quaternion.LookRotation(forward);
            }
        }

        private static int SquaredDistance(Int2 first, Int2 second)
        {
            int x = first.X - second.X, y = first.Y - second.Y;
            return x * x + y * y;
        }

        private static bool HasSolidNeighbour(CaveWorldData world, int x, int y)
        {
            return !world.IsWalkable(x + 1, y) || !world.IsWalkable(x - 1, y) || !world.IsWalkable(x, y + 1) || !world.IsWalkable(x, y - 1);
        }

        private static bool HasWalkableNeighbour(CaveWorldData world, int x, int y)
        {
            return world.IsWalkable(x + 1, y) || world.IsWalkable(x - 1, y) || world.IsWalkable(x, y + 1) || world.IsWalkable(x, y - 1);
        }

        private static bool HasRoadNeighbour(BiomeWorldData world, int x, int y)
        {
            return IsTile(world, x + 1, y, BiomeTile.Road) || IsTile(world, x - 1, y, BiomeTile.Road) || IsTile(world, x, y + 1, BiomeTile.Road) || IsTile(world, x, y - 1, BiomeTile.Road);
        }

        private static bool[] BuildForestTreeClearance(BiomeWorldData world, int radius)
        {
            bool[] blocked = new bool[world.Tiles.Length];
            int radiusSquared = radius * radius;
            for (int index = 0; index < world.Props.Count; index++)
            {
                Int2 tree = world.Props[index];
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (offsetX * offsetX + offsetY * offsetY > radiusSquared) continue;
                    int x = tree.X + offsetX, y = tree.Y + offsetY;
                    if (world.IsInBounds(x, y)) blocked[y * world.Width + x] = true;
                }
            }
            return blocked;
        }

        private static bool HasPathOrWaterNeighbour(BiomeWorldData world, int x, int y)
        {
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                int sampleX = x + offsetX, sampleY = y + offsetY;
                if (!world.IsInBounds(sampleX, sampleY)) continue;
                BiomeTile tile = world.Tiles[sampleY * world.Width + sampleX];
                if (tile == BiomeTile.Path || tile == BiomeTile.Road || tile == BiomeTile.Water) return true;
            }
            return false;
        }

        private static float MaximumNeighbourHeightDelta(BiomeWorldData world, int x, int y)
        {
            float height = world.GetSurfaceHeight(x, y), maximum = 0f;
            if (world.IsInBounds(x + 1, y)) maximum = Mathf.Max(maximum, Mathf.Abs(height - world.GetSurfaceHeight(x + 1, y)));
            if (world.IsInBounds(x - 1, y)) maximum = Mathf.Max(maximum, Mathf.Abs(height - world.GetSurfaceHeight(x - 1, y)));
            if (world.IsInBounds(x, y + 1)) maximum = Mathf.Max(maximum, Mathf.Abs(height - world.GetSurfaceHeight(x, y + 1)));
            if (world.IsInBounds(x, y - 1)) maximum = Mathf.Max(maximum, Mathf.Abs(height - world.GetSurfaceHeight(x, y - 1)));
            return maximum;
        }

        private static bool IsTile(BiomeWorldData world, int x, int y, BiomeTile tile)
        {
            return world.IsInBounds(x, y) && world.Tiles[y * world.Width + x] == tile;
        }

        private static bool IsNear(int x, int y, Int2 point, int radius)
        {
            int dx = x - point.X, dy = y - point.Y;
            return dx * dx + dy * dy <= radius * radius;
        }

        private static float StableVariation(Int2 point, DungeonPropType type)
        {
            unchecked
            {
                uint value = (uint)(point.X * 73856093) ^ (uint)(point.Y * 19349663) ^ (uint)((int)type * 83492791);
                value ^= value >> 16;
                return (value & 1023u) / 1023f;
            }
        }

        private static uint StableCell(int seed, int x, int y, int stage)
        {
            unchecked
            {
                uint value = (uint)PcgSeed.Stage(seed, stage) ^ (uint)(x * 73856093) ^ (uint)(y * 19349663);
                value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; value ^= value >> 16;
                return value;
            }
        }
    }
}
