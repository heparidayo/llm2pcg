using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;

namespace Llm2Pcg.Generators.Dungeon
{
    /// <summary>Deterministic BSP dungeon generation: partitions, rooms, Kruskal MST, then A* corridors.</summary>
    public sealed class DungeonBSPGenerator : IPCGGenerator
    {
        public string WorldType => PCGRequest.DungeonWorldType;
        public string GeneratorVersion => PCGRequest.DungeonGeneratorVersion;
        IPCGWorldData IPCGGenerator.Generate(PCGRequest request) => Generate(request);

        public DungeonWorldData Generate(PCGRequest request)
        {
            PCGValidationResult validation = PCGRequestValidator.Validate(request);
            if (!validation.IsValid)
                throw new ArgumentException(validation.Code + ": " + validation.Message, nameof(request));

            DeterministicRandom random = new DeterministicRandom(request.seed);
            DungeonGeneratorSettings settings = ResolveSettings(request);
            List<IntRect> leaves = new List<IntRect>();
            Split(new IntRect(0, 0, request.mapWidth, request.mapHeight), 0, settings.maxDepth, settings.minLeafSize, random, leaves);

            List<IntRect> rooms = new List<IntRect>(leaves.Count);
            for (int index = 0; index < leaves.Count; index++)
                rooms.Add(CreateRoom(leaves[index], random));

            byte[] cells = new byte[request.mapWidth * request.mapHeight];
            for (int index = 0; index < rooms.Count; index++)
                CarveRoom(cells, request.mapWidth, rooms[index]);

            List<DungeonConnection> connections = BuildMinimumSpanningTree(rooms);
            for (int index = 0; index < connections.Count; index++)
            {
                Int2 start = rooms[connections[index].RoomA].Center;
                Int2 end = rooms[connections[index].RoomB].Center;
                List<Int2> path = GridAStar.FindPath(request.mapWidth, request.mapHeight, start, end);
                for (int pointIndex = 0; pointIndex < path.Count; pointIndex++)
                    CarveCorridor(cells, request.mapWidth, path[pointIndex]);
            }

            int startRoomIndex = 0;
            int[] distancesFromStart = CalculateConnectionDistances(rooms.Count, connections, startRoomIndex);
            int exitRoomIndex = FindFarthestRoom(distancesFromStart, startRoomIndex);
            List<int> bossRoomIndices = SelectBossRooms(request.specialRooms.boss, distancesFromStart, startRoomIndex, exitRoomIndex);
            List<int> treasureRoomIndices = SelectAdditionalRooms(request.specialRooms.treasure, distancesFromStart, startRoomIndex, exitRoomIndex, bossRoomIndices);
            List<int> shopRoomIndices = SelectAdditionalRooms(request.specialRooms.shop, distancesFromStart, startRoomIndex, exitRoomIndex, bossRoomIndices, treasureRoomIndices);
            List<int> secretRoomIndices = SelectAdditionalRooms(request.specialRooms.secret, distancesFromStart, startRoomIndex, exitRoomIndex, bossRoomIndices, treasureRoomIndices, shopRoomIndices);
            List<DungeonPropPlacement> props = PlaceProps(request.propSettings, cells, request.mapWidth, request.mapHeight, rooms, startRoomIndex, exitRoomIndex, bossRoomIndices, treasureRoomIndices, shopRoomIndices, secretRoomIndices, random);
            return new DungeonWorldData(request.mapWidth, request.mapHeight, request.seed, cells, rooms, connections, startRoomIndex, exitRoomIndex, bossRoomIndices, props, treasureRoomIndices, shopRoomIndices, secretRoomIndices);
        }

        private static DungeonGeneratorSettings ResolveSettings(PCGRequest request)
        {
            DungeonGeneratorSettings source = request.generatorSettings.dungeon;
            int maxDepth = source.maxDepth;
            int minLeafSize = source.minLeafSize;
            if (request.generationProfile == PCGRequest.CompactDungeonProfile)
            {
                maxDepth = Math.Min(10, maxDepth + 1);
                minLeafSize = Math.Max(3, minLeafSize - 2);
            }
            else if (request.generationProfile == PCGRequest.SprawlingDungeonProfile)
            {
                maxDepth = Math.Max(1, maxDepth - 1);
                minLeafSize = Math.Min(Math.Min(request.mapWidth, request.mapHeight) - 1, minLeafSize + 4);
            }
            return new DungeonGeneratorSettings { maxDepth = maxDepth, minLeafSize = minLeafSize };
        }

