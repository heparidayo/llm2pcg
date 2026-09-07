using System.Linq;
using System.Reflection;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.Dungeon;
using Llm2Pcg.Presentation;
using Llm2Pcg.Rendering;
using Llm2Pcg.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class DungeonCitadelTests
    {
        [Test]
        public void Catalog_HasNineAuthoredModelsAndCheaperLods()
        {
            var p = VisualProfileLoader.Load("Dungeon");
            Assert.That(p.profileId, Is.EqualTo("dungeon.citadel"));
            var variants = p.categories.SelectMany(c => c.variants).ToArray();
            Assert.That(variants.Select(v => v.stableId).Distinct().Count(), Is.EqualTo(9));
            foreach (var v in variants)
            {
                Assert.That(v.LodTierCount, Is.EqualTo(2));
                Assert.That(v.fitMode, Is.EqualTo(VisualFitMode.StretchToFootprint));
                Assert.That(v.GetLodParts(1)[0].mesh.vertexCount, Is.LessThan(v.GetLodParts(0)[0].mesh.vertexCount));
                foreach (var tier in v.lodTiers)
                {
                    Assert.That(tier.parts.Length, Is.EqualTo(1));
                    Assert.That(tier.parts[0].mesh.subMeshCount, Is.EqualTo(1));
                    Assert.That(tier.parts[0].material.enableInstancing, Is.True);
                }
            }
            foreach (string kind in new[] {"RoomFloor","CorridorFloor","Wall","Trim","Doorway"})
                Assert.That(Resources.Load<Material>("PCGSurfaceMaterials/DungeonCitadel/" + kind)?.mainTexture, Is.Not.Null);
        }

        [Test]
        public void Doorway_BothLodsKeepAnEyeHeightOpening()
        {
            var v = VisualProfileLoader.Load("Dungeon").FindCategory(VisualCategoryIds.DungeonDoorways).variants[0];
            foreach (var tier in v.lodTiers)
            foreach (var part in tier.parts)
            foreach (var point in part.mesh.vertices)
            {
                Vector3 p = part.LocalMatrix.MultiplyPoint3x4(point);
                if (p.y < 2.05f) Assert.That(Mathf.Abs(p.x), Is.GreaterThanOrEqualTo(.395f));
            }
        }

        [TestCase(234,64,"A140F747")]
        [TestCase(24680,128,null)]
        [TestCase(-17,32,null)]
        public void ArchitectureAndSconces_AreStableWithoutChangingCore(int seed,int size,string golden)
        {
            var w = new DungeonBSPGenerator().Generate(PCGRequest.CreateDefault(seed,size,size));
            string hash = w.ComputeStableHash();
            var p = VisualProfileLoader.Load("Dungeon");
            var a = DungeonArchitectureLayoutBuilder.Build(w,p,1f,2.6f);
            var b = DungeonArchitectureLayoutBuilder.Build(w,p,1f,2.6f);
            var sconces = DungeonCitadelDetails.Build(w,p,1f);
            Assert.That(VisualLayoutHasher.Compute(p,a), Is.EqualTo(VisualLayoutHasher.Compute(p,b)));
            Assert.That(VisualLayoutHasher.Compute(p,sconces), Is.EqualTo(VisualLayoutHasher.Compute(p,DungeonCitadelDetails.Build(w,p,1f))));
            Assert.That(sconces.Count, Is.InRange(1,DungeonCitadelDetails.MaximumSconces));
            Assert.That(a.Count(x => x.CategoryId == VisualCategoryIds.DungeonStraightWalls), Is.EqualTo(DungeonSurfaceMeshBuilder.GetBoundarySegments(w).Count));
            Assert.That(a.Count(x => x.CategoryId == VisualCategoryIds.DungeonDoorways), Is.EqualTo(DungeonSurfaceMeshBuilder.GetDoorwaySegments(w).Count));
            Assert.That(w.Props.Count, Is.Zero);
            Assert.That(w.ComputeStableHash(), Is.EqualTo(hash));
            if (golden != null) Assert.That(hash, Is.EqualTo(golden));
        }

        [Test]
        public void Renderer_LodsPropsOffVoxelAndClearRemainConsistent()
        {
            var host = new GameObject("Citadel renderer test");
            var cameraHost = new GameObject("Citadel test camera");
            try
            {
                var w = new DungeonBSPGenerator().Generate(PCGRequest.CreateDefault(234,64,64));
                var r = host.AddComponent<DungeonInstancedRenderer>(); r.Render(w);
                string hash = r.VisualLayoutHash, surface = r.DungeonSurfaceHash;
                Assert.That(r.DungeonArchitectureInstanceCount, Is.GreaterThan(0));
                r.RefreshVisualLod(null,true); long near = r.EstimatedVisualVertexCount;
                var camera = cameraHost.AddComponent<Camera>(); camera.transform.position = Vector3.one*10000;
                r.RefreshVisualLod(camera,true);
                Assert.That(r.EstimatedVisualVertexCount, Is.LessThan(near));
                Assert.That(r.GetLodPlacementCount(1), Is.EqualTo(r.VisualPlacementCount));
                Assert.That(r.RefreshVisualLod(camera), Is.False);
                Assert.That(r.VisualLayoutHash, Is.EqualTo(hash)); Assert.That(r.DungeonSurfaceHash, Is.EqualTo(surface));
                Assert.That(r.MaximumVisualSubmissionSize, Is.LessThanOrEqualTo(1023));
                var settings = PCGRequest.DefaultPresentation(); settings.propsEnabled = false; r.Render(w,settings);
                Assert.That(r.VisualPlacementCount, Is.Zero); Assert.That(r.DungeonSurfaceVertexCount, Is.GreaterThan(0));
                settings.propsEnabled = true; settings.geometryMode = PresentationGeometryModes.Voxel; r.Render(w,settings);
                Assert.That(r.DungeonArchitectureInstanceCount, Is.Zero); Assert.That(r.VoxelGeometryInstanceCount, Is.GreaterThan(0));
                r.Clear();
                Assert.That(r.VisualPlacementCount, Is.Zero); Assert.That(r.GetLodPlacementCount(1), Is.Zero);
                Assert.That(r.ActiveTorchLightCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(cameraHost); }
        }

        [Test]
        public void Presentation_RestoresBaselineAcrossAllBiomeFamilies()
        {
            var host = new GameObject("Citadel state test"); var ch = new GameObject("Citadel camera test");
            var pipeline = QualitySettings.renderPipeline; var ambient = RenderSettings.ambientMode;
            var sky = RenderSettings.ambientSkyColor; bool fog = RenderSettings.fog;
            PCGDungeonCameraFramer framer = null;
            try
            {
                var camera = ch.AddComponent<Camera>(); framer = host.AddComponent<PCGDungeonCameraFramer>();
                var s = new UnityEditor.SerializedObject(framer);s.FindProperty("targetCamera").objectReferenceValue=camera;s.ApplyModifiedPropertiesWithoutUndo();
                framer.SetFramingSuspended(true);
                foreach (var request in new[] {PCGRequest.CreateDefault(234,32,32),PCGRequest.CreateDesertDefault(234,32,32),
                    PCGRequest.CreateDefault(234,32,32),PCGRequest.CreateCaveDefault(234,32,32),
                    PCGRequest.CreateDefault(234,32,32),PCGRequest.CreateCityDefault(234,32,32),PCGRequest.CreateForestDefault(234,32,32)})
                {
                    framer.FrameWorld(PCGGeneratorRegistry.Default.GetRequired(request).Generate(request));
                    Assert.That(camera.transform.position, Is.EqualTo(Vector3.zero));
                }
                Assert.That(QualitySettings.renderPipeline, Is.SameAs(Resources.Load<RenderPipelineAsset>("PCGPresentation/ForestWoodlandPipeline")));
                typeof(PCGDungeonCameraFramer).GetMethod("OnDisable",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(framer,null);
                Assert.That(QualitySettings.renderPipeline, Is.SameAs(pipeline));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(ambient));
                Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(sky)); Assert.That(RenderSettings.fog, Is.EqualTo(fog));
            }
            finally
            {
                if(framer!=null)typeof(PCGDungeonCameraFramer).GetMethod("OnDisable",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(framer,null);
                Object.DestroyImmediate(host); Object.DestroyImmediate(ch); QualitySettings.renderPipeline=pipeline;
            }
        }
    }
}
