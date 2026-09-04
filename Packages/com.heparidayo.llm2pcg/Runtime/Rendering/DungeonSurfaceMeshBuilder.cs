using System;
using System.Collections.Generic;
using Llm2Pcg.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Rendering
{
    public readonly struct DungeonSurfaceBuildOptions
    {
        public readonly bool IncludeRoomFloor;
        public readonly bool IncludeCorridorFloor;
        public readonly bool IncludeWalls;
        public readonly bool IncludeTrims;
        public readonly bool IncludeDoorways;

        public DungeonSurfaceBuildOptions(bool includeRoomFloor, bool includeCorridorFloor, bool includeWalls, bool includeTrims, bool includeDoorways)
        {
            IncludeRoomFloor = includeRoomFloor;
            IncludeCorridorFloor = includeCorridorFloor;
            IncludeWalls = includeWalls;
            IncludeTrims = includeTrims;
            IncludeDoorways = includeDoorways;
        }

        public static DungeonSurfaceBuildOptions AllFallback => new DungeonSurfaceBuildOptions(true, true, true, true, true);
    }

    public readonly struct DungeonBoundarySegment
    {
        public readonly int CellX;
        public readonly int CellY;
        public readonly int Direction;
        public readonly bool IsExterior;

        public DungeonBoundarySegment(int cellX, int cellY, int direction, bool isExterior)
        {
            CellX = cellX;
            CellY = cellY;
            Direction = direction;
            IsExterior = isExterior;
        }

        public bool ExtendsAlongX => Direction == 0 || Direction == 2;
        public Quaternion Rotation => ExtendsAlongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);

        public Vector3 Center(float tileSize)
        {
            float x = CellX * tileSize;
            float z = CellY * tileSize;
            if (Direction == 0) z -= tileSize * .5f;
            else if (Direction == 1) x += tileSize * .5f;
            else if (Direction == 2) z += tileSize * .5f;
            else x -= tileSize * .5f;
            return new Vector3(x, 0f, z);
        }
    }

    public readonly struct DungeonDoorwaySegment
    {
        public readonly int CellX;
        public readonly int CellY;
        public readonly bool ExtendsAlongX;

        public DungeonDoorwaySegment(int cellX, int cellY, bool extendsAlongX)
        {
            CellX = cellX;
            CellY = cellY;
            ExtendsAlongX = extendsAlongX;
        }

        public Quaternion Rotation => ExtendsAlongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
        public Vector3 Center(float tileSize) => new Vector3(CellX * tileSize, 0f, CellY * tileSize);
    }

    public readonly struct DungeonCornerPoint
    {
        public readonly int GridX;
        public readonly int GridY;

        public DungeonCornerPoint(int gridX, int gridY) { GridX = gridX; GridY = gridY; }
        public Vector3 Center(float tileSize) => new Vector3((GridX - .5f) * tileSize, 0f, (GridY - .5f) * tileSize);
    }

    public sealed class DungeonSurfaceMeshData
    {
        public readonly Vector3[] Vertices;
        public readonly Vector3[] Normals;
        public readonly Vector2[] Uvs;
        public readonly int[][] SubmeshTriangles;
        public readonly int BoundaryEdgeCount;
        public readonly int ExteriorBoundaryEdgeCount;
        public readonly int DoorwayCount;
        public readonly int CornerCount;
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

        public DungeonSurfaceMeshData(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[][] submeshTriangles, int boundaryEdgeCount, int exteriorBoundaryEdgeCount, int doorwayCount, int cornerCount, string stableHash)
        {
            Vertices = vertices;
            Normals = normals;
            Uvs = uvs;
            SubmeshTriangles = submeshTriangles;
            BoundaryEdgeCount = boundaryEdgeCount;
            ExteriorBoundaryEdgeCount = exteriorBoundaryEdgeCount;
            DoorwayCount = doorwayCount;
            CornerCount = cornerCount;
            StableHash = stableHash;
        }
    }

    /// <summary>
    /// Converts Dungeon walkability into one deterministic architectural mesh. Floor cells become connected
    /// room/corridor surfaces while only walkable-to-solid boundaries create thin wall and top-trim segments.
    /// </summary>
    public static class DungeonSurfaceMeshBuilder
    {
        public const int RoomFloorSubmesh = 0;
        public const int CorridorFloorSubmesh = 1;
        public const int WallSubmesh = 2;
        public const int TrimSubmesh = 3;
        public const int DoorwaySubmesh = 4;
        public const float DefaultWallHeight = 1.8f;
        public const float WallThickness = .14f;
        public const float TrimHeight = .14f;
        public const float TrimThickness = .22f;
        private const float FloorUvScale = .25f;

        public static DungeonSurfaceMeshData BuildData(DungeonWorldData world)
        {
            return BuildData(world, 1f, DefaultWallHeight, DungeonSurfaceBuildOptions.AllFallback);
        }

        public static DungeonSurfaceMeshData BuildData(DungeonWorldData world, float tileSize, float wallHeight, DungeonSurfaceBuildOptions options)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (tileSize <= 0f) throw new ArgumentOutOfRangeException(nameof(tileSize));
            if (wallHeight <= 0f) throw new ArgumentOutOfRangeException(nameof(wallHeight));

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };

            float half = tileSize * .5f;
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                DungeonCell cell = world.GetCell(x, y);
                if (cell == DungeonCell.Floor && options.IncludeRoomFloor)
                    AddTopQuad(x * tileSize - half, y * tileSize - half, x * tileSize + half, y * tileSize + half, 0f, vertices, normals, uvs, triangles[RoomFloorSubmesh]);
                else if (cell == DungeonCell.Corridor && options.IncludeCorridorFloor)
                    AddTopQuad(x * tileSize - half, y * tileSize - half, x * tileSize + half, y * tileSize + half, .006f, vertices, normals, uvs, triangles[CorridorFloorSubmesh]);
            }

            List<DungeonBoundarySegment> boundaries = GetBoundarySegments(world);
            int exteriorCount = 0;
            for (int index = 0; index < boundaries.Count; index++)
            {
                DungeonBoundarySegment segment = boundaries[index];
                if (segment.IsExterior) exteriorCount++;
                Vector3 center = segment.Center(tileSize);
                if (options.IncludeWalls)
                    AddOrientedBox(center, segment.Rotation, new Vector3(tileSize * 1.025f, wallHeight, WallThickness * tileSize), vertices, normals, uvs, triangles[WallSubmesh]);
                if (options.IncludeTrims)
                {
                    Vector3 trimBase = center + Vector3.up * (wallHeight - TrimHeight * tileSize);
                    AddOrientedBox(trimBase, segment.Rotation, new Vector3(tileSize * 1.065f, TrimHeight * tileSize, TrimThickness * tileSize), vertices, normals, uvs, triangles[TrimSubmesh]);
                }
            }

            List<DungeonDoorwaySegment> doorways = GetDoorwaySegments(world);
            if (options.IncludeDoorways)
                for (int index = 0; index < doorways.Count; index++) AddDoorway(doorways[index], tileSize, wallHeight, vertices, normals, uvs, triangles[DoorwaySubmesh]);

            List<DungeonCornerPoint> corners = GetCornerPoints(boundaries);
            int[][] submeshes = { triangles[0].ToArray(), triangles[1].ToArray(), triangles[2].ToArray(), triangles[3].ToArray(), triangles[4].ToArray() };
            Vector3[] vertexArray = vertices.ToArray();
            return new DungeonSurfaceMeshData(vertexArray, normals.ToArray(), uvs.ToArray(), submeshes, boundaries.Count, exteriorCount, doorways.Count, corners.Count, ComputeHash(vertexArray, submeshes));
        }

        public static Mesh CreateMesh(DungeonSurfaceMeshData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Mesh mesh = new Mesh
            {
                name = "PCG Dungeon Surface " + data.StableHash,
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

        public static List<DungeonBoundarySegment> GetBoundarySegments(DungeonWorldData world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            List<DungeonBoundarySegment> result = new List<DungeonBoundarySegment>();
            int[] dx = { 0, 1, 0, -1 };
            int[] dy = { -1, 0, 1, 0 };
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if (!world.IsWalkable(x, y)) continue;
                for (int direction = 0; direction < 4; direction++)
                {
                    int neighbourX = x + dx[direction], neighbourY = y + dy[direction];
                    if (world.IsWalkable(neighbourX, neighbourY)) continue;
                    bool exterior = neighbourX < 0 || neighbourY < 0 || neighbourX >= world.Width || neighbourY >= world.Height;
                    result.Add(new DungeonBoundarySegment(x, y, direction, exterior));
                }
            }
            return result;
        }

        public static List<DungeonDoorwaySegment> GetDoorwaySegments(DungeonWorldData world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            List<DungeonDoorwaySegment> result = new List<DungeonDoorwaySegment>();
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                DungeonCell cell = world.GetCell(x, y);
                if (!IsRoomOrCorridor(cell)) continue;
                if (x + 1 < world.Width && IsRoomCorridorTransition(cell, world.GetCell(x + 1, y)))
                    result.Add(new DungeonDoorwaySegment(x * 2 + 1, y * 2, false));
                if (y + 1 < world.Height && IsRoomCorridorTransition(cell, world.GetCell(x, y + 1)))
                    result.Add(new DungeonDoorwaySegment(x * 2, y * 2 + 1, true));
            }
            return result;
        }

        public static List<DungeonCornerPoint> GetCornerPoints(IReadOnlyList<DungeonBoundarySegment> boundaries)
        {
            SortedDictionary<long, int> flags = new SortedDictionary<long, int>();
            if (boundaries != null)
                for (int index = 0; index < boundaries.Count; index++)
                {
                    DungeonBoundarySegment segment = boundaries[index];
                    if (segment.Direction == 0) { AddCornerFlag(flags, segment.CellX, segment.CellY, 1); AddCornerFlag(flags, segment.CellX + 1, segment.CellY, 1); }
                    else if (segment.Direction == 2) { AddCornerFlag(flags, segment.CellX, segment.CellY + 1, 1); AddCornerFlag(flags, segment.CellX + 1, segment.CellY + 1, 1); }
                    else if (segment.Direction == 1) { AddCornerFlag(flags, segment.CellX + 1, segment.CellY, 2); AddCornerFlag(flags, segment.CellX + 1, segment.CellY + 1, 2); }
                    else { AddCornerFlag(flags, segment.CellX, segment.CellY, 2); AddCornerFlag(flags, segment.CellX, segment.CellY + 1, 2); }
                }
            List<DungeonCornerPoint> result = new List<DungeonCornerPoint>();
            foreach (KeyValuePair<long, int> entry in flags)
                if (entry.Value == 3)
                {
                    int gridX = (int)(entry.Key & 0xFFFFFFFFL);
                    int gridY = (int)(entry.Key >> 32);
                    result.Add(new DungeonCornerPoint(gridX, gridY));
                }
            result.Sort((left, right) => { int value = left.GridY.CompareTo(right.GridY); return value != 0 ? value : left.GridX.CompareTo(right.GridX); });
            return result;
        }

        public static float SampleSurfaceHeight(DungeonWorldData world, float worldX, float worldZ)
        {
            return world == null ? 0f : 0f;
        }

        private static bool IsRoomOrCorridor(DungeonCell cell) => cell == DungeonCell.Floor || cell == DungeonCell.Corridor;
        private static bool IsRoomCorridorTransition(DungeonCell first, DungeonCell second) =>
            (first == DungeonCell.Floor && second == DungeonCell.Corridor) || (first == DungeonCell.Corridor && second == DungeonCell.Floor);

        private static void AddCornerFlag(IDictionary<long, int> flags, int gridX, int gridY, int flag)
        {
            long key = ((long)gridY << 32) | (uint)gridX;
            flags.TryGetValue(key, out int current);
            flags[key] = current | flag;
        }

        private static void AddDoorway(DungeonDoorwaySegment doorway, float tileSize, float wallHeight, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            Vector3 center = doorway.Center(tileSize * .5f);
            Quaternion rotation = doorway.Rotation;
            float postWidth = tileSize * .14f;
            float depth = tileSize * .18f;
            float openingHalf = tileSize * .43f;
            AddOrientedBox(center + rotation * new Vector3(-openingHalf, 0f, 0f), rotation, new Vector3(postWidth, wallHeight, depth), vertices, normals, uvs, triangles);
            AddOrientedBox(center + rotation * new Vector3(openingHalf, 0f, 0f), rotation, new Vector3(postWidth, wallHeight, depth), vertices, normals, uvs, triangles);
            AddOrientedBox(center + Vector3.up * (wallHeight - tileSize * .18f), rotation, new Vector3(tileSize, tileSize * .18f, depth), vertices, normals, uvs, triangles);
            AddOrientedBox(center + Vector3.up * .008f, rotation, new Vector3(tileSize * .88f, tileSize * .025f, tileSize * .32f), vertices, normals, uvs, triangles);
        }

        private static void AddTopQuad(float x0, float z0, float x1, float z1, float height, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(x0, height, z0)); vertices.Add(new Vector3(x1, height, z0)); vertices.Add(new Vector3(x1, height, z1)); vertices.Add(new Vector3(x0, height, z1));
            for (int index = 0; index < 4; index++) normals.Add(Vector3.up);
            uvs.Add(new Vector2(x0, z0) * FloorUvScale); uvs.Add(new Vector2(x1, z0) * FloorUvScale); uvs.Add(new Vector2(x1, z1) * FloorUvScale); uvs.Add(new Vector2(x0, z1) * FloorUvScale);
            AddTriangles(triangles, start);
        }

        private static void AddOrientedBox(Vector3 bottomCenter, Quaternion rotation, Vector3 size, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            float hx = size.x * .5f, hz = size.z * .5f, height = size.y;
            Matrix4x4 matrix = Matrix4x4.TRS(bottomCenter, rotation, Vector3.one);
            AddFace(matrix, new Vector3(-hx, height, -hz), new Vector3(hx, height, -hz), new Vector3(hx, height, hz), new Vector3(-hx, height, hz), Vector3.up, size.x, size.z, vertices, normals, uvs, triangles);
            AddFace(matrix, new Vector3(-hx, 0f, -hz), new Vector3(-hx, 0f, hz), new Vector3(hx, 0f, hz), new Vector3(hx, 0f, -hz), Vector3.down, size.x, size.z, vertices, normals, uvs, triangles);
            AddFace(matrix, new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, height, -hz), new Vector3(-hx, height, -hz), Vector3.back, size.x, height, vertices, normals, uvs, triangles);
            AddFace(matrix, new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz), new Vector3(-hx, height, hz), new Vector3(hx, height, hz), Vector3.forward, size.x, height, vertices, normals, uvs, triangles);
            AddFace(matrix, new Vector3(-hx, 0f, hz), new Vector3(-hx, 0f, -hz), new Vector3(-hx, height, -hz), new Vector3(-hx, height, hz), Vector3.left, size.z, height, vertices, normals, uvs, triangles);
            AddFace(matrix, new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(hx, height, hz), new Vector3(hx, height, -hz), Vector3.right, size.z, height, vertices, normals, uvs, triangles);
        }

        private static void AddFace(Matrix4x4 matrix, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float width, float height, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            int start = vertices.Count;
            vertices.Add(matrix.MultiplyPoint3x4(a)); vertices.Add(matrix.MultiplyPoint3x4(b)); vertices.Add(matrix.MultiplyPoint3x4(c)); vertices.Add(matrix.MultiplyPoint3x4(d));
            Vector3 worldNormal = matrix.MultiplyVector(normal).normalized;
            normals.Add(worldNormal); normals.Add(worldNormal); normals.Add(worldNormal); normals.Add(worldNormal);
            uvs.Add(Vector2.zero); uvs.Add(new Vector2(width, 0f) * FloorUvScale); uvs.Add(new Vector2(width, height) * FloorUvScale); uvs.Add(new Vector2(0f, height) * FloorUvScale);
            AddTriangles(triangles, start);
        }

        private static void AddTriangles(List<int> triangles, int start)
        {
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
            triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
        }

        private static string ComputeHash(Vector3[] vertices, int[][] submeshes)
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, vertices.Length);
                for (int index = 0; index < vertices.Length; index++)
                { Mix(ref hash, Quantize(vertices[index].x)); Mix(ref hash, Quantize(vertices[index].y)); Mix(ref hash, Quantize(vertices[index].z)); }
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
            hash ^= (byte)value; hash *= 16777619u; hash ^= (byte)(value >> 8); hash *= 16777619u;
            hash ^= (byte)(value >> 16); hash *= 16777619u; hash ^= (byte)(value >> 24); hash *= 16777619u;
        }
    }
}
