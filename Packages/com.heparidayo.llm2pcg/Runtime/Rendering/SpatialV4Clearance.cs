using System;
using System.Collections.Generic;
using System.Linq;
using Llm2Pcg.Core.V4;
using UnityEngine;

namespace Llm2Pcg.Rendering
{
    [Serializable] public sealed class SpatialV4ClearanceReport
    {
        public string policy;
        public int evaluatedCount, adjustedCount, overlapPairsBefore, overlapPairsAfter;
        public int protectedIntrusionsBefore, protectedIntrusionsAfter, colliderCount;
        public int minimumScalePermille=1000;
    }
    /// <summary>Conservative horizontal circles, not exact mesh intersection or navigation validation.</summary>
    public sealed class SpatialV4Clearance
    {
        public const string FitPolicy="footprint-fit@1", AuditPolicy="footprint-audit@1";
        public const float Margin=.05f;
        public sealed class Item
        {
            public string id;
            public float x,z,radius;
            public int scalePermille=1000;
        }
        public readonly SpatialV4ClearanceReport report;
        public readonly Item[] items;
        private readonly Dictionary<string,int> scales;
        public int ScaleFor(string id)=>scales.TryGetValue(id,out int scale)?scale:1000;
        public static bool Evaluated(string category)=>category=="trees"||category=="rocks"||category=="bushes";
        public static SpatialV4Clearance Build(SpatialWorld world,SpatialV4AssetProfile profile,bool fit)
        {
            return new SpatialV4Clearance(world,world.placements.Where(p=>Evaluated(p.category)).Select(p=>new Item {
                id=p.stableId,x=p.x,z=p.y,radius=profile.Find(p.category,p.type).footprintRadius*p.scalePermille*.001f
            }).ToArray(),fit);
        }
        public SpatialV4Clearance(SpatialWorld world,Item[] source,bool fit)
        {
            items=source.Select(p=>new Item {id=p.id,x=p.x,z=p.z,radius=p.radius}).OrderBy(p=>p.id,StringComparer.Ordinal).ToArray();
            if(items.Any(p=>string.IsNullOrEmpty(p.id)||!Finite(p.radius)||p.radius<=0||p.radius>64||!Finite(p.x)||!Finite(p.z)
                ||p.x<0||p.z<0||p.x>=world.width||p.z>=world.height)||items.Select(p=>p.id).Distinct().Count()!=items.Length)
                throw new ConstraintFailure("V4_INVALID_FOOTPRINT","Invalid or duplicate footprint metadata.");
            report=new SpatialV4ClearanceReport {policy=fit?FitPolicy:AuditPolicy,evaluatedCount=items.Length};
            // Bucket diameter covers every possible original overlap. Scale only decreases.
            float cellSize=items.Length==0?1:items.Max(p=>p.radius)*2+Margin;
            var buckets=new Dictionary<(int,int),List<int>>();
            var pairs=new List<(int,int,float)>();
            for(int i=0;i<items.Length;i++)
            {
                var p=items[i];int bx=(int)Math.Floor(p.x/cellSize),bz=(int)Math.Floor(p.z/cellSize);
                for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                    if(buckets.TryGetValue((bx+dx,bz+dz),out var list))foreach(int j in list)
                    {
                        var q=items[j];float distance=Mathf.Sqrt((p.x-q.x)*(p.x-q.x)+(p.z-q.z)*(p.z-q.z));
                        if(distance<p.radius+q.radius+Margin)
                        {
                            pairs.Add((i,j,distance));report.overlapPairsBefore++;
                            if(fit){int ratio=Quantize((distance-Margin)/(p.radius+q.radius));p.scalePermille=Math.Min(p.scalePermille,ratio);q.scalePermille=Math.Min(q.scalePermille,ratio);}
                        }
                    }
                if(!buckets.TryGetValue((bx,bz),out var bucket))buckets.Add((bx,bz),bucket=new List<int>());
                bucket.Add(i);
                float clearance=ProtectedClearance(world,p);
                if(p.radius+Margin>clearance)
                {
                    report.protectedIntrusionsBefore++;
                    if(fit)p.scalePermille=Math.Min(p.scalePermille,Quantize((clearance-Margin)/p.radius));
                }
            }
            if(fit && items.Any(p=>p.scalePermille<100))
                throw new ConstraintFailure("V4_FOOTPRINT_FIT_UNSATISFIABLE","Footprint fitting would require less than 10% model scale. No placements were dropped or moved.");
            foreach(var pair in pairs)
                if(items[pair.Item1].radius*items[pair.Item1].scalePermille*.001f+items[pair.Item2].radius*items[pair.Item2].scalePermille*.001f+Margin>pair.Item3+.00001f)
                    report.overlapPairsAfter++;
            foreach(var p in items)
            {
                if(p.scalePermille<1000)report.adjustedCount++;
                report.minimumScalePermille=Math.Min(report.minimumScalePermille,p.scalePermille);
                if(p.radius*p.scalePermille*.001f+Margin>ProtectedClearance(world,p)+.00001f)report.protectedIntrusionsAfter++;
            }
            if(fit && (report.overlapPairsAfter!=0||report.protectedIntrusionsAfter!=0))
                throw new ConstraintFailure("V4_FOOTPRINT_FIT_UNSATISFIABLE","Final conservative clearance check failed.");
            scales=items.ToDictionary(p=>p.id,p=>p.scalePermille,StringComparer.Ordinal);
        }
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        private static int Quantize(float ratio)=>Math.Max(0,Math.Min(1000,(int)Math.Floor(ratio*1000)));
        private static float ProtectedClearance(SpatialWorld world,Item p)
        {
            float best=Math.Min(Math.Min(p.x+.5f,world.width-.5f-p.x),Math.Min(p.z+.5f,world.height-.5f-p.z));
            int radius=(int)Math.Ceiling(p.radius+Margin+.5f);
            for(int y=Math.Max(0,(int)p.z-radius);y<=Math.Min(world.height-1,(int)p.z+radius);y++)
            for(int x=Math.Max(0,(int)p.x-radius);x<=Math.Min(world.width-1,(int)p.x+radius);x++)
            {
                int i=y*world.width+x;
                if(world.waterMask[i]==0&&world.routeMask[i]==0&&world.bridgeMask[i]==0)continue;
                float dx=Math.Max(0,Math.Abs(p.x-x)-.5f),dz=Math.Max(0,Math.Abs(p.z-y)-.5f);
                best=Math.Min(best,Mathf.Sqrt(dx*dx+dz*dz));
            }
            return best;
        }
    }
}
