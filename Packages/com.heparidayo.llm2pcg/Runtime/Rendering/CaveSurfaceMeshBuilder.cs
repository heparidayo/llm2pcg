using System;
using System.Collections.Generic;
using Llm2Pcg.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Rendering
{
    public sealed class CaveSurfaceMeshData
    {
        public readonly int GridWidth;
        public readonly int GridHeight;
        public readonly Vector3[] Vertices;
        public readonly Vector3[] Normals;
        public readonly Vector2[] Uvs;
        public readonly int[][] SubmeshTriangles;
        public readonly int BoundaryEdgeCount;
        public readonly string StableHash;

        public int VertexCount => Vertices.Length;
        public int TriangleCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < SubmeshTriangles.Length; index++) count += SubmeshTriangles[index].Length / 3;
                return count;
            }
        }

        public CaveSurfaceMeshData(int gridWidth, int gridHeight, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[][] submeshTriangles, int boundaryEdgeCount, string stableHash)
        {
            GridWidth = gridWidth;
            GridHeight = gridHeight;
            Vertices = vertices;
            Normals = normals;
            Uvs = uvs;
            SubmeshTriangles = submeshTriangles;
            BoundaryEdgeCount = boundaryEdgeCount;
            StableHash = stableHash;
        }
    }

    /// <summary>
    /// Converts the immutable Cave occupancy grid into continuous floor, rock-cap, and wall surfaces.
    /// Every variation comes from a stage-derived seed and stable grid coordinates; topology is never changed.
    /// </summary>
    public static class CaveSurfaceMeshBuilder
    {
        public const int FloorSubmesh = 0;
        public const int RockSubmesh = 1;
        public const int WallSubmesh = 2;
        public const float UvWorldScale = 1f / 5f;
        public const int WallBandCount = 5;
        private const int FloorHeightStage = 3600;
        private const int RockHeightStage = 3610;
        private const int GridJitterStageX = 3620;
        private const int GridJitterStageZ = 3630;
        private const float FloorHeightAmplitude = .055f;
        private const float MinimumRockHeight = 2.35f;
        private const float RockHeightVariation = 1.25f;
        private const float GridJitterAmplitude = .075f;

        public static CaveSurfaceMeshData BuildData(CaveWorldData world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            int gridWidth = world.Width * 2 + 1;
            int gridHeight = world.Height * 2 + 1;
            int gridVertexCount = gridWidth * gridHeight;
            List<Vector3> vertices = new List<Vector3>(gridVertexCount * 2 + world.Width * world.Height);
            List<Vector3> normals = new List<Vector3>(vertices.Capacity);
            List<Vector2> uvs = new List<Vector2>(vertices.Capacity);

            AddLayerVertices(world, false, gridWidth, gridHeight, vertices, normals, uvs);
            AddLayerVertices(world, true, gridWidth, gridHeight, vertices, normals, uvs);

            List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                bool solid = !world.IsWalkable(x, y);
                int layerOffset = solid ? gridVertexCount : 0;
                List<int> target = triangles[solid ? RockSubmesh : FloorSubmesh];
                AddCellTop(target, gridWidth, layerOffset, x, y);
            }

            // Split cap triangles for deliberate low-poly facets, without altering positions.
            List<int> cap = triangles[RockSubmesh];
            for (int i = 0; i < cap.Count; i += 3)
            {
                Vector3 a = vertices[cap[i]], b = vertices[cap[i + 1]], c = vertices[cap[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                for (int j = 0; j < 3; j++)
                {
                    int source = cap[i + j];
                    cap[i + j] = vertices.Count;
                    vertices.Add(vertices[source]); normals.Add(normal); uvs.Add(uvs[source]);
                }
            }

            int boundaryEdgeCount = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if (!world.IsWalkable(x, y)) continue;
                if (!world.IsWalkable(x, y - 1)) { AddWallEdge(world, x, y, 0, vertices, normals, uvs, triangles[WallSubmesh]); boundaryEdgeCount++; }
                if (!world.IsWalkable(x + 1, y)) { AddWallEdge(world, x, y, 1, vertices, normals, uvs, triangles[WallSubmesh]); boundaryEdgeCount++; }
                if (!world.IsWalkable(x, y + 1)) { AddWallEdge(world, x, y, 2, vertices, normals, uvs, triangles[WallSubmesh]); boundaryEdgeCount++; }
                if (!world.IsWalkable(x - 1, y)) { AddWallEdge(world, x, y, 3, vertices, normals, uvs, triangles[WallSubmesh]); boundaryEdgeCount++; }
            }

            int[][] submeshes = { triangles[0].ToArray(), triangles[1].ToArray(), triangles[2].ToArray() };
            Vector3[] vertexArray = vertices.ToArray();
            return new CaveSurfaceMeshData(gridWidth, gridHeight, vertexArray, normals.ToArray(), uvs.ToArray(), submeshes, boundaryEdgeCount, ComputeHash(vertexArray, submeshes));
        }

        public static Mesh CreateMesh(CaveSurfaceMeshData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Mesh mesh = new Mesh
            {
                name = "PCG Cave Surface " + data.StableHash,
                indexFormat = data.VertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.vertices = data.Vertices;
            mesh.normals = data.Normals;
            mesh.uv = data.Uvs;
            mesh.subMeshCount = data.SubmeshTriangles.Length;
            for (int index = 0; index < data.SubmeshTriangles.Length; index++) mesh.SetTriangles(data.SubmeshTriangles[index], index, false);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        public static float SampleSurfaceHeight(CaveWorldData world, float worldX, float worldZ)
        {
            if (world == null) return 0f;
            return SampleLayerSurfaceHeight(world, worldX, worldZ, false);
        }

        public static float SampleRockSurfaceHeight(CaveWorldData world, float worldX, float worldZ)
        {
            if (world == null) return MinimumRockHeight;
            return SampleLayerSurfaceHeight(world, worldX, worldZ, true);
        }

        private static void AddLayerVertices(CaveWorldData world, bool solidLayer, int gridWidth, int gridHeight, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs)
        {
            for (int gridY = 0; gridY < gridHeight; gridY++)
            for (int gridX = 0; gridX < gridWidth; gridX++)
            {
                Vector2 horizontal = GridPosition(world, gridX, gridY);
                float height = SampleLayerHeight(world, gridX, gridY, solidLayer);
                vertices.Add(new Vector3(horizontal.x, height, horizontal.y));
                uvs.Add(horizontal * UvWorldScale);
            }

            for (int gridY = 0; gridY < gridHeight; gridY++)
            for (int gridX = 0; gridX < gridWidth; gridX++)
            {
                float left = SampleLayerHeight(world, Math.Max(0, gridX - 1), gridY, solidLayer);
                float right = SampleLayerHeight(world, Math.Min(gridWidth - 1, gridX + 1), gridY, solidLayer);
                float down = SampleLayerHeight(world, gridX, Math.Max(0, gridY - 1), solidLayer);
                float up = SampleLayerHeight(world, gridX, Math.Min(gridHeight - 1, gridY + 1), solidLayer);
                normals.Add(new Vector3(left - right, 1f, down - up).normalized);
            }
        }

        private static void AddCellTop(List<int> target, int gridWidth, int layerOffset, int x, int y)
        {
            int row0 = layerOffset + y * 2 * gridWidth;
            int row1 = layerOffset + (y * 2 + 1) * gridWidth;
            int row2 = layerOffset + (y * 2 + 2) * gridWidth;
            int column = x * 2;
            int bottomLeft = row0 + column, bottomMiddle = bottomLeft + 1, bottomRight = bottomLeft + 2;
            int leftMiddle = row1 + column, center = leftMiddle + 1, rightMiddle = leftMiddle + 2;
            int topLeft = row2 + column, topMiddle = topLeft + 1, topRight = topLeft + 2;
            AddTriangle(target, center, bottomMiddle, bottomLeft);
            AddTriangle(target, center, bottomRight, bottomMiddle);
            AddTriangle(target, center, rightMiddle, bottomRight);
            AddTriangle(target, center, topRight, rightMiddle);
            AddTriangle(target, center, topMiddle, topRight);
            AddTriangle(target, center, topLeft, topMiddle);
            AddTriangle(target, center, leftMiddle, topLeft);
            AddTriangle(target, center, bottomLeft, leftMiddle);
        }

        // Direction order: south, east, north, west. Endpoints are wound toward the open cell.
        private static void AddWallEdge(CaveWorldData world, int x, int y, int direction, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            int ax, ay, mx, my, bx, by;
            if (direction == 0) { ax = x * 2; ay = y * 2; mx = ax + 1; my = ay; bx = ax + 2; by = ay; }
            else if (direction == 1) { ax = x * 2 + 2; ay = y * 2; mx = ax; my = ay + 1; bx = ax; by = ay + 2; }
            else if (direction == 2) { ax = x * 2 + 2; ay = y * 2 + 2; mx = ax - 1; my = ay; bx = ax - 2; by = ay; }
            else { ax = x * 2; ay = y * 2 + 2; mx = ax; my = ay - 1; bx = ax; by = ay - 2; }
            AddWallQuad(world, ax, ay, mx, my, vertices, normals, uvs, triangles);
            AddWallQuad(world, mx, my, bx, by, vertices, normals, uvs, triangles);
        }

        private static void AddWallQuad(CaveWorldData world, int ax, int ay, int bx, int by, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            Vector2 a = GridPosition(world, ax, ay), b = GridPosition(world, bx, by);
            Vector3 baseA = new Vector3(a.x, SampleLayerHeight(world, ax, ay, false), a.y);
            Vector3 baseB = new Vector3(b.x, SampleLayerHeight(world, bx, by, false), b.y);
            Vector3 topB = new Vector3(b.x, SampleLayerHeight(world, bx, by, true), b.y);
            Vector3 topA = new Vector3(a.x, SampleLayerHeight(world, ax, ay, true), a.y);
            // Shared endpoint offsets keep adjacent facets watertight, including corners.
            // Recess only into rock: the existing floor and navigation boundary stay intact.
            for (int band = 0; band < WallBandCount; band++)
            {
                Vector3 p0 = WallPoint(world, ax, ay, baseA, topA, band);
                Vector3 p1 = WallPoint(world, bx, by, baseB, topB, band);
                Vector3 p2 = WallPoint(world, bx, by, baseB, topB, band + 1);
                Vector3 p3 = WallPoint(world, ax, ay, baseA, topA, band + 1);
                Vector3 normal = Vector3.Cross(p1 - p0, p3 - p0).normalized;
                int start = vertices.Count;
                vertices.Add(p0); vertices.Add(p1); vertices.Add(p2); vertices.Add(p3);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                // World-anchored strata instead of restarting the texture every half cell.
                uvs.Add(WallUv(p0)); uvs.Add(WallUv(p1)); uvs.Add(WallUv(p2)); uvs.Add(WallUv(p3));
                AddTriangle(triangles, start, start + 1, start + 2);
                AddTriangle(triangles, start, start + 2, start + 3);
            }
        }

        private static Vector3 WallPoint(CaveWorldData world, int gx, int gy, Vector3 bottom, Vector3 top, int band)
        {
            float t = band / (float)WallBandCount;
            Vector3 p = Vector3.Lerp(bottom, top, t);
            if (band == 0 || band == WallBandCount) return p;
            Vector2 towardSolid = Vector2.zero;
            for (int y = (gy - 1) / 2; y <= gy / 2; y++)
            for (int x = (gx - 1) / 2; x <= gx / 2; x++)
                if (!world.IsWalkable(x, y))
                    towardSolid += new Vector2(x - (gx * .5f - .5f), y - (gy * .5f - .5f));
            float depth = Mathf.Sin(t * Mathf.PI) * (.13f + .22f * StableUnit(world.Seed, gx, gy, 3640 + band));
            towardSolid = towardSolid.normalized * depth;
            p.x += towardSolid.x; p.z += towardSolid.y;
            return p;
        }

        private static Vector2 WallUv(Vector3 p) => new Vector2((p.x + p.z) * UvWorldScale, p.y * UvWorldScale);

        private static float SampleLayerSurfaceHeight(CaveWorldData world, float worldX, float worldZ, bool solidLayer)
        {
            float gridX = Mathf.Clamp((worldX + .5f) * 2f, 0f, world.Width * 2f);
            float gridY = Mathf.Clamp((worldZ + .5f) * 2f, 0f, world.Height * 2f);
            int x0 = Mathf.FloorToInt(gridX), y0 = Mathf.FloorToInt(gridY);
            int x1 = Math.Min(world.Width * 2, x0 + 1), y1 = Math.Min(world.Height * 2, y0 + 1);
            float tx = gridX - x0, ty = gridY - y0;
            float bottom = Mathf.Lerp(SampleLayerHeight(world, x0, y0, solidLayer), SampleLayerHeight(world, x1, y0, solidLayer), tx);
            float top = Mathf.Lerp(SampleLayerHeight(world, x0, y1, solidLayer), SampleLayerHeight(world, x1, y1, solidLayer), tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private static float SampleLayerHeight(CaveWorldData world, int gridX, int gridY, bool solidLayer)
        {
            int minimumX = (gridX - 1) / 2;
            int maximumX = gridX / 2;
            int minimumY = (gridY - 1) / 2;
            int maximumY = gridY / 2;
            float sum = 0f;
            int count = 0;
            for (int y = minimumY; y <= maximumY; y++)
            for (int x = minimumX; x <= maximumX; x++)
            {
                if (!world.IsInBounds(x, y) || world.SolidCells[y * world.Width + x] != solidLayer) continue;
                sum += solidLayer ? RockCellHeight(world.Seed, x, y) : FloorCellHeight(world.Seed, x, y);
                count++;
            }
            if (count > 0) return sum / count;
            return solidLayer ? MinimumRockHeight : 0f;
        }

        private static Vector2 GridPosition(CaveWorldData world, int gridX, int gridY)
        {
            float x = gridX * .5f - .5f;
            float z = gridY * .5f - .5f;
            bool boundary = gridX == 0 || gridY == 0 || gridX == world.Width * 2 || gridY == world.Height * 2;
            bool center = (gridX & 1) == 1 && (gridY & 1) == 1;
            if (!boundary && !center)
            {
                x += (StableUnit(world.Seed, gridX, gridY, GridJitterStageX) * 2f - 1f) * GridJitterAmplitude;
                z += (StableUnit(world.Seed, gridX, gridY, GridJitterStageZ) * 2f - 1f) * GridJitterAmplitude;
            }
            return new Vector2(x, z);
        }

        private static float FloorCellHeight(int seed, int x, int y)
        {
            return (StableUnit(seed, x, y, FloorHeightStage) * 2f - 1f) * FloorHeightAmplitude;
        }

        private static float RockCellHeight(int seed, int x, int y)
        {
            return MinimumRockHeight + StableUnit(seed, x, y, RockHeightStage) * RockHeightVariation;
        }

        private static float StableUnit(int seed, int x, int y, int stage)
        {
            unchecked
            {
                uint value = (uint)PcgSeed.Stage(seed, stage) ^ (uint)(x * 73856093) ^ (uint)(y * 19349663);
                value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static void AddTriangle(List<int> target, int first, int second, int third)
        {
            target.Add(first); target.Add(second); target.Add(third);
        }

        private static string ComputeHash(Vector3[] vertices, int[][] submeshes)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, vertices.Length);
                for (int index = 0; index < vertices.Length; index++)
                {
                    Mix(ref hash, Quantize(vertices[index].x));
                    Mix(ref hash, Quantize(vertices[index].y));
                    Mix(ref hash, Quantize(vertices[index].z));
                }
                for (int submesh = 0; submesh < submeshes.Length; submesh++)
                {
                    Mix(ref hash, submesh); Mix(ref hash, submeshes[submesh].Length);
                    for (int index = 0; index < submeshes[submesh].Length; index++) Mix(ref hash, submeshes[submesh][index]);
                }
                return hash.ToString("X8");
            }
        }

        private static int Quantize(float value) => (int)Math.Round(value * 10000f, MidpointRounding.AwayFromZero);
        private static void Mix(ref uint hash, int value)
        {
            hash ^= (byte)value; hash *= 16777619u;
            hash ^= (byte)(value >> 8); hash *= 16777619u;
            hash ^= (byte)(value >> 16); hash *= 16777619u;
            hash ^= (byte)(value >> 24); hash *= 16777619u;
        }
    }
}
