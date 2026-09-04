using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.Cave;
using Llm2Pcg.Generators.Dungeon;
using Llm2Pcg.Generators.Forest;
using Llm2Pcg.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class PresentationRenderingTests
    {
        [TestCase(PresentationGeometryModes.Voxel, false)]
        [TestCase(PresentationGeometryModes.Voxel, true)]
        [TestCase(PresentationGeometryModes.Surface, false)]
        [TestCase(PresentationGeometryModes.Surface, true)]
        public void DungeonRenderer_SupportsEveryPresentationCombination(string geometryMode, bool propsEnabled)
        {
            GameObject host = new GameObject("DungeonPresentationRendererTest");
            try
            {
                DungeonInstancedRenderer renderer = host.AddComponent<DungeonInstancedRenderer>();
                DungeonWorldData world = new DungeonBSPGenerator().Generate(PCGRequest.CreateDefault(234, 48, 48));
                renderer.Render(world, Mode(geometryMode, propsEnabled));

                Assert.That(renderer.PresentationGeometryMode, Is.EqualTo(geometryMode));
                Assert.That(renderer.PresentationPropsEnabled, Is.EqualTo(propsEnabled));
                AssertGeometry(renderer.VoxelGeometryInstanceCount, renderer.DungeonSurfaceVertexCount, geometryMode);
                if (!propsEnabled)
                {
                    Assert.That(renderer.DungeonArchitectureInstanceCount, Is.Zero);
                    Assert.That(renderer.VisualInstanceCount, Is.Zero);
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(PresentationGeometryModes.Voxel, false)]
        [TestCase(PresentationGeometryModes.Surface, false)]
        public void CaveRenderer_SwitchesGeometryAndSuppressesProps(string geometryMode, bool propsEnabled)
        {
            GameObject host = new GameObject("CavePresentationRendererTest");
            try
            {
                CaveInstancedRenderer renderer = host.AddComponent<CaveInstancedRenderer>();
                CaveWorldData world = new CaveCellularGenerator().GenerateCave(PCGRequest.CreateCaveDefault(234, 48, 48));
                renderer.Render(world, Mode(geometryMode, propsEnabled));

                Assert.That(renderer.PresentationGeometryMode, Is.EqualTo(geometryMode));
                Assert.That(renderer.PresentationPropsEnabled, Is.False);
                AssertGeometry(renderer.VoxelGeometryInstanceCount, renderer.CaveSurfaceVertexCount, geometryMode);
                Assert.That(renderer.VisualInstanceCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(PresentationGeometryModes.Voxel, false)]
        [TestCase(PresentationGeometryModes.Surface, false)]
        public void ForestRenderer_SwitchesGeometryAndSuppressesProps(string geometryMode, bool propsEnabled)
        {
            GameObject host = new GameObject("ForestPresentationRendererTest");
            try
            {
                PCGRequest request = PCGRequest.CreateForestDefault(234, 48, 48);
                BiomeWorldData world = new ForestBiomeGenerator().GenerateForest(request);
                BiomeInstancedRenderer renderer = host.AddComponent<BiomeInstancedRenderer>();
                renderer.Render(world, request.visualSettings, Mode(geometryMode, propsEnabled));

                Assert.That(renderer.PresentationGeometryMode, Is.EqualTo(geometryMode));
                Assert.That(renderer.PresentationPropsEnabled, Is.False);
                if (geometryMode == PresentationGeometryModes.Voxel)
                {
                    Assert.That(renderer.PrimitiveInstanceCount, Is.GreaterThan(0));
                    Assert.That(renderer.ForestSurfaceVertexCount, Is.Zero);
                }
                else
                {
                    Assert.That(renderer.PrimitiveInstanceCount, Is.EqualTo(1));
                    Assert.That(renderer.ForestSurfaceVertexCount, Is.GreaterThan(0));
                }
                Assert.That(renderer.VisualInstanceCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static PresentationSettings Mode(string geometryMode, bool propsEnabled)
        {
            return new PresentationSettings { geometryMode = geometryMode, propsEnabled = propsEnabled };
        }

        private static void AssertGeometry(int voxelCount, int surfaceVertexCount, string geometryMode)
        {
            if (geometryMode == PresentationGeometryModes.Voxel)
            {
                Assert.That(voxelCount, Is.GreaterThan(0));
                Assert.That(surfaceVertexCount, Is.Zero);
            }
            else
            {
                Assert.That(voxelCount, Is.Zero);
                Assert.That(surfaceVertexCount, Is.GreaterThan(0));
            }
        }
    }
}
