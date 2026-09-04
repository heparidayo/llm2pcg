using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;

namespace Llm2Pcg.Core
{
    /// <summary>Pure cave grid. True in SolidCells means an unwalkable rock cell.</summary>
    public sealed class CaveWorldData : IPCGNavigableWorldData
    {
        public int Width { get; }
        public int Height { get; }
        public int Seed { get; }
        public string WorldType => PCGRequest.CaveWorldType;
        public string GeneratorVersion => PCGRequest.CaveGeneratorVersion;
        public bool[] SolidCells { get; }
        public Int2 StartPosition { get; }
        public Int2 ExitPosition { get; }
        public IReadOnlyList<Int2> TunnelCells { get; }

        public CaveWorldData(int width, int height, int seed, bool[] solidCells, Int2 start, Int2 exit, List<Int2> tunnels)
        { Width = width; Height = height; Seed = seed; SolidCells = solidCells; StartPosition = start; ExitPosition = exit; TunnelCells = tunnels.AsReadOnly(); }
        public bool IsInBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
        public bool IsWalkable(int x, int y) => IsInBounds(x, y) && !SolidCells[y * Width + x];
        public float GetSurfaceHeight(int x, int y) => 0f;
        public string ComputeStableHash()
        {
            unchecked
            {
                uint hash = 2166136261u;
                Mix(ref hash, Width); Mix(ref hash, Height); Mix(ref hash, Seed); Mix(ref hash, StartPosition.X); Mix(ref hash, StartPosition.Y); Mix(ref hash, ExitPosition.X); Mix(ref hash, ExitPosition.Y);
                for (int i = 0; i < SolidCells.Length; i++) { hash ^= SolidCells[i] ? (byte)1 : (byte)0; hash *= 16777619u; }
                return hash.ToString("X8");
            }
        }
        private static void Mix(ref uint hash, int value) { hash ^= (byte)value; hash *= 16777619u; hash ^= (byte)(value >> 8); hash *= 16777619u; hash ^= (byte)(value >> 16); hash *= 16777619u; hash ^= (byte)(value >> 24); hash *= 16777619u; }
    }
}
