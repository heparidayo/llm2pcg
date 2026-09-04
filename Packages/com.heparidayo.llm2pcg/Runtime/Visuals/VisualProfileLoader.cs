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
            BiomeVisualProfile profile = Resources.Load<BiomeVisualProfile>(ResourceRoot + worldType + "Default");
            return profile != null && string.Equals(profile.worldType, worldType, StringComparison.Ordinal) ? profile : null;
        }
    }
}
