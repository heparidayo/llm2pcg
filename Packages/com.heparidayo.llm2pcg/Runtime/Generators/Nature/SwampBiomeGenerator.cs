using Llm2Pcg.Contract;
using Llm2Pcg.Core;

namespace Llm2Pcg.Generators.Nature
{
    public sealed class SwampBiomeGenerator : IPCGGenerator
    {
        private readonly NatureBiomeGeneratorCore core = new NatureBiomeGeneratorCore();
        public string WorldType => PCGRequest.SwampWorldType;
        public string GeneratorVersion => PCGRequest.SwampGeneratorVersion;
        public IPCGWorldData Generate(PCGRequest request) => core.Generate(request, NatureWorldTypes.Swamp);
    }
}
