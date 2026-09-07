using System.Collections.Generic;
using Llm2Pcg.Core;
using Llm2Pcg.Rendering;
using UnityEngine;

namespace Llm2Pcg.Visuals
{
    /// <summary>Wall-mounted presentation only. Does not add gameplay props or alter walkability.</summary>
    public static class DungeonCitadelDetails
    {
        public const int MaximumSconces = 96;
        public static List<ResolvedVisualPlacement> Build(DungeonWorldData world, BiomeVisualProfile profile, float tileSize)
        {
            var result = new List<ResolvedVisualPlacement>();
            if (world == null || profile == null || profile.profileId != "dungeon.citadel") return result;
            var boundaries = DungeonSurfaceMeshBuilder.GetBoundarySegments(world);
            foreach (var edge in boundaries)
            {
                Vector3 inward = edge.Direction == 0 ? Vector3.forward : edge.Direction == 1 ? Vector3.left :
                    edge.Direction == 2 ? Vector3.back : Vector3.right;
                Vector3 position = edge.Center(tileSize) + inward * (.20f * tileSize) + Vector3.up * 1.4f;
                bool occupied = false;
                foreach (var existing in result)
                    if ((position - (Vector3)existing.WorldMatrix.GetColumn(3)).sqrMagnitude < 36f * tileSize * tileSize) { occupied = true; break; }
                foreach (var prop in world.Props)
                    if (prop.Type == DungeonPropType.Torch &&
                        (new Vector2(prop.Position.X * tileSize - position.x, prop.Position.Y * tileSize - position.z)).sqrMagnitude < 4f * tileSize * tileSize) { occupied = true; break; }
                if (occupied) continue;
                if (VisualVariantResolver.TryResolve(profile, VisualCategoryIds.DungeonTorches, world.Seed,
                    edge.CellX, edge.CellY, edge.Direction, position, Quaternion.LookRotation(inward),
                    new Vector3(.24f * tileSize, .85f, .24f * tileSize), out var placement)) result.Add(placement);
                if (result.Count >= MaximumSconces) break;
            }
            return result;
        }
    }
}
