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

    [Serializable]
    public sealed class PCGGenerationDiagnostic
    {
        public string code;
        public string message;
        public PCGGenerationDiagnostic(string code, string message) { this.code = code; this.message = message; }
    }

    /// <summary>Non-mutating outcome feedback. Diagnostics never alter seed, request, or world hash.</summary>
    public static class PCGGenerationDiagnostics
    {
        public static List<PCGGenerationDiagnostic> Inspect(PCGRequest request, IPCGWorldData world)
        {
            if (request == null || world == null) throw new ArgumentNullException(request == null ? nameof(request) : nameof(world));
            var result = new List<PCGGenerationDiagnostic>();
            if (!(world is DungeonWorldData dungeon)) return result;
            PropSettings props = request.propSettings;
            if (props.enabled && props.density > 0f && props.maxCount == 0)
                result.Add(new PCGGenerationDiagnostic("DUNGEON_PROP_ZERO_CAP", "Dungeon propSettings.maxCount=0 disables placement, unlike visualSettings' unlimited cap. Set a positive cap to generate dungeon props."));
            else if (props.enabled && props.density > 0f && props.maxCount > 0 && dungeon.Props.Count == 0)
                result.Add(new PCGGenerationDiagnostic("NO_ELIGIBLE_DUNGEON_PROPS", "No dungeon props were placed under the requested type, density, and surface constraints."));
            CheckRooms(result, "boss", request.specialRooms.boss.enabled, request.specialRooms.boss.count, dungeon.BossRoomIndices.Count);
            CheckRooms(result, "treasure", request.specialRooms.treasure.enabled, request.specialRooms.treasure.count, dungeon.TreasureRoomIndices.Count);
            CheckRooms(result, "shop", request.specialRooms.shop.enabled, request.specialRooms.shop.count, dungeon.ShopRoomIndices.Count);
            CheckRooms(result, "secret", request.specialRooms.secret.enabled, request.specialRooms.secret.count, dungeon.SecretRoomIndices.Count);
            return result;
        }

        private static void CheckRooms(List<PCGGenerationDiagnostic> result, string role, bool enabled, int requested, int actual)
        {
            if (enabled && actual < requested)
                result.Add(new PCGGenerationDiagnostic("SPECIAL_ROOM_COUNT_CAPPED", role + " rooms: requested " + requested + ", generated " + actual + ". Not enough eligible rooms."));
        }
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
