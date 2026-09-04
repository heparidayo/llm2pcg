using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;

namespace Llm2Pcg.Generators.Cave
{
    /// <summary>Seeded fill, double-buffer cellular automata, region cleanup and Kruskal tunnels.</summary>
    public sealed class CaveCellularGenerator : IPCGGenerator
    {
        public string WorldType => PCGRequest.CaveWorldType;
        public string GeneratorVersion => PCGRequest.CaveGeneratorVersion;
        public IPCGWorldData Generate(PCGRequest request) => GenerateCave(request);

        public CaveWorldData GenerateCave(PCGRequest request)
        {
            PCGValidationResult validation = PCGRequestValidator.Validate(request);
            if (!validation.IsValid) throw new ArgumentException(validation.Code + ": " + validation.Message, nameof(request));
            CaveGeneratorSettings s = Resolve(request);
            int width = request.mapWidth, height = request.mapHeight;
            bool[] solid = Fill(width, height, s.fillPercent, new PcgDeterministicRandom(PcgSeed.Stage(request.seed, 300)));
            for (int step = 0; step < s.automataSteps; step++) solid = Automata(solid, width, height);
            List<List<Int2>> regions = Cleanup(solid, width, height, s.minimumRegionSize);
            if (regions.Count == 0) regions.Add(CarveFallback(solid, width, height));
            List<Int2> tunnels = ConnectRegions(solid, regions, width, height, s.tunnelRadius, s.extraTunnelCount);
            Int2 start = regions[0][0];
            Int2 exit = FarthestWalkable(solid, width, height, start);
            return new CaveWorldData(width, height, request.seed, solid, start, exit, tunnels);
        }

