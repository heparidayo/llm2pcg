using System;
using System.Collections.Generic;
using System.Linq;

namespace Llm2Pcg.Core.V4
{
    // Separate DTO: legacy PCGRequest and its Unity deserializer remain unchanged.
    [Serializable] public sealed class SpatialRequest
    {
        public int schemaVersion, seed, mapWidth, mapHeight;
        public string generatorVersion, resolverVersion, catalogVersion, worldType, waterMode;
        public TerrainSettings terrain;
        public SpatialFeature[] spatialFeatures;
        public RouteSettings route;
        public DistributionRule[] distributionRules;
    }
    [Serializable] public sealed class TerrainSettings { public int heightUnits, noiseScale, octaves, waterLevelUnits; }
    [Serializable] public sealed class SpatialFeature
    {
        public string id, kind, placement, orientation;
        public int radiusCells, widthCells, heightUnits;
        public bool exclusive;
    }
    [Serializable] public sealed class RouteSettings { public string mode, orientation, crossingPolicy; public int widthCells; }
    [Serializable] public sealed class DistributionRule
    {
        public string category, amountMode, region, featureId;
        public string[] types;
        public int densityPermille, maxCount, minDistanceCells, minDistanceToFeature, maxDistanceToFeature;
    }
    public sealed class ConstraintFailure : Exception
    {
        public string Code { get; }
        public ConstraintFailure(string code, string message) : base(message) { Code = code; }
    }
    public static class SemanticCatalog
    {
        public static readonly string[] Categories = { "trees", "rocks", "bushes", "groundDetails", "waterProps" };
        public static string[] Types(string category)
        {
            switch (category)
            {
                case "trees": return new[] { "broadleaf", "cherry_blossom", "conifer", "willow", "dead_tree" };
                case "rocks": return new[] { "rock" };
                case "bushes": return new[] { "bush" };
                case "groundDetails": return new[] { "grass", "flower", "mushroom" };
                case "waterProps": return new[] { "reeds", "lily_pad", "water_lily" };
                default: return Array.Empty<string>();
            }
        }
    }
    public static class SpatialRequestValidator
    {
        public static string GeneratorFor(string worldType)
        {
            switch(worldType) { case "Forest":return "forest-biome@2";case "Desert":return "desert-biome@2";
                case "Snowfield":return "snowfield-biome@2";case "Swamp":return "swamp-biome@2";default:return null; }
        }
        private static bool Between(int value, int min, int max) => value >= min && value <= max;
        private static bool One(string value, params string[] choices) => Array.IndexOf(choices, value) >= 0;
        public static void Validate(SpatialRequest r)
        {
            void Need(bool condition, string message) { if (!condition) throw new ConstraintFailure("INVALID_V4_REQUEST", message); }
            Need(r != null, "Request required.");
            Need(r.schemaVersion == 4 && GeneratorFor(r.worldType)!=null && r.generatorVersion == GeneratorFor(r.worldType)
                && r.resolverVersion == "pcg-resolver@1" && r.catalogVersion == "semantic-forest@1", "Unsupported version/world.");
            Need(Between(r.mapWidth,16,500) && Between(r.mapHeight,16,500), "Map bounds.");
            Need(r.terrain != null && Between(r.terrain.heightUnits,4,256) && Between(r.terrain.noiseScale,4,256)
                && Between(r.terrain.octaves,1,8) && Between(r.terrain.waterLevelUnits,0,128), "Terrain bounds.");
            Need(One(r.waterMode,"Default","None"), "Water mode.");
            Need(r.spatialFeatures != null && r.spatialFeatures.Length <= 3, "Feature bounds.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in r.spatialFeatures)
            {
                Need(f != null && f.id != null && System.Text.RegularExpressions.Regex.IsMatch(f.id, "^[a-z][a-z0-9-]{0,31}$"), "Feature ID.");
                Need(ids.Add(f.id) && kinds.Add(f.kind), "Duplicate feature ID/kind.");
                Need(One(f.kind,"Mountain","Lake","River") && One(f.placement,"Center","North","South","East","West","Seeded","CenterCrossing"), "Feature kind/placement.");
                Need(Between(f.radiusCells,0,170) && Between(f.widthCells,0,50) && Between(f.heightUnits,0,128), "Feature bounds.");
                if (f.kind == "River")
                    Need(f.placement == "CenterCrossing" && One(f.orientation,"NorthSouth","EastWest") && f.widthCells > 0 && f.radiusCells == 0 && f.heightUnits == 0, "River fields.");
                else
                {
                    Need(f.placement != "CenterCrossing" && f.orientation == "None" && f.widthCells == 0 && f.radiusCells > 0
                        && f.radiusCells <= Math.Min(r.mapWidth,r.mapHeight)*2/5, "Landmark fields.");
                    Need(f.kind == "Mountain" ? f.heightUnits > 0 && !f.exclusive : f.heightUnits == 0, "Landmark height/exclusive.");
                }
                Need(f.kind == "Mountain" || r.waterMode != "None", "Water feature conflicts with None.");
            }
            Need(!(kinds.Contains("River") && kinds.Contains("Lake") && r.spatialFeatures.Any(f=>f.kind!="Mountain" && f.exclusive)), "Exclusive water conflicts with another water feature.");
            Need(r.route != null && One(r.route.mode,"None","StraightCrossing") && One(r.route.orientation,"NorthSouth","EastWest")
                && One(r.route.crossingPolicy,"BridgeIfNeeded","NoCrossing") && Between(r.route.widthCells,1,16), "Route fields.");
            Need(r.distributionRules != null && r.distributionRules.Length == 5, "Five categories required.");
            var categories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in r.distributionRules)
            {
                Need(d != null && One(d.category,SemanticCatalog.Categories) && categories.Add(d.category), "Duplicate/unknown category.");
                Need(d.types != null && d.types.Length > 0 && d.types.Length <= 10 && d.types.Distinct(StringComparer.Ordinal).Count() == d.types.Length
                    && d.types.All(t=>One(t,SemanticCatalog.Types(d.category))), "Semantic types.");
                Need(One(d.amountMode,"Off","RelativeDensity","AtMost") && Between(d.densityPermille,0,1000)
                    && Between(d.maxCount,0,10000) && Between(d.minDistanceCells,1,32), "Amount/spacing.");
                Need(One(d.region,"WholeMap","Center","North","South","East","West","NearFeature")
                    && Between(d.minDistanceToFeature,0,64) && Between(d.maxDistanceToFeature,0,64)
                    && d.minDistanceToFeature <= d.maxDistanceToFeature, "Region distance.");
                Need(d.region == "NearFeature" ? ids.Contains(d.featureId) : d.featureId == "" && d.minDistanceToFeature == 0 && d.maxDistanceToFeature == 0, "Feature reference.");
                Need(d.amountMode != "Off" || d.maxCount == 0 && d.densityPermille == 0, "Off fields.");
                Need(d.amountMode != "AtMost" || d.densityPermille == 1000, "AtMost density.");
            }
        }
    }
}
