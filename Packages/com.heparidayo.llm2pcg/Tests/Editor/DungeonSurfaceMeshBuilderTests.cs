using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.Dungeon;
using Llm2Pcg.Rendering;
using Llm2Pcg.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class DungeonSurfaceMeshBuilderTests
    {
        [Test]
        public void BuildData_IsDeterministicAndDoesNotChangeWorldHash()
        {
            DungeonWorldData world = new DungeonBSPGenerator().Generate(PCGRequest.CreateDefault(24680, 128, 128));
            string worldHash = world.ComputeStableHash();

            DungeonSurfaceMeshData first = DungeonSurfaceMeshBuilder.BuildData(world);
            DungeonSurfaceMeshData second = DungeonSurfaceMeshBuilder.BuildData(world);

            Assert.That(second.StableHash, Is.EqualTo(first.StableHash));
            Assert.That(world.ComputeStableHash(), Is.EqualTo(worldHash));
            Assert.That(first.SubmeshTriangles.Length, Is.EqualTo(5));
            Assert.That(first.BoundaryEdgeCount, Is.GreaterThan(0));
            Assert.That(first.DoorwayCount, Is.GreaterThan(0));
        }

        [Test]
        public void DifferentSeed_ChangesGeneratedDungeonSurface()
        {
            DungeonBSPGenerator generator = new DungeonBSPGenerator();
            string first = DungeonSurfaceMeshBuilder.BuildData(generator.Generate(PCGRequest.CreateDefault(24680, 128, 128))).StableHash;
            string second = DungeonSurfaceMeshBuilder.BuildData(generator.Generate(PCGRequest.CreateDefault(24681, 128, 128))).StableHash;

            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void FloorCorridorPair_HasThinBoundaryTrimAndOneDoorway()
        {
            DungeonWorldData world = World(2, 1, new[] { DungeonCell.Floor, DungeonCell.Corridor });
            DungeonSurfaceMeshData data = DungeonSurfaceMeshBuilder.BuildData(world);

            Assert.That(data.SubmeshTriangles[DungeonSurfaceMeshBuilder.RoomFloorSubmesh].Length / 3, Is.EqualTo(2));
            Assert.That(data.SubmeshTriangles[DungeonSurfaceMeshBuilder.CorridorFloorSubmesh].Length / 3, Is.EqualTo(2));
            Assert.That(data.BoundaryEdgeCount, Is.EqualTo(6));
            Assert.That(data.ExteriorBoundaryEdgeCount, Is.EqualTo(6));
            Assert.That(data.CornerCount, Is.EqualTo(4));
            Assert.That(data.DoorwayCount, Is.EqualTo(1));
            Assert.That(data.SubmeshTriangles[DungeonSurfaceMeshBuilder.WallSubmesh].Length / 3, Is.EqualTo(72));
            Assert.That(data.SubmeshTriangles[DungeonSurfaceMeshBuilder.TrimSubmesh].Length / 3, Is.EqualTo(72));
            Assert.That(data.SubmeshTriangles[DungeonSurfaceMeshBuilder.DoorwaySubmesh].Length / 3, Is.EqualTo(48));

            Mesh mesh = DungeonSurfaceMeshBuilder.CreateMesh(data);
            Assert.That(mesh.subMeshCount, Is.EqualTo(5));
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public void BuildOptions_RemoveOnlyCategoriesReplacedByExternalKit()
        {
            DungeonWorldData world = World(2, 1, new[] { DungeonCell.Floor, DungeonCell.Corridor });
            DungeonSurfaceBuildOptions options = new DungeonSurfaceBuildOptions(false, true, false, true, false);
            DungeonSurfaceMeshData data = DungeonSurfaceMeshBuilder.BuildData(world, 1f, DungeonSurfaceMeshBuilder.DefaultWallHeight, options);

            Assert.That(data.SubmeshTriangles[DungeonSurfaceMeshBuilder.RoomFloorSubmesh], Is.Empty);
            Assert.That(data.SubmeshTriangles[DungeonSurfaceMeshBuilder.CorridorFloorSubmesh], Is.Not.Empty);
            Assert.That(data.SubmeshTriangles[DungeonSurfaceMeshBuilder.WallSubmesh], Is.Empty);
            Assert.That(data.SubmeshTriangles[DungeonSurfaceMeshBuilder.TrimSubmesh], Is.Not.Empty);
            Assert.That(data.SubmeshTriangles[DungeonSurfaceMeshBuilder.DoorwaySubmesh], Is.Empty);
            Assert.That(data.BoundaryEdgeCount, Is.EqualTo(6));
            Assert.That(data.DoorwayCount, Is.EqualTo(1));
        }

        [Test]
        public void ArchitectureLayout_MapsEverySupportedModuleDeterministically()
        {
            DungeonWorldData world = World(2, 1, new[] { DungeonCell.Floor, DungeonCell.Corridor });
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader) { enableInstancing = true };
            Mesh mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            BiomeVisualProfile profile = ScriptableObject.CreateInstance<BiomeVisualProfile>();
            profile.profileId = "dungeon.architecture.test";
            profile.profileVersion = 1;
            profile.worldType = PCGRequest.DungeonWorldType;
            profile.categories = new[]
            {
                Category(VisualCategoryIds.DungeonRoomFloors, mesh, material), Category(VisualCategoryIds.DungeonCorridorFloors, mesh, material),
                Category(VisualCategoryIds.DungeonStraightWalls, mesh, material), Category(VisualCategoryIds.DungeonWallCorners, mesh, material),
                Category(VisualCategoryIds.DungeonDoorways, mesh, material), Category(VisualCategoryIds.DungeonWallTrims, mesh, material)
            };

            List<ResolvedVisualPlacement> first = DungeonArchitectureLayoutBuilder.Build(world, profile, 1f, DungeonSurfaceMeshBuilder.DefaultWallHeight);
            List<ResolvedVisualPlacement> second = DungeonArchitectureLayoutBuilder.Build(world, profile, 1f, DungeonSurfaceMeshBuilder.DefaultWallHeight);

            Assert.That(first.Count, Is.EqualTo(19));
            Assert.That(VisualLayoutHasher.Compute(profile, first), Is.EqualTo(VisualLayoutHasher.Compute(profile, second)));
            Assert.That(first.FindAll(item => item.CategoryId == VisualCategoryIds.DungeonStraightWalls).Count, Is.EqualTo(6));
            Assert.That(first.FindAll(item => item.CategoryId == VisualCategoryIds.DungeonWallCorners).Count, Is.EqualTo(4));
            Assert.That(first.FindAll(item => item.CategoryId == VisualCategoryIds.DungeonDoorways).Count, Is.EqualTo(1));

            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(material);
        }

        private static VisualCategory Category(string id, Mesh mesh, Material material)
        {
            VisualRenderPart part = new VisualRenderPart { stableId = id + "/part", mesh = mesh, subMeshIndex = 0, material = material, localScale = Vector3.one, localRotation = Quaternion.identity };
            VisualVariant variant = new VisualVariant { stableId = id + "/sample", parts = new[] { part }, fitMode = VisualFitMode.StretchToFootprint, sourceBoundsSize = Vector3.one, uniformScaleRange = Vector2.one };
            return new VisualCategory { categoryId = id, variants = new[] { variant }, placementRules = new VisualPlacementRules { randomYaw = false, yawStep = 0f, uniformScaleRange = Vector2.one } };
        }

        private static DungeonWorldData World(int width, int height, DungeonCell[] cells)
        {
            byte[] raw = new byte[cells.Length];
            for (int index = 0; index < cells.Length; index++) raw[index] = (byte)cells[index];
            return new DungeonWorldData(width, height, 7, raw, new List<IntRect> { new IntRect(0, 0, width, height) }, new List<DungeonConnection>(), 0, 0, new List<int>(), new List<DungeonPropPlacement>());
        }
    }
}
