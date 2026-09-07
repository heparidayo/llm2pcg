using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using Llm2Pcg.Contract;

namespace Llm2Pcg.Core
{
    /// <summary>
    /// Renderer-neutral result contract shared by the standalone host and browser renderers.
    /// Grid payloads are compact base64 buffers so a 500x500 world remains practical over HTTP.
    /// </summary>
    public sealed class GeneratedWorldDocument
    {
        public int FormatVersion { get; init; } = 1;
        public string WorldType { get; init; }
        public string GeneratorVersion { get; init; }
        public int Seed { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public string WorldHash { get; init; }
        public string GridEncoding { get; init; } = "base64-u8";
        public string FloatEncoding { get; init; } = "base64-f32le";
        public string[] CellLegend { get; init; } = Array.Empty<string>();
        public string Cells { get; init; }
        public string Elevations { get; init; }
        public string StructureHeights { get; init; }
        public WorldPoint Start { get; init; }
        public WorldPoint Exit { get; init; }
        public List<WorldRoom> Rooms { get; init; } = new List<WorldRoom>();
        public List<WorldConnection> Connections { get; init; } = new List<WorldConnection>();
        public List<WorldProp> Props { get; init; } = new List<WorldProp>();
        public Dictionary<string, int> Statistics { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<PCGGenerationDiagnostic> Diagnostics { get; init; } = new List<PCGGenerationDiagnostic>();
    }

    public sealed class WorldPoint
    {
        public int X { get; init; }
        public int Y { get; init; }
        public WorldPoint() { }
        public WorldPoint(int x, int y) { X = x; Y = y; }
    }

    public sealed class WorldRoom
    {
        public int Index { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public string[] Roles { get; init; } = Array.Empty<string>();
    }

    public sealed class WorldConnection
    {
        public int A { get; init; }
        public int B { get; init; }
        public int Cost { get; init; }
    }

    public sealed class WorldProp
    {
        public string Kind { get; init; }
        public string AssetId { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int SemanticIndex { get; init; }
        public int WallNormalX { get; init; }
        public int WallNormalY { get; init; }
    }

    public static class GeneratedWorldDocumentFactory
    {
        private static readonly string[] DungeonLegend = { "Empty", "Floor", "Corridor" };
        private static readonly string[] CaveLegend = { "Open", "Solid" };
        private static readonly string[] BiomeLegend = { "Ground", "Water", "Path", "Road", "Building", "Park" };

        public static GeneratedWorldDocument Create(IPCGWorldData world, PCGRequest request = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            GeneratedWorldDocument document;
            if (world is DungeonWorldData dungeon) document = CreateDungeon(dungeon);
            else if (world is CaveWorldData cave) document = CreateCave(cave);
            else if (world is BiomeWorldData biome) document = CreateBiome(biome);
            else throw new ArgumentException("Unsupported generated world data type: " + world.GetType().FullName, nameof(world));
            if (request != null) document.Diagnostics.AddRange(PCGGenerationDiagnostics.Inspect(request, world));
            return document;
        }

        private static GeneratedWorldDocument CreateDungeon(DungeonWorldData world)
        {
            List<WorldRoom> rooms = new List<WorldRoom>(world.Rooms.Count);
            for (int index = 0; index < world.Rooms.Count; index++)
            {
                IntRect room = world.Rooms[index];
                rooms.Add(new WorldRoom
                {
                    Index = index,
                    X = room.X,
                    Y = room.Y,
                    Width = room.Width,
                    Height = room.Height,
                    Roles = RolesFor(world, index)
                });
            }

            List<WorldConnection> connections = new List<WorldConnection>(world.Connections.Count);
            for (int index = 0; index < world.Connections.Count; index++)
            {
                DungeonConnection connection = world.Connections[index];
                connections.Add(new WorldConnection { A = connection.RoomA, B = connection.RoomB, Cost = connection.Cost });
            }

            List<WorldProp> props = new List<WorldProp>(world.Props.Count);
            for (int index = 0; index < world.Props.Count; index++)
            {
                DungeonPropPlacement prop = world.Props[index];
                props.Add(new WorldProp
                {
                    Kind = prop.Type.ToString(),
                    AssetId = "Dungeon/" + prop.Type,
                    X = prop.Position.X,
                    Y = prop.Position.Y,
                    SemanticIndex = index,
                    WallNormalX = prop.WallNormal.X,
                    WallNormalY = prop.WallNormal.Y
                });
            }

            return new GeneratedWorldDocument
            {
                WorldType = world.WorldType,
                GeneratorVersion = world.GeneratorVersion,
                Seed = world.Seed,
                Width = world.Width,
                Height = world.Height,
                WorldHash = world.ComputeStableHash(),
                CellLegend = DungeonLegend,
                Cells = Convert.ToBase64String(world.Cells),
                Start = Point(world.StartPosition),
                Exit = Point(world.ExitPosition),
                Rooms = rooms,
                Connections = connections,
                Props = props,
                Statistics = CountCells(world.Cells, DungeonLegend, props.Count)
            };
        }

        private static GeneratedWorldDocument CreateCave(CaveWorldData world)
        {
            byte[] cells = new byte[world.SolidCells.Length];
            int solid = 0;
            for (int index = 0; index < cells.Length; index++)
            {
                if (!world.SolidCells[index]) continue;
                cells[index] = 1;
                solid++;
            }

            return new GeneratedWorldDocument
            {
                WorldType = world.WorldType,
                GeneratorVersion = world.GeneratorVersion,
                Seed = world.Seed,
                Width = world.Width,
                Height = world.Height,
                WorldHash = world.ComputeStableHash(),
                CellLegend = CaveLegend,
                Cells = Convert.ToBase64String(cells),
                Start = Point(world.StartPosition),
                Exit = Point(world.ExitPosition),
                Statistics = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["Open"] = cells.Length - solid,
                    ["Solid"] = solid,
                    ["Props"] = 0
                }
            };
        }

        private static GeneratedWorldDocument CreateBiome(BiomeWorldData world)
        {
            byte[] cells = new byte[world.Tiles.Length];
            for (int index = 0; index < cells.Length; index++) cells[index] = (byte)world.Tiles[index];

            List<WorldProp> props = new List<WorldProp>(world.Props.Count);
            string kind = string.Equals(world.WorldType, PCGRequest.CityWorldType, StringComparison.Ordinal) ? "CityPropAnchor" : "Vegetation";
            for (int index = 0; index < world.Props.Count; index++)
            {
                Int2 point = world.Props[index];
                props.Add(new WorldProp
                {
                    Kind = kind,
                    AssetId = world.WorldType + "/" + kind,
                    X = point.X,
                    Y = point.Y,
                    SemanticIndex = index
                });
            }

            return new GeneratedWorldDocument
            {
                WorldType = world.WorldType,
                GeneratorVersion = world.GeneratorVersion,
                Seed = world.Seed,
                Width = world.Width,
                Height = world.Height,
                WorldHash = world.ComputeStableHash(),
                CellLegend = BiomeLegend,
                Cells = Convert.ToBase64String(cells),
                Elevations = EncodeFloats(world.Elevations),
                StructureHeights = EncodeFloats(world.StructureHeights),
                Start = Point(world.StartPosition),
                Exit = Point(world.ExitPosition),
                Props = props,
                Statistics = CountCells(cells, BiomeLegend, props.Count)
            };
        }

        private static WorldPoint Point(Int2 point) => new WorldPoint(point.X, point.Y);

        private static string[] RolesFor(DungeonWorldData world, int roomIndex)
        {
            List<string> roles = new List<string>();
            if (roomIndex == world.StartRoomIndex) roles.Add("Start");
            if (roomIndex == world.ExitRoomIndex) roles.Add("Exit");
            AddRole(roles, world.BossRoomIndices, roomIndex, "Boss");
            AddRole(roles, world.TreasureRoomIndices, roomIndex, "Treasure");
            AddRole(roles, world.ShopRoomIndices, roomIndex, "Shop");
            AddRole(roles, world.SecretRoomIndices, roomIndex, "Secret");
            return roles.ToArray();
        }

        private static void AddRole(List<string> roles, IReadOnlyList<int> indices, int roomIndex, string role)
        {
            for (int index = 0; index < indices.Count; index++)
                if (indices[index] == roomIndex) { roles.Add(role); return; }
        }

        private static Dictionary<string, int> CountCells(byte[] cells, string[] legend, int propCount)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < legend.Length; index++) result[legend[index]] = 0;
            for (int index = 0; index < cells.Length; index++)
            {
                int value = cells[index];
                if (value >= 0 && value < legend.Length) result[legend[value]]++;
            }
            result["Props"] = propCount;
            return result;
        }

        private static string EncodeFloats(float[] values)
        {
            if (values == null || values.Length == 0) return null;
            byte[] bytes = new byte[values.Length * sizeof(float)];
            for (int index = 0; index < values.Length; index++)
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(float), sizeof(float)), BitConverter.SingleToInt32Bits(values[index]));
            return Convert.ToBase64String(bytes);
        }
    }
}
