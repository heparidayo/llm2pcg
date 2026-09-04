using System;
using System.Collections.Generic;

namespace Llm2Pcg.Core
{
    public enum DungeonCell : byte
    {
        Empty = 0,
        Floor = 1,
        Corridor = 2
    }

    public readonly struct Int2
    {
        public readonly int X;
        public readonly int Y;

        public Int2(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>Pure C# integer rectangle with exclusive maximum coordinates.</summary>
    public readonly struct IntRect
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;
        public int XMax => X + Width;
        public int YMax => Y + Height;
        public int Area => Width * Height;
        public Int2 Center => new Int2(X + Width / 2, Y + Height / 2);

        public IntRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(int x, int y)
        {
            return x >= X && x < XMax && y >= Y && y < YMax;
        }
    }

    public readonly struct DungeonConnection
    {
        public readonly int RoomA;
        public readonly int RoomB;
        public readonly int Cost;

        public DungeonConnection(int roomA, int roomB, int cost)
        {
            RoomA = roomA;
            RoomB = roomB;
            Cost = cost;
        }
    }

    public enum DungeonPropType : byte
    {
        Pillar = 0,
        Crate = 1,
        Crystal = 2,
        Torch = 3
    }

    public readonly struct DungeonPropPlacement
    {
        public readonly Int2 Position;
        public readonly DungeonPropType Type;
        public readonly Int2 WallNormal;

        public DungeonPropPlacement(Int2 position, DungeonPropType type)
            : this(position, type, new Int2(0, 0))
        {
        }

        public DungeonPropPlacement(Int2 position, DungeonPropType type, Int2 wallNormal)
        {
            Position = position;
            Type = type;
            WallNormal = wallNormal;
        }
    }

    /// <summary>Generated data only. This type deliberately has no UnityEngine dependency.</summary>
    public sealed class DungeonWorldData : IPCGNavigableWorldData
    {
        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        public int Width { get; }
        public int Height { get; }
        public int Seed { get; }
        public string WorldType => Contract.PCGRequest.DungeonWorldType;
        public string GeneratorVersion => Contract.PCGRequest.DungeonGeneratorVersion;
        public byte[] Cells { get; }
        public IReadOnlyList<IntRect> Rooms { get; }
        public IReadOnlyList<DungeonConnection> Connections { get; }
        public int StartRoomIndex { get; }
        public int ExitRoomIndex { get; }
        public IReadOnlyList<int> BossRoomIndices { get; }
        public IReadOnlyList<int> TreasureRoomIndices { get; }
        public IReadOnlyList<int> ShopRoomIndices { get; }
        public IReadOnlyList<int> SecretRoomIndices { get; }
        public IReadOnlyList<DungeonPropPlacement> Props { get; }

        public Int2 StartPosition => Rooms[StartRoomIndex].Center;
        public Int2 ExitPosition => Rooms[ExitRoomIndex].Center;

        public DungeonWorldData(
            int width,
            int height,
            int seed,
            byte[] cells,
            List<IntRect> rooms,
            List<DungeonConnection> connections,
            int startRoomIndex,
            int exitRoomIndex,
            List<int> bossRoomIndices,
            List<DungeonPropPlacement> props,
            List<int> treasureRoomIndices = null,
            List<int> shopRoomIndices = null,
            List<int> secretRoomIndices = null)
        {
            Width = width;
            Height = height;
            Seed = seed;
            Cells = cells;
            Rooms = rooms.AsReadOnly();
            Connections = connections.AsReadOnly();
            StartRoomIndex = startRoomIndex;
            ExitRoomIndex = exitRoomIndex;
            BossRoomIndices = bossRoomIndices.AsReadOnly();
            TreasureRoomIndices = (treasureRoomIndices ?? new List<int>()).AsReadOnly();
            ShopRoomIndices = (shopRoomIndices ?? new List<int>()).AsReadOnly();
            SecretRoomIndices = (secretRoomIndices ?? new List<int>()).AsReadOnly();
            Props = props.AsReadOnly();
        }

        public bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public DungeonCell GetCell(int x, int y)
        {
            return IsInBounds(x, y) ? (DungeonCell)Cells[y * Width + x] : DungeonCell.Empty;
        }

        public bool IsWalkable(int x, int y)
        {
            return GetCell(x, y) != DungeonCell.Empty;
        }

        public float GetSurfaceHeight(int x, int y) => 0f;

        public string ComputeStableHash()
        {
            uint hash = FnvOffsetBasis;
            AppendInt(ref hash, Width);
            AppendInt(ref hash, Height);
            AppendInt(ref hash, Seed);
            AppendInt(ref hash, StartRoomIndex);
            AppendInt(ref hash, ExitRoomIndex);
            for (int index = 0; index < BossRoomIndices.Count; index++)
                AppendInt(ref hash, BossRoomIndices[index]);
            for (int index = 0; index < Props.Count; index++)
            {
                AppendInt(ref hash, Props[index].Position.X);
                AppendInt(ref hash, Props[index].Position.Y);
                AppendInt(ref hash, (int)Props[index].Type);
                AppendInt(ref hash, Props[index].WallNormal.X);
                AppendInt(ref hash, Props[index].WallNormal.Y);
            }
            AppendList(ref hash, TreasureRoomIndices);
            AppendList(ref hash, ShopRoomIndices);
            AppendList(ref hash, SecretRoomIndices);
            for (int index = 0; index < Cells.Length; index++)
            {
                hash ^= Cells[index];
                hash *= FnvPrime;
            }

            return hash.ToString("X8");
        }

        private static void AppendList(ref uint hash, IReadOnlyList<int> values)
        {
            for (int index = 0; index < values.Count; index++)
                AppendInt(ref hash, values[index]);
        }

        private static void AppendInt(ref uint hash, int value)
        {
            unchecked
            {
                hash ^= (byte)value;
                hash *= FnvPrime;
                hash ^= (byte)(value >> 8);
                hash *= FnvPrime;
                hash ^= (byte)(value >> 16);
                hash *= FnvPrime;
                hash ^= (byte)(value >> 24);
                hash *= FnvPrime;
            }
        }
    }
}
