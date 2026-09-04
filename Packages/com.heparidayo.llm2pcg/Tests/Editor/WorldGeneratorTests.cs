using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.City;
using Llm2Pcg.Generators.Forest;
using Llm2Pcg.Generators.Nature;
using NUnit.Framework;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class WorldGeneratorTests
    {
        [Test] public void Forest_SameRequestIsDeterministic() { ForestBiomeGenerator generator = new ForestBiomeGenerator(); PCGRequest request = PCGRequest.CreateForestDefault(901); Assert.That(generator.GenerateForest(request).ComputeStableHash(), Is.EqualTo(generator.GenerateForest(request).ComputeStableHash())); }
        [Test] public void Forest_DifferentSeedChangesHash() { ForestBiomeGenerator generator = new ForestBiomeGenerator(); Assert.That(generator.GenerateForest(PCGRequest.CreateForestDefault(901)).ComputeStableHash(), Is.Not.EqualTo(generator.GenerateForest(PCGRequest.CreateForestDefault(902)).ComputeStableHash())); }
        [Test] public void Forest_StartAndExitAreConnectedByWalkablePath() { BiomeWorldData world = new ForestBiomeGenerator().GenerateForest(PCGRequest.CreateForestDefault(903)); Assert.That(IsReachable(world, world.StartPosition, world.ExitPosition, false), Is.True); }
        [Test]
        public void Forest_DefaultWaterCoverageStaysModestAcrossRepresentativeSeeds()
        {
            foreach (int seed in new[] { 1, 42, 234, 901, 24680 })
            {
                PCGRequest request = PCGRequest.CreateForestDefault(seed, 128, 128);
                BiomeWorldData world = new ForestBiomeGenerator().GenerateForest(request);
                Assert.That(request.generatorSettings.forest.waterThreshold, Is.EqualTo(.20f));
                Assert.That(CountTile(world, BiomeTile.Water) / (float)world.Tiles.Length, Is.LessThanOrEqualTo(.04f), "seed " + seed);
            }
        }
        [Test] public void Forest_ElevationIsQuantizedDeterministicAndAdjustable()
        {
            PCGRequest request = PCGRequest.CreateForestDefault(909); request.generatorSettings.forest.elevationScale = 12f;
            BiomeWorldData first = new ForestBiomeGenerator().GenerateForest(request); BiomeWorldData second = new ForestBiomeGenerator().GenerateForest(request);
            float maximum = 0f;
            for (int i = 0; i < first.Elevations.Length; i++) { maximum = System.Math.Max(maximum, first.Elevations[i]); Assert.That(first.Elevations[i] * 4f, Is.EqualTo(System.Math.Round(first.Elevations[i] * 4f)).Within(.0001f)); Assert.That(first.Elevations[i], Is.EqualTo(second.Elevations[i])); }
            Assert.That(maximum, Is.GreaterThan(1f)); Assert.That(maximum, Is.LessThanOrEqualTo(12f));
        }
        [Test] public void Forest_VegetationAvoidsWaterAndPathsAndKeepsMinimumDistance()
        {
            PCGRequest request = PCGRequest.CreateForestDefault(907); BiomeWorldData world = new ForestBiomeGenerator().GenerateForest(request); float minimum = request.generatorSettings.forest.vegetationMinDistance;
            for (int i = 0; i < world.Props.Count; i++)
            {
                Int2 tree = world.Props[i]; Assert.That(world.Tiles[tree.Y * world.Width + tree.X], Is.EqualTo(BiomeTile.Ground));
                for (int j = 0; j < i; j++) { int dx = tree.X - world.Props[j].X, dy = tree.Y - world.Props[j].Y; Assert.That(dx * dx + dy * dy, Is.GreaterThanOrEqualTo(minimum * minimum)); }
            }
        }
        [Test] public void City_SameRequestIsDeterministic() { CityHybridWfcGenerator generator = new CityHybridWfcGenerator(); PCGRequest request = PCGRequest.CreateCityDefault(904); Assert.That(generator.GenerateCity(request).ComputeStableHash(), Is.EqualTo(generator.GenerateCity(request).ComputeStableHash())); }
        [Test] public void City_DifferentSeedChangesHash() { CityHybridWfcGenerator generator = new CityHybridWfcGenerator(); Assert.That(generator.GenerateCity(PCGRequest.CreateCityDefault(904)).ComputeStableHash(), Is.Not.EqualTo(generator.GenerateCity(PCGRequest.CreateCityDefault(905)).ComputeStableHash())); }
        [Test] public void City_StartAndExitAreConnectedByRoads() { BiomeWorldData world = new CityHybridWfcGenerator().GenerateCity(PCGRequest.CreateCityDefault(906)); Assert.That(world.Tiles[world.StartPosition.Y * world.Width + world.StartPosition.X], Is.EqualTo(BiomeTile.Road)); Assert.That(world.Tiles[world.ExitPosition.Y * world.Width + world.ExitPosition.X], Is.EqualTo(BiomeTile.Road)); Assert.That(IsReachable(world, world.StartPosition, world.ExitPosition, true), Is.True); }
        [Test] public void City_BuildingHeightsRespectRequestedRangeAndVaryByLot()
        {
            PCGRequest request = PCGRequest.CreateCityDefault(910); request.generatorSettings.city.buildingMinHeight = 5f; request.generatorSettings.city.buildingMaxHeight = 22f;
            BiomeWorldData world = new CityHybridWfcGenerator().GenerateCity(request); HashSet<float> heights = new HashSet<float>();
            for (int i = 0; i < world.Tiles.Length; i++) if (world.Tiles[i] == BiomeTile.Building) { Assert.That(world.StructureHeights[i], Is.InRange(5f, 22f)); heights.Add(world.StructureHeights[i]); } else Assert.That(world.StructureHeights[i], Is.EqualTo(0f));
            Assert.That(heights.Count, Is.GreaterThan(1));
        }
        [Test] public void City_BuildingsFormLotsInsteadOfSingleTileNoise()
        {
            BiomeWorldData world = new CityHybridWfcGenerator().GenerateCity(PCGRequest.CreateCityDefault(908)); int isolated = 0, buildings = 0;
            for (int y = 1; y < world.Height - 1; y++) for (int x = 1; x < world.Width - 1; x++) if (world.Tiles[y * world.Width + x] == BiomeTile.Building) { buildings++; if (!IsTile(world, x + 1, y, BiomeTile.Building) && !IsTile(world, x - 1, y, BiomeTile.Building) && !IsTile(world, x, y + 1, BiomeTile.Building) && !IsTile(world, x, y - 1, BiomeTile.Building)) isolated++; }
            Assert.That(buildings, Is.GreaterThan(0)); Assert.That(isolated, Is.EqualTo(0));
        }

        [Test]
        public void DefaultGoldenHashes_AreStable()
        {
            Assert.That(PCGGeneratorRegistry.Default.GetRequired(PCGRequest.CreateDefault(24680,128,128)).Generate(PCGRequest.CreateDefault(24680,128,128)).ComputeStableHash(), Is.EqualTo("7BFB4589"));
            Assert.That(PCGGeneratorRegistry.Default.GetRequired(PCGRequest.CreateCaveDefault(24680,128,128)).Generate(PCGRequest.CreateCaveDefault(24680,128,128)).ComputeStableHash(), Is.EqualTo("A2EF3948"));
            Assert.That(PCGGeneratorRegistry.Default.GetRequired(PCGRequest.CreateForestDefault(24680,128,128)).Generate(PCGRequest.CreateForestDefault(24680,128,128)).ComputeStableHash(), Is.EqualTo("2418314A"));
            Assert.That(PCGGeneratorRegistry.Default.GetRequired(PCGRequest.CreateCityDefault(24680,128,128)).Generate(PCGRequest.CreateCityDefault(24680,128,128)).ComputeStableHash(), Is.EqualTo("34B02A50"));
        }

        [TestCase("Swamp")]
        [TestCase("Snowfield")]
        [TestCase("Desert")]
        public void NatureWorlds_AreDeterministicSeedSensitiveConnectedAndQuantized(string worldType)
        {
            PCGRequest request = NatureRequest(worldType, 12001);
            BiomeWorldData first = (BiomeWorldData)PCGGeneratorRegistry.Default.GetRequired(request).Generate(request);
            BiomeWorldData second = (BiomeWorldData)PCGGeneratorRegistry.Default.GetRequired(request).Generate(request);
            PCGRequest differentRequest = NatureRequest(worldType, 12002);
            BiomeWorldData different = (BiomeWorldData)PCGGeneratorRegistry.Default.GetRequired(differentRequest).Generate(differentRequest);

            Assert.That(first.ComputeStableHash(), Is.EqualTo(second.ComputeStableHash()));
            Assert.That(first.ComputeStableHash(), Is.Not.EqualTo(different.ComputeStableHash()));
            Assert.That(IsReachable(first, first.StartPosition, first.ExitPosition, false), Is.True);
            for (int i = 0; i < first.Elevations.Length; i++) Assert.That(first.Elevations[i] * 4f, Is.EqualTo(System.Math.Round(first.Elevations[i] * 4f)).Within(.0001f));
            for (int i = 0; i < first.Props.Count; i++) Assert.That(first.Tiles[first.Props[i].Y * first.Width + first.Props[i].X], Is.EqualTo(BiomeTile.Ground));
        }

        [Test]
        public void NatureProfiles_ProduceDistinctBiomeSpecificWorlds()
        {
            PCGRequest swamp = PCGRequest.CreateSwampDefault(12101); swamp.generationProfile = PCGRequest.OpenMarshProfile;
            PCGRequest snow = PCGRequest.CreateSnowfieldDefault(12102); snow.generationProfile = PCGRequest.FrozenGroveProfile;
            PCGRequest desert = PCGRequest.CreateDesertDefault(12103); desert.generationProfile = PCGRequest.OasisDesertProfile;
            BiomeWorldData swampWorld = (BiomeWorldData)PCGGeneratorRegistry.Default.GetRequired(swamp).Generate(swamp);
            BiomeWorldData snowWorld = (BiomeWorldData)PCGGeneratorRegistry.Default.GetRequired(snow).Generate(snow);
            BiomeWorldData desertWorld = (BiomeWorldData)PCGGeneratorRegistry.Default.GetRequired(desert).Generate(desert);

            Assert.That(CountTile(swampWorld, BiomeTile.Water), Is.GreaterThan(0));
            Assert.That(CountTile(snowWorld, BiomeTile.Water), Is.GreaterThan(0));
            Assert.That(CountTile(desertWorld, BiomeTile.Water), Is.GreaterThan(0));
            Assert.That(MaxElevation(snowWorld), Is.GreaterThan(2f));
            Assert.That(MaxElevation(desertWorld), Is.GreaterThan(2f));
        }

        [TestCase("Swamp")]
        [TestCase("Snowfield")]
        [TestCase("Desert")]
        public void NatureProfiles_WithSameSeedProduceDistinctStableHashes(string worldType)
        {
            PCGRequest defaultRequest = NatureRequest(worldType, 12201);
            PCGRequest profileA = NatureRequest(worldType, 12201);
            PCGRequest profileB = NatureRequest(worldType, 12201);
            if (worldType == PCGRequest.SwampWorldType) { profileA.generationProfile = PCGRequest.OpenMarshProfile; profileB.generationProfile = PCGRequest.DenseBogProfile; }
            else if (worldType == PCGRequest.SnowfieldWorldType) { profileA.generationProfile = PCGRequest.SparseTundraProfile; profileB.generationProfile = PCGRequest.FrozenGroveProfile; }
            else { profileA.generationProfile = PCGRequest.DuneSeaProfile; profileB.generationProfile = PCGRequest.OasisDesertProfile; }
            string defaultHash = PCGGeneratorRegistry.Default.GetRequired(defaultRequest).Generate(defaultRequest).ComputeStableHash();
            string firstHash = PCGGeneratorRegistry.Default.GetRequired(profileA).Generate(profileA).ComputeStableHash();
            string secondHash = PCGGeneratorRegistry.Default.GetRequired(profileB).Generate(profileB).ComputeStableHash();
            Assert.That(firstHash, Is.Not.EqualTo(defaultHash));
            Assert.That(secondHash, Is.Not.EqualTo(defaultHash));
            Assert.That(firstHash, Is.Not.EqualTo(secondHash));
        }

        private static PCGRequest NatureRequest(string worldType, int seed) => worldType == PCGRequest.SwampWorldType ? PCGRequest.CreateSwampDefault(seed) : worldType == PCGRequest.SnowfieldWorldType ? PCGRequest.CreateSnowfieldDefault(seed) : PCGRequest.CreateDesertDefault(seed);
        private static int CountTile(BiomeWorldData world, BiomeTile tile) { int count = 0; for (int i = 0; i < world.Tiles.Length; i++) if (world.Tiles[i] == tile) count++; return count; }
        private static float MaxElevation(BiomeWorldData world) { float maximum = 0f; for (int i = 0; i < world.Elevations.Length; i++) maximum = System.Math.Max(maximum, world.Elevations[i]); return maximum; }
        private static bool IsReachable(BiomeWorldData world, Int2 start, Int2 end, bool roadOnly)
        {
            Queue<Int2> queue = new Queue<Int2>(); bool[] seen = new bool[world.Tiles.Length]; queue.Enqueue(start); seen[start.Y * world.Width + start.X] = true;
            int[] dx = { 0, 1, 0, -1 }, dy = { -1, 0, 1, 0 };
            while (queue.Count > 0) { Int2 point = queue.Dequeue(); if (point.X == end.X && point.Y == end.Y) return true; for (int d = 0; d < 4; d++) { int x = point.X + dx[d], y = point.Y + dy[d]; if (!world.IsInBounds(x, y)) continue; int index = y * world.Width + x; bool valid = roadOnly ? world.Tiles[index] == BiomeTile.Road : world.IsWalkable(x, y); if (valid && !seen[index]) { seen[index] = true; queue.Enqueue(new Int2(x, y)); } } }
            return false;
        }
        private static bool IsTile(BiomeWorldData world, int x, int y, BiomeTile tile) => world.IsInBounds(x, y) && world.Tiles[y * world.Width + x] == tile;
    }
}
