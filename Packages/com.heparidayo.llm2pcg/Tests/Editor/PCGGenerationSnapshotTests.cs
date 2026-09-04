using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.Dungeon;
using NUnit.Framework;
using UnityEngine;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class PCGGenerationSnapshotTests
    {
        [Test]
        public void Snapshot_RoundTripsRequestAndVerifiesRegeneratedHash()
        {
            PCGRequest request = PCGRequest.CreateDefault(seed: 6721);
            request.generationProfile = PCGRequest.CompactDungeonProfile;
            request.specialRooms.treasure.enabled = true;
            request.specialRooms.treasure.count = 1;
            DungeonWorldData original = new DungeonBSPGenerator().Generate(request);

            PCGGenerationSnapshot loaded = JsonUtility.FromJson<PCGGenerationSnapshot>(JsonUtility.ToJson(PCGGenerationSnapshot.Create(request, original)));
            DungeonWorldData regenerated = new DungeonBSPGenerator().Generate(loaded.request);

            Assert.That(loaded.formatVersion, Is.EqualTo(PCGGenerationSnapshot.CurrentFormatVersion));
            Assert.That(regenerated.ComputeStableHash(), Is.EqualTo(loaded.worldHash));
        }

        [TestCase("Dungeon")]
        [TestCase("Cave")]
        [TestCase("Forest")]
        [TestCase("City")]
        [TestCase("Swamp")]
        [TestCase("Snowfield")]
        [TestCase("Desert")]
        public void Snapshot_RoundTripsEveryWorldType(string worldType)
        {
            PCGRequest request = worldType == "Cave" ? PCGRequest.CreateCaveDefault(7102) : worldType == "Forest" ? PCGRequest.CreateForestDefault(7103) : worldType == "City" ? PCGRequest.CreateCityDefault(7104) : worldType == "Swamp" ? PCGRequest.CreateSwampDefault(7105) : worldType == "Snowfield" ? PCGRequest.CreateSnowfieldDefault(7106) : worldType == "Desert" ? PCGRequest.CreateDesertDefault(7107) : PCGRequest.CreateDefault(7101);
            IPCGWorldData original = PCGGeneratorRegistry.Default.GetRequired(request).Generate(request);
            PCGGenerationSnapshot loaded = JsonUtility.FromJson<PCGGenerationSnapshot>(JsonUtility.ToJson(PCGGenerationSnapshot.Create(request, original)));
            PCGValidationResult validation = PCGRequestValidator.Validate(loaded.request);
            Assert.That(validation.IsValid, Is.True, validation.Message);
            IPCGWorldData regenerated = PCGGeneratorRegistry.Default.GetRequired(loaded.request).Generate(loaded.request);
            Assert.That(regenerated.ComputeStableHash(), Is.EqualTo(loaded.worldHash));
        }

        [Test]
        public void Snapshot_RoundTripsNatureVisualRestrictions()
        {
            PCGRequest request = PCGRequest.CreateForestDefault(7110, 128, 128);
            request.visualSettings.trees.density = .65f;
            request.visualSettings.trees.maxCount = 120;
            request.visualSettings.trees.allowedTypes = new[] { "cherry_blossom" };
            IPCGWorldData original = PCGGeneratorRegistry.Default.GetRequired(request).Generate(request);

            PCGGenerationSnapshot loaded = JsonUtility.FromJson<PCGGenerationSnapshot>(JsonUtility.ToJson(PCGGenerationSnapshot.Create(request, original)));

            Assert.That(loaded.request.visualSettings.trees.density, Is.EqualTo(.65f).Within(.0001f));
            Assert.That(loaded.request.visualSettings.trees.maxCount, Is.EqualTo(120));
            Assert.That(loaded.request.visualSettings.trees.allowedTypes, Is.EqualTo(new[] { "cherry_blossom" }));
            Assert.That(PCGRequestValidator.Validate(loaded.request).IsValid, Is.True);
        }

        [Test]
        public void Snapshot_RoundTripsPresentationModeWithoutChangingWorldHash()
        {
            PCGRequest request = PCGRequest.CreateCaveDefault(7111, 64, 64);
            request.presentationSettings = new PresentationSettings { geometryMode = PresentationGeometryModes.Voxel, propsEnabled = false };
            IPCGWorldData original = PCGGeneratorRegistry.Default.GetRequired(request).Generate(request);

            PCGGenerationSnapshot loaded = JsonUtility.FromJson<PCGGenerationSnapshot>(JsonUtility.ToJson(PCGGenerationSnapshot.Create(request, original)));
            PCGValidationResult validation = PCGRequestValidator.Validate(loaded.request);
            IPCGWorldData regenerated = PCGGeneratorRegistry.Default.GetRequired(loaded.request).Generate(loaded.request);

            Assert.That(validation.IsValid, Is.True, validation.Message);
            Assert.That(loaded.request.presentationSettings.geometryMode, Is.EqualTo(PresentationGeometryModes.Voxel));
            Assert.That(loaded.request.presentationSettings.propsEnabled, Is.False);
            Assert.That(regenerated.ComputeStableHash(), Is.EqualTo(original.ComputeStableHash()));
        }
    }
}
