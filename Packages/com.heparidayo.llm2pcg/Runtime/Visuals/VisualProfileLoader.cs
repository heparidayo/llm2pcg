using System;
using UnityEngine;

namespace Llm2Pcg.Visuals
{
    public static class VisualProfileLoader
    {
        private const string ResourceRoot = "PCGVisualProfiles/";

        public static BiomeVisualProfile Load(string worldType)
        {
            if (string.IsNullOrWhiteSpace(worldType)) return null;
            if (worldType == "Forest")
            {
                var woodland = Resources.Load<BiomeVisualProfile>(ResourceRoot + "ForestWoodland");
                if (woodland != null && woodland.worldType == worldType) return woodland;
            }
            if (worldType == "Dungeon")
            {
                var citadel = Resources.Load<BiomeVisualProfile>(ResourceRoot + "DungeonCitadel");
                if (citadel != null && citadel.worldType == worldType) return citadel;
            }
            if (worldType == "Desert" || worldType == "Snowfield" || worldType == "Swamp")
            {
                var nature = Resources.Load<BiomeVisualProfile>(ResourceRoot + worldType + "NatureBiomes");
                if (nature != null && nature.worldType == worldType) return nature;
            }
            if (string.Equals(worldType, "Cave", StringComparison.Ordinal))
            {
                var grotto = Resources.Load<BiomeVisualProfile>(ResourceRoot + "CaveCrystalGrotto");
                if (grotto != null && grotto.worldType == worldType) return grotto;
            }
            if (string.Equals(worldType, "City", StringComparison.Ordinal))
            {
                var district = Resources.Load<BiomeVisualProfile>(ResourceRoot + "CityCanalDistrict");
                if (district != null && district.worldType == worldType) return district;
            }
            BiomeVisualProfile profile = Resources.Load<BiomeVisualProfile>(ResourceRoot + worldType + "Default");
            return profile != null && string.Equals(profile.worldType, worldType, StringComparison.Ordinal) ? profile : null;
        }
    }
}
