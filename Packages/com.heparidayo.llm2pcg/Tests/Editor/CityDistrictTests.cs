using System;
using System.Linq;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.City;
using Llm2Pcg.Presentation;
using Llm2Pcg.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class CityDistrictTests
    {
        [Test]
        public void SuspendedFraming_KeepsCityQualityAndCameraPose_AndRestoresOtherBiomes()
        {
            var cameraObject = new GameObject("City suspension camera test");
            var host = new GameObject("City suspension framer test");
            var previousPipeline = QualitySettings.renderPipeline;
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var framer = host.AddComponent<PCGDungeonCameraFramer>();
                var serialized = new UnityEditor.SerializedObject(framer);
                serialized.FindProperty("targetCamera").objectReferenceValue = camera;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Vector3 position = new Vector3(12, 1.65f, 4);
                Quaternion rotation = Quaternion.Euler(0, 37, 0);
                camera.transform.SetPositionAndRotation(position, rotation);
                framer.SetFramingSuspended(true);
                var city = new CityHybridWfcGenerator().GenerateCity(PCGRequest.CreateCityDefault(234, 32, 32));
                framer.FrameWorld(city);
                Assert.That(QualitySettings.renderPipeline, Is.SameAs(Resources.Load<UnityEngine.Rendering.RenderPipelineAsset>("PCGPresentation/CityPipeline")));
                Assert.That(camera.transform.position, Is.EqualTo(position));
                Assert.That(Quaternion.Angle(camera.transform.rotation, rotation), Is.LessThan(.001f));
                var cave = PCGGeneratorRegistry.Default.GetRequired(PCGRequest.CreateCaveDefault(234,32,32)).Generate(PCGRequest.CreateCaveDefault(234,32,32));
                framer.FrameWorld(cave);
                Assert.That(QualitySettings.renderPipeline, Is.SameAs(Resources.Load<UnityEngine.Rendering.RenderPipelineAsset>("PCGPresentation/CavePipeline")));
                Assert.That(camera.transform.position, Is.EqualTo(position));
                framer.SetFramingSuspended(false);
                framer.FrameWorld(city);
                Assert.That(camera.transform.position, Is.Not.EqualTo(position));
                framer.FrameWorld(cave);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                QualitySettings.renderPipeline = previousPipeline;
            }
        }

        [Test]
        public void AuthoredCatalog_HasTwoLodsAndOneSharedMaterial()
        {
            var profile = VisualProfileLoader.Load("City");
            Assert.That(profile.profileId, Is.EqualTo("city.canal-district"));
            var variants = profile.categories.SelectMany(c => c.variants).ToArray();
            Assert.That(variants.Length, Is.EqualTo(12));
            var palette = variants[0].parts[0].material;
            foreach (var v in variants)
            {
                Assert.That(v.LodTierCount, Is.EqualTo(2));
                Assert.That(v.GetLodParts(1)[0].mesh.vertexCount, Is.LessThan(v.GetLodParts(0)[0].mesh.vertexCount), v.stableId);
                Assert.That(v.sourceBoundsSize.x, Is.GreaterThan(.1f).And.LessThan(6));
                Assert.That(v.sourceBoundsSize.y, Is.GreaterThan(.1f).And.LessThan(17));
                foreach (var tier in v.lodTiers)
                {
                    Assert.That(tier.parts.Length, Is.EqualTo(1));
                    Assert.That(tier.parts[0].material, Is.SameAs(palette));
                    Assert.That(tier.parts[0].mesh.subMeshCount, Is.EqualTo(1));
                }
            }
            Assert.That(palette.enableInstancing, Is.True);
            Assert.That(palette.mainTexture, Is.Not.Null);
        }

        [TestCase(234, 32)]
        [TestCase(234, 64)]
        [TestCase(24680, 128)]
        [TestCase(24681, 128)]
        [TestCase(-17, 64)]
        public void DistrictLayout_IsStableBoundedAndPreservesNavigableWorld(int seed, int size)
        {
            var world = new CityHybridWfcGenerator().GenerateCity(PCGRequest.CreateCityDefault(seed,size,size));
            string before = world.ComputeStableHash();
            var profile = VisualProfileLoader.Load("City");
            var structures = CityStructureExtractor.Extract(world);
            var placements = VisualWorldLayoutBuilder.BuildCityVisuals(world,profile,structures);
            var repeat = VisualWorldLayoutBuilder.BuildCityVisuals(world,profile,structures);
            Assert.That(VisualLayoutHasher.Compute(profile,repeat), Is.EqualTo(VisualLayoutHasher.Compute(profile,placements)));
            Assert.That(world.ComputeStableHash(), Is.EqualTo(before));
            if (seed == 234 && size == 64) Assert.That(before, Is.EqualTo("8DFCA3BB"));
            if (seed == 24680 && size == 128) Assert.That(before, Is.EqualTo("34B02A50"));
            Assert.That(placements.Count(p=>p.CategoryId.StartsWith("City/Buildings/",StringComparison.Ordinal)),Is.EqualTo(structures.Count));
            var decorations = placements.Where(p=>!p.CategoryId.StartsWith("City/Buildings/",StringComparison.Ordinal)).ToArray();
            Assert.That(decorations.Length, Is.LessThanOrEqualTo(768));
            foreach(var p in decorations)
            {
                var tile = world.Tiles[p.Y*size+p.X];
                Assert.That(tile,Is.Not.EqualTo(BiomeTile.Road).And.Not.EqualTo(BiomeTile.Building));
                Assert.That(Math.Pow(p.X-world.StartPosition.X,2)+Math.Pow(p.Y-world.StartPosition.Y,2),Is.GreaterThan(9));
                Assert.That(Math.Pow(p.X-world.ExitPosition.X,2)+Math.Pow(p.Y-world.ExitPosition.Y,2),Is.GreaterThan(9));
            }
            var collisions = VisualWorldLayoutBuilder.BuildCityPropCollisions(world,profile);
            Assert.That(collisions.Count, Is.EqualTo(decorations.Length));
            for(int i=0;i<collisions.Count;i++)
            {
                Vector3 p = decorations[i].WorldMatrix.GetColumn(3);
                Assert.That(collisions[i].X,Is.EqualTo(p.x).Within(.001f));
                Assert.That(collisions[i].Z,Is.EqualTo(p.z).Within(.001f));
                Assert.That(PCGFirstPersonTestDummy.CanOccupy(world,p,p.y,.2f,.5f,collisions),Is.False);
            }
            Assert.That(PCGFirstPersonTestDummy.CanOccupy(world,new Vector3(world.StartPosition.X,0,world.StartPosition.Y),0,.2f,.5f,collisions),Is.True);
        }

        [TestCase(.5625f,false)]
        [TestCase(1f,false)]
        [TestCase(1.6f,false)]
        [TestCase(2.4f,false)]
        [TestCase(.5625f,true)]
        [TestCase(1.6f,true)]
        public void CityCamera_ContainsEveryRooftopCorner(float aspect,bool orthographic)
        {
            var go = new GameObject("City camera test");
            try
            {
                var camera=go.AddComponent<Camera>(); camera.aspect=aspect; camera.orthographic=orthographic;
                PCGDungeonCameraFramer.FrameCity(camera,128,64,24,1.1f);
                for(int i=0;i<8;i++)
                {
                    var corner=new Vector3((i&1)==0?-.5f:127.5f,(i&2)==0?0:24,(i&4)==0?-.5f:63.5f);
                    var viewport=camera.WorldToViewportPoint(corner);
                    Assert.That(viewport.x,Is.InRange(0f,1f)); Assert.That(viewport.y,Is.InRange(0f,1f));
                    Assert.That(viewport.z,Is.GreaterThan(camera.nearClipPlane));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
