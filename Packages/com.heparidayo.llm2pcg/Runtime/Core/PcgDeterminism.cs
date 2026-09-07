using System;
using System.Collections.Generic;

namespace Llm2Pcg.Core
{
    /// <summary>Small deterministic PRNG and stage-seed derivation with no Unity/clock state.</summary>
    public struct PcgDeterministicRandom
    {
        private uint state;
        public PcgDeterministicRandom(int seed) { state = (uint)seed; if (state == 0) state = 0x6D2B79F5u; }
        public uint NextUInt() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
        public int NextInt(int exclusiveMax) => exclusiveMax <= 1 ? 0 : (int)(NextUInt() % (uint)exclusiveMax);
        public float NextFloat() => (NextUInt() & 0x00FFFFFFu) / 16777216f;
    }

    public static class PcgSeed
    {
        // Force double division before midpoint rounding. Mono can retain extra precision
        // for a float expression while CoreCLR rounds it to float first (0.08f: 13 vs 12).
        public static int NoiseScale(float frequency) => Math.Max(3,
            (int)Math.Round(1.0 / Math.Max(.001, (double)frequency), MidpointRounding.ToEven));

        public static int Stage(int seed, int stage)
        {
            unchecked { uint value = (uint)seed ^ ((uint)stage * 0x9E3779B9u); value ^= value >> 16; value *= 0x85EBCA6Bu; value ^= value >> 13; value *= 0xC2B2AE35u; value ^= value >> 16; return (int)value; }
        }
    }

    /// <summary>Exact minimum-distance lookup; preserves candidate order and consumes no random values.</summary>
    public sealed class PcgPointSpacingIndex
    {
        private readonly int width, height, distance, columns, rows;
        private readonly long minimumSquared;
        private readonly List<Int2>[] buckets;

        public PcgPointSpacingIndex(int width, int height, int minimumDistance)
        {
            if (width <= 0 || height <= 0 || minimumDistance <= 0) throw new ArgumentOutOfRangeException(nameof(minimumDistance));
            this.width = width; this.height = height; distance = minimumDistance;
            columns = (width - 1) / distance + 1; rows = (height - 1) / distance + 1;
            minimumSquared = (long)distance * distance;
            buckets = new List<Int2>[checked(columns * rows)];
        }

        public bool CanPlace(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return false;
            int bx = x / distance, by = y / distance;
            for (int row = Math.Max(0, by - 1); row <= Math.Min(rows - 1, by + 1); row++)
            for (int column = Math.Max(0, bx - 1); column <= Math.Min(columns - 1, bx + 1); column++)
            {
                List<Int2> points = buckets[row * columns + column];
                if (points == null) continue;
                for (int i = 0; i < points.Count; i++)
                {
                    long dx = x - points[i].X, dy = y - points[i].Y;
                    if (dx * dx + dy * dy < minimumSquared) return false;
                }
            }
            return true;
        }

        public void Add(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) throw new ArgumentOutOfRangeException(nameof(x));
            int index = (y / distance) * columns + x / distance;
            if (buckets[index] == null) buckets[index] = new List<Int2>();
            buckets[index].Add(new Int2(x, y));
        }
    }
}