        private static List<DungeonPropPlacement> PlaceProps(PropSettings settings, byte[] cells, int width, int height, List<IntRect> rooms, int startRoomIndex, int exitRoomIndex, List<int> bossRoomIndices, List<int> treasureRoomIndices, List<int> shopRoomIndices, List<int> secretRoomIndices, DeterministicRandom random)
        {
            List<DungeonPropPlacement> placements = new List<DungeonPropPlacement>();
            if (!settings.enabled || settings.density <= 0f || settings.maxCount == 0)
                return placements;

            List<DungeonPropType> allowedTypes = GetAllowedPropTypes(settings.allowedTypes);
            Int2 start = rooms[startRoomIndex].Center;
            Int2 exit = rooms[exitRoomIndex].Center;
            List<Int2> candidates = new List<Int2>();
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (cells[y * width + x] == (byte)DungeonCell.Empty || IsReservedMarkerCell(x, y, start, exit, rooms, bossRoomIndices, treasureRoomIndices, shopRoomIndices, secretRoomIndices))
                    continue;
                candidates.Add(new Int2(x, y));
            }
            for (int index = candidates.Count - 1; index > 0; index--)
            {
                int swapIndex = random.Next(0, index + 1);
                Int2 temporary = candidates[index];
                candidates[index] = candidates[swapIndex];
                candidates[swapIndex] = temporary;
            }
            for (int index = 0; index < candidates.Count && placements.Count < settings.maxCount; index++)
            {
                if (!random.NextChance(settings.density))
                    continue;
                DungeonCell surface = (DungeonCell)cells[candidates[index].Y * width + candidates[index].X];
                Int2 wallNormal = FindWallNormal(cells, width, height, candidates[index]);
                List<DungeonPropType> surfaceTypes = GetTypesForSurface(allowedTypes, surface, wallNormal);
                if (surfaceTypes.Count == 0)
                    continue;
                DungeonPropType type = surfaceTypes[random.Next(0, surfaceTypes.Count)];
                placements.Add(new DungeonPropPlacement(candidates[index], type, type == DungeonPropType.Torch ? wallNormal : new Int2(0, 0)));
            }
            return placements;
        }

        private static List<DungeonPropType> GetTypesForSurface(List<DungeonPropType> allowedTypes, DungeonCell surface, Int2 wallNormal)
        {
            List<DungeonPropType> result = new List<DungeonPropType>();
            for (int index = 0; index < allowedTypes.Count; index++)
            {
                DungeonPropType type = allowedTypes[index];
                if ((type == DungeonPropType.Pillar && surface == DungeonCell.Floor) ||
                    (type == DungeonPropType.Crate && (surface == DungeonCell.Floor || surface == DungeonCell.Corridor)) ||
                    (type == DungeonPropType.Crystal && surface == DungeonCell.Corridor) ||
                    (type == DungeonPropType.Torch && (surface == DungeonCell.Floor || surface == DungeonCell.Corridor) && (wallNormal.X != 0 || wallNormal.Y != 0)))
                    result.Add(type);
            }
            return result;
        }

        private static List<DungeonPropType> GetAllowedPropTypes(string[] allowedTypes)
        {
            List<DungeonPropType> result = new List<DungeonPropType>();
            if (allowedTypes != null)
            {
                for (int index = 0; index < allowedTypes.Length; index++)
                {
                    if (string.Equals(allowedTypes[index], "Pillar", StringComparison.OrdinalIgnoreCase)) result.Add(DungeonPropType.Pillar);
                    else if (string.Equals(allowedTypes[index], "Crate", StringComparison.OrdinalIgnoreCase)) result.Add(DungeonPropType.Crate);
                    else if (string.Equals(allowedTypes[index], "Crystal", StringComparison.OrdinalIgnoreCase)) result.Add(DungeonPropType.Crystal);
                    else if (string.Equals(allowedTypes[index], "Torch", StringComparison.OrdinalIgnoreCase)) result.Add(DungeonPropType.Torch);
                }
            }
            if (result.Count == 0)
            {
                result.Add(DungeonPropType.Pillar);
                result.Add(DungeonPropType.Crate);
                result.Add(DungeonPropType.Crystal);
                result.Add(DungeonPropType.Torch);
            }
            return result;
        }

        private static bool IsReservedMarkerCell(int x, int y, Int2 start, Int2 exit, List<IntRect> rooms, params List<int>[] roomIndexGroups)
        {
            if (IsNear(x, y, start) || IsNear(x, y, exit))
                return true;
            for (int groupIndex = 0; groupIndex < roomIndexGroups.Length; groupIndex++)
                for (int index = 0; index < roomIndexGroups[groupIndex].Count; index++)
                    if (IsNear(x, y, rooms[roomIndexGroups[groupIndex][index]].Center))
                        return true;
            return false;
        }

