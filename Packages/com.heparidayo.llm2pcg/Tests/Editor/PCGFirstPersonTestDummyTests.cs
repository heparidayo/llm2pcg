using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Presentation;
using Llm2Pcg.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class PCGFirstPersonTestDummyTests
    {
        [Test]
        public void CanOccupy_AcceptsWalkableGentleTerrainAndRejectsWater()
        {
            BiomeTile[] tiles = { BiomeTile.Ground, BiomeTile.Ground, BiomeTile.Ground, BiomeTile.Ground, BiomeTile.Water, BiomeTile.Ground, BiomeTile.Ground, BiomeTile.Ground, BiomeTile.Ground };
            float[] elevations = { 0f, 0f, 0f, 0f, .5f, .5f, .5f, .5f, .5f };
            BiomeWorldData world = World(tiles, elevations, new List<Int2>());

            Assert.That(PCGFirstPersonTestDummy.CanOccupy(world, Vector3.zero, 0f, .1f, 1f), Is.True);
            Assert.That(PCGFirstPersonTestDummy.CanOccupy(world, new Vector3(1f, 0f, 1f), 0f, .1f, 1f), Is.False);
        }

        [Test]
        public void CanOccupy_RejectsSteepStepsAndForestProps()
        {
            BiomeTile[] tiles = new BiomeTile[9];
            float[] elevations = { 0f, 0f, 0f, 0f, 3f, 0f, 0f, 0f, 0f };
            BiomeWorldData steep = World(tiles, elevations, new List<Int2>());
            BiomeWorldData tree = World(new BiomeTile[9], new float[9], new List<Int2> { new Int2(1, 1) });

            Assert.That(PCGFirstPersonTestDummy.CanOccupy(steep, new Vector3(1f, 0f, 1f), 0f, .1f, 1f), Is.False);
            Assert.That(PCGFirstPersonTestDummy.CanOccupy(tree, new Vector3(1f, 0f, 1f), 0f, .1f, 1f), Is.False);
        }

        [Test]
        public void CanOccupy_UsesTrunkRadiusInsteadOfCanopyFootprint()
        {
            BiomeWorldData world = World(new BiomeTile[9], new float[9], new List<Int2> { new Int2(1, 1) });
            var trunks = new List<ForestTreeCollision> { new ForestTreeCollision(1f, 1f, .2f) };

            Assert.That(PCGFirstPersonTestDummy.CanOccupy(world, new Vector3(1.2f, 0f, 1f), 0f, .1f, 1f, trunks), Is.False);
            Assert.That(PCGFirstPersonTestDummy.CanOccupy(world, new Vector3(1.6f, 0f, 1f), 0f, .1f, 1f, trunks), Is.True);
        }

        [TestCase("Swamp")]
        [TestCase("Snowfield")]
        [TestCase("Desert")]
        public void CanOccupy_NatureFamilyUsesHeightfieldAndVegetationCollision(string worldType)
        {
            string version = worldType == PCGRequest.SwampWorldType ? PCGRequest.SwampGeneratorVersion : worldType == PCGRequest.SnowfieldWorldType ? PCGRequest.SnowfieldGeneratorVersion : PCGRequest.DesertGeneratorVersion;
            BiomeWorldData world = new BiomeWorldData(worldType, version, 3, 3, 4, new BiomeTile[9], new Int2(0,0), new Int2(2,2), new List<Int2> { new Int2(1,1) }, new float[9]);

            Assert.That(PCGFirstPersonTestDummy.CanOccupy(world, new Vector3(1f,0f,1f), 0f, .1f, 1f), Is.False);
            Assert.That(PCGFirstPersonTestDummy.CanOccupy(world, new Vector3(2f,0f,2f), 0f, .1f, 1f), Is.True);
        }

        private static BiomeWorldData World(BiomeTile[] tiles, float[] elevations, List<Int2> props)
        {
            return new BiomeWorldData(PCGRequest.ForestWorldType, PCGRequest.ForestGeneratorVersion, 3, 3, 1, tiles, new Int2(0, 0), new Int2(2, 2), props, elevations);
        }
    }
}
