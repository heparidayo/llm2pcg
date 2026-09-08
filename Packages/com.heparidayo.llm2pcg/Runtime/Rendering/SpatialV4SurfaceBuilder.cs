using System;
using System.Collections.Generic;
using Llm2Pcg.Core.V4;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Rendering
{
    /// <summary>Presentation only: never overwrites the v4 masks or quantized heights.</summary>
    public static class SpatialV4SurfaceBuilder
    {
        public const int Ground = 0, Route = 1, Bed = 2, Water = 3, Bridge = 4;
        public const float WaterOffset = .08f;

        public static float GroundHeight(SpatialWorld world, int index)
            => world.elevationUnits[index] * .25f - (world.waterMask[index] != 0 ? .25f : 0f);

        // Odd grid coordinates are exact cell centers. Shared edges/corners average adjacent cells.
        // Negative division must use floor, not C# truncation (important at the map boundary).
        private static float Height(SpatialWorld w, int gx, int gy)
        {
            int x0 = Mathf.FloorToInt((gx - 1) * .5f), y0 = Mathf.FloorToInt((gy - 1) * .5f);
            float sum = 0; int count = 0;
            for (int y = y0; y <= gy / 2; y++)
            for (int x = x0; x <= gx / 2; x++)
                if (x >= 0 && y >= 0 && x < w.width && y < w.height)
                { sum += GroundHeight(w, y * w.width + x); count++; }
            return sum / count;
        }

        public static Mesh Build(SpatialRequest request, SpatialWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            int gw = world.width * 2 + 1, gh = world.height * 2 + 1;
            var vertices = new List<Vector3>(gw * gh);
            var triangles = new List<int>[5];
            for (int s = 0; s < triangles.Length; s++) triangles[s] = new List<int>();
            for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
                vertices.Add(new Vector3(gx * .5f - .5f, Height(world, gx, gy), gy * .5f - .5f));
            float waterY = request.terrain.waterLevelUnits * .25f + WaterOffset;
            var bridgeHeights = BridgeHeights(world, waterY);
            for (int y = 0; y < world.height; y++)
            for (int x = 0; x < world.width; x++)
            {
                int i = y * world.width + x, bl = y * 2 * gw + x * 2;
                int center = bl + gw + 1;
                int[] ring = { bl, bl + 1, bl + 2, bl + gw + 2, bl + gw * 2 + 2,
                    bl + gw * 2 + 1, bl + gw * 2, bl + gw };
                int slot = world.waterMask[i] != 0 ? Bed : world.routeMask[i] != 0 ? Route : Ground;
                for (int k = 0; k < 8; k++)
                { triangles[slot].Add(center); triangles[slot].Add(ring[(k + 1) % 8]); triangles[slot].Add(ring[k]); }
                if (world.waterMask[i] != 0) Quad(vertices, triangles[Water], x, y, waterY);
                if (world.bridgeMask[i] != 0) Quad(vertices, triangles[Bridge], x, y, bridgeHeights[i]);
            }
            var mesh = new Mesh { name = "Spatial v4 layered surface", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            var uv = new Vector2[vertices.Count];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(vertices[i].x, vertices[i].z) / 6f;
            mesh.uv = uv; mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i, false);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // A component uses one deck height above water and adjoining route banks. No walkability claim:
        // bank ramps/navigation are a later adapter stage; the water remains beneath the deck.
        private static float[] BridgeHeights(SpatialWorld world, float waterY)
        {
            var heights = new float[world.width * world.height];
            var visited = new bool[heights.Length];
            var queue = new Queue<int>(); var component = new List<int>();
            for (int start = 0; start < heights.Length; start++)
            {
                if (world.bridgeMask[start] == 0 || visited[start]) continue;
                component.Clear(); queue.Enqueue(start); visited[start] = true;
                float deck = waterY + .25f;
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue(); component.Add(i);
                    foreach (int next in SpatialGenerator.Neighbours(i, world.width, world.height))
                    {
                        if (world.waterMask[next] == 0 && world.routeMask[next] != 0)
                            deck = Mathf.Max(deck, GroundHeight(world, next) + .12f);
                        if (world.bridgeMask[next] == 0 || visited[next]) continue;
                        visited[next] = true; queue.Enqueue(next);
                    }
                }
                foreach (int i in component) heights[i] = deck;
            }
            return heights;
        }

        private static void Quad(List<Vector3> v, List<int> t, int x, int y, float height)
        {
            int b = v.Count;
            v.Add(new Vector3(x - .5f, height, y - .5f)); v.Add(new Vector3(x + .5f, height, y - .5f));
            v.Add(new Vector3(x + .5f, height, y + .5f)); v.Add(new Vector3(x - .5f, height, y + .5f));
            t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
        }
    }
}
