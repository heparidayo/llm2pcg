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
using Object=UnityEngine.Object;
namespace Llm2Pcg.Tests.EditMode
{
    public sealed class ForestWoodlandTests
    {
        private static BiomeWorldData World(int seed,int size)
        {
            var request=PCGRequest.CreateForestDefault(seed,size,size);
            return (BiomeWorldData)PCGGeneratorRegistry.Default.GetRequired(request).Generate(request);
        }
        [Test]
        public void Catalog_HasThirteenModelsAndCheaperLods()
        {
            var p=VisualProfileLoader.Load("Forest");
            Assert.That(p.profileId,Is.EqualTo("forest.woodland"));
            var variants=p.categories.SelectMany(c=>c.variants).ToArray();
            Assert.That(variants.Length,Is.EqualTo(13));
            foreach(var v in variants)
            {
                Assert.That(v.LodTierCount,Is.EqualTo(2));
                Assert.That(v.GetLodParts(1)[0].mesh.vertexCount,Is.LessThan(v.GetLodParts(0)[0].mesh.vertexCount));
                Assert.That(v.pivotOffset.x,Is.Zero);Assert.That(v.pivotOffset.z,Is.Zero);
                foreach(var tier in v.lodTiers)
                {
                    Assert.That(tier.parts.Length,Is.EqualTo(1));
                    Assert.That(tier.parts[0].material.enableInstancing,Is.True);
                    Assert.That(tier.parts[0].mesh.subMeshCount,Is.EqualTo(1));
                }
            }
            foreach(string kind in new[]{"Ground","Path","Underwater","Water"})
                Assert.That(Resources.Load<Material>("PCGSurfaceMaterials/ForestWoodland/"+kind)?.mainTexture,Is.Not.Null);
        }
        [TestCase("broadleaf")]
        [TestCase("conifer")]
        [TestCase("cherry_blossom")]
        [TestCase("willow")]
        [TestCase("dead_tree")]
        public void TreeTypeFiltersRemainAvailable(string type)
        {
            var w=World(234,64);var p=VisualProfileLoader.Load("Forest");var settings=PCGRequest.DefaultVisuals();
            settings.trees.allowedTypes=new[]{type};settings.trees.maxCount=12;settings.trees.density=.5f;
            var trees=VisualWorldLayoutBuilder.BuildForestTrees(w,p,settings);
            Assert.That(trees.Count,Is.InRange(1,12));
            foreach(var tree in trees)Assert.That(VisualVariantResolver.MatchesAnyAllowedType(tree.Variant,new[]{type}),Is.True);
            Assert.That(w.ComputeStableHash(),Is.EqualTo("58D24C18"));
        }
        [TestCase(234,64,"58D24C18")]
        [TestCase(24680,128,"2418314A")]
        [TestCase(-17,32,null)]
        public void TreeCollisionAndWaterDetailsPreserveWorld(int seed,int size,string golden)
        {
            var w=World(seed,size);string hash=w.ComputeStableHash();var p=VisualProfileLoader.Load("Forest");
            var trees=VisualWorldLayoutBuilder.BuildForestTrees(w,p);
            var collisions=VisualWorldLayoutBuilder.BuildForestTreeCollisions(w,p);
            Assert.That(collisions.Count,Is.EqualTo(trees.Count));
            for(int i=0;i<trees.Count;i++)
            {
                Vector3 pos=trees[i].WorldMatrix.GetColumn(3);
                Assert.That(collisions[i].X,Is.EqualTo(pos.x));Assert.That(collisions[i].Z,Is.EqualTo(pos.z));
                Assert.That(collisions[i].Radius,Is.EqualTo(.3f).Within(.001));
            }
            var water=ForestWoodlandDetails.Build(w,p);
            Assert.That(water.Count,Is.LessThanOrEqualTo(ForestWoodlandDetails.Maximum));
            foreach(var detail in water)Assert.That(w.Tiles[detail.Y*w.Width+detail.X],Is.EqualTo(BiomeTile.Water));
            Assert.That(VisualLayoutHasher.Compute(p,water),Is.EqualTo(VisualLayoutHasher.Compute(p,ForestWoodlandDetails.Build(w,p))));
            var settings=PCGRequest.DefaultVisuals();settings.waterProps.density=0;
            Assert.That(ForestWoodlandDetails.Build(w,p,settings),Is.Empty);
            settings.waterProps.density=1;settings.waterProps.maxCount=2;
            Assert.That(ForestWoodlandDetails.Build(w,p,settings).Count,Is.LessThanOrEqualTo(2));
            Assert.That(w.ComputeStableHash(),Is.EqualTo(hash));if(golden!=null)Assert.That(hash,Is.EqualTo(golden));
        }
        [Test]
        public void RendererLodAndPropsOffKeepTheSurface()
        {
            var host=new GameObject("Woodland renderer test");var ch=new GameObject("Woodland LOD camera");
            try
            {
                var w=World(234,64);var r=host.AddComponent<BiomeInstancedRenderer>();r.Render(w);
                var surface=r.ForestSurfaceHash;var layout=r.VisualLayoutHash;
                r.RefreshVisualLod(null,true);long near=r.EstimatedVisualVertexCount;
                var camera=ch.AddComponent<Camera>();camera.transform.position=Vector3.one*10000;
                r.RefreshVisualLod(camera,true);
                Assert.That(r.EstimatedVisualVertexCount,Is.LessThan(near));
                Assert.That(r.RefreshVisualLod(camera),Is.False);
                Assert.That(r.VisualLayoutHash,Is.EqualTo(layout));Assert.That(r.ForestSurfaceHash,Is.EqualTo(surface));
                var settings=PCGRequest.DefaultPresentation();settings.propsEnabled=false;r.Render(w,null,settings);
                Assert.That(r.VisualPlacementCount,Is.Zero);Assert.That(r.ForestSurfaceHash,Is.EqualTo(surface));
            }
            finally {Object.DestroyImmediate(host);Object.DestroyImmediate(ch);}
        }
        [Test]
        public void ForestPresentationRestoresEverySavedSettingOnDisable()
        {
            var host=new GameObject("Woodland framer");var ch=new GameObject("Woodland camera");
            var pipeline=QualitySettings.renderPipeline;var sky=RenderSettings.ambientSkyColor;
            var equator=RenderSettings.ambientEquatorColor;var ground=RenderSettings.ambientGroundColor;
            var mode=RenderSettings.ambientMode;bool fog=RenderSettings.fog;var fogMode=RenderSettings.fogMode;
            var fogColor=RenderSettings.fogColor;float start=RenderSettings.fogStartDistance,end=RenderSettings.fogEndDistance;
            PCGDungeonCameraFramer framer=null;
            try
            {
                var camera=ch.AddComponent<Camera>();var background=camera.backgroundColor;var flags=camera.clearFlags;
                framer=host.AddComponent<PCGDungeonCameraFramer>();var so=new UnityEditor.SerializedObject(framer);
                so.FindProperty("targetCamera").objectReferenceValue=camera;so.ApplyModifiedPropertiesWithoutUndo();
                framer.SetFramingSuspended(true);framer.FrameWorld(World(234,64));
                Assert.That(QualitySettings.renderPipeline,Is.SameAs(Resources.Load<RenderPipelineAsset>("PCGPresentation/ForestWoodlandPipeline")));
                Assert.That(camera.transform.position,Is.EqualTo(Vector3.zero));Assert.That(RenderSettings.fog,Is.True);
                typeof(PCGDungeonCameraFramer).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(framer,null);
                Assert.That(QualitySettings.renderPipeline,Is.SameAs(pipeline));Assert.That(camera.backgroundColor,Is.EqualTo(background));
                Assert.That(camera.clearFlags,Is.EqualTo(flags));Assert.That(RenderSettings.ambientMode,Is.EqualTo(mode));
                Assert.That(RenderSettings.ambientSkyColor,Is.EqualTo(sky));Assert.That(RenderSettings.ambientEquatorColor,Is.EqualTo(equator));
                Assert.That(RenderSettings.ambientGroundColor,Is.EqualTo(ground));Assert.That(RenderSettings.fog,Is.EqualTo(fog));
                Assert.That(RenderSettings.fogMode,Is.EqualTo(fogMode));Assert.That(RenderSettings.fogColor,Is.EqualTo(fogColor));
                Assert.That(RenderSettings.fogStartDistance,Is.EqualTo(start));Assert.That(RenderSettings.fogEndDistance,Is.EqualTo(end));
            }
            finally
            {
                if(framer!=null)typeof(PCGDungeonCameraFramer).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(framer,null);
                Object.DestroyImmediate(host);Object.DestroyImmediate(ch);QualitySettings.renderPipeline=pipeline;
            }
        }
    }
}
