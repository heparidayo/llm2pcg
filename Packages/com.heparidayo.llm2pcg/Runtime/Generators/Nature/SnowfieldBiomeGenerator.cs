using Llm2Pcg.Contract;
using Llm2Pcg.Core;

namespace Llm2Pcg.Generators.Nature
{
    public sealed class SnowfieldBiomeGenerator : IPCGGenerator
    {
        private readonly NatureBiomeGeneratorCore core = new NatureBiomeGeneratorCore();
        public string WorldType => PCGRequest.SnowfieldWorldType;
        public string GeneratorVersion => PCGRequest.SnowfieldGeneratorVersion;
        public IPCGWorldData Generate(PCGRequest request) => core.Generate(request, NatureWorldTypes.Snowfield);
    }
}