        private static Int2 FindWallNormal(byte[] cells, int width, int height, Int2 point)
        {
            int[] offsetX = { 1, 0, -1, 0 };
            int[] offsetY = { 0, 1, 0, -1 };
            for (int index = 0; index < offsetX.Length; index++)
            {
                int x = point.X + offsetX[index];
                int y = point.Y + offsetY[index];
                if (x < 0 || x >= width || y < 0 || y >= height || cells[y * width + x] == (byte)DungeonCell.Empty)
                    return new Int2(offsetX[index], offsetY[index]);
            }
            return new Int2(0, 0);
        }

        private static bool IsNear(int x, int y, Int2 point)
        {
            return Math.Abs(x - point.X) <= 1 && Math.Abs(y - point.Y) <= 1;
        }

        private static void Split(IntRect leaf, int depth, int maxDepth, int minLeafSize, DeterministicRandom random, List<IntRect> output)
        {
            if (depth >= maxDepth || !TrySplit(leaf, minLeafSize, random, out IntRect first, out IntRect second))
            {
                output.Add(leaf);
                return;
            }

            Split(first, depth + 1, maxDepth, minLeafSize, random, output);
            Split(second, depth + 1, maxDepth, minLeafSize, random, output);
        }

        private static bool TrySplit(IntRect leaf, int minLeafSize, DeterministicRandom random, out IntRect first, out IntRect second)
        {
            first = default;
            second = default;
            bool canSplitVertically = leaf.Width >= minLeafSize * 2;
            bool canSplitHorizontally = leaf.Height >= minLeafSize * 2;
            if (!canSplitVertically && !canSplitHorizontally)
                return false;

            bool splitVertically;
            if (canSplitVertically && !canSplitHorizontally)
                splitVertically = true;
            else if (!canSplitVertically && canSplitHorizontally)
                splitVertically = false;
            else if (leaf.Width > leaf.Height * 3 / 2)
                splitVertically = true;
            else if (leaf.Height > leaf.Width * 3 / 2)
                splitVertically = false;
            else
                splitVertically = random.Next(0, 2) == 0;

            if (splitVertically)
            {
                int split = random.Next(minLeafSize, leaf.Width - minLeafSize + 1);
                first = new IntRect(leaf.X, leaf.Y, split, leaf.Height);
                second = new IntRect(leaf.X + split, leaf.Y, leaf.Width - split, leaf.Height);
            }
            else
            {
                int split = random.Next(minLeafSize, leaf.Height - minLeafSize + 1);
                first = new IntRect(leaf.X, leaf.Y, leaf.Width, split);
                second = new IntRect(leaf.X, leaf.Y + split, leaf.Width, leaf.Height - split);
            }

            return true;
        }

        private static IntRect CreateRoom(IntRect leaf, DeterministicRandom random)
        {
            int paddingX = random.Next(0, Math.Min(2, (leaf.Width - 1) / 2) + 1);
            int paddingY = random.Next(0, Math.Min(2, (leaf.Height - 1) / 2) + 1);
            int availableWidth = leaf.Width - paddingX * 2;
            int availableHeight = leaf.Height - paddingY * 2;
            int minimumWidth = Math.Min(3, availableWidth);
            int minimumHeight = Math.Min(3, availableHeight);
            int roomWidth = random.Next(minimumWidth, availableWidth + 1);
            int roomHeight = random.Next(minimumHeight, availableHeight + 1);
            int x = leaf.X + paddingX + random.Next(0, availableWidth - roomWidth + 1);
            int y = leaf.Y + paddingY + random.Next(0, availableHeight - roomHeight + 1);
            return new IntRect(x, y, roomWidth, roomHeight);
        }

        private static List<DungeonConnection> BuildMinimumSpanningTree(List<IntRect> rooms)
        {
            List<DungeonConnection> candidates = new List<DungeonConnection>();
            for (int left = 0; left < rooms.Count; left++)
            {
                Int2 leftCenter = rooms[left].Center;
                for (int right = left + 1; right < rooms.Count; right++)
                {
                    Int2 rightCenter = rooms[right].Center;
                    int cost = Math.Abs(leftCenter.X - rightCenter.X) + Math.Abs(leftCenter.Y - rightCenter.Y);
                    candidates.Add(new DungeonConnection(left, right, cost));
                }
            }

            candidates.Sort(CompareConnections);
            DisjointSet sets = new DisjointSet(rooms.Count);
            List<DungeonConnection> result = new List<DungeonConnection>();
            for (int index = 0; index < candidates.Count && result.Count + 1 < rooms.Count; index++)
            {
                DungeonConnection edge = candidates[index];
                if (sets.TryUnion(edge.RoomA, edge.RoomB))
                    result.Add(edge);
            }

            return result;
        }