        private static CaveGeneratorSettings Resolve(PCGRequest r)
        {
            CaveGeneratorSettings source = r.generatorSettings.cave;
            int fill = source.fillPercent, steps = source.automataSteps, min = source.minimumRegionSize;
            if (r.generationProfile == PCGRequest.CavernousCaveProfile) { fill = Math.Max(38, fill - 3); min = Math.Max(8, min / 2); }
            if (r.generationProfile == PCGRequest.TightCaveProfile) { fill = Math.Min(62, fill + 6); steps += 1; }
            return new CaveGeneratorSettings { fillPercent = fill, automataSteps = steps, minimumRegionSize = min, tunnelRadius = source.tunnelRadius, extraTunnelCount = source.extraTunnelCount };
        }
        private static bool[] Fill(int w, int h, int percent, PcgDeterministicRandom random)
        {
            bool[] cells = new bool[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) cells[y * w + x] = x == 0 || y == 0 || x == w - 1 || y == h - 1 || random.NextInt(100) < percent;
            return cells;
        }
        private static bool[] Automata(bool[] source, int w, int h)
        {
            bool[] next = new bool[source.Length];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) { next[y * w + x] = true; continue; }
                int neighbors = 0; for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if ((dx != 0 || dy != 0) && source[(y + dy) * w + x + dx]) neighbors++;
                int index = y * w + x;
                next[index] = neighbors > 4 || (neighbors == 4 && source[index]);
            }
            return next;
        }
        private static List<List<Int2>> Cleanup(bool[] solid, int w, int h, int minimum)
        {
            bool[] visited = new bool[solid.Length]; List<List<Int2>> kept = new List<List<Int2>>();
            for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
            {
                int index = y * w + x; if (solid[index] || visited[index]) continue;
                List<Int2> region = Flood(solid, visited, w, h, new Int2(x, y));
                if (region.Count >= minimum) kept.Add(region); else for (int i = 0; i < region.Count; i++) solid[region[i].Y * w + region[i].X] = true;
            }
            kept.Sort((a, b) => (a[0].Y * w + a[0].X).CompareTo(b[0].Y * w + b[0].X)); return kept;
        }
        private static List<Int2> Flood(bool[] solid, bool[] visited, int w, int h, Int2 start)
        {
            List<Int2> result = new List<Int2>(); Queue<Int2> queue = new Queue<Int2>(); queue.Enqueue(start); visited[start.Y * w + start.X] = true;
            int[] dx = { 0, 1, 0, -1 }, dy = { -1, 0, 1, 0 };
            while (queue.Count > 0) { Int2 p = queue.Dequeue(); result.Add(p); for (int d = 0; d < 4; d++) { int x = p.X + dx[d], y = p.Y + dy[d], index = y * w + x; if (x > 0 && y > 0 && x < w - 1 && y < h - 1 && !solid[index] && !visited[index]) { visited[index] = true; queue.Enqueue(new Int2(x, y)); } } }
            return result;
        }
        private static List<Int2> CarveFallback(bool[] solid, int w, int h)
        {
            List<Int2> region = new List<Int2>(); int cx = w / 2, cy = h / 2;
            for (int y = Math.Max(1, cy - 2); y <= Math.Min(h - 2, cy + 2); y++) for (int x = Math.Max(1, cx - 2); x <= Math.Min(w - 2, cx + 2); x++) { solid[y * w + x] = false; region.Add(new Int2(x, y)); }
            return region;
        }
        private static List<Int2> ConnectRegions(bool[] solid, List<List<Int2>> regions, int w, int h, int radius, int extra)
        {
            List<Edge> edges = new List<Edge>();
            for (int a = 0; a < regions.Count; a++) for (int b = a + 1; b < regions.Count; b++) { Int2 pa = regions[a][0], pb = regions[b][0]; int best = int.MaxValue; for (int i = 0; i < regions[a].Count; i++) for (int j = 0; j < regions[b].Count; j++) { int cost = Math.Abs(regions[a][i].X - regions[b][j].X) + Math.Abs(regions[a][i].Y - regions[b][j].Y); if (cost < best) { best = cost; pa = regions[a][i]; pb = regions[b][j]; } } edges.Add(new Edge(a,b,best,pa,pb)); }
            edges.Sort((a,b) => a.CompareTo(b)); int[] parent = new int[regions.Count]; for (int i=0;i<parent.Length;i++) parent[i]=i; List<Int2> tunnels = new List<Int2>(); int added=0;
            for (int i=0;i<edges.Count;i++) { Edge e=edges[i]; if (Find(parent,e.A)!=Find(parent,e.B) || extra-- > 0) { Union(parent,e.A,e.B); CarveLine(solid,w,h,e.From,e.To,radius,tunnels); added++; } if (added >= regions.Count - 1 && extra <= 0) break; }
            return tunnels;
        }
        private static void CarveLine(bool[] solid,int w,int h,Int2 a,Int2 b,int radius,List<Int2> tunnel) { int x=a.X,y=a.Y; while (x!=b.X || y!=b.Y) { CarveDisk(solid,w,h,x,y,radius,tunnel); if (x!=b.X) x += x < b.X ? 1 : -1; else y += y < b.Y ? 1 : -1; } CarveDisk(solid,w,h,x,y,radius,tunnel); }
        private static void CarveDisk(bool[] solid,int w,int h,int cx,int cy,int r,List<Int2> tunnel) { for(int y=cy-r;y<=cy+r;y++) for(int x=cx-r;x<=cx+r;x++) if(x>0&&y>0&&x<w-1&&y<h-1&&Math.Abs(x-cx)+Math.Abs(y-cy)<=r) { int i=y*w+x; if(solid[i]) { solid[i]=false; tunnel.Add(new Int2(x,y)); } } }
        private static Int2 FarthestWalkable(bool[] solid,int w,int h,Int2 start) { int[] d=new int[solid.Length]; for(int i=0;i<d.Length;i++)d[i]=-1; Queue<Int2> q=new Queue<Int2>(); q.Enqueue(start);d[start.Y*w+start.X]=0;Int2 far=start;int[] dx={0,1,0,-1},dy={-1,0,1,0};while(q.Count>0){Int2 p=q.Dequeue();if(d[p.Y*w+p.X]>d[far.Y*w+far.X])far=p;for(int k=0;k<4;k++){int x=p.X+dx[k],y=p.Y+dy[k],i=y*w+x;if(x>=0&&y>=0&&x<w&&y<h&&!solid[i]&&d[i]<0){d[i]=d[p.Y*w+p.X]+1;q.Enqueue(new Int2(x,y));}}}return far; }
        private static int Find(int[] p,int i){while(p[i]!=i){p[i]=p[p[i]];i=p[i];}return i;} private static void Union(int[]p,int a,int b){a=Find(p,a);b=Find(p,b);if(a!=b)p[b]=a;}
        private readonly struct Edge : IComparable<Edge> { public readonly int A,B,Cost; public readonly Int2 From,To; public Edge(int a,int b,int cost,Int2 from,Int2 to){A=a;B=b;Cost=cost;From=from;To=to;} public int CompareTo(Edge other){int c=Cost.CompareTo(other.Cost);if(c!=0)return c;c=A.CompareTo(other.A);return c!=0?c:B.CompareTo(other.B);} }
    }
}
