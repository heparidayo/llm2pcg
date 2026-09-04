using System;
using System.Collections.Generic;
using Llm2Pcg.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Rendering
{
    public sealed class ForestSurfaceMeshData
    {
        public readonly int GridWidth;
        public readonly int GridHeight;
        public readonly Vector3[] Vertices;
        public readonly Vector3[] Normals;
        public readonly Vector2[] Uvs;
        public readonly int[][] SubmeshTriangles;
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

        public ForestSurfaceMeshData(int gridWidth, int gridHeight, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[][] submeshTriangles, string stableHash)
        {
            GridWidth = gridWidth;
            GridHeight = gridHeight;
            Vertices = vertices;
            Normals = normals;
            Uvs = uvs;
            SubmeshTriangles = submeshTriangles;
            StableHash = stableHash;
        }
    }

    /// <summary>
    /// Converts immutable Forest tile/elevation data into one continuous, object-free heightfield mesh.
    /// Tile centers preserve the generator height while shared half-cell vertices smooth every boundary.
    /// </summary>
    public static class ForestSurfaceMeshBuilder
    {
        public const int GroundSubmesh = 0;
        public const int PathSubmesh = 1;
        public const int UnderwaterSubmesh = 2;
        public const int WaterSurfaceSubmesh = 3;
        public const float UnderwaterBedHeight = -.12f;
        public const float WaterSurfaceHeight = .08f;
        public const float UvWorldScale = 1f / 6f;

        public static ForestSurfaceMeshData BuildData(BiomeWorldData world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            int gridWidth = world.Width * 2 + 1;
            int gridHeight = world.Height * 2 + 1;
            Vector3[] heightfieldVertices = new Vector3[gridWidth * gridHeight];
            Vector3[] heightfieldNormals = new Vector3[heightfieldVertices.Length];
            Vector2[] heightfieldUvs = new Vector2[heightfieldVertices.Length];

            for (int gridY = 0; gridY < gridHeight; gridY++)
            for (int gridX = 0; gridX < gridWidth; gridX++)
            {
                int index = gridY * gridWidth + gridX;
                float x = gridX * .5f - .5f;
                float z = gridY * .5f - .5f;
                heightfieldVertices[index] = new Vector3(x, SampleHeight(world, gridX, gridY), z);
                heightfieldUvs[index] = new Vector2(x * UvWorldScale, z * UvWorldScale);
            }

            for (int gridY = 0; gridY < gridHeight; gridY++)
            for (int gridX = 0; gridX < gridWidth; gridX++)
            {
                float left = heightfieldVertices[gridY * gridWidth + Math.Max(0, gridX - 1)].y;
                float right = heightfieldVertices[gridY * gridWidth + Math.Min(gridWidth - 1, gridX + 1)].y;
                float down = heightfieldVertices[Math.Max(0, gridY - 1) * gridWidth + gridX].y;
                float up = heightfieldVertices[Math.Min(gridHeight - 1, gridY + 1) * gridWidth + gridX].y;
                heightfieldNormals[gridY * gridWidth + gridX] = new Vector3(left - right, 1f, down - up).normalized;
            }

            List<Vector3> vertices = new List<Vector3>(heightfieldVertices);
            List<Vector3> normals = new List<Vector3>(heightfieldNormals);
            List<Vector2> uvs = new List<Vector2>(heightfieldUvs);
            List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                int row0 = y * 2 * gridWidth;
                int row1 = (y * 2 + 1) * gridWidth;
                int row2 = (y * 2 + 2) * gridWidth;
                int column = x * 2;
                int bottomLeft = row0 + column, bottomMiddle = bottomLeft + 1, bottomRight = bottomLeft + 2;
                int leftMiddle = row1 + column, center = leftMiddle + 1, rightMiddle = leftMiddle + 2;
                int topLeft = row2 + column, topMiddle = topLeft + 1, topRight = topLeft + 2;
                BiomeTile tile = world.Tiles[y * world.Width + x];
                List<int> target = triangles[SubmeshFor(tile)];
                AddTriangle(target, center, bottomMiddle, bottomLeft);
                AddTriangle(target, center, bottomRight, bottomMiddle);
                AddTriangle(target, center, rightMiddle, bottomRight);
                AddTriangle(target, center, topRight, rightMiddle);
                AddTriangle(target, center, topMiddle, topRight);
                AddTriangle(target, center, topLeft, topMiddle);
                AddTriangle(target, center, leftMiddle, topLeft);
                AddTriangle(target, center, bottomLeft, leftMiddle);
                if (tile == BiomeTile.Water) AddWaterSurfaceQuad(x, y, vertices, normals, uvs, triangles[WaterSurfaceSubmesh]);
            }

            Vector3[] vertexArray = vertices.ToArray();
            int[][] submeshes = { triangles[0].ToArray(), triangles[1].ToArray(), triangles[2].ToArray(), triangles[3].ToArray() };
            return new ForestSurfaceMeshData(gridWidth, gridHeight, vertexArray, normals.ToArray(), uvs.ToArray(), submeshes, ComputeHash(vertexArray, submeshes));
        }

        public static Mesh CreateMesh(ForestSurfaceMeshData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Mesh mesh = new Mesh
            {
                name = "PCG Forest Surface " + data.StableHash,
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

        public static float SampleSurfaceHeight(BiomeWorldData world, float worldX, float worldZ)
        {
            if (world == null) return 0f;
            float gridX = Mathf.Clamp((worldX + .5f) * 2f, 0f, world.Width * 2f);
            float gridY = Mathf.Clamp((worldZ + .5f) * 2f, 0f, world.Height * 2f);
            int x0 = Mathf.FloorToInt(gridX), y0 = Mathf.FloorToInt(gridY);
            int x1 = Math.Min(world.Width * 2, x0 + 1), y1 = Math.Min(world.Height * 2, y0 + 1);
            float tx = gridX - x0, ty = gridY - y0;
            float bottom = Mathf.Lerp(SampleHeight(world, x0, y0), SampleHeight(world, x1, y0), tx);
            float top = Mathf.Lerp(SampleHeight(world, x0, y1), SampleHeight(world, x1, y1), tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private static float SampleHeight(BiomeWorldData world, int gridX, int gridY)
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
                if (!world.IsInBounds(x, y)) continue;
                int index = y * world.Width + x;
                sum += world.Tiles[index] == BiomeTile.Water ? UnderwaterBedHeight : world.Elevations[index];
                count++;
            }
            return count == 0 ? 0f : sum / count;
        }

        private static int SubmeshFor(BiomeTile tile)
        {
            // Outdoor roads intentionally reuse the biome path surface slot. This preserves the
            // four-submesh compatibility contract while rendering a wide packed-snow/dirt road.
            if (tile == BiomeTile.Path || tile == BiomeTile.Road) return PathSubmesh;
            if (tile == BiomeTile.Water) return UnderwaterSubmesh;
            return GroundSubmesh;
        }

        private static void AddTriangle(List<int> target, int first, int second, int third)
        {
            target.Add(first); target.Add(second); target.Add(third);
        }

        private static void AddWaterSurfaceQuad(int x, int y, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            int first = vertices.Count;
            float left = x - .5f, right = x + .5f, bottom = y - .5f, top = y + .5f;
            vertices.Add(new Vector3(left, WaterSurfaceHeight, bottom));
            vertices.Add(new Vector3(right, WaterSurfaceHeight, bottom));
            vertices.Add(new Vector3(right, WaterSurfaceHeight, top));
            vertices.Add(new Vector3(left, WaterSurfaceHeight, top));
            for (int index = 0; index < 4; index++) normals.Add(Vector3.up);
            uvs.Add(new Vector2(left * UvWorldScale, bottom * UvWorldScale));
            uvs.Add(new Vector2(right * UvWorldScale, bottom * UvWorldScale));
            uvs.Add(new Vector2(right * UvWorldScale, top * UvWorldScale));
            uvs.Add(new Vector2(left * UvWorldScale, top * UvWorldScale));
            AddTriangle(triangles, first, first + 2, first + 1);
            AddTriangle(triangles, first, first + 3, first + 2);
        }

        private static string ComputeHash(Vector3[] vertices, int[][] submeshes)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, vertices.Length);
                for (int index = 0; index < vertices.Length; index++) Mix(ref hash, Quantize(vertices[index].y));
                for (int submesh = 0; submesh < submeshes.Length; submesh++)
                {
                    Mix(ref hash, submesh);
                    Mix(ref hash, submeshes[submesh].Length);
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
