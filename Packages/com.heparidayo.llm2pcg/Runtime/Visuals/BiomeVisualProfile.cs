using System;
using UnityEngine;

namespace Llm2Pcg.Visuals
{
    public enum VisualFitMode
    {
        Natural = 0,
        StretchToFootprint = 1
    }

    [Serializable]
    public sealed class VisualPlacementRules
    {
        public bool randomYaw = true;
        [Min(0f)] public float yawStep = 90f;
        public Vector2 uniformScaleRange = Vector2.one;
        public bool alignToSurfaceNormal;
        [Min(0f)] public float minimumClearance;
    }

    [Serializable]
    public sealed class VisualRenderPart
    {
        public string stableId;
        public Mesh mesh;
        [Min(0)] public int subMeshIndex;
        public Material material;
        public Vector3 localPosition;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;

        public Matrix4x4 LocalMatrix => Matrix4x4.TRS(localPosition, localRotation, localScale);
    }

    [Serializable]
    public sealed class VisualLodTier
    {
        public string stableId;
        [Range(0f, 1f)] public float screenRelativeTransitionHeight;
        public VisualRenderPart[] parts = Array.Empty<VisualRenderPart>();
    }

    [Serializable]
    public sealed class VisualVariant
    {
        public string stableId;
        [Min(1)] public int weight = 1;
        // Legacy LOD0 field. Existing profiles and renderers remain valid when lodTiers is empty.
        public VisualRenderPart[] parts = Array.Empty<VisualRenderPart>();
        public VisualLodTier[] lodTiers = Array.Empty<VisualLodTier>();
        public Vector2 uniformScaleRange = Vector2.one;
        [Min(0f)] public float yawStep = 90f;
        public float verticalOffset;
        public Vector3 pivotOffset;
        [Min(0f)] public float footprintRadius = .35f;
        [Min(0f)] public float collisionRadius = .35f;
        [Min(0f)] public float collisionRadiusOverride;
        public VisualFitMode fitMode;
        public Vector3 sourceBoundsSize = Vector3.one;
        public Vector3 lodReferencePoint;
        [Min(0f)] public float lodSize = 1f;

        public float EffectiveCollisionRadius => collisionRadiusOverride > 0f ? collisionRadiusOverride : collisionRadius;
        public int LodTierCount => lodTiers == null || lodTiers.Length == 0 ? 1 : lodTiers.Length;

        public VisualRenderPart[] GetLodParts(int lodIndex)
        {
            if (lodTiers != null && lodTiers.Length > 0)
            {
                int clamped = Mathf.Clamp(lodIndex, 0, lodTiers.Length - 1);
                VisualLodTier tier = lodTiers[clamped];
                if (tier != null && tier.parts != null && tier.parts.Length > 0) return tier.parts;
            }
            return parts ?? Array.Empty<VisualRenderPart>();
        }
    }

    [Serializable]
    public sealed class VisualCategory
    {
        public string categoryId;
        public VisualVariant[] variants = Array.Empty<VisualVariant>();
        public VisualPlacementRules placementRules = new VisualPlacementRules();
    }

    /// <summary>Editor-baked, explicitly referenced biome model catalog used at runtime.</summary>
    [CreateAssetMenu(fileName = "BiomeVisualProfile", menuName = "LLM2PCG/Biome Visual Profile")]
    public sealed class BiomeVisualProfile : ScriptableObject
    {
        public string profileId;
        [Min(1)] public int profileVersion = 1;
        public string worldType;
        public VisualCategory[] categories = Array.Empty<VisualCategory>();

        public VisualCategory FindCategory(string categoryId)
        {
            if (categories == null || string.IsNullOrEmpty(categoryId)) return null;
            for (int index = 0; index < categories.Length; index++)
                if (categories[index] != null && string.Equals(categories[index].categoryId, categoryId, StringComparison.Ordinal))
                    return categories[index];
            return null;
        }
    }

    public static class VisualCategoryIds
    {
        public const string ForestTrees = "Forest/Trees";
        public const string ForestRocks = "Forest/Rocks";
        public const string ForestBushes = "Forest/Bushes";
        public const string ForestGroundDetails = "Forest/GroundDetails";
        public const string ForestWater = "Forest/Water";
        public const string SwampTrees = "Swamp/Trees";
        public const string SwampReeds = "Swamp/Reeds";
        public const string SwampRocks = "Swamp/Rocks";
        public const string SwampGroundDetails = "Swamp/GroundDetails";
        public const string SwampWaterProps = "Swamp/WaterProps";
        public const string SwampWater = "Swamp/Water";
        public const string SnowfieldTrees = "Snowfield/Trees";
        public const string SnowfieldRocks = "Snowfield/Rocks";
        public const string SnowfieldGroundDetails = "Snowfield/GroundDetails";
        public const string SnowfieldIceProps = "Snowfield/IceProps";
        public const string SnowfieldIce = "Snowfield/Ice";
        public const string DesertCacti = "Desert/Cacti";
        public const string DesertRocks = "Desert/Rocks";
        public const string DesertGroundDetails = "Desert/GroundDetails";
        public const string DesertOasisProps = "Desert/OasisProps";
        public const string DesertWater = "Desert/Water";
        public const string DungeonPillars = "Dungeon/Pillars";
        public const string DungeonCrates = "Dungeon/Crates";
        public const string DungeonCrystals = "Dungeon/Crystals";
        public const string DungeonTorches = "Dungeon/Torches";
        public const string DungeonRoomFloors = "Dungeon/Architecture/Floors/Room";
        public const string DungeonCorridorFloors = "Dungeon/Architecture/Floors/Corridor";
        public const string DungeonStraightWalls = "Dungeon/Architecture/Walls/Straight";
        public const string DungeonWallCorners = "Dungeon/Architecture/Walls/Corners";
        public const string DungeonDoorways = "Dungeon/Architecture/Doorways";
        public const string DungeonWallTrims = "Dungeon/Architecture/Trims";
        public const string CaveRocks = "Cave/Rocks";
        public const string CaveCrystals = "Cave/Crystals";
        public const string CaveGroundDetails = "Cave/GroundDetails";
        public const string CaveEntrances = "Cave/Entrances";
        public const string CityLowRise = "City/Buildings/LowRise";
        public const string CityMidRise = "City/Buildings/MidRise";
        public const string CityHighRise = "City/Buildings/HighRise";
        public const string CityParkProps = "City/ParkProps";
        public const string CityTrees = "City/Trees";
        public const string CityStreetProps = "City/StreetProps";
    }
}
