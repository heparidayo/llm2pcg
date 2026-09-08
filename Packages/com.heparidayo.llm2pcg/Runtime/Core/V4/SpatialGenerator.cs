using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace Llm2Pcg.Core.V4
{
    public sealed class SpatialWorld
    {
        public int formatVersion = 2;
        public string worldType = "Forest", generatorVersion = "forest-biome@2";
        public int width, height, seed;
        // Heights are integers in quarter-cell units; no float rounding at serialization.
        public int[] elevationUnits;
        public byte[] waterMask, routeMask, bridgeMask, protectedMask;
        public int startIndex, exitIndex;
        public Dictionary<string, byte[]> featureMasks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        public List<SemanticPlacement> placements = new List<SemanticPlacement>();
        public List<PlacementDiagnostic> placementDiagnostics = new List<PlacementDiagnostic>();
        public List<ConstraintMetric> constraints = new List<ConstraintMetric>();
        public string worldHash, semanticHash;
    }
    public sealed class SemanticPlacement
    {
        public string category, type, stableId;
        public int x, y, elevationUnits, yawDegrees, scalePermille;
    }
    public sealed class PlacementDiagnostic
    {
        public string category, code;
        public int candidateCount, eligibleCount, selectedCount, placedCount;
        public Dictionary<string,int> rejected = new Dictionary<string,int>(StringComparer.Ordinal);
        // A renderer must populate its own count; Core cannot claim pixels/models were rendered.
        public int? renderedCount;
        public void Reject(string reason) { rejected[reason] = rejected.TryGetValue(reason,out int n) ? n+1 : 1; }
    }
    public sealed class ConstraintMetric
    {
        public string id, status = "passed";
        public int cellCount, borderContacts, centerContacts;
    }
    public static class SpatialGenerator
    {
        public static SpatialWorld Generate(SpatialRequest request)
        {
            SpatialRequestValidator.Validate(request);
            int w=request.mapWidth,h=request.mapHeight,n=w*h;
            var world=new SpatialWorld { worldType=request.worldType,generatorVersion=request.generatorVersion,width=w,height=h,seed=request.seed,elevationUnits=new int[n],
                waterMask=new byte[n],routeMask=new byte[n],bridgeMask=new byte[n],protectedMask=new byte[n] };
            for(int y=0;y<h;y++) for(int x=0;x<w;x++)
            {
                int sum=0,weight=128,total=0,scale=request.terrain.noiseScale;
                for(int octave=0;octave<request.terrain.octaves;octave++)
                {
                    sum+=Noise(request.seed,x,y,scale,octave)*weight;total+=weight;weight=Math.Max(1,weight/2);scale=Math.Max(2,scale/2);
                }
                int i=y*w+x;
                world.elevationUnits[i]=sum/total*request.terrain.heightUnits/1024;
                if(request.waterMode=="Default" && world.elevationUnits[i]<request.terrain.waterLevelUnits) world.waterMask[i]=1;
            }
            // Versioned Nature baseline. Explicit None suppresses this; exclusive water clears it.
            // An oasis/frozen pond is a default, not an invented user feature ID.
            if(request.worldType!="Forest" && request.waterMode=="Default")
            {
                int radius=Math.Max(1,Math.Min(w,h)/(request.worldType=="Swamp"?4:8));
                int cx=w/2,cy=h/2;
                for(int y=cy-radius;y<=cy+radius;y++)for(int x=cx-radius;x<=cx+radius;x++)
                    if((x-cx)*(x-cx)+(y-cy)*(y-cy)<=radius*radius)
                    {int i=y*w+x;world.waterMask[i]=1;world.elevationUnits[i]=Math.Min(world.elevationUnits[i],request.terrain.waterLevelUnits);}
            }
            foreach(var feature in request.spatialFeatures.OrderBy(f=>f.kind=="Mountain"?0:1).ThenBy(f=>f.id,StringComparer.Ordinal))
            {
                byte[] mask=new byte[n];
                world.featureMasks.Add(feature.id,mask);
                if(feature.kind=="River") Crossing(mask,w,h,feature.orientation,feature.widthCells);
                else
                {
                    var center=Center(feature,request);
                    int summit=world.elevationUnits.Max()+feature.heightUnits;
                    long radiusSquared=(long)feature.radiusCells*feature.radiusCells;
                    for(int y=0;y<h;y++) for(int x=0;x<w;x++)
                    {
                        long dx=x-center.X,dy=y-center.Y,d2=dx*dx+dy*dy;
                        if(d2>radiusSquared) continue;
                        int i=y*w+x;mask[i]=1;
                        if(feature.kind=="Mountain")
                        {
                            if(request.worldType!="Forest")world.waterMask[i]=0;
                            // Integer radial falloff; semantic landmark owns its deterministic footprint.
                            long t=radiusSquared-d2;
                            long strength=1024*t*t/(radiusSquared*radiusSquared);
                            world.elevationUnits[i]=(int)((world.elevationUnits[i]*(1024-strength)+summit*strength)/1024);
                            world.protectedMask[i]=1;
                        }
                    }
                }
                if(feature.kind!="Mountain")
                {
                    if(feature.exclusive) Array.Clear(world.waterMask,0,n);
                    for(int i=0;i<n;i++) if(mask[i]!=0)
                    {
                        world.waterMask[i]=1;world.protectedMask[i]=1;
                        world.elevationUnits[i]=Math.Min(world.elevationUnits[i],request.terrain.waterLevelUnits);
                    }
                }
            }
            if(request.route.mode=="StraightCrossing")
            {
                Crossing(world.routeMask,w,h,request.route.orientation,request.route.widthCells);
                for(int i=0;i<n;i++) if(world.routeMask[i]!=0 && world.waterMask[i]!=0)
                {
                    if(request.route.crossingPolicy=="NoCrossing")
                        throw new ConstraintFailure("UNSATISFIABLE_ROUTE","Straight route crosses water; rerouting is not supported by this experimental route mode.");
                    world.bridgeMask[i]=1;
                }
            }
            var anchors=Enumerable.Range(0,n).Where(i=>world.waterMask[i]==0 && world.protectedMask[i]==0).ToArray();
            if(anchors.Length<2) throw new ConstraintFailure("NO_SAFE_ANCHORS","No two unprotected land anchors.");
            world.startIndex=anchors[0];world.exitIndex=anchors[anchors.Length-1];
            Evaluate(request,world);
            SemanticPlanner.Build(request,world);
            world.worldHash=DigestWorld(world);
            world.semanticHash=DigestPlacements(world);
            return world;
        }

        public static uint CellHash(int seed,int x,int y,int stage)
        {
            unchecked { uint v=(uint)PcgSeed.Stage(seed,stage) ^ (uint)x*374761393u ^ (uint)y*668265263u;
                v=(v^(v>>13))*1274126177u;return v^(v>>16); }
        }
        private static int Noise(int seed,int x,int y,int scale,int octave)
        {
            int cx=x/scale,cy=y/scale,tx=(x%scale)*1024/scale,ty=(y%scale)*1024/scale;
            tx=tx*tx/1024*(3072-2*tx)/1024;ty=ty*ty/1024*(3072-2*ty)/1024;
            int a=(int)(CellHash(seed,cx,cy,8100+octave)%1024),b=(int)(CellHash(seed,cx+1,cy,8100+octave)%1024);
            int c=(int)(CellHash(seed,cx,cy+1,8100+octave)%1024),d=(int)(CellHash(seed,cx+1,cy+1,8100+octave)%1024);
            int top=a+(b-a)*tx/1024,bottom=c+(d-c)*tx/1024;
            return top+(bottom-top)*ty/1024;
        }
        private static Int2 Center(SpatialFeature f,SpatialRequest r)
        {
            int x=(r.mapWidth-1)/2,y=(r.mapHeight-1)/2;
            if(f.placement=="North")y=r.mapHeight*3/4;
            if(f.placement=="South")y=r.mapHeight/4;
            if(f.placement=="East")x=r.mapWidth*3/4;
            if(f.placement=="West")x=r.mapWidth/4;
            if(f.placement=="Seeded") {x+= (int)(CellHash(r.seed,1,1,8200)%9)-4;y+=(int)(CellHash(r.seed,2,2,8200)%9)-4;}
            return new Int2(Math.Max(f.radiusCells+1,Math.Min(r.mapWidth-f.radiusCells-2,x)),
                Math.Max(f.radiusCells+1,Math.Min(r.mapHeight-f.radiusCells-2,y)));
        }
        private static void Crossing(byte[] mask,int w,int h,string orientation,int width)
        {
            bool ns=orientation=="NorthSouth";int cross=ns?w:h,main=ns?h:w;
            int start=(cross-width)/2;
            for(int p=0;p<main;p++)for(int q=Math.Max(0,start);q<Math.Min(cross,start+width);q++)
                mask[ns?p*w+q:q*w+p]=1;
        }
        public static bool Crosses(byte[] mask,int w,int h,string orientation)
        {
            var seen=new bool[mask.Length];var queue=new int[mask.Length];
            for(int i=0;i<mask.Length;i++)
            {
                if(mask[i]==0||seen[i])continue;
                int read=0,write=0;queue[write++]=i;seen[i]=true;
                bool first=false,last=false,center=false;
                while(read<write)
                {
                    int cell=queue[read++],x=cell%w,y=cell/w;
                    first |= orientation=="NorthSouth"?y==0:x==0;
                    last |= orientation=="NorthSouth"?y==h-1:x==w-1;
                    center |= x>=w*35/100&&x<=w*65/100&&y>=h*35/100&&y<=h*65/100;
                    foreach(int next in Neighbours(cell,w,h))
                        if(mask[next]!=0&&!seen[next]){seen[next]=true;queue[write++]=next;}
                }
                if(first&&last&&center)return true;
            }
            return false;
        }
        public static IEnumerable<int> Neighbours(int i,int w,int h)
        {
            if(i%w>0)yield return i-1;if(i%w<w-1)yield return i+1;
            if(i>=w)yield return i-w;if(i<w*(h-1))yield return i+w;
        }
        public static int[] Distance(byte[] mask,int w,int h)
        {
            var distance=Enumerable.Repeat(int.MaxValue,mask.Length).ToArray();var queue=new int[mask.Length];
            int read=0,write=0;
            for(int i=0;i<mask.Length;i++)if(mask[i]!=0){distance[i]=0;queue[write++]=i;}
            while(read<write)
            {
                int i=queue[read++];
                foreach(int next in Neighbours(i,w,h))
                    if(distance[next]==int.MaxValue){distance[next]=distance[i]+1;queue[write++]=next;}
            }
            return distance;
        }
        private static ConstraintMetric Metric(string id,byte[] mask,int w,int h)
        {
            bool left=false,right=false,bottom=false,top=false;int count=0,center=0;
            for(int i=0;i<mask.Length;i++)if(mask[i]!=0)
            {
                int x=i%w,y=i/w;count++;left|=x==0;right|=x==w-1;bottom|=y==0;top|=y==h-1;
                if(x>=w*35/100&&x<=w*65/100&&y>=h*35/100&&y<=h*65/100)center++;
            }
            return new ConstraintMetric{id=id,cellCount=count,centerContacts=center,borderContacts=(left?1:0)+(right?1:0)+(bottom?1:0)+(top?1:0)};
        }
        private static void Evaluate(SpatialRequest r,SpatialWorld w)
        {
            foreach(var f in r.spatialFeatures)
            {
                byte[] mask=w.featureMasks[f.id];var metric=Metric(f.id,mask,w.width,w.height);
                if(f.kind=="River"&&!Crosses(mask,w.width,w.height,f.orientation))
                    throw new ConstraintFailure("UNSATISFIED_RIVER",f.id);
                if(f.kind=="Lake"&&f.exclusive&&(metric.borderContacts!=0||Enumerable.Range(0,mask.Length).Any(i=>w.waterMask[i]!=0&&mask[i]==0)))
                    throw new ConstraintFailure("UNSATISFIED_EXCLUSIVE_WATER",f.id);
                if(f.kind!="Mountain" && Enumerable.Range(0,mask.Length).Any(i=>mask[i]!=0&&w.waterMask[i]==0))
                    throw new ConstraintFailure("UNSATISFIED_WATER_FEATURE",f.id);
                if(f.kind=="Mountain")
                {
                    var center=Center(f,r);int centerIndex=center.Y*w.width+center.X;
                    if(w.waterMask[centerIndex]!=0 || w.elevationUnits[centerIndex]!=w.elevationUnits.Max())
                        throw new ConstraintFailure("UNSATISFIED_MOUNTAIN","Mountain summit conflicts with another feature.");
                }
                w.constraints.Add(metric);
            }
            if(r.waterMode=="None"&&w.waterMask.Any(v=>v!=0))throw new ConstraintFailure("UNSATISFIED_NO_WATER","Water remains.");
            if(r.route.mode=="None"&&w.routeMask.Any(v=>v!=0))throw new ConstraintFailure("UNSATISFIED_NO_ROUTE","Route remains.");
            if(r.route.mode=="StraightCrossing"&&!Crosses(w.routeMask,w.width,w.height,r.route.orientation))
                throw new ConstraintFailure("UNSATISFIED_ROUTE","Route disconnected.");
            w.constraints.Add(Metric("water",w.waterMask,w.width,w.height));
            w.constraints.Add(Metric("route",w.routeMask,w.width,w.height));
            w.constraints.Add(Metric("bridge",w.bridgeMask,w.width,w.height));
        }
        private static string DigestWorld(SpatialWorld w)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
            {
                writer.Write(w.generatorVersion);writer.Write(w.width);writer.Write(w.height);writer.Write(w.seed);
                writer.Write(w.startIndex);writer.Write(w.exitIndex);
                foreach(int elevation in w.elevationUnits)writer.Write(elevation);
                writer.Write(w.waterMask);writer.Write(w.routeMask);writer.Write(w.bridgeMask);writer.Write(w.protectedMask);
                foreach(var id in w.featureMasks.Keys.OrderBy(id=>id,StringComparer.Ordinal)){writer.Write(id);writer.Write(w.featureMasks[id]);}
                writer.Flush();return Sha(stream.ToArray());
            }
        }
        private static string DigestPlacements(SpatialWorld w)
        {
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
            {
                writer.Write("semantic-forest@1");
                foreach(var p in w.placements){writer.Write(p.stableId);writer.Write(p.type);writer.Write(p.x);writer.Write(p.y);writer.Write(p.elevationUnits);writer.Write(p.yawDegrees);writer.Write(p.scalePermille);}
                writer.Flush();return Sha(stream.ToArray());
            }
        }
        private static string Sha(byte[] bytes)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        }
    }
}
