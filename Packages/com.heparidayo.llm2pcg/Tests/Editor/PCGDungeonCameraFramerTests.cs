using Llm2Pcg.Presentation;
using NUnit.Framework;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class PCGDungeonCameraFramerTests
    {
        [Test]
        public void PerspectiveHeight_GrowsWithDungeonSize()
        {
            float small = PCGDungeonCameraFramer.CalculatePerspectiveHeight(100, 100, 60f, 16f / 9f, 1.15f, 20f);
            float large = PCGDungeonCameraFramer.CalculatePerspectiveHeight(200, 200, 60f, 16f / 9f, 1.15f, 20f);

            Assert.That(small, Is.GreaterThanOrEqualTo(20f));
            Assert.That(large, Is.GreaterThan(small));
        }

        [Test]
        public void OrthographicSize_AccountsForCameraAspectRatio()
        {
            float wideCamera = PCGDungeonCameraFramer.CalculateOrthographicSize(200, 100, 2f, 1f);
            float narrowCamera = PCGDungeonCameraFramer.CalculateOrthographicSize(200, 100, 1f, 1f);

            Assert.That(wideCamera, Is.EqualTo(50f));
            Assert.That(narrowCamera, Is.EqualTo(100f));
        }
    }
}
