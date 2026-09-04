using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Generators.Dungeon;
using Llm2Pcg.Generators.Cave;
using Llm2Pcg.Generators.Forest;
using Llm2Pcg.Generators.City;
using Llm2Pcg.Generators.Nature;

namespace Llm2Pcg.Core
{
    public interface IPCGWorldData
    {
        string WorldType { get; }
        string GeneratorVersion { get; }
        int Width { get; }
        int Height { get; }
        int Seed { get; }
        string ComputeStableHash();
    }

    public interface IPCGNavigableWorldData : IPCGWorldData
    {
        Int2 StartPosition { get; }
        Int2 ExitPosition { get; }
        bool IsWalkable(int x, int y);
        float GetSurfaceHeight(int x, int y);
    }

    public interface IPCGGenerator
    {
        string WorldType { get; }
        string GeneratorVersion { get; }
        IPCGWorldData Generate(PCGRequest request);
    }

    public interface IPCGWorldRenderer
    {
        string WorldType { get; }
        void Render(IPCGWorldData world);
        void Clear();
    }

    /// <summary>Explicitly ordered registry. Registration is intentionally not reflection based.</summary>
    public sealed class PCGGeneratorRegistry
    {
        private readonly Dictionary<string, IPCGGenerator> generators = new Dictionary<string, IPCGGenerator>(StringComparer.Ordinal);
        public static PCGGeneratorRegistry Default { get; } = CreateDefault();

        public void Register(IPCGGenerator generator)
        {
            if (generator == null) throw new ArgumentNullException(nameof(generator));
            string key = Key(generator.WorldType, generator.GeneratorVersion);
            if (generators.ContainsKey(key)) throw new InvalidOperationException("Generator already registered: " + key);
            generators.Add(key, generator);
        }

        public bool TryGet(string worldType, string generatorVersion, out IPCGGenerator generator) => generators.TryGetValue(Key(worldType, generatorVersion), out generator);
        public IPCGGenerator GetRequired(PCGRequest request)
        {
            if (!TryGet(request.worldType, request.generatorVersion, out IPCGGenerator generator))
                throw new ArgumentException("UNSUPPORTED_GENERATOR_VERSION: No generator registered for " + request.worldType + " / " + request.generatorVersion + ".", nameof(request));
            return generator;
        }

        private static PCGGeneratorRegistry CreateDefault()
        {
            PCGGeneratorRegistry registry = new PCGGeneratorRegistry();
            registry.Register(new DungeonBSPGenerator());
            registry.Register(new CaveCellularGenerator());
            registry.Register(new ForestBiomeGenerator());
            registry.Register(new CityHybridWfcGenerator());
            registry.Register(new SwampBiomeGenerator());
            registry.Register(new SnowfieldBiomeGenerator());
            registry.Register(new DesertBiomeGenerator());
            return registry;
        }
        private static string Key(string worldType, string generatorVersion) => (worldType ?? string.Empty) + "|" + (generatorVersion ?? string.Empty);
    }
}
