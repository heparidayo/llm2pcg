using System;
using Llm2Pcg.Contract;

namespace Llm2Pcg.Core
{
    /// <summary>Small deterministic save record: regenerate from the request, then verify the recorded hash.</summary>
    [Serializable]
    public sealed class PCGGenerationSnapshot
    {
        public const int CurrentFormatVersion = 2;

        public int formatVersion;
        public PCGRequest request;
        public string worldHash;

        public static PCGGenerationSnapshot Create(PCGRequest request, IPCGWorldData world)
        {
            return new PCGGenerationSnapshot
            {
                formatVersion = CurrentFormatVersion,
                request = request,
                worldHash = world.ComputeStableHash()
            };
        }
    }
}
