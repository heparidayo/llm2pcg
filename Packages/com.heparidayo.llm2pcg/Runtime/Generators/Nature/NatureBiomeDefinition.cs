using Llm2Pcg.Contract;

namespace Llm2Pcg.Generators.Nature
{
    public enum NatureTerrainStyle : byte { Lowland, Rolling, Ridged }

    /// <summary>Immutable policy data consumed by the shared deterministic Nature generator.</summary>
    public sealed class NatureBiomeDefinition
    {
        public readonly string WorldType;
        public readonly string GeneratorVersion;
        public readonly string Biome;
        public readonly int StageOffset;
        public readonly NatureTerrainStyle TerrainStyle;

        public NatureBiomeDefinition(string worldType, string generatorVersion, string biome, int stageOffset, NatureTerrainStyle terrainStyle)
        {
            WorldType = worldType;
            GeneratorVersion = generatorVersion;
            Biome = biome;
            StageOffset = stageOffset;
            TerrainStyle = terrainStyle;
        }
    }

    public static class NatureWorldTypes
    {
        public static readonly NatureBiomeDefinition Swamp = new NatureBiomeDefinition(PCGRequest.SwampWorldType, PCGRequest.SwampGeneratorVersion, PCGRequest.WetlandBiome, 0, NatureTerrainStyle.Lowland);
        public static readonly NatureBiomeDefinition Snowfield = new NatureBiomeDefinition(PCGRequest.SnowfieldWorldType, PCGRequest.SnowfieldGeneratorVersion, PCGRequest.ArcticBiome, 100, NatureTerrainStyle.Rolling);
        public static readonly NatureBiomeDefinition Desert = new NatureBiomeDefinition(PCGRequest.DesertWorldType, PCGRequest.DesertGeneratorVersion, PCGRequest.AridBiome, 200, NatureTerrainStyle.Ridged);

        public static bool IsNature(string worldType) => worldType == PCGRequest.SwampWorldType || worldType == PCGRequest.SnowfieldWorldType || worldType == PCGRequest.DesertWorldType;
        public static bool IsForestFamily(string worldType) => worldType == PCGRequest.ForestWorldType || IsNature(worldType);
        public static NatureBiomeDefinition GetRequired(string worldType)
        {
            if (worldType == PCGRequest.SwampWorldType) return Swamp;
            if (worldType == PCGRequest.SnowfieldWorldType) return Snowfield;
            if (worldType == PCGRequest.DesertWorldType) return Desert;
            throw new System.ArgumentException("Unsupported Nature world type: " + worldType + ".", nameof(worldType));
        }
    }
}
