using System.Linq;
using System.Reflection;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Presentation;
using Llm2Pcg.Rendering;
using Llm2Pcg.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class NatureBiomesVisualTests
    {
        private static PCGRequest Request(string world,int seed,int size) =>
            world=="Desert" ? PCGRequest.CreateDesertDefault(seed,size,size) :
            world=="Snowfield" ? PCGRequest.CreateSnowfieldDefault(seed,size,size) :
            PCGRequest.CreateSwampDefault(seed,size,size);

        [TestCase("Desert",7)]
        [TestCase("Snowfield",5)]
        [TestCase("Swamp",6)]
        public void AuthoredCatalog_HasValidDistinctModelsAndCheaperLods(string world,int count)
        {
            var profile=VisualProfileLoader.Load(world);
            Assert.That(profile.profileId,Is.EqualTo(world.ToLowerInvariant()+".nature-biomes"));
            var variants=profile.categories.SelectMany(c=>c.variants).ToArray();
            Assert.That(variants.Select(v=>v.stableId).Distinct().Count(),Is.EqualTo(count));
            foreach(var v in variants)
            {
                Assert.That(v.LodTierCount,Is.EqualTo(2));
                Assert.That(v.GetLodParts(1)[0].mesh.vertexCount,Is.LessThan(v.GetLodParts(0)[0].mesh.vertexCount),v.stableId);
                Assert.That(v.sourceBoundsSize.y,Is.GreaterThan(.005f).And.LessThan(8),v.stableId);
                Assert.That(v.pivotOffset.x,Is.Zero);
                Assert.That(v.pivotOffset.z,Is.Zero);
                foreach(var tier in v.lodTiers)
                {
                    Assert.That(tier.parts.Length,Is.EqualTo(1));
                    Assert.That(tier.parts[0].material,Is.SameAs(variants[0].parts[0].material));
                    Assert.That(tier.parts[0].material.enableInstancing,Is.True);
                    Assert.That(tier.parts[0].mesh.subMeshCount,Is.EqualTo(1));
                }
            }
            foreach(string suffix in new[]{"Ground","Path","Underwater","Water"})
            {
                var mat=Resources.Load<Material>("PCGSurfaceMaterials/NatureBiomes/"+world+suffix);
                Assert.That(mat,Is.Not.Null);
                Assert.That(mat.mainTexture,Is.Not.Null);
                Assert.That(mat.shader.name,Is.EqualTo("Universal Render Pipeline/Lit"));
            }
        }

        [TestCase("Desert",234,64,"F5EC6781")]
        [TestCase("Snowfield",234,64,"8148DB6B")]
        [TestCase("Swamp",234,64,"E74C0EA8")]
        [TestCase("Desert",24680,128,null)]
        [TestCase("Snowfield",24680,128,null)]
        [TestCase("Swamp",24680,128,null)]
        [TestCase("Desert",-17,32,null)]
        [TestCase("Snowfield",-17,32,null)]
        [TestCase("Swamp",-17,32,null)]
        public void Visuals_PreserveWorldSurfaceAndStableTreeCollisions(string type,int seed,int size,string golden)
        {
            var request=Request(type,seed,size);
            var world=(BiomeWorldData)PCGGeneratorRegistry.Default.GetRequired(request).Generate(request);
            string hash=world.ComputeStableHash();
            string surface=ForestSurfaceMeshBuilder.BuildData(world).StableHash;
            var profile=VisualProfileLoader.Load(type);
            var trees=VisualWorldLayoutBuilder.BuildNaturePrimaryVegetation(world,profile);
            var decorations=VisualWorldLayoutBuilder.BuildNatureDecorations(world,profile);
            var collisions=VisualWorldLayoutBuilder.BuildNatureTreeCollisions(world,profile);
            Assert.That(collisions.Count,Is.EqualTo(trees.Count));
            Assert.That(decorations.Count,Is.LessThanOrEqualTo(96+128+384+96));
            for(int i=0;i<trees.Count;i++)
            {
                Vector3 p=trees[i].WorldMatrix.GetColumn(3);
                Assert.That(collisions[i].X,Is.EqualTo(p.x).Within(.0001));
                Assert.That(collisions[i].Z,Is.EqualTo(p.z).Within(.0001));
                Assert.That(PCGFirstPersonTestDummy.CanOccupy(world,p,p.y,.2f,.5f,collisions),Is.False);
            }
            var repeated=VisualWorldLayoutBuilder.BuildNaturePrimaryVegetation(world,profile);
            Assert.That(VisualLayoutHasher.Compute(profile,repeated),Is.EqualTo(VisualLayoutHasher.Compute(profile,trees)));
            Assert.That(ForestSurfaceMeshBuilder.BuildData(world).StableHash,Is.EqualTo(surface));
            Assert.That(world.ComputeStableHash(),Is.EqualTo(hash));
            if(golden!=null) Assert.That(hash,Is.EqualTo(golden));
            foreach(var p in new[]{world.StartPosition,world.ExitPosition})
            {
                float h=ForestSurfaceMeshBuilder.SampleSurfaceHeight(world,p.X,p.Y);
                Assert.That(PCGFirstPersonTestDummy.CanOccupy(world,new Vector3(p.X,h,p.Y),h,.2f,.5f,collisions),Is.True);
            }
        }

        [TestCase("Desert")]
        [TestCase("Snowfield")]
        [TestCase("Swamp")]
        public void Renderer_LodSwitchAndPropsOffPreserveSurface(string type)
        {
            var host=new GameObject("Nature LOD test");var cameraObject=new GameObject("Nature camera test");
            try
            {
                var request=Request(type,234,64);
                var world=(BiomeWorldData)PCGGeneratorRegistry.Default.GetRequired(request).Generate(request);
                var r=host.AddComponent<BiomeInstancedRenderer>();r.Render(world);
                string layout=r.VisualLayoutHash,surface=r.ForestSurfaceHash;
                r.RefreshVisualLod(null,true);long near=r.EstimatedVisualVertexCount;
                var camera=cameraObject.AddComponent<Camera>();camera.transform.position=Vector3.one*10000;
                r.RefreshVisualLod(camera,true);
                Assert.That(r.EstimatedVisualVertexCount,Is.LessThan(near));
                Assert.That(r.VisualLayoutHash,Is.EqualTo(layout));
                Assert.That(r.ForestSurfaceHash,Is.EqualTo(surface));
                Assert.That(r.RefreshVisualLod(camera),Is.False);
                Assert.That(r.MaximumVisualSubmissionSize,Is.LessThanOrEqualTo(1023));
                var presentation=PCGRequest.DefaultPresentation();presentation.propsEnabled=false;
                r.Render(world,null,presentation);
                Assert.That(r.VisualPlacementCount,Is.Zero);
                Assert.That(r.ForestSurfaceHash,Is.EqualTo(surface));
            }
            finally {Object.DestroyImmediate(host);Object.DestroyImmediate(cameraObject);}
        }

        [Test]
        public void Presentation_TransitionsRestoreForestCaveAndCityState()
        {
            var host=new GameObject("Nature presentation test");var cameraObject=new GameObject("Nature state camera");
            var pipeline=QualitySettings.renderPipeline;var ambient=RenderSettings.ambientMode;
            var sky=RenderSettings.ambientSkyColor;bool fog=RenderSettings.fog;
            PCGDungeonCameraFramer framer=null;
            try
            {
                var camera=cameraObject.AddComponent<Camera>();framer=host.AddComponent<PCGDungeonCameraFramer>();
                var serialized=new UnityEditor.SerializedObject(framer);
                serialized.FindProperty("targetCamera").objectReferenceValue=camera;serialized.ApplyModifiedPropertiesWithoutUndo();
                framer.SetFramingSuspended(true);
                Vector3 pose=camera.transform.position;
                foreach(string type in new[]{"Desert","Swamp","Snowfield","Desert"})
                {
                    var request=Request(type,234,32);
                    framer.FrameWorld(PCGGeneratorRegistry.Default.GetRequired(request).Generate(request));
                    Assert.That(QualitySettings.renderPipeline,Is.SameAs(Resources.Load<RenderPipelineAsset>("PCGPresentation/NatureBiomesPipeline")));
                    Assert.That(camera.transform.position,Is.EqualTo(pose));
                    Assert.That(RenderSettings.fog,Is.True);
                }
                foreach(var request in new[]{PCGRequest.CreateCaveDefault(234,32,32),PCGRequest.CreateCityDefault(234,32,32),PCGRequest.CreateForestDefault(234,32,32)})
                    framer.FrameWorld(PCGGeneratorRegistry.Default.GetRequired(request).Generate(request));
                Assert.That(QualitySettings.renderPipeline,Is.SameAs(Resources.Load<RenderPipelineAsset>("PCGPresentation/ForestWoodlandPipeline")));
                typeof(PCGDungeonCameraFramer).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(framer,null);
                Assert.That(QualitySettings.renderPipeline,Is.SameAs(pipeline));
                Assert.That(RenderSettings.ambientMode,Is.EqualTo(ambient));
                Assert.That(RenderSettings.ambientSkyColor,Is.EqualTo(sky));
                Assert.That(RenderSettings.fog,Is.EqualTo(fog));
            }
            finally
            {
                if(framer!=null) typeof(PCGDungeonCameraFramer).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(framer,null);
                Object.DestroyImmediate(host);Object.DestroyImmediate(cameraObject);QualitySettings.renderPipeline=pipeline;
            }
        }
    }
}
