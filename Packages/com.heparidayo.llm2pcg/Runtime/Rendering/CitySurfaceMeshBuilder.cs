using System;
using System.Collections.Generic;
using Llm2Pcg.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Rendering
{
    public sealed class CitySurfaceMeshData
    {
        public readonly Vector3[] Vertices;
        public readonly Vector3[] Normals;
        public readonly Vector2[] Uvs;
        public readonly int[][] SubmeshTriangles;
        public readonly int ElevationEdgeCount;
        public readonly int RoadMarkingSegmentCount;
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

        public CitySurfaceMeshData(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[][] submeshTriangles, int elevationEdgeCount, int roadMarkingSegmentCount, string stableHash)
        {
            Vertices = vertices;
            Normals = normals;
            Uvs = uvs;
            SubmeshTriangles = submeshTriangles;
            ElevationEdgeCount = elevationEdgeCount;
            RoadMarkingSegmentCount = roadMarkingSegmentCount;
            StableHash = stableHash;
        }
    }

    /// <summary>
    /// Bakes City tile semantics into one object-free mesh: pavement pads, asphalt, parks, curbs, and road edge markings.
    /// Stable row-major traversal preserves determinism while replacing per-cell Cube submissions.
    /// </summary>
    public static class CitySurfaceMeshBuilder
    {
        public const int SidewalkSubmesh = 0;
        public const int RoadSubmesh = 1;
        public const int ParkSubmesh = 2;
        public const int CurbSubmesh = 3;
        public const int RoadMarkingSubmesh = 4;
        public const float RoadHeight = 0f;
        public const float ParkHeight = .035f;
        public const float SidewalkHeight = .075f;
        public const float RoadMarkingHeight = .014f;
        private const float UvWorldScale = 1f / 4f;
        private const float MarkingInset = .075f;
        private const float MarkingWidth = .085f;
        private const float CenterLineHalfLength = .31f;
        private const float CenterLineHalfWidth = .045f;

        public static CitySurfaceMeshData BuildData(BiomeWorldData world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            List<Vector3> vertices = new List<Vector3>(world.Width * world.Height * 6);
            List<Vector3> normals = new List<Vector3>(vertices.Capacity);
            List<Vector2> uvs = new List<Vector2>(vertices.Capacity);
            List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };

            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                BiomeTile tile = world.Tiles[y * world.Width + x];
                AddTopQuad(x - .5f, y - .5f, x + .5f, y + .5f, HeightFor(tile), vertices, normals, uvs, triangles[SubmeshFor(tile)]);
            }

            int elevationEdges = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                float height = HeightFor(world.Tiles[y * world.Width + x]);
                if (x + 1 < world.Width)
                {
                    float neighbour = HeightFor(world.Tiles[y * world.Width + x + 1]);
                    if (!Mathf.Approximately(height, neighbour))
                    {
                        AddDoubleSidedVerticalQuad(new Vector3(x + .5f, Math.Min(height, neighbour), y - .5f), new Vector3(x + .5f, Math.Min(height, neighbour), y + .5f), Math.Max(height, neighbour), vertices, normals, uvs, triangles[CurbSubmesh]);
                        elevationEdges++;
                    }
                }
                if (y + 1 < world.Height)
                {
                    float neighbour = HeightFor(world.Tiles[(y + 1) * world.Width + x]);
                    if (!Mathf.Approximately(height, neighbour))
                    {
                        AddDoubleSidedVerticalQuad(new Vector3(x + .5f, Math.Min(height, neighbour), y + .5f), new Vector3(x - .5f, Math.Min(height, neighbour), y + .5f), Math.Max(height, neighbour), vertices, normals, uvs, triangles[CurbSubmesh]);
                        elevationEdges++;
                    }
                }
            }

            int markingSegments = 0;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if (!IsRoad(world, x, y)) continue;
                if (!IsRoad(world, x, y - 1)) { AddTopQuad(x - .5f, y - .5f + MarkingInset, x + .5f, y - .5f + MarkingInset + MarkingWidth, RoadMarkingHeight, vertices, normals, uvs, triangles[RoadMarkingSubmesh]); markingSegments++; }
                if (!IsRoad(world, x + 1, y)) { AddTopQuad(x + .5f - MarkingInset - MarkingWidth, y - .5f, x + .5f - MarkingInset, y + .5f, RoadMarkingHeight, vertices, normals, uvs, triangles[RoadMarkingSubmesh]); markingSegments++; }
                if (!IsRoad(world, x, y + 1)) { AddTopQuad(x - .5f, y + .5f - MarkingInset - MarkingWidth, x + .5f, y + .5f - MarkingInset, RoadMarkingHeight, vertices, normals, uvs, triangles[RoadMarkingSubmesh]); markingSegments++; }
                if (!IsRoad(world, x - 1, y)) { AddTopQuad(x - .5f + MarkingInset, y - .5f, x - .5f + MarkingInset + MarkingWidth, y + .5f, RoadMarkingHeight, vertices, normals, uvs, triangles[RoadMarkingSubmesh]); markingSegments++; }
                if (((x + y) & 1) == 0)
                {
                    int north = RoadRun(world, x, y, 0, -1), south = RoadRun(world, x, y, 0, 1);
                    int west = RoadRun(world, x, y, -1, 0), east = RoadRun(world, x, y, 1, 0);
                    int horizontalSpan = west + east, verticalSpan = north + south;
                    if (horizontalSpan > verticalSpan && north == south)
                    {
                        AddTopQuad(x - CenterLineHalfLength, y - CenterLineHalfWidth, x + CenterLineHalfLength, y + CenterLineHalfWidth, RoadMarkingHeight, vertices, normals, uvs, triangles[RoadMarkingSubmesh]);
                        markingSegments++;
                    }
                    else if (verticalSpan > horizontalSpan && west == east)
                    {
                        AddTopQuad(x - CenterLineHalfWidth, y - CenterLineHalfLength, x + CenterLineHalfWidth, y + CenterLineHalfLength, RoadMarkingHeight, vertices, normals, uvs, triangles[RoadMarkingSubmesh]);
                        markingSegments++;
                    }
                    if (IsRoad(world, x, y + 1) && !IsRoad(world, x, y - 1) && !IsRoad(world, x, y + 2) &&
                        ((IsRoad(world, x - 1, y) && IsRoad(world, x - 1, y + 1)) || (IsRoad(world, x + 1, y) && IsRoad(world, x + 1, y + 1))))
                    {
                        AddTopQuad(x - CenterLineHalfLength, y + .5f - CenterLineHalfWidth, x + CenterLineHalfLength, y + .5f + CenterLineHalfWidth, RoadMarkingHeight, vertices, normals, uvs, triangles[RoadMarkingSubmesh]);
                        markingSegments++;
                    }
                    if (IsRoad(world, x + 1, y) && !IsRoad(world, x - 1, y) && !IsRoad(world, x + 2, y) &&
                        ((IsRoad(world, x, y - 1) && IsRoad(world, x + 1, y - 1)) || (IsRoad(world, x, y + 1) && IsRoad(world, x + 1, y + 1))))
                    {
                        AddTopQuad(x + .5f - CenterLineHalfWidth, y - CenterLineHalfLength, x + .5f + CenterLineHalfWidth, y + CenterLineHalfLength, RoadMarkingHeight, vertices, normals, uvs, triangles[RoadMarkingSubmesh]);
                        markingSegments++;
                    }
                }
            }

            int[][] submeshes = { triangles[0].ToArray(), triangles[1].ToArray(), triangles[2].ToArray(), triangles[3].ToArray(), triangles[4].ToArray() };
            Vector3[] vertexArray = vertices.ToArray();
            return new CitySurfaceMeshData(vertexArray, normals.ToArray(), uvs.ToArray(), submeshes, elevationEdges, markingSegments, ComputeHash(vertexArray, submeshes));
        }

        public static Mesh CreateMesh(CitySurfaceMeshData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Mesh mesh = new Mesh
            {
                name = "PCG City Surface " + data.StableHash,
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
            int x = Mathf.Clamp(Mathf.RoundToInt(worldX), 0, world.Width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(worldZ), 0, world.Height - 1);
            return HeightFor(world.Tiles[y * world.Width + x]);
        }

        public static float HeightFor(BiomeTile tile)
        {
            if (tile == BiomeTile.Road) return RoadHeight;
            if (tile == BiomeTile.Park) return ParkHeight;
            return SidewalkHeight;
        }

        private static int SubmeshFor(BiomeTile tile)
        {
            if (tile == BiomeTile.Road) return RoadSubmesh;
            if (tile == BiomeTile.Park) return ParkSubmesh;
            return SidewalkSubmesh;
        }

        private static bool IsRoad(BiomeWorldData world, int x, int y)
        {
            return world.IsInBounds(x, y) && world.Tiles[y * world.Width + x] == BiomeTile.Road;
        }

        private static int RoadRun(BiomeWorldData world, int x, int y, int stepX, int stepY)
        {
            int count = 0;
            for (int step = 1; step <= 4; step++)
            {
                if (!IsRoad(world, x + stepX * step, y + stepY * step)) break;
                count++;
            }
            return count;
        }

        private static void AddTopQuad(float x0, float z0, float x1, float z1, float height, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(x0, height, z0));
            vertices.Add(new Vector3(x1, height, z0));
            vertices.Add(new Vector3(x1, height, z1));
            vertices.Add(new Vector3(x0, height, z1));
            for (int index = 0; index < 4; index++) normals.Add(Vector3.up);
            uvs.Add(new Vector2(x0, z0) * UvWorldScale);
            uvs.Add(new Vector2(x1, z0) * UvWorldScale);
            uvs.Add(new Vector2(x1, z1) * UvWorldScale);
            uvs.Add(new Vector2(x0, z1) * UvWorldScale);
            AddTriangle(triangles, start, start + 2, start + 1);
            AddTriangle(triangles, start, start + 3, start + 2);
        }

        private static void AddDoubleSidedVerticalQuad(Vector3 bottomA, Vector3 bottomB, float topHeight, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            Vector3 topA = new Vector3(bottomA.x, topHeight, bottomA.z);
            Vector3 topB = new Vector3(bottomB.x, topHeight, bottomB.z);
            Vector3 normal = Vector3.Cross(bottomB - bottomA, topA - bottomA).normalized;
            AddVerticalFace(bottomA, bottomB, topB, topA, normal, vertices, normals, uvs, triangles);
            AddVerticalFace(bottomB, bottomA, topA, topB, -normal, vertices, normals, uvs, triangles);
        }

        private static void AddVerticalFace(Vector3 bottomA, Vector3 bottomB, Vector3 topB, Vector3 topA, Vector3 normal, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            int start = vertices.Count;
            vertices.Add(bottomA); vertices.Add(bottomB); vertices.Add(topB); vertices.Add(topA);
            normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
            float distance = Vector3.Distance(bottomA, bottomB) * UvWorldScale;
            float height = Math.Max(topA.y - bottomA.y, topB.y - bottomB.y) * UvWorldScale;
            uvs.Add(Vector2.zero); uvs.Add(new Vector2(distance, 0f)); uvs.Add(new Vector2(distance, height)); uvs.Add(new Vector2(0f, height));
            AddTriangle(triangles, start, start + 1, start + 2);
            AddTriangle(triangles, start, start + 2, start + 3);
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
                    Mix(ref hash, Quantize(vertices[index].x)); Mix(ref hash, Quantize(vertices[index].y)); Mix(ref hash, Quantize(vertices[index].z));
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
