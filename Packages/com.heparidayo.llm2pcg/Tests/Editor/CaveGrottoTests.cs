using System;
using System.Collections.Generic;
using System.Linq;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.Cave;
using Llm2Pcg.Presentation;
using Llm2Pcg.Rendering;
using Llm2Pcg.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class CaveGrottoTests
    {
        [Test]
        public void Catalog_IsAuthoredBoundedAndHasCheaperLods()
        {
            var profile=VisualProfileLoader.Load("Cave");
            Assert.That(profile.profileId,Is.EqualTo("cave.crystal-grotto"));
            var variants=profile.categories.SelectMany(c=>c.variants).ToArray();
            Assert.That(variants.Length,Is.EqualTo(8));
            var material=variants[0].parts[0].material;
            foreach(var v in variants)
            {
                Assert.That(v.LodTierCount,Is.EqualTo(2));
                Assert.That(v.GetLodParts(1)[0].mesh.vertexCount,Is.LessThan(v.GetLodParts(0)[0].mesh.vertexCount));
                Assert.That(v.sourceBoundsSize.y,Is.GreaterThan(.01f).And.LessThan(3.5f));
                foreach(var tier in v.lodTiers)
                {
                    Assert.That(tier.parts.Length,Is.EqualTo(1));
                    Assert.That(tier.parts[0].mesh.subMeshCount,Is.EqualTo(1));
                    Assert.That(tier.parts[0].material,Is.SameAs(material));
                }
            }
            Assert.That(material.IsKeywordEnabled("_EMISSION"),Is.True);
            Assert.That(material.GetTexture("_EmissionMap"),Is.Not.Null);
        }

        [TestCase(234,32)]
        [TestCase(234,64)]
        [TestCase(24680,128)]
        [TestCase(24681,128)]
        [TestCase(-17,64)]
        public void Layout_IsDeterministicAndPreservesNavigation(int seed,int size)
        {
            var world=new CaveCellularGenerator().GenerateCave(PCGRequest.CreateCaveDefault(seed,size,size));
            string before=world.ComputeStableHash();
            var profile=VisualProfileLoader.Load("Cave");
            var placements=VisualWorldLayoutBuilder.BuildCaveDetails(world,profile);
            Assert.That(VisualLayoutHasher.Compute(profile,placements),
                Is.EqualTo(VisualLayoutHasher.Compute(profile,VisualWorldLayoutBuilder.BuildCaveDetails(world,profile))));
            Assert.That(placements.Count,Is.LessThanOrEqualTo(306));
            Assert.That(placements.Count(p=>p.CategoryId==VisualCategoryIds.CaveEntrances),Is.EqualTo(2));
            foreach(var p in placements.Where(p=>p.CategoryId==VisualCategoryIds.CaveCrystals))
            {
                // Compare transformed local bounds center: pivot correction must not drift roots.
                var center=p.WorldMatrix.MultiplyPoint3x4(p.Variant.lodReferencePoint);
                float distance=new Vector2(center.x-p.X,center.z-p.Y).magnitude;
                Assert.That(distance,Is.EqualTo(.54f).Within(.002f));
            }
            foreach(var point in new[]{world.StartPosition,world.ExitPosition})
                Assert.That(PCGFirstPersonTestDummy.CanOccupy(world,new Vector3(point.X,0,point.Y),0,.2f,.5f),Is.True);
            Assert.That(world.ComputeStableHash(),Is.EqualTo(before));
            if(seed==234 && size==64) Assert.That(before,Is.EqualTo("47F4F069"));
            if(seed==24680 && size==128) Assert.That(before,Is.EqualTo("A2EF3948"));
        }

        [Test]
        public void WallBands_StayOutsideSingleCellCorridorAndHaveSharedSeams()
        {
            bool[] solid={true,true,true,true,false,true,true,true,true};
            var world=new CaveWorldData(3,3,17,solid,new Int2(1,1),new Int2(1,1),new List<Int2>());
            var data=CaveSurfaceMeshBuilder.BuildData(world);
            int first=2*data.GridWidth*data.GridHeight+data.SubmeshTriangles[1].Length;
            int bands=CaveSurfaceMeshBuilder.WallBandCount;
            for(int edge=0;edge<8;edge++)
            {
                int start=first+edge*bands*4;
                Vector3 a=data.Vertices[start],b=data.Vertices[start+1];
                for(int band=0;band<bands;band++)
                {
                    int i=start+band*4;
                    foreach(int offset in new[]{0,1,2,3})
                    {
                        Vector3 p=data.Vertices[i+offset];
                        // The x/z projection cannot move closer to the open cell's center.
                        Vector2 basePoint=offset==0 || offset==3 ? new Vector2(a.x,a.z) : new Vector2(b.x,b.z);
                        Assert.That(Vector2.Distance(new Vector2(p.x,p.z),Vector2.one),
                            Is.GreaterThanOrEqualTo(Vector2.Distance(basePoint,Vector2.one)-.0001f));
                    }
                    if(band+1<bands)
                    {
                        Assert.That(data.Vertices[i+3],Is.EqualTo(data.Vertices[i+4]));
                        Assert.That(data.Vertices[i+2],Is.EqualTo(data.Vertices[i+5]));
                    }
                    Assert.That(data.Normals[i].sqrMagnitude,Is.EqualTo(1).Within(.001f));
                }
            }
        }

        [Test]
        public void LodSwitching_IsCheaperAndDoesNotChangeLayout()
        {
            var host=new GameObject("Cave LOD test");
            var cameraObject=new GameObject("Cave LOD camera");
            try
            {
                var renderer=host.AddComponent<CaveInstancedRenderer>();
                var camera=cameraObject.AddComponent<Camera>();
                renderer.Render(new CaveCellularGenerator().GenerateCave(PCGRequest.CreateCaveDefault(234,64,64)));
                string hash=renderer.VisualLayoutHash;
                renderer.RefreshVisualLod(null,true);
                long detailed=renderer.EstimatedVisualVertexCount;
                camera.transform.position=new Vector3(1000,1000,1000);
                renderer.RefreshVisualLod(camera,true);
                Assert.That(renderer.GetLodPlacementCount(0),Is.Zero);
                Assert.That(renderer.EstimatedVisualVertexCount,Is.LessThan(detailed));
                Assert.That(renderer.VisualLayoutHash,Is.EqualTo(hash));
                Assert.That(renderer.RefreshVisualLod(camera),Is.False);
                Assert.That(renderer.MaximumVisualSubmissionSize,Is.LessThanOrEqualTo(1023));
                renderer.Clear();
                Assert.That(renderer.VisualInstanceCount,Is.Zero);
                Assert.That(renderer.GetLodPlacementCount(1),Is.Zero);
            }
            finally {Object.DestroyImmediate(host);Object.DestroyImmediate(cameraObject);}
        }

        [Test]
        public void CaveLighting_RestoresCameraAmbientFogAndQuality_WhenLeavingOrDisabling()
        {
            var host=new GameObject("Cave framer test");
            var cameraObject=new GameObject("Cave presentation camera");
            var pipeline=QualitySettings.renderPipeline;
            var ambient=RenderSettings.ambientMode;
            var sky=RenderSettings.ambientSkyColor;
            bool fog=RenderSettings.fog;
            try
            {
                var camera=cameraObject.AddComponent<Camera>();
                var framer=host.AddComponent<PCGDungeonCameraFramer>();
                var serialized=new UnityEditor.SerializedObject(framer);
                serialized.FindProperty("targetCamera").objectReferenceValue=camera;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var background=camera.backgroundColor;
                var cave=new CaveCellularGenerator().GenerateCave(PCGRequest.CreateCaveDefault(234,32,32));
                var forestRequest=PCGRequest.CreateForestDefault(234,32,32);
                var forest=PCGGeneratorRegistry.Default.GetRequired(forestRequest).Generate(forestRequest);
                framer.FrameWorld(cave);
                Assert.That(QualitySettings.renderPipeline,Is.SameAs(Resources.Load<RenderPipelineAsset>("PCGPresentation/CavePipeline")));
                Assert.That(RenderSettings.fog,Is.False);
                Vector3 pose=camera.transform.position;
                framer.SetFramingSuspended(true);
                Assert.That(RenderSettings.fog,Is.True);
                framer.FrameWorld(cave);
                Assert.That(camera.transform.position,Is.EqualTo(pose));
                framer.FrameWorld(forest);
                Assert.That(QualitySettings.renderPipeline,Is.SameAs(Resources.Load<RenderPipelineAsset>("PCGPresentation/ForestWoodlandPipeline")));
                Assert.That(camera.transform.position,Is.EqualTo(pose));
                InvokeDisable(framer);
                Assert.That(QualitySettings.renderPipeline,Is.SameAs(pipeline));
                Assert.That(RenderSettings.ambientMode,Is.EqualTo(ambient));
                Assert.That(RenderSettings.ambientSkyColor,Is.EqualTo(sky));
                Assert.That(RenderSettings.fog,Is.EqualTo(fog));
                Assert.That(camera.backgroundColor,Is.EqualTo(background));
                framer.FrameWorld(cave);
                // EditMode components have not entered the runtime lifecycle; invoke its callback explicitly.
                InvokeDisable(framer);
                framer.enabled=false;
                Assert.That(QualitySettings.renderPipeline,Is.SameAs(pipeline));
                Assert.That(RenderSettings.fog,Is.EqualTo(fog));
            }
            finally
            {
                InvokeDisable(host.GetComponent<PCGDungeonCameraFramer>());
                Object.DestroyImmediate(host);Object.DestroyImmediate(cameraObject);
                QualitySettings.renderPipeline=pipeline;
            }
        }

        private static void InvokeDisable(PCGDungeonCameraFramer framer)
        {
            // Unity 6000.5 rejects SendMessage lifecycle dispatch on non-playing behaviours.
            typeof(PCGDungeonCameraFramer).GetMethod("OnDisable",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(framer,null);
        }
    }
}
