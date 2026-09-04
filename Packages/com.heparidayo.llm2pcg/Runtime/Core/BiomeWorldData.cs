using System;
using System.Collections.Generic;

namespace Llm2Pcg.Core
{
    public enum BiomeTile : byte { Ground, Water, Path, Road, Building, Park }

    /// <summary>Pure deterministic biome data including quantized terrain and structure heights.</summary>
    public sealed class BiomeWorldData : IPCGNavigableWorldData
    {
        public int Width { get; }
        public int Height { get; }
        public int Seed { get; }
        public string WorldType { get; }
        public string GeneratorVersion { get; }
        public BiomeTile[] Tiles { get; }
        public float[] Elevations { get; }
        public float[] StructureHeights { get; }
        public Int2 StartPosition { get; }
        public Int2 ExitPosition { get; }
        public IReadOnlyList<Int2> Props { get; }

        public BiomeWorldData(string type, string version, int width, int height, int seed, BiomeTile[] tiles, Int2 start, Int2 exit, List<Int2> props, float[] elevations = null, float[] structureHeights = null)
        {
            WorldType = type;
            GeneratorVersion = version;
            Width = width;
            Height = height;
            Seed = seed;
            Tiles = tiles;
            Elevations = elevations ?? new float[width * height];
            StructureHeights = structureHeights ?? new float[width * height];
            StartPosition = start;
            ExitPosition = exit;
            Props = props.AsReadOnly();
        }

        public bool IsInBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool IsWalkable(int x, int y)
        {
            if (!IsInBounds(x, y)) return false;
            BiomeTile tile = Tiles[y * Width + x];
            return tile != BiomeTile.Water && tile != BiomeTile.Building;
        }
        public float GetSurfaceHeight(int x, int y) => IsInBounds(x, y) ? Elevations[y * Width + x] : 0f;

        public string ComputeStableHash()
        {
            unchecked
            {
                uint hash = 2166136261;
                Mix(ref hash, Width); Mix(ref hash, Height); Mix(ref hash, Seed);
                for (int i = 0; i < Tiles.Length; i++)
                {
                    hash ^= (byte)Tiles[i]; hash *= 16777619;
                    Mix(ref hash, Quantize(Elevations[i]));
                    Mix(ref hash, Quantize(StructureHeights[i]));
                }
                for (int i = 0; i < Props.Count; i++) { Mix(ref hash, Props[i].X); Mix(ref hash, Props[i].Y); }
                return hash.ToString("X8");
            }
        }

        private static int Quantize(float value) => (int)Math.Round(value * 1000f, MidpointRounding.AwayFromZero);
        private static void Mix(ref uint hash, int value) { hash ^= (byte)value; hash *= 16777619; hash ^= (byte)(value >> 8); hash *= 16777619; hash ^= (byte)(value >> 16); hash *= 16777619; hash ^= (byte)(value >> 24); hash *= 16777619; }
    }
}
