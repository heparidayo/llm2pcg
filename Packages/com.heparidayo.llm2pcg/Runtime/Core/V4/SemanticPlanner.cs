using System;
using System.Collections.Generic;
using System.Linq;

namespace Llm2Pcg.Core.V4
{
    public static class SemanticPlanner
    {
        public static void Build(SpatialRequest request, SpatialWorld world)
        {
            int w=world.width,h=world.height,n=w*h;
            var occupied=new bool[n];
            int[] waterDistance=SpatialGenerator.Distance(world.waterMask,w,h);
            int[] routeDistance=SpatialGenerator.Distance(world.routeMask,w,h);
            // Dependency order is versioned: later categories never relocate earlier trees.
            foreach(string category in SemanticCatalog.Categories)
            {
                var rule=request.distributionRules.Single(r=>r.category==category);
                var d=new PlacementDiagnostic { category=category,candidateCount=n };
                world.placementDiagnostics.Add(d);
                if(rule.amountMode=="Off"||rule.maxCount==0||rule.densityPermille==0)
                {d.rejected["disabled"]=n;d.code="INTENTIONAL_ZERO";continue;}
                int stage=8400+Array.IndexOf(SemanticCatalog.Categories,category);
                var previousCategoryFootprints=(bool[])occupied.Clone();
                int[] featureDistance=rule.region=="NearFeature"?SpatialGenerator.Distance(world.featureMasks[rule.featureId],w,h):null;
                var spacing=new PcgPointSpacingIndex(w,h,rule.minDistanceCells);
                var candidates=Enumerable.Range(0,n).OrderBy(i=>SpatialGenerator.CellHash(request.seed,i%w,i/w,stage)).ThenBy(i=>i);
                string[] types=rule.types.OrderBy(t=>t,StringComparer.Ordinal).ToArray();
                foreach(int i in candidates)
                {
                    int x=i%w,y=i/w;
                    string reason=null;
                    if(category=="waterProps" ? world.waterMask[i]==0 : world.waterMask[i]!=0 || waterDistance[i]<=1)reason="surface";
                    else if(routeDistance[i]<=1)reason="route-clearance";
                    else if(Near(i,world.startIndex,w,3)||Near(i,world.exitIndex,w,3))reason="anchor-clearance";
                    else if(!InRegion(rule,x,y,w,h,featureDistance==null?0:featureDistance[i]))reason="region";
                    else if(category!="waterProps"&&SpatialGenerator.Neighbours(i,w,h).Any(j=>Math.Abs(world.elevationUnits[i]-world.elevationUnits[j])>8))reason="slope";
                    else if(previousCategoryFootprints[i])reason="higher-priority-placement";
                    else if(!spacing.CanPlace(x,y))reason="spacing";
                    if(reason!=null){d.Reject(reason);continue;}
                    // Fixed geometric backbone, independent of density and count. Increasing density
                    // therefore selects a nested subset for a fixed world/other-category configuration.
                    spacing.Add(x,y);d.eligibleCount++;
                    if(SpatialGenerator.CellHash(request.seed,x,y,stage+100)%1000 >= rule.densityPermille){d.Reject("density");continue;}
                    d.selectedCount++;
                    if(d.placedCount>=rule.maxCount){d.Reject("cap");continue;}
                    string type=types[SpatialGenerator.CellHash(request.seed,x,y,stage+200)%(uint)types.Length];
                    world.placements.Add(new SemanticPlacement { category=category,type=type,x=x,y=y,
                        stableId=category+":"+x+":"+y,elevationUnits=world.elevationUnits[i],
                        yawDegrees=(int)(SpatialGenerator.CellHash(request.seed,x,y,stage+300)%360),
                        scalePermille=850+(int)(SpatialGenerator.CellHash(request.seed,x,y,stage+400)%301) });
                    d.placedCount++;
                    // Explicit small footprint exclusion, not renderer-dependent model bounds.
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                        if(x+dx>=0&&x+dx<w&&y+dy>=0&&y+dy<h)occupied[(y+dy)*w+x+dx]=true;
                }
                d.code=d.placedCount==0?(d.eligibleCount==0?"NO_ELIGIBLE_CANDIDATES":"DENSITY_SELECTED_ZERO"):
                    d.placedCount<rule.maxCount&&rule.amountMode=="AtMost"?"BELOW_CAP":"PLACED";
            }
            // Distribution hard constraints are checked on the final list, not just during selection.
            foreach(var rule in request.distributionRules)
            {
                var placements=world.placements.Where(p=>p.category==rule.category).ToArray();
                if(placements.Length>rule.maxCount || placements.Any(p=>!rule.types.Contains(p.type))
                    || rule.amountMode=="Off" && placements.Length!=0)
                    throw new ConstraintFailure("UNSATISFIED_DISTRIBUTION",rule.category);
                int[] distance=rule.region=="NearFeature"?SpatialGenerator.Distance(world.featureMasks[rule.featureId],w,h):null;
                foreach(var p in placements)
                    if(!InRegion(rule,p.x,p.y,w,h,distance==null?0:distance[p.y*w+p.x]))
                        throw new ConstraintFailure("UNSATISFIED_REGION",rule.category);
            }
        }
        private static bool Near(int a,int b,int w,int radius) { int dx=a%w-b%w,dy=a/w-b/w;return dx*dx+dy*dy<=radius*radius; }
        private static bool InRegion(DistributionRule r,int x,int y,int w,int h,int distance)
        {
            switch(r.region)
            {
                case "NearFeature": return distance>=r.minDistanceToFeature&&distance<=r.maxDistanceToFeature;
                case "Center": return x>=w/4&&x<w*3/4&&y>=h/4&&y<h*3/4;
                case "North": return y>=h/2;
                case "South": return y<h/2;
                case "East": return x>=w/2;
                case "West": return x<w/2;
                default: return true;
            }
        }
    }
}
