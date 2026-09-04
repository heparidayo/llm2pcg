using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.City;
using Llm2Pcg.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class CitySurfaceMeshBuilderTests
    {
        [Test]
        public void BuildData_IsDeterministicAndDoesNotChangeWorldHash()
        {
            BiomeWorldData world = new CityHybridWfcGenerator().GenerateCity(PCGRequest.CreateCityDefault(24680, 128, 128));
            string worldHash = world.ComputeStableHash();

            CitySurfaceMeshData first = CitySurfaceMeshBuilder.BuildData(world);
            CitySurfaceMeshData second = CitySurfaceMeshBuilder.BuildData(world);

            Assert.That(second.StableHash, Is.EqualTo(first.StableHash));
            Assert.That(world.ComputeStableHash(), Is.EqualTo(worldHash));
            Assert.That(first.SubmeshTriangles.Length, Is.EqualTo(5));
            Assert.That(first.RoadMarkingSegmentCount, Is.GreaterThan(0));
            Assert.That(first.ElevationEdgeCount, Is.GreaterThan(0));
        }

        [Test]
        public void DifferentSeed_ChangesGeneratedCitySurface()
        {
            CityHybridWfcGenerator generator = new CityHybridWfcGenerator();
            CitySurfaceMeshData first = CitySurfaceMeshBuilder.BuildData(generator.GenerateCity(PCGRequest.CreateCityDefault(24680, 128, 128)));
            CitySurfaceMeshData second = CitySurfaceMeshBuilder.BuildData(generator.GenerateCity(PCGRequest.CreateCityDefault(24681, 128, 128)));

            Assert.That(second.StableHash, Is.Not.EqualTo(first.StableHash));
        }

        [Test]
        public void MixedTwoByTwoCity_SeparatesSurfacesCurbsAndRoadMarkings()
        {
            BiomeTile[] tiles = { BiomeTile.Ground, BiomeTile.Road, BiomeTile.Park, BiomeTile.Building };
            BiomeWorldData world = World(2, 2, tiles);
            CitySurfaceMeshData data = CitySurfaceMeshBuilder.BuildData(world);

            Assert.That(data.SubmeshTriangles[CitySurfaceMeshBuilder.SidewalkSubmesh].Length / 3, Is.EqualTo(4));
            Assert.That(data.SubmeshTriangles[CitySurfaceMeshBuilder.RoadSubmesh].Length / 3, Is.EqualTo(2));
            Assert.That(data.SubmeshTriangles[CitySurfaceMeshBuilder.ParkSubmesh].Length / 3, Is.EqualTo(2));
            Assert.That(data.ElevationEdgeCount, Is.EqualTo(4));
            Assert.That(data.SubmeshTriangles[CitySurfaceMeshBuilder.CurbSubmesh].Length / 3, Is.EqualTo(16));
            Assert.That(data.RoadMarkingSegmentCount, Is.EqualTo(4));
            Assert.That(data.SubmeshTriangles[CitySurfaceMeshBuilder.RoadMarkingSubmesh].Length / 3, Is.EqualTo(8));
            Assert.That(data.VertexCount, Is.EqualTo(64));

            Mesh mesh = CitySurfaceMeshBuilder.CreateMesh(data);
            Assert.That(mesh.subMeshCount, Is.EqualTo(5));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void SurfaceSampler_UsesRoadParkAndSidewalkLevels()
        {
            BiomeWorldData world = World(2, 2, new[] { BiomeTile.Ground, BiomeTile.Road, BiomeTile.Park, BiomeTile.Building });

            Assert.That(CitySurfaceMeshBuilder.SampleSurfaceHeight(world, 0f, 0f), Is.EqualTo(CitySurfaceMeshBuilder.SidewalkHeight));
            Assert.That(CitySurfaceMeshBuilder.SampleSurfaceHeight(world, 1f, 0f), Is.EqualTo(CitySurfaceMeshBuilder.RoadHeight));
            Assert.That(CitySurfaceMeshBuilder.SampleSurfaceHeight(world, 0f, 1f), Is.EqualTo(CitySurfaceMeshBuilder.ParkHeight));
            Assert.That(CitySurfaceMeshBuilder.SampleSurfaceHeight(world, 1f, 1f), Is.EqualTo(CitySurfaceMeshBuilder.SidewalkHeight));
        }

        private static BiomeWorldData World(int width, int height, BiomeTile[] tiles)
        {
            return new BiomeWorldData(PCGRequest.CityWorldType, PCGRequest.CityGeneratorVersion, width, height, 7, tiles, new Int2(0, 0), new Int2(width - 1, height - 1), new List<Int2>(), new float[width * height], new float[width * height]);
        }
    }
}