        private static int CompareConnections(DungeonConnection left, DungeonConnection right)
        {
            int costComparison = left.Cost.CompareTo(right.Cost);
            if (costComparison != 0)
                return costComparison;
            int firstComparison = left.RoomA.CompareTo(right.RoomA);
            return firstComparison != 0 ? firstComparison : left.RoomB.CompareTo(right.RoomB);
        }

        private static int[] CalculateConnectionDistances(int roomCount, List<DungeonConnection> connections, int startRoomIndex)
        {
            int[] distances = new int[roomCount];
            bool[] visited = new bool[roomCount];
            for (int index = 0; index < roomCount; index++)
                distances[index] = int.MaxValue;
            distances[startRoomIndex] = 0;

            for (int iteration = 0; iteration < roomCount; iteration++)
            {
                int current = -1;
                for (int roomIndex = 0; roomIndex < roomCount; roomIndex++)
                {
                    if (!visited[roomIndex] && distances[roomIndex] != int.MaxValue &&
                        (current < 0 || distances[roomIndex] < distances[current] ||
                         (distances[roomIndex] == distances[current] && roomIndex < current)))
                        current = roomIndex;
                }

                if (current < 0)
                    break;
                visited[current] = true;
                for (int edgeIndex = 0; edgeIndex < connections.Count; edgeIndex++)
                {
                    DungeonConnection edge = connections[edgeIndex];
                    int neighbour = edge.RoomA == current ? edge.RoomB : edge.RoomB == current ? edge.RoomA : -1;
                    if (neighbour < 0 || visited[neighbour])
                        continue;
                    int candidate = distances[current] + edge.Cost;
                    if (candidate < distances[neighbour])
                        distances[neighbour] = candidate;
                }
            }

            return distances;
        }

        private static int FindFarthestRoom(int[] distances, int excludedRoomIndex)
        {
            int farthest = excludedRoomIndex;
            for (int index = 0; index < distances.Length; index++)
            {
                if (index == excludedRoomIndex)
                    continue;
                if (distances[index] > distances[farthest] ||
                    (distances[index] == distances[farthest] && index < farthest))
                    farthest = index;
            }

            return farthest;
        }

        private static List<int> SelectBossRooms(BossRoomSettings settings, int[] distances, int startRoomIndex, int exitRoomIndex)
        {
            List<int> candidates = new List<int>();
            if (!settings.enabled || settings.count == 0)
                return candidates;
            for (int index = 0; index < distances.Length; index++)
            {
                if (index != startRoomIndex && index != exitRoomIndex)
                    candidates.Add(index);
            }

            candidates.Sort((left, right) =>
            {
                int distanceComparison = distances[right].CompareTo(distances[left]);
                return distanceComparison != 0 ? distanceComparison : left.CompareTo(right);
            });
            if (candidates.Count > settings.count)
                candidates.RemoveRange(settings.count, candidates.Count - settings.count);
            return candidates;
        }

        private static List<int> SelectAdditionalRooms(RoomSelectionSettings settings, int[] distances, int startRoomIndex, int exitRoomIndex, params List<int>[] excludedGroups)
        {
            List<int> candidates = new List<int>();
            if (!settings.enabled || settings.count == 0)
                return candidates;
            for (int index = 0; index < distances.Length; index++)
            {
                if (index == startRoomIndex || index == exitRoomIndex || IsExcluded(index, excludedGroups))
                    continue;
                candidates.Add(index);
            }
            candidates.Sort((left, right) =>
            {
                int distanceComparison = distances[right].CompareTo(distances[left]);
                return distanceComparison != 0 ? distanceComparison : left.CompareTo(right);
            });
            if (candidates.Count > settings.count)
                candidates.RemoveRange(settings.count, candidates.Count - settings.count);
            return candidates;
        }

        private static bool IsExcluded(int roomIndex, List<int>[] groups)
        {
            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                if (groups[groupIndex].Contains(roomIndex))
                    return true;
            return false;
        }

