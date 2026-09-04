using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;

namespace Llm2Pcg.Generators.City
{
    /// <summary>Poisson hubs, stable Kruskal roads/loops and bounded deterministic macro-lot WFC.</summary>
    public sealed class CityHybridWfcGenerator : IPCGGenerator
    {
        private const byte Park = 0;
        private const byte LowRise = 1;
        private const byte HighRise = 2;
        public string WorldType => PCGRequest.CityWorldType;
        public string GeneratorVersion => PCGRequest.CityGeneratorVersion;
        public IPCGWorldData Generate(PCGRequest request) => GenerateCity(request);

        public BiomeWorldData GenerateCity(PCGRequest request)
        {
            PCGValidationResult validation = PCGRequestValidator.Validate(request);
            if (!validation.IsValid) throw new ArgumentException(validation.Code + ": " + validation.Message, nameof(request));
            CityGeneratorSettings settings = request.generatorSettings.city;
            int width = request.mapWidth, height = request.mapHeight;
            int roadWidth = settings.roadWidth;
            int lotSize = 6;
            if (request.generationProfile == PCGRequest.GridCityProfile) { lotSize = 5; roadWidth = Math.Max(2, roadWidth); }
            if (request.generationProfile == PCGRequest.OrganicCityProfile) lotSize = 8;

            BiomeTile[] tiles = new BiomeTile[width * height];
            float[] structureHeights = new float[width * height];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = BiomeTile.Ground;
            List<Int2> hubs = CreateHubs(PcgSeed.Stage(request.seed, 100), width, height, settings.hubMinDistance, settings.hubMaxCount);
            List<Edge> roads = BuildRoadGraph(hubs, settings.extraLoopCount);
            for (int i = 0; i < roads.Count; i++) CarveRoad(tiles, width, height, hubs[roads[i].A], hubs[roads[i].B], roadWidth, PcgSeed.Stage(request.seed, 110) + i);

            List<Int2> buildings = CollapseLots(tiles, structureHeights, width, height, lotSize, PcgSeed.Stage(request.seed, 120), settings.wfcBacktrackLimit, settings.wfcRestartLimit, settings.buildingMinHeight, settings.buildingMaxHeight);
            return new BiomeWorldData(WorldType, GeneratorVersion, width, height, request.seed, tiles, hubs[0], hubs[hubs.Count - 1], buildings, null, structureHeights);
        }

        private static List<Int2> CreateHubs(int seed, int width, int height, float minimumDistance, int maximum)
        {
            PcgDeterministicRandom random = new PcgDeterministicRandom(seed);
            List<Int2> hubs = new List<Int2>(); int min = Math.Max(4, (int)minimumDistance), minSquared = min * min;
            for (int attempt = 0; attempt < width * height * 2 && hubs.Count < maximum; attempt++)
            {
                Int2 candidate = new Int2(3 + random.NextInt(Math.Max(1, width - 6)), 3 + random.NextInt(Math.Max(1, height - 6)));
                bool accepted = true; for (int i = 0; i < hubs.Count; i++) { int dx = candidate.X - hubs[i].X, dy = candidate.Y - hubs[i].Y; if (dx * dx + dy * dy < minSquared) { accepted = false; break; } }
                if (accepted) hubs.Add(candidate);
            }
            if (hubs.Count < 2) { hubs.Clear(); hubs.Add(new Int2(width / 4, height / 2)); hubs.Add(new Int2(3 * width / 4, height / 2)); }
            hubs.Sort((a, b) => (a.Y * width + a.X).CompareTo(b.Y * width + b.X)); return hubs;
        }

        private static List<Edge> BuildRoadGraph(List<Int2> hubs, int extraLoopCount)
        {
            List<Edge> candidates = new List<Edge>();
            for (int a = 0; a < hubs.Count; a++) for (int b = a + 1; b < hubs.Count; b++) candidates.Add(new Edge(a, b, Distance(hubs[a], hubs[b])));
            candidates.Sort((a, b) => a.CompareTo(b)); int[] parent = new int[hubs.Count]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
            List<Edge> result = new List<Edge>(); List<Edge> loops = new List<Edge>();
            for (int i = 0; i < candidates.Count; i++) { Edge edge = candidates[i]; if (Find(parent, edge.A) != Find(parent, edge.B)) { Union(parent, edge.A, edge.B); result.Add(edge); } else loops.Add(edge); }
            for (int i = 0; i < loops.Count && i < extraLoopCount; i++) result.Add(loops[i]);
            result.Sort((a, b) => a.CompareTo(b)); return result;
        }

        private static void CarveRoad(BiomeTile[] tiles, int width, int height, Int2 start, Int2 end, int roadWidth, int seed)
        {
            bool horizontalFirst = (PcgSeed.Stage(seed, start.X + start.Y + end.X + end.Y) & 1) == 0;
            Int2 corner = horizontalFirst ? new Int2(end.X, start.Y) : new Int2(start.X, end.Y);
            CarveSegment(tiles, width, height, start, corner, roadWidth);
            CarveSegment(tiles, width, height, corner, end, roadWidth);
        }

