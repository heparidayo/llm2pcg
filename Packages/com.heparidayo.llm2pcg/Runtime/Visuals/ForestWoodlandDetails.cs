using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using UnityEngine;

namespace Llm2Pcg.Visuals
{
    /// <summary>Bounded water-only shoreline decoration. Never changes terrain or walkability.</summary>
    public static class ForestWoodlandDetails
    {
        public const string Category = "Forest/WaterProps";
        public const int Maximum = 96;
        public static List<ResolvedVisualPlacement> Build(BiomeWorldData world, BiomeVisualProfile profile, VisualSettings settings = null)
        {
            var result = new List<ResolvedVisualPlacement>();
            if (world == null || world.WorldType != "Forest" || profile == null || profile.profileId != "forest.woodland") return result;
            var rule = settings?.waterProps;
            int limit = rule != null && rule.maxCount > 0 ? Mathf.Min(Maximum,rule.maxCount) : Maximum;
            float density = rule == null ? 1f : Mathf.Clamp01(rule.density);
            for (int z=0;z<world.Height;z++)
            for (int x=0;x<world.Width;x++)
            {
                if (world.Tiles[z*world.Width+x] != BiomeTile.Water) continue;
                bool shore = false;
                foreach (var offset in new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down})
                {
                    int nx=x+offset.x,nz=z+offset.y;
                    if(world.IsInBounds(nx,nz)&&world.Tiles[nz*world.Width+nx]==BiomeTile.Ground){shore=true;break;}
                }
                if (!shore) continue;
                uint hash=unchecked((uint)(x*73856093 ^ z*19349663 ^ world.Seed*83492791));
                hash^=hash>>16;
                if(hash%5!=0 || ((hash>>8)&65535)/65536f>=density) continue;
                if(VisualVariantResolver.TryResolve(profile,Category,world.Seed,x,z,z*world.Width+x,
                    new Vector3(x,.09f,z),Quaternion.identity,Vector3.one*.8f,rule?.allowedTypes,out var p)) result.Add(p);
                if(result.Count>=limit) return result;
            }
            return result;
        }
    }
}