        private static void CarveRoom(byte[] cells, int width, IntRect room)
        {
            for (int y = room.Y; y < room.YMax; y++)
            for (int x = room.X; x < room.XMax; x++)
                cells[y * width + x] = (byte)DungeonCell.Floor;
        }

        private static void CarveCorridor(byte[] cells, int width, Int2 point)
        {
            int index = point.Y * width + point.X;
            if (cells[index] == (byte)DungeonCell.Empty)
                cells[index] = (byte)DungeonCell.Corridor;
        }
    }

    internal sealed class DeterministicRandom
    {
        private uint state;

        public DeterministicRandom(int seed)
        {
            state = unchecked((uint)seed);
            if (state == 0)
                state = 0x6D2B79F5u;
        }

        public int Next(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
                return minInclusive;

            uint range = (uint)(maxExclusive - minInclusive);
            uint limit = uint.MaxValue - uint.MaxValue % range;
            uint value;
            do
            {
                value = NextUInt();
            } while (value >= limit);
            return minInclusive + (int)(value % range);
        }

        public bool NextChance(float probability)
        {
            if (probability <= 0f)
                return false;
            if (probability >= 1f)
                return true;
            return Next(0, 1000000) < probability * 1000000f;
        }

        private uint NextUInt()
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }
    }

    internal sealed class DisjointSet
    {
        private readonly int[] parent;
        private readonly byte[] rank;

        public DisjointSet(int count)
        {
            parent = new int[count];
            rank = new byte[count];
            for (int index = 0; index < count; index++)
                parent[index] = index;
        }

        public bool TryUnion(int first, int second)
        {
            int firstRoot = Find(first);
            int secondRoot = Find(second);
            if (firstRoot == secondRoot)
                return false;
            if (rank[firstRoot] < rank[secondRoot])
                parent[firstRoot] = secondRoot;
            else if (rank[firstRoot] > rank[secondRoot])
                parent[secondRoot] = firstRoot;
            else
            {
                parent[secondRoot] = firstRoot;
                rank[firstRoot]++;
            }

            return true;
        }

        private int Find(int item)
        {
            int root = item;
            while (parent[root] != root)
                root = parent[root];
            while (parent[item] != item)
            {
                int next = parent[item];
                parent[item] = root;
                item = next;
            }

            return root;
        }
    }

    internal static class GridAStar
    {
        private static readonly int[] OffsetX = { 1, 0, -1, 0 };
        private static readonly int[] OffsetY = { 0, 1, 0, -1 };

        public static List<Int2> FindPath(int width, int height, Int2 start, Int2 target)
        {
            int count = width * height;
            int startIndex = start.Y * width + start.X;
            int targetIndex = target.Y * width + target.X;
            int[] gScore = new int[count];
            int[] parent = new int[count];
            bool[] open = new bool[count];
            bool[] closed = new bool[count];
            for (int index = 0; index < count; index++)
            {
                gScore[index] = int.MaxValue;
                parent[index] = -1;
            }

            gScore[startIndex] = 0;
            open[startIndex] = true;
            while (true)
            {
                int current = FindBestOpen(open, closed, gScore, width, target);
                if (current < 0)
                    return new List<Int2>();
                if (current == targetIndex)
                    return ReconstructPath(parent, current, width);

                open[current] = false;
                closed[current] = true;
                int currentX = current % width;
                int currentY = current / width;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nextX = currentX + OffsetX[direction];
                    int nextY = currentY + OffsetY[direction];
                    if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height)
                        continue;
                    int next = nextY * width + nextX;
                    if (closed[next])
                        continue;
                    int candidate = gScore[current] + 1;
                    if (!open[next] || candidate < gScore[next])
                    {
                        parent[next] = current;
                        gScore[next] = candidate;
                        open[next] = true;
                    }
                }
            }
        }

        private static int FindBestOpen(bool[] open, bool[] closed, int[] gScore, int width, Int2 target)
        {
            int best = -1;
            int bestScore = int.MaxValue;
            for (int index = 0; index < open.Length; index++)
            {
                if (!open[index] || closed[index])
                    continue;
                int x = index % width;
                int y = index / width;
                int score = gScore[index] + Math.Abs(x - target.X) + Math.Abs(y - target.Y);
                if (score < bestScore || (score == bestScore && index < best))
                {
                    best = index;
                    bestScore = score;
                }
            }

            return best;
        }

        private static List<Int2> ReconstructPath(int[] parent, int current, int width)
        {
            List<Int2> path = new List<Int2>();
            while (current >= 0)
            {
                path.Add(new Int2(current % width, current / width));
                current = parent[current];
            }

            path.Reverse();
            return path;
        }
    }
}
