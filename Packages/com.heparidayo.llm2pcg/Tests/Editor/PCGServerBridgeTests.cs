using Llm2Pcg.Bridge;
using NUnit.Framework;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class PCGServerBridgeTests
    {
        private static readonly string[] AllowedOrigins =
        {
            "http://127.0.0.1:3000",
            "http://localhost:3000"
        };

        [TestCase(null)]
        [TestCase("")]
        [TestCase("http://127.0.0.1:3000")]
        [TestCase("http://localhost:3000/")]
        public void LocalOrServerOrigin_IsAllowed(string origin)
        {
            Assert.That(PCGServerBridge.IsOriginAllowed(origin, AllowedOrigins), Is.True);
        }

        [TestCase("http://example.com")]
        [TestCase("http://localhost:8080")]
        [TestCase("https://localhost:3000")]
        public void UnlistedBrowserOrigin_IsRejected(string origin)
        {
            Assert.That(PCGServerBridge.IsOriginAllowed(origin, AllowedOrigins), Is.False);
        }
    }
}
