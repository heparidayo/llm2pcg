using System;
using System.IO;
using System.Linq;
using Llm2Pcg.Core.V4;
using Llm2Pcg.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class SpatialV4RenderingTests
    {
        [Serializable] public sealed class ReferenceSet { public ReferenceCase[] cases; }
        [Serializable] public sealed class RawSet {public RawCase[] cases;}
        [Serializable] public sealed class RawCase {public string id,raw;public bool valid;}
        [Serializable] public sealed class ReferenceCase { public string id; public SpatialRequest request; public ExpectedWorld expected; }
        [Serializable] public sealed class Feature { public string id, mask; }
        [Serializable] public sealed class Placement
        { public string category, type, stableId; public int x, y, elevationUnits, yawDegrees, scalePermille; }
        [Serializable] public sealed class ExpectedWorld
        {
            public string worldHash, semanticHash, waterMask, routeMask, bridgeMask, protectedMask;
            public int width, height, seed, startIndex, exitIndex;
            public int[] elevationUnits;
            public Feature[] features;
            public Placement[] placements;
        }

        internal static SpatialRequest Request(bool bridge = false)
        {
            return new SpatialRequest {
                schemaVersion=4, worldType="Forest", generatorVersion="forest-biome@2", resolverVersion="pcg-resolver@1", catalogVersion="semantic-forest@1",
                seed=234, mapWidth=32, mapHeight=32, waterMode="Default",
                terrain=new TerrainSettings {heightUnits=24,noiseScale=24,octaves=4,waterLevelUnits=0},
                spatialFeatures=new[] {new SpatialFeature {id="river",kind="River",placement="CenterCrossing",orientation="NorthSouth",widthCells=5}},
                route=new RouteSettings {mode=bridge?"StraightCrossing":"None",orientation="EastWest",widthCells=3,crossingPolicy="BridgeIfNeeded"},
                distributionRules=SemanticCatalog.Categories.Select(c=>new DistributionRule {category=c,types=SemanticCatalog.Types(c),
                    amountMode=c=="rocks"?"Off":"RelativeDensity",densityPermille=c=="rocks"?0:1000,maxCount=c=="rocks"?0:30,
                    minDistanceCells=3,region="WholeMap",featureId=""}).ToArray()
            };
        }

        [Test] public void StrictJsonRequiresEveryFieldAndRejectsDuplicateUnknownAndMalformedNumbers()
        {
            string json=JsonUtility.ToJson(Request());
            Assert.That(SpatialRequestJson.Parse(json).seed,Is.EqualTo(234));
            Assert.That(SpatialRequestJson.Parse(json.Replace("\"seed\"","\"se\\u0065d\"")).seed,Is.EqualTo(234));
            foreach(string bad in new[]{json.Replace("\"seed\":234,",""),json.Replace("\"seed\":234","\"seed\":null"),
                json.Replace("\"seed\":234","\"seed\":2.34e2"),json.Replace("\"seed\":234","\"seed\":0234"),
                json.Replace("\"seed\":234","\"seed\":2147483648"),json.Replace("\"seed\":234","\"seed\":true"),
                "{\"seed\":234,"+json.Substring(1),"{\"se\\u0065d\":234,"+json.Substring(1),"{\"unexpected\":0,"+json.Substring(1),json+"{}",json.Substring(0,json.Length-1)+",}",new string(' ',65537)})
                Assert.Throws<ConstraintFailure>(()=>SpatialRequestJson.Parse(bad),bad.Substring(0,Math.Min(80,bad.Length)));
            foreach(int seed in new[]{int.MinValue,int.MaxValue,0})
                Assert.That(SpatialRequestJson.Parse(json.Replace("\"seed\":234","\"seed\":"+seed)).seed,Is.EqualTo(seed));
        }

        [Test] public void StrictUnityJsonMatchesAllEightyContractFixtures()
        {
            string path=Path.Combine(Application.dataPath,"../Temp/SpatialV4Unity/contracts.json");
            if(!File.Exists(path))Assert.Ignore("Run Prepare-SpatialV4Unity.mjs first.");
            var set=JsonUtility.FromJson<RawSet>(File.ReadAllText(path));Assert.That(set.cases.Length,Is.EqualTo(80));
            foreach(var c in set.cases)
                if(c.valid)Assert.DoesNotThrow(()=>SpatialRequestJson.Parse(c.raw),c.id);
                else Assert.Throws<ConstraintFailure>(()=>SpatialRequestJson.Parse(c.raw),c.id);
        }

        [Test] public void IndependentDotNetBuffersAndAllSemanticFieldsMatchUnity()
        {
            string path=Path.Combine(Application.dataPath,"../Temp/SpatialV4Unity/reference.json");
            if(!File.Exists(path)) Assert.Ignore("Run node Scripts/Prepare-SpatialV4Unity.mjs after building CoreHost to enable cross-runtime validation.");
            var set=JsonUtility.FromJson<ReferenceSet>(File.ReadAllText(path));
            Assert.That(set.cases.Length,Is.GreaterThanOrEqualTo(66));
            foreach(var c in set.cases)
            {
                var w=SpatialGenerator.Generate(c.request); var e=c.expected;
                Assert.That(w.worldHash,Is.EqualTo(e.worldHash),c.id); Assert.That(w.semanticHash,Is.EqualTo(e.semanticHash),c.id);
                Assert.That(new[]{w.width,w.height,w.seed,w.startIndex,w.exitIndex},Is.EqualTo(new[]{e.width,e.height,e.seed,e.startIndex,e.exitIndex}),c.id);
                Assert.That(w.elevationUnits,Is.EqualTo(e.elevationUnits),c.id);
                Assert.That(w.waterMask,Is.EqualTo(Convert.FromBase64String(e.waterMask)),c.id);
                Assert.That(w.routeMask,Is.EqualTo(Convert.FromBase64String(e.routeMask)),c.id);
                Assert.That(w.bridgeMask,Is.EqualTo(Convert.FromBase64String(e.bridgeMask)),c.id);
                Assert.That(w.protectedMask,Is.EqualTo(Convert.FromBase64String(e.protectedMask)),c.id);
                Assert.That(w.featureMasks.Count,Is.EqualTo(e.features.Length),c.id);
                foreach(var f in e.features) Assert.That(w.featureMasks[f.id],Is.EqualTo(Convert.FromBase64String(f.mask)),c.id);
                Assert.That(w.placements.Count,Is.EqualTo(e.placements.Length),c.id);
                for(int i=0;i<w.placements.Count;i++)
                {
                    var a=w.placements[i];var b=e.placements[i];
                    Assert.That(new[]{a.category,a.type,a.stableId},Is.EqualTo(new[]{b.category,b.type,b.stableId}),c.id);
                    Assert.That(new[]{a.x,a.y,a.elevationUnits,a.yawDegrees,a.scalePermille},Is.EqualTo(new[]{b.x,b.y,b.elevationUnits,b.yawDegrees,b.scalePermille}),c.id);
                }
            }
            TestContext.WriteLine("Compared "+set.cases.Length+" .NET/Unity worlds: full heights/masks/features/anchors/placement fields plus both hashes.");
        }

        [Test] public void SurfacePreservesMasksAndBridgeDoesNotEraseWater()
        {
            var r=Request(true);var w=SpatialGenerator.Generate(r);var before=SpatialGenerator.Generate(r);
            var mesh=SpatialV4SurfaceBuilder.Build(r,w);
            try {
                Assert.That(mesh.GetTriangles(SpatialV4SurfaceBuilder.Water).Length,Is.EqualTo(w.waterMask.Count(v=>v!=0)*6));
                Assert.That(mesh.GetTriangles(SpatialV4SurfaceBuilder.Bridge).Length,Is.EqualTo(w.bridgeMask.Count(v=>v!=0)*6));
                foreach(int i in mesh.GetTriangles(SpatialV4SurfaceBuilder.Bridge))
                    Assert.That(mesh.vertices[i].y,Is.GreaterThan(r.terrain.waterLevelUnits*.25f+SpatialV4SurfaceBuilder.WaterOffset));
                Assert.That(w.waterMask.Where((v,i)=>w.bridgeMask[i]!=0).All(v=>v==1),Is.True);
                Assert.That(w.elevationUnits,Is.EqualTo(before.elevationUnits));
                Assert.That(w.waterMask,Is.EqualTo(before.waterMask));
                Assert.That(w.routeMask,Is.EqualTo(before.routeMask));
                Assert.That(w.bridgeMask,Is.EqualTo(before.bridgeMask));
            } finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test] public void DryCellCentersMatchPlacementElevationIncludingMapEdges()
        {
            var r=Request();var w=SpatialGenerator.Generate(r);var mesh=SpatialV4SurfaceBuilder.Build(r,w);
            try { var vertices=mesh.vertices;int stride=w.width*2+1;
                for(int y=0;y<w.height;y++) for(int x=0;x<w.width;x++)
                    Assert.That(vertices[(y*2+1)*stride+x*2+1].y,Is.EqualTo(SpatialV4SurfaceBuilder.GroundHeight(w,y*w.width+x)));
                Assert.That(vertices[0].y,Is.EqualTo(SpatialV4SurfaceBuilder.GroundHeight(w,0)));
            } finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test] public void ExplicitPrimitiveCatalogCoversAllSemanticTypesAndRejectsUnknown()
        {
            foreach(string c in SemanticCatalog.Categories) foreach(string t in SemanticCatalog.Types(c))
            {
                var mesh=SpatialV4PrimitiveProfile.Create(c,t);
                try { Assert.That(mesh.vertexCount,Is.GreaterThan(0)); Assert.That(mesh.bounds.size.x,Is.LessThanOrEqualTo(1.5f)); }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            Assert.Throws<ArgumentException>(()=>SpatialV4PrimitiveProfile.Create("trees","rock"));
        }

        [Test] public void RendererBuildRebuildAndClearPreserveCountsOffAndSemanticHash()
        {
            var r=Request();var w=SpatialGenerator.Generate(r);var root=new GameObject("v4 renderer test");
            try {
                var renderer=root.AddComponent<SpatialV4WorldRenderer>();
                renderer.Build(r,w);renderer.Build(r,w);
                Assert.That(root.transform.childCount,Is.EqualTo(1));
                Assert.That(renderer.Counts.Sum(c=>c.renderedCount),Is.EqualTo(w.placements.Count));
                Assert.That(renderer.Counts.Single(c=>c.category=="rocks").renderedCount,Is.Zero);
                Assert.That(renderer.SemanticHash,Is.EqualTo(w.semanticHash));
                Assert.That(w.placementDiagnostics.All(d=>d.renderedCount==null),Is.True,"Renderer must not mutate Core diagnostics");
                renderer.Clear();Assert.That(root.transform.childCount,Is.Zero);Assert.That(renderer.Counts,Is.Empty);
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
