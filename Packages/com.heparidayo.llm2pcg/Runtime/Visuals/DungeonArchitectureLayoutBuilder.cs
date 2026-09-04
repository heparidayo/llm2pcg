using System;
using System.Collections.Generic;
using Llm2Pcg.Core;
using Llm2Pcg.Rendering;
using UnityEngine;

namespace Llm2Pcg.Visuals
{
    /// <summary>Creates stable GPU-instanced placements for optional modular Dungeon architecture assets.</summary>
    public static class DungeonArchitectureLayoutBuilder
    {
        public static List<ResolvedVisualPlacement> Build(DungeonWorldData world, BiomeVisualProfile profile, float tileSize, float wallHeight)
        {
            List<ResolvedVisualPlacement> result = new List<ResolvedVisualPlacement>();
            if (world == null || profile == null) return result;

            bool roomFloors = HasVariants(profile, VisualCategoryIds.DungeonRoomFloors);
            bool corridorFloors = HasVariants(profile, VisualCategoryIds.DungeonCorridorFloors);
            int semantic = 0;
            if (roomFloors || corridorFloors)
                for (int y = 0; y < world.Height; y++)
                for (int x = 0; x < world.Width; x++)
                {
                    DungeonCell cell = world.GetCell(x, y);
                    string category = cell == DungeonCell.Floor && roomFloors ? VisualCategoryIds.DungeonRoomFloors :
                        cell == DungeonCell.Corridor && corridorFloors ? VisualCategoryIds.DungeonCorridorFloors : null;
                    if (category == null) continue;
                    Vector3 position = new Vector3(x * tileSize, 0f, y * tileSize);
                    if (VisualVariantResolver.TryResolve(profile, category, world.Seed, x, y, semantic++, position, Quaternion.identity, new Vector3(tileSize, .08f * tileSize, tileSize), out ResolvedVisualPlacement placement))
                        result.Add(placement);
                }

            List<DungeonBoundarySegment> boundaries = DungeonSurfaceMeshBuilder.GetBoundarySegments(world);
            bool walls = HasVariants(profile, VisualCategoryIds.DungeonStraightWalls);
            bool trims = HasVariants(profile, VisualCategoryIds.DungeonWallTrims);
            for (int index = 0; index < boundaries.Count; index++)
            {
                DungeonBoundarySegment segment = boundaries[index];
                Vector3 center = segment.Center(tileSize);
                if (walls && VisualVariantResolver.TryResolve(profile, VisualCategoryIds.DungeonStraightWalls, world.Seed, segment.CellX, segment.CellY, segment.Direction, center, segment.Rotation, new Vector3(tileSize * 1.025f, wallHeight, DungeonSurfaceMeshBuilder.WallThickness * tileSize), out ResolvedVisualPlacement wall))
                    result.Add(wall);
                if (trims)
                {
                    Vector3 trimPosition = center + Vector3.up * (wallHeight - DungeonSurfaceMeshBuilder.TrimHeight * tileSize);
                    if (VisualVariantResolver.TryResolve(profile, VisualCategoryIds.DungeonWallTrims, world.Seed, segment.CellX, segment.CellY, segment.Direction, trimPosition, segment.Rotation, new Vector3(tileSize * 1.065f, DungeonSurfaceMeshBuilder.TrimHeight * tileSize, DungeonSurfaceMeshBuilder.TrimThickness * tileSize), out ResolvedVisualPlacement trim))
                        result.Add(trim);
                }
            }

            if (HasVariants(profile, VisualCategoryIds.DungeonWallCorners))
            {
                List<DungeonCornerPoint> corners = DungeonSurfaceMeshBuilder.GetCornerPoints(boundaries);
                for (int index = 0; index < corners.Count; index++)
                {
                    DungeonCornerPoint corner = corners[index];
                    Vector3 size = new Vector3(DungeonSurfaceMeshBuilder.TrimThickness * tileSize, wallHeight, DungeonSurfaceMeshBuilder.TrimThickness * tileSize);
                    if (VisualVariantResolver.TryResolve(profile, VisualCategoryIds.DungeonWallCorners, world.Seed, corner.GridX, corner.GridY, index, corner.Center(tileSize), Quaternion.identity, size, out ResolvedVisualPlacement placement))
                        result.Add(placement);
                }
            }

            if (HasVariants(profile, VisualCategoryIds.DungeonDoorways))
            {
                List<DungeonDoorwaySegment> doorways = DungeonSurfaceMeshBuilder.GetDoorwaySegments(world);
                for (int index = 0; index < doorways.Count; index++)
                {
                    DungeonDoorwaySegment doorway = doorways[index];
                    if (VisualVariantResolver.TryResolve(profile, VisualCategoryIds.DungeonDoorways, world.Seed, doorway.CellX, doorway.CellY, index, doorway.Center(tileSize * .5f), doorway.Rotation, new Vector3(tileSize, wallHeight, tileSize * .18f), out ResolvedVisualPlacement placement))
                        result.Add(placement);
                }
            }

            return result;
        }

        public static bool HasVariants(BiomeVisualProfile profile, string categoryId)
        {
            return profile != null && VisualVariantResolver.GetSortedValidVariants(profile.FindCategory(categoryId)).Count > 0;
        }
    }
}
