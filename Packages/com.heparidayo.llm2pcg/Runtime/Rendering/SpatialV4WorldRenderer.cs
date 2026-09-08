using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Llm2Pcg.Core.V4;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Rendering
{
    [Serializable] public sealed class SpatialV4RenderCount
    {
        public string category;
        public int placedCount, renderedCount;
        public int candidateCount, eligibleCount, selectedCount;
        public string code;
    }

    /// <summary>Opt-in v4 renderer. Does not call legacy layout builders or add extra decorations.</summary>
    public sealed class SpatialV4WorldRenderer : MonoBehaviour
    {
        public string ProfileVersion { get; private set; } = SpatialV4PrimitiveProfile.Version;
        public string AssetManifestHash { get; private set; } = "";
        public string VisualHash { get; private set; } = "";
        public string WorldHash { get; private set; }
        public string SemanticHash { get; private set; }
        public SpatialV4ClearanceReport Clearance { get; private set; }
        public SpatialV4RenderCount[] Counts { get; private set; } = Array.Empty<SpatialV4RenderCount>();
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private GameObject content;

        public void Build(SpatialRequest request, SpatialWorld world, SpatialV4AssetProfile assetProfile = null, bool fitFootprints = false)
        {
            SpatialRequestValidator.Validate(request);
            if (world == null || world.worldType!=request.worldType || world.generatorVersion!=request.generatorVersion || world.width != request.mapWidth || world.height != request.mapHeight || world.seed != request.seed)
                throw new ArgumentException("Request/world mismatch.");
            // Fail before clearing the last valid preview if any semantic type cannot be represented.
            foreach (var p in world.placements)
                if (!SpatialV4PrimitiveProfile.Supports(p.category, p.type)) throw new ArgumentException("Unmapped semantic type: " + p.type);
            if(assetProfile!=null && request.worldType!="Forest")throw new ConstraintFailure("V4_MODEL_UNAVAILABLE","Nature v4 currently requires the explicit primitive profile; Forest asset bindings are not Nature profiles.");
            assetProfile?.Validate(request);
            if(assetProfile!=null && world.placements.Any(p=>assetProfile.Find(p.category,p.type)==null))
                throw new ConstraintFailure("V4_MODEL_UNAVAILABLE","A placement has no explicit model binding.");
            if(fitFootprints && assetProfile==null)throw new ArgumentException("Footprint fitting requires an asset profile.");
            var clearance=assetProfile==null?null:SpatialV4Clearance.Build(world,assetProfile,fitFootprints);
            Clear();
            try
            {
                content = new GameObject("v4 generated content"); content.transform.SetParent(transform, false);
                ProfileVersion = assetProfile==null?SpatialV4PrimitiveProfile.Version:SpatialV4AssetProfile.Version;
                AssetManifestHash = assetProfile?.manifestHash??"";
                Clearance=clearance?.report;
                Material props = assetProfile==null?Material("Semantic primitives", Color.white, true):null;
                var surfaceMaterials = new[] { Material("Ground", new Color(.29f,.43f,.21f)), Material("Route", new Color(.61f,.47f,.29f)),
                    Material("Riverbed", new Color(.30f,.34f,.29f)), Material("Water", new Color(.12f,.48f,.64f)), Material("Bridge", new Color(.55f,.32f,.16f)) };
                if(world.worldType!="Forest")
                {
                    int[] palette=world.worldType=="Desert"?new[]{0xcbaa68,0xa88b59,0x82704b,0x279fba}:world.worldType=="Snowfield"?new[]{0xe4edf2,0x939fae,0x5d7587,0x8bc3df}:new[]{0x4a5736,0x77704e,0x3c4531,0x425f47};
                    for(int i=0;i<palette.Length;i++)surfaceMaterials[i].color=new Color(((palette[i]>>16)&255)/255f,((palette[i]>>8)&255)/255f,(palette[i]&255)/255f);
                    ProfileVersion="primitive-"+world.worldType.ToLowerInvariant()+"@1";
                }
                Mesh surface = Own(SpatialV4SurfaceBuilder.Build(request, world));
                Draw("Ground + route + water + bridge", surface, surfaceMaterials);
                // Water has no physical floor collider. Terrain and decks retain separate triangle sets.
                Mesh collision = Own(new Mesh { name = "v4 ground and bridge collision", indexFormat = IndexFormat.UInt32 });
                collision.vertices = surface.vertices;
                collision.triangles = new[] { 0, 1, 2, 4 }.SelectMany(surface.GetTriangles).ToArray();
                collision.RecalculateBounds();
                var collider = content.AddComponent<MeshCollider>(); collider.sharedMesh = collision;
                foreach (var group in world.placements.GroupBy(p => p.category + "/" + p.type).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    var first = group.First();
                    if(assetProfile!=null)
                    {
                        var model=assetProfile.Find(first.category,first.type);
                        foreach(var placement in group) DrawModel(request,placement,model,clearance.ScaleFor(placement.stableId));
                        continue;
                    }
                    Mesh glyph = Own(SpatialV4PrimitiveProfile.Create(first.category, first.type));
                    var instances = group.Select(p => new CombineInstance { mesh = glyph,
                        transform = Matrix4x4.TRS(new Vector3(p.x, p.category == "waterProps" ? request.terrain.waterLevelUnits*.25f + SpatialV4SurfaceBuilder.WaterOffset : p.elevationUnits*.25f, p.y),
                            Quaternion.Euler(0,p.yawDegrees,0), Vector3.one * (p.scalePermille*.001f)) }).ToArray();
                    Mesh batch = Own(new Mesh { name = group.Key, indexFormat = IndexFormat.UInt32 });
                    batch.CombineMeshes(instances, true, true); Draw(group.Key, batch, new[] { props });
                }
                Counts = SemanticCatalog.Categories.Select(c => {
                    var diagnostic=world.placementDiagnostics.Single(d=>d.category==c);
                    return new SpatialV4RenderCount { category = c, candidateCount=diagnostic.candidateCount,
                        eligibleCount=diagnostic.eligibleCount,selectedCount=diagnostic.selectedCount,code=diagnostic.code,
                        placedCount = world.placements.Count(p=>p.category==c), renderedCount = world.placements.Count(p=>p.category==c) };
                }).ToArray();
                WorldHash = world.worldHash; SemanticHash = world.semanticHash;
                string bindingText=assetProfile==null?"":string.Join("\n",world.placements.Select(p=>p.category+"/"+p.type)
                    .Distinct().OrderBy(k=>k,StringComparer.Ordinal).Select(k=>{var key=k.Split('/');return k+"="+assetProfile.Find(key[0],key[1]).binding.variantId;}));
                using(var sha=SHA256.Create()) VisualHash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                    "spatial-unity-renderer@3\n"+ProfileVersion+"\n"+AssetManifestHash+"\n"+WorldHash+"\n"+SemanticHash+"\nLOD0\n"+bindingText
                    +"\n"+(clearance==null?"none":clearance.report.policy+"\n"+string.Join("\n",clearance.items.Select(p=>p.id+":"+p.scalePermille.ToString(System.Globalization.CultureInfo.InvariantCulture))))))).Replace("-","").ToLowerInvariant();
            }
            catch { Clear(); throw; }
        }

        private void DrawModel(SpatialRequest request, SemanticPlacement placement, SpatialV4AssetProfile.Model model, int fitScalePermille)
        {
            var instance=new GameObject(placement.stableId+" / "+model.binding.type);
            instance.transform.SetParent(content.transform,false);
            float scale=placement.scalePermille*.001f*fitScalePermille*.001f;
            var rotation=Quaternion.Euler(0,placement.yawDegrees,0);
            float elevation=placement.category=="waterProps"?request.terrain.waterLevelUnits*.25f+SpatialV4SurfaceBuilder.WaterOffset:placement.elevationUnits*.25f;
            instance.transform.localPosition=new Vector3(placement.x,elevation+model.variant.verticalOffset,placement.y)
                +rotation*(model.variant.pivotOffset*scale);
            instance.transform.localRotation=rotation;instance.transform.localScale=Vector3.one*scale;
            foreach(var part in model.parts)
            {
                var child=new GameObject(model.binding.variantId);child.transform.SetParent(instance.transform,false);
                child.transform.localPosition=part.position;child.transform.localRotation=part.rotation;child.transform.localScale=part.scale;
                child.AddComponent<MeshFilter>().sharedMesh=part.mesh;
                child.AddComponent<MeshRenderer>().sharedMaterials=part.materials;
            }
            // Simple solid proxies only. Never make leaves, grass or water props into walls.
            if(placement.category=="trees")
            {
                var capsule=instance.AddComponent<CapsuleCollider>();capsule.direction=1;
                capsule.radius=Mathf.Clamp(model.variant.EffectiveCollisionRadius,.05f,Math.Min(model.localBounds.extents.x,model.localBounds.extents.z));
                capsule.height=Math.Max(capsule.radius*2,model.localBounds.size.y*.65f);
                capsule.center=new Vector3(0,model.localBounds.min.y+capsule.height*.5f,0);
                Clearance.colliderCount++;
            }
            else if(placement.category=="rocks")
            {
                var box=instance.AddComponent<BoxCollider>();box.center=model.localBounds.center;box.size=model.localBounds.size;
                Clearance.colliderCount++;
            }
        }

        private Material Material(string label, Color color, bool vertexColors = false)
        {
            Shader shader = vertexColors ? Shader.Find("LLM2PCG/SpatialV4VertexColor") : Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null && !vertexColors) shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Missing v4 presentation shader: " + label);
            var material = Own(new Material(shader) { name = "v4 " + label, color = color });
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .12f);
            return material;
        }
        private void Draw(string label, Mesh mesh, Material[] materials)
        {
            var child = new GameObject(label); child.transform.SetParent(content.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }
        private T Own<T>(T obj) where T : UnityEngine.Object { owned.Add(obj); return obj; }
        public void Clear()
        {
            if (content != null) { content.SetActive(false); Release(content); content = null; }
            foreach (var obj in owned) if (obj != null) Release(obj);
            owned.Clear(); Counts = Array.Empty<SpatialV4RenderCount>(); WorldHash = null; SemanticHash = null;
            AssetManifestHash="";VisualHash="";ProfileVersion=SpatialV4PrimitiveProfile.Version;
            Clearance=null;
        }
        private static void Release(UnityEngine.Object obj)
        { if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
        private void OnDestroy() => Clear();
    }
}
