using System;
using System.Collections.Generic;
using System.Linq;
using Llm2Pcg.Core.V4;
using Llm2Pcg.Visuals;
using UnityEngine;

namespace Llm2Pcg.Rendering
{
    /// <summary>Explicit caller-supplied bindings. No substring matching or arbitrary substitutions.</summary>
    public sealed class SpatialV4AssetProfile
    {
        public const string Version = "woodland-semantic@1";
        public sealed class Binding
        {
            public readonly string category, type, resource, sourceCategory, variantId;
            public Binding(string c, string t, string r, string s, string v)
            { category=c; type=t; resource=r; sourceCategory=s; variantId=v; }
        }


        // A draw references an original mesh and all of its submeshes. CPU-readable import is unnecessary.
        public sealed class DrawPart
        {
            public Mesh mesh;
            public Material[] materials;
            public Vector3 position, scale;
            public Quaternion rotation;
        }
        public sealed class Model
        {
            public Binding binding;
            public VisualVariant variant;
            public DrawPart[] parts;
            public Bounds localBounds;
            public float footprintRadius;
        }
        private readonly Dictionary<string,Model> models = new Dictionary<string,Model>(StringComparer.Ordinal);
        public readonly string manifestHash;
        public SpatialV4AssetProfile(string manifestHash = "", Func<string,BiomeVisualProfile> load = null, Binding[] bindings = null)
        {
            this.manifestHash=manifestHash;
            load=load??(r=>Resources.Load<BiomeVisualProfile>("PCGVisualProfiles/"+r));
            foreach(var binding in bindings ?? Array.Empty<Binding>())
            {
                var category=load(binding.resource)?.FindCategory(binding.sourceCategory);
                var matches=category?.variants?.Where(v=>v!=null && v.stableId==binding.variantId).ToArray();
                if(matches==null || matches.Length!=1)continue;
                var parts=PrepareParts(matches[0]);
                if(parts!=null)
                {
                    var bounds=BoundsFor(parts);
                    Vector3 center=bounds.center+matches[0].pivotOffset,extents=bounds.extents;
                    float x=Math.Abs(center.x)+extents.x,z=Math.Abs(center.z)+extents.z;
                    models.Add(binding.category+"/"+binding.type,new Model {binding=binding,variant=matches[0],parts=parts,
                        localBounds=bounds,footprintRadius=Mathf.Sqrt(x*x+z*z)});
                }
            }
        }
        public static Bounds BoundsFor(DrawPart[] parts)
        {
            bool first=true;var bounds=new Bounds();
            foreach(var part in parts)
            {
                var source=part.mesh.bounds;var matrix=Matrix4x4.TRS(part.position,part.rotation,part.scale);
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                {
                    var point=matrix.MultiplyPoint3x4(source.center+Vector3.Scale(source.extents,new Vector3(x,y,z)));
                    if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
                }
            }
            return bounds;
        }
        private static DrawPart[] PrepareParts(VisualVariant variant)
        {
            var parts=variant.GetLodParts(0);
            if(parts.Length==0 || parts.Any(p=>p==null || p.mesh==null || p.material==null || p.material.shader==null || !p.material.shader.isSupported
                || p.subMeshIndex<0 || p.subMeshIndex>=p.mesh.subMeshCount))return null;
            var result=new List<DrawPart>();
            foreach(var group in parts.GroupBy(p=>new {p.mesh,p.localPosition,p.localRotation,p.localScale}))
            {
                var first=group.First();var materials=new Material[first.mesh.subMeshCount];
                foreach(var p in group) {if(materials[p.subMeshIndex]!=null)return null;materials[p.subMeshIndex]=p.material;}
                if(materials.Any(m=>m==null))return null; // Do not silently draw an unbound submesh.
                result.Add(new DrawPart {mesh=first.mesh,materials=materials,position=first.localPosition,rotation=first.localRotation,scale=first.localScale});
            }
            return result.ToArray();
        }
        public Model Find(string category,string type) => models.TryGetValue(category+"/"+type,out var model)?model:null;
        public void Validate(SpatialRequest request)
        {
            foreach(var rule in request.distributionRules)
                if(rule.amountMode!="Off" && rule.maxCount>0 && rule.densityPermille>0)
                    foreach(string type in rule.types)
                        if(Find(rule.category,type)==null)throw new ConstraintFailure("V4_MODEL_UNAVAILABLE",rule.category+"/"+type+" is unavailable in "+Version);
        }
    }
}