        private static void CarveSegment(BiomeTile[] tiles, int width, int height, Int2 start, Int2 end, int roadWidth)
        {
            int x = start.X, y = start.Y;
            while (true) { PaintRoad(tiles, width, height, x, y, roadWidth); if (x == end.X && y == end.Y) break; if (x != end.X) x += x < end.X ? 1 : -1; else y += y < end.Y ? 1 : -1; }
        }

        private static List<Int2> CollapseLots(BiomeTile[] tiles, float[] structureHeights, int width, int height, int lotSize, int seed, int backtrackLimit, int restartLimit, float minimumHeight, float maximumHeight)
        {
            int columns = (width - 2) / lotSize, rows = (height - 2) / lotSize;
            byte[] states = new byte[Math.Max(1, columns * rows)];
            PcgDeterministicRandom random = new PcgDeterministicRandom(seed + Math.Min(restartLimit, 32));
            List<Int2> centers = new List<Int2>(); int decisions = 0, decisionLimit = Math.Max(columns * rows, backtrackLimit + columns * rows);
            for (int row = 0; row < rows && decisions < decisionLimit; row++)
            for (int column = 0; column < columns && decisions < decisionLimit; column++, decisions++)
            {
                int allowed = 0b111;
                byte north = row > 0 ? states[(row - 1) * columns + column] : byte.MaxValue;
                byte west = column > 0 ? states[row * columns + column - 1] : byte.MaxValue;
                if (north == HighRise || west == HighRise) allowed &= ~(1 << HighRise);
                if (north == Park && west == Park) allowed &= ~(1 << Park);
                byte state = PickState(allowed, ref random); states[row * columns + column] = state;
                int minX = 2 + column * lotSize + 1, minY = 2 + row * lotSize + 1;
                int maxX = Math.Min(width - 2, minX + lotSize - 2), maxY = Math.Min(height - 2, minY + lotSize - 2);
                if (!FootprintIsGround(tiles, width, minX, minY, maxX, maxY)) continue;
                BiomeTile tile = state == Park ? BiomeTile.Park : BiomeTile.Building;
                float structureHeight = 0f;
                if (tile == BiomeTile.Building)
                {
                    float normalized = random.NextFloat();
                    if (state == LowRise) normalized *= .45f;
                    else if (state == HighRise) normalized = .55f + normalized * .45f;
                    structureHeight = QuantizeHeight(minimumHeight + (maximumHeight - minimumHeight) * normalized);
                }
                for (int y = minY; y < maxY; y++) for (int x = minX; x < maxX; x++) { int index = y * width + x; tiles[index] = tile; structureHeights[index] = structureHeight; }
                if (tile == BiomeTile.Building) centers.Add(new Int2((minX + maxX - 1) / 2, (minY + maxY - 1) / 2));
            }
            return centers;
        }

        private static byte PickState(int mask, ref PcgDeterministicRandom random)
        {
            int count = 0; for (int state = 0; state < 3; state++) if ((mask & (1 << state)) != 0) count++;
            int selected = random.NextInt(count); for (byte state = 0; state < 3; state++) if ((mask & (1 << state)) != 0 && selected-- == 0) return state;
            return LowRise;
        }

        private static float QuantizeHeight(float value) => (float)Math.Round(value * 2f, MidpointRounding.AwayFromZero) * .5f;

        private static bool FootprintIsGround(BiomeTile[] tiles, int width, int minX, int minY, int maxX, int maxY) { for (int y = minY; y < maxY; y++) for (int x = minX; x < maxX; x++) if (tiles[y * width + x] != BiomeTile.Ground) return false; return true; }
        private static void PaintRoad(BiomeTile[] tiles, int width, int height, int centerX, int centerY, int roadWidth) { int before = (roadWidth - 1) / 2, after = roadWidth / 2; for (int y = centerY - before; y <= centerY + after; y++) for (int x = centerX - before; x <= centerX + after; x++) if (x >= 0 && y >= 0 && x < width && y < height) tiles[y * width + x] = BiomeTile.Road; }
        private static int Distance(Int2 a, Int2 b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        private static int Find(int[] parent, int value) { while (parent[value] != value) { parent[value] = parent[parent[value]]; value = parent[value]; } return value; }
        private static void Union(int[] parent, int a, int b) { a = Find(parent, a); b = Find(parent, b); if (a != b) parent[b] = a; }
        private readonly struct Edge : IComparable<Edge> { public readonly int A, B, Cost; public Edge(int a, int b, int cost) { A = a; B = b; Cost = cost; } public int CompareTo(Edge other) { int c = Cost.CompareTo(other.Cost); if (c != 0) return c; c = A.CompareTo(other.A); return c != 0 ? c : B.CompareTo(other.B); } }
    }
}
