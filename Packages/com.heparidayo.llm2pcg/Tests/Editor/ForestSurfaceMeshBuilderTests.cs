using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.Forest;
using Llm2Pcg.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class ForestSurfaceMeshBuilderTests
    {
        [Test]
        public void BuildData_IsDeterministicAndDoesNotChangeWorldHash()
        {
            PCGRequest request = PCGRequest.CreateForestDefault(24680, 128, 128);
            request.generationProfile = PCGRequest.DenseForestProfile;
            request.generatorSettings.forest.vegetationMinDistance = 3.75f;
            request.generatorSettings.forest.elevationScale = 7f;
            BiomeWorldData world = new ForestBiomeGenerator().GenerateForest(request);
            string worldHash = world.ComputeStableHash();

            ForestSurfaceMeshData first = ForestSurfaceMeshBuilder.BuildData(world);
            ForestSurfaceMeshData second = ForestSurfaceMeshBuilder.BuildData(world);
            int waterCount = CountTiles(world, BiomeTile.Water);

            Assert.That(second.StableHash, Is.EqualTo(first.StableHash));
            Assert.That(world.ComputeStableHash(), Is.EqualTo(worldHash));
            Assert.That(first.VertexCount, Is.EqualTo(257 * 257 + waterCount * 4));
            Assert.That(first.TriangleCount, Is.EqualTo(128 * 128 * 8 + waterCount * 2));
        }

        [Test]
        public void TileCenters_PreserveGeneratedHeightAndWaterUsesSubmergedBed()
        {
            BiomeTile[] tiles = { BiomeTile.Ground, BiomeTile.Path, BiomeTile.Water, BiomeTile.Ground };
            float[] heights = { 1f, 2f, 0f, 3f };
            BiomeWorldData world = new BiomeWorldData(PCGRequest.ForestWorldType, PCGRequest.ForestGeneratorVersion, 2, 2, 1, tiles, new Int2(0, 0), new Int2(1, 1), new List<Int2>(), heights);
            ForestSurfaceMeshData data = ForestSurfaceMeshBuilder.BuildData(world);

            Assert.That(CenterHeight(data, 0, 0), Is.EqualTo(1f));
            Assert.That(CenterHeight(data, 1, 0), Is.EqualTo(2f));
            Assert.That(CenterHeight(data, 0, 1), Is.EqualTo(ForestSurfaceMeshBuilder.UnderwaterBedHeight));
            Assert.That(CenterHeight(data, 1, 1), Is.EqualTo(3f));
            Assert.That(ForestSurfaceMeshBuilder.SampleSurfaceHeight(world, 0f, 0f), Is.EqualTo(1f));
            Assert.That(ForestSurfaceMeshBuilder.SampleSurfaceHeight(world, 1f, 0f), Is.EqualTo(2f));
            Assert.That(ForestSurfaceMeshBuilder.SampleSurfaceHeight(world, .5f, 0f), Is.InRange(1f, 2f));
        }

        [Test]
        public void Submeshes_SeparateGroundPathUnderwaterBedAndConnectedWaterSurface()
        {
            BiomeTile[] tiles = { BiomeTile.Ground, BiomeTile.Path, BiomeTile.Road, BiomeTile.Water };
            BiomeWorldData world = new BiomeWorldData(PCGRequest.ForestWorldType, PCGRequest.ForestGeneratorVersion, 4, 1, 2, tiles, new Int2(0, 0), new Int2(3, 0), new List<Int2>(), new float[4]);
            ForestSurfaceMeshData data = ForestSurfaceMeshBuilder.BuildData(world);

            Assert.That(data.SubmeshTriangles[ForestSurfaceMeshBuilder.GroundSubmesh].Length, Is.EqualTo(24));
            Assert.That(data.SubmeshTriangles[ForestSurfaceMeshBuilder.PathSubmesh].Length, Is.EqualTo(48));
            Assert.That(data.SubmeshTriangles[ForestSurfaceMeshBuilder.UnderwaterSubmesh].Length, Is.EqualTo(24));
            Assert.That(data.SubmeshTriangles[ForestSurfaceMeshBuilder.WaterSurfaceSubmesh].Length, Is.EqualTo(6));
            Assert.That(data.VertexCount, Is.EqualTo(9 * 3 + 4));
            Mesh mesh = ForestSurfaceMeshBuilder.CreateMesh(data);
            Assert.That(mesh.subMeshCount, Is.EqualTo(4));
            Object.DestroyImmediate(mesh);
        }

        [TestCase("Swamp")]
        [TestCase("Snowfield")]
        [TestCase("Desert")]
        public void NatureWorlds_UseOneConnectedHeightfieldMeshWithoutChangingWorldHash(string worldType)
        {
            PCGRequest request = worldType == PCGRequest.SwampWorldType ? PCGRequest.CreateSwampDefault(15401,128,128) : worldType == PCGRequest.SnowfieldWorldType ? PCGRequest.CreateSnowfieldDefault(15402,128,128) : PCGRequest.CreateDesertDefault(15403,128,128);
            BiomeWorldData world = (BiomeWorldData)PCGGeneratorRegistry.Default.GetRequired(request).Generate(request);
            string hash = world.ComputeStableHash();

            ForestSurfaceMeshData data = ForestSurfaceMeshBuilder.BuildData(world);
            int waterCount = CountTiles(world, BiomeTile.Water);

            Assert.That(data.VertexCount, Is.EqualTo(257 * 257 + waterCount * 4));
            Assert.That(data.TriangleCount, Is.EqualTo(128 * 128 * 8 + waterCount * 2));
            Assert.That(world.ComputeStableHash(), Is.EqualTo(hash));
        }

        private static int CountTiles(BiomeWorldData world, BiomeTile tile)
        {
            int count = 0;
            for (int index = 0; index < world.Tiles.Length; index++) if (world.Tiles[index] == tile) count++;
            return count;
        }

        private static float CenterHeight(ForestSurfaceMeshData data, int x, int y)
        {
            return data.Vertices[(y * 2 + 1) * data.GridWidth + x * 2 + 1].y;
        }
    }
}
