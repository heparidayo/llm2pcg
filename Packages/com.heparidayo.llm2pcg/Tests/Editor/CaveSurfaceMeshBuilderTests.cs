using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.Cave;
using Llm2Pcg.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class CaveSurfaceMeshBuilderTests
    {
        [Test]
        public void BuildData_IsDeterministicAndDoesNotChangeWorldHash()
        {
            CaveWorldData world = new CaveCellularGenerator().GenerateCave(PCGRequest.CreateCaveDefault(24680, 128, 128));
            string worldHash = world.ComputeStableHash();

            CaveSurfaceMeshData first = CaveSurfaceMeshBuilder.BuildData(world);
            CaveSurfaceMeshData second = CaveSurfaceMeshBuilder.BuildData(world);

            Assert.That(second.StableHash, Is.EqualTo(first.StableHash));
            Assert.That(world.ComputeStableHash(), Is.EqualTo(worldHash));
            Assert.That(first.GridWidth, Is.EqualTo(257));
            Assert.That(first.GridHeight, Is.EqualTo(257));
            Assert.That(first.TriangleCount, Is.EqualTo(128 * 128 * 8 + first.BoundaryEdgeCount * 4));
            Assert.That(first.VertexCount, Is.EqualTo(257 * 257 * 2 + first.BoundaryEdgeCount * 8));
        }

        [Test]
        public void DifferentSeed_ChangesVisualSurfaceWithoutChangingTopologyContract()
        {
            bool[] solid = { true, true, true, true, false, true, true, true, true };
            CaveWorldData firstWorld = World(3, 3, 101, solid);
            CaveWorldData secondWorld = World(3, 3, 102, (bool[])solid.Clone());

            CaveSurfaceMeshData first = CaveSurfaceMeshBuilder.BuildData(firstWorld);
            CaveSurfaceMeshData second = CaveSurfaceMeshBuilder.BuildData(secondWorld);

            Assert.That(second.StableHash, Is.Not.EqualTo(first.StableHash));
            Assert.That(second.BoundaryEdgeCount, Is.EqualTo(first.BoundaryEdgeCount));
            Assert.That(second.TriangleCount, Is.EqualTo(first.TriangleCount));
        }

        [Test]
        public void IsolatedOpenCell_CreatesFloorRockAndFourContinuousWallEdges()
        {
            bool[] solid = { true, true, true, true, false, true, true, true, true };
            CaveSurfaceMeshData data = CaveSurfaceMeshBuilder.BuildData(World(3, 3, 7, solid));

            Assert.That(data.SubmeshTriangles[CaveSurfaceMeshBuilder.FloorSubmesh].Length / 3, Is.EqualTo(8));
            Assert.That(data.SubmeshTriangles[CaveSurfaceMeshBuilder.RockSubmesh].Length / 3, Is.EqualTo(8 * 8));
            Assert.That(data.BoundaryEdgeCount, Is.EqualTo(4));
            Assert.That(data.SubmeshTriangles[CaveSurfaceMeshBuilder.WallSubmesh].Length / 3, Is.EqualTo(16));

            Mesh mesh = CaveSurfaceMeshBuilder.CreateMesh(data);
            Assert.That(mesh.subMeshCount, Is.EqualTo(3));
            Assert.That(mesh.indexFormat, Is.EqualTo(UnityEngine.Rendering.IndexFormat.UInt16));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void FloorSampler_MatchesWalkableCellCenterAndRockStaysAboveEyeLevel()
        {
            bool[] solid = { true, true, true, true, false, true, true, true, true };
            CaveWorldData world = World(3, 3, 17, solid);
            CaveSurfaceMeshData data = CaveSurfaceMeshBuilder.BuildData(world);
            int floorCenter = (1 * 2 + 1) * data.GridWidth + 1 * 2 + 1;

            Assert.That(CaveSurfaceMeshBuilder.SampleSurfaceHeight(world, 1f, 1f), Is.EqualTo(data.Vertices[floorCenter].y).Within(.00001f));
            Assert.That(CaveSurfaceMeshBuilder.SampleSurfaceHeight(world, 1f, 1f), Is.InRange(-.055f, .055f));
            Assert.That(CaveSurfaceMeshBuilder.SampleRockSurfaceHeight(world, 0f, 0f), Is.GreaterThanOrEqualTo(2.35f));
        }

        private static CaveWorldData World(int width, int height, int seed, bool[] solid)
        {
            return new CaveWorldData(width, height, seed, solid, new Int2(1, 1), new Int2(1, 1), new List<Int2>());
        }
    }
}
