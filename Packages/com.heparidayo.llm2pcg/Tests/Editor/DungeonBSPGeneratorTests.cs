using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Generators.Dungeon;
using Llm2Pcg.Rendering;
using NUnit.Framework;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class DungeonBSPGeneratorTests
    {
        [Test]
        public void SameRequest_ProducesIdenticalWorldData()
        {
            DungeonBSPGenerator generator = new DungeonBSPGenerator();
            PCGRequest request = PCGRequest.CreateDefault(seed: 12345);

            DungeonWorldData first = generator.Generate(request);
            DungeonWorldData second = generator.Generate(request);

            Assert.That(first.ComputeStableHash(), Is.EqualTo(second.ComputeStableHash()));
            Assert.That(first.Cells, Is.EqualTo(second.Cells));
            Assert.That(first.Rooms.Count, Is.EqualTo(second.Rooms.Count));
        }

        [Test]
        public void DifferentSeed_ProducesDifferentWorldData()
        {
            DungeonBSPGenerator generator = new DungeonBSPGenerator();
            DungeonWorldData first = generator.Generate(PCGRequest.CreateDefault(seed: 12345));
            DungeonWorldData second = generator.Generate(PCGRequest.CreateDefault(seed: 12346));

            Assert.That(first.ComputeStableHash(), Is.Not.EqualTo(second.ComputeStableHash()));
        }

        [Test]
        public void GeneratedRooms_AreConnectedByMinimumSpanningTree()
        {
            DungeonWorldData dungeon = new DungeonBSPGenerator().Generate(PCGRequest.CreateDefault());

            Assert.That(dungeon.Rooms.Count, Is.GreaterThan(0));
            Assert.That(dungeon.Connections.Count, Is.EqualTo(dungeon.Rooms.Count - 1));
        }

        [Test]
        public void BossSettings_SelectDeterministicRoomsWithoutOverlappingStartOrExit()
        {
            PCGRequest request = PCGRequest.CreateDefault();
            request.specialRooms.boss.enabled = true;
            request.specialRooms.boss.count = 2;
            DungeonWorldData first = new DungeonBSPGenerator().Generate(request);
            DungeonWorldData second = new DungeonBSPGenerator().Generate(request);

            Assert.That(first.StartRoomIndex, Is.Not.EqualTo(first.ExitRoomIndex));
            Assert.That(first.BossRoomIndices, Has.Count.EqualTo(2));
            for (int i = 0; i < first.BossRoomIndices.Count; i++)
            {
                Assert.That(first.BossRoomIndices[i], Is.Not.EqualTo(first.StartRoomIndex));
                Assert.That(first.BossRoomIndices[i], Is.Not.EqualTo(first.ExitRoomIndex));
                Assert.That(first.BossRoomIndices[i], Is.EqualTo(second.BossRoomIndices[i]));
            }
        }

        [Test]
        public void GoldenRequest_ProducesExpectedStableHash()
        {
            PCGRequest request = PCGRequest.CreateDefault(seed: 42042);
            request.specialRooms.boss.enabled = true;
            request.specialRooms.boss.count = 2;

            DungeonWorldData dungeon = new DungeonBSPGenerator().Generate(request);

            Assert.That(dungeon.ComputeStableHash(), Is.EqualTo("DE7589AF"));
        }

        [Test]
        public void Props_AreDeterministicCappedAndAvoidMarkerCentres()
        {
            PCGRequest request = PCGRequest.CreateDefault(seed: 7777);
            request.specialRooms.boss.enabled = true;
            request.specialRooms.boss.count = 2;
            request.propSettings.enabled = true;
            request.propSettings.density = 1f;
            request.propSettings.maxCount = 7;
            request.propSettings.allowedTypes = new[] { "Crate" };

            DungeonWorldData first = new DungeonBSPGenerator().Generate(request);
            DungeonWorldData second = new DungeonBSPGenerator().Generate(request);

            Assert.That(first.Props, Has.Count.EqualTo(7));
            for (int index = 0; index < first.Props.Count; index++)
            {
                DungeonPropPlacement prop = first.Props[index];
                Assert.That(prop.Type, Is.EqualTo(DungeonPropType.Crate));
                Assert.That(IsNear(prop.Position, first.StartPosition), Is.False);
                Assert.That(IsNear(prop.Position, first.ExitPosition), Is.False);
                for (int bossIndex = 0; bossIndex < first.BossRoomIndices.Count; bossIndex++)
                    Assert.That(IsNear(prop.Position, first.Rooms[first.BossRoomIndices[bossIndex]].Center), Is.False);
                Assert.That(prop.Position.X, Is.EqualTo(second.Props[index].Position.X));
                Assert.That(prop.Position.Y, Is.EqualTo(second.Props[index].Position.Y));
                Assert.That(prop.Type, Is.EqualTo(second.Props[index].Type));
            }
        }

        [Test]
        public void BoundaryWalkableCells_ReceiveExteriorWallSegments()
        {
            DungeonWorldData dungeon = new DungeonWorldData(
                2, 2, 1,
                new byte[] { (byte)DungeonCell.Floor, (byte)DungeonCell.Empty, (byte)DungeonCell.Empty, (byte)DungeonCell.Floor },
                new System.Collections.Generic.List<IntRect> { new IntRect(0, 0, 2, 2) },
                new System.Collections.Generic.List<DungeonConnection>(),
                0, 0,
                new System.Collections.Generic.List<int>(),
                new System.Collections.Generic.List<DungeonPropPlacement>());

            Assert.That(DungeonInstancedRenderer.CountExteriorBoundaryWalls(dungeon), Is.EqualTo(4));
        }

        [Test]
        public void Props_RespectRoomAndCorridorPlacementRules()
        {
            PCGRequest request = PCGRequest.CreateDefault(seed: 24680);
            request.propSettings.enabled = true;
            request.propSettings.density = 1f;
            request.propSettings.maxCount = 500;
            request.propSettings.allowedTypes = new[] { "Pillar", "Crate", "Crystal" };

            DungeonWorldData dungeon = new DungeonBSPGenerator().Generate(request);
            Assert.That(dungeon.Props, Is.Not.Empty);
            for (int index = 0; index < dungeon.Props.Count; index++)
            {
                DungeonPropPlacement prop = dungeon.Props[index];
                DungeonCell surface = dungeon.GetCell(prop.Position.X, prop.Position.Y);
                if (prop.Type == DungeonPropType.Pillar)
                    Assert.That(surface, Is.EqualTo(DungeonCell.Floor));
                if (prop.Type == DungeonPropType.Crystal)
                    Assert.That(surface, Is.EqualTo(DungeonCell.Corridor));
                if (prop.Type == DungeonPropType.Crate)
                    Assert.That(surface, Is.Not.EqualTo(DungeonCell.Empty));
            }
        }

        [Test]
        public void TorchProps_AreAttachedToWalkableCellsWithWallNormals()
        {
            PCGRequest request = PCGRequest.CreateDefault(seed: 24681);
            request.propSettings.enabled = true;
            request.propSettings.density = 1f;
            request.propSettings.maxCount = 100;
            request.propSettings.allowedTypes = new[] { "Torch" };

            DungeonWorldData dungeon = new DungeonBSPGenerator().Generate(request);

            Assert.That(dungeon.Props, Is.Not.Empty);
            for (int index = 0; index < dungeon.Props.Count; index++)
            {
                DungeonPropPlacement prop = dungeon.Props[index];
                Assert.That(prop.Type, Is.EqualTo(DungeonPropType.Torch));
                Assert.That(dungeon.IsWalkable(prop.Position.X, prop.Position.Y), Is.True);
                Assert.That(prop.WallNormal.X != 0 || prop.WallNormal.Y != 0, Is.True);
                Assert.That(dungeon.IsWalkable(prop.Position.X + prop.WallNormal.X, prop.Position.Y + prop.WallNormal.Y), Is.False);
            }
        }

        [Test]
        public void AdditionalSpecialRooms_AreDisjointAndDeterministic()
        {
            PCGRequest request = PCGRequest.CreateDefault(seed: 8899);
            request.specialRooms.boss.enabled = true;
            request.specialRooms.boss.count = 1;
            request.specialRooms.treasure.enabled = true;
            request.specialRooms.treasure.count = 1;
            request.specialRooms.shop.enabled = true;
            request.specialRooms.shop.count = 1;
            request.specialRooms.secret.enabled = true;
            request.specialRooms.secret.count = 1;
            DungeonWorldData first = new DungeonBSPGenerator().Generate(request);
            DungeonWorldData second = new DungeonBSPGenerator().Generate(request);

            System.Collections.Generic.HashSet<int> selected = new System.Collections.Generic.HashSet<int>();
            AddAll(selected, first.BossRoomIndices);
            AddAll(selected, first.TreasureRoomIndices);
            AddAll(selected, first.ShopRoomIndices);
            AddAll(selected, first.SecretRoomIndices);
            Assert.That(selected.Count, Is.EqualTo(4));
            Assert.That(first.ComputeStableHash(), Is.EqualTo(second.ComputeStableHash()));
        }

        [Test]
        public void DungeonProfiles_ProduceStableAndDistinctLayouts()
        {
            PCGRequest compact = PCGRequest.CreateDefault(seed: 5533);
            compact.generationProfile = PCGRequest.CompactDungeonProfile;
            PCGRequest sprawling = PCGRequest.CreateDefault(seed: 5533);
            sprawling.generationProfile = PCGRequest.SprawlingDungeonProfile;
            DungeonBSPGenerator generator = new DungeonBSPGenerator();
            DungeonWorldData compactFirst = generator.Generate(compact);
            DungeonWorldData compactSecond = generator.Generate(compact);
            DungeonWorldData sprawlingWorld = generator.Generate(sprawling);

            Assert.That(compactFirst.ComputeStableHash(), Is.EqualTo(compactSecond.ComputeStableHash()));
            Assert.That(compactFirst.ComputeStableHash(), Is.Not.EqualTo(sprawlingWorld.ComputeStableHash()));
        }

        private static void AddAll(System.Collections.Generic.HashSet<int> destination, System.Collections.Generic.IReadOnlyList<int> values)
        {
            for (int index = 0; index < values.Count; index++)
                destination.Add(values[index]);
        }

        private static bool IsNear(Int2 first, Int2 second)
        {
            return System.Math.Abs(first.X - second.X) <= 1 && System.Math.Abs(first.Y - second.Y) <= 1;
        }
    }
}
