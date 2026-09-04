using System;

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
        public static int Stage(int seed, int stage)
        {
            unchecked { uint value = (uint)seed ^ ((uint)stage * 0x9E3779B9u); value ^= value >> 16; value *= 0x85EBCA6Bu; value ^= value >> 13; value *= 0xC2B2AE35u; value ^= value >> 16; return (int)value; }
        }
    }
}
