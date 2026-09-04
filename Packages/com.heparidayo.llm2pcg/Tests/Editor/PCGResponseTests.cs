using Llm2Pcg.Contract;
using NUnit.Framework;
using UnityEngine;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class PCGResponseTests
    {
        [Test]
        public void Request_RoundTripsThroughUnityJson()
        {
            PCGRequest source = PCGRequest.CreateDefault(seed: 42, mapWidth: 120, mapHeight: 80);
            PCGRequest roundTripped = JsonUtility.FromJson<PCGRequest>(JsonUtility.ToJson(source));

            Assert.That(PCGRequestValidator.Validate(roundTripped).IsValid, Is.True);
            Assert.That(roundTripped.seed, Is.EqualTo(42));
            Assert.That(roundTripped.generatorSettings.dungeon.maxDepth, Is.EqualTo(5));
        }
    }
}
