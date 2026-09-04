using Llm2Pcg.Contract;
using Llm2Pcg.Core;

namespace Llm2Pcg.Generators.Nature
{
    public sealed class DesertBiomeGenerator : IPCGGenerator
    {
        private readonly NatureBiomeGeneratorCore core = new NatureBiomeGeneratorCore();
        public string WorldType => PCGRequest.DesertWorldType;
        public string GeneratorVersion => PCGRequest.DesertGeneratorVersion;
        public IPCGWorldData Generate(PCGRequest request) => core.Generate(request, NatureWorldTypes.Desert);
    }
}
