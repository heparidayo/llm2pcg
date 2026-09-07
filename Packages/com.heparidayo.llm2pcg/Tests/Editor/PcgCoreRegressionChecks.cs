using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;

namespace Llm2Pcg.Tests
{
    // Shared by Unity EditMode and the standalone --self-test; no Unity/NUnit dependency.
    public static class PcgCoreRegressionChecks
    {
        public static void Run()
        {
            NoiseRounding(); SpacingMatchesBruteForce(); NumericGuards(); MapGuards();
            LegacyDefaults(); DungeonOutcomeDiagnostics(); NavigableEndpoints();
        }

        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

        public static void NoiseRounding()
        {
            Require(PcgSeed.NoiseScale(.08f) == 13, "0.08 float reciprocal must round in explicit double precision.");
            Require(PcgSeed.NoiseScale(.016f) == 62, "0.016 boundary regression.");
            Require(PcgSeed.NoiseScale(.025f) == 40, "0.025 boundary regression.");
            Require(PcgSeed.NoiseScale(.001f) == 1000, "Minimum frequency regression.");
            Require(PcgSeed.NoiseScale(1f) == 3, "Minimum scale regression.");
        }

        public static void SpacingMatchesBruteForce()
        {
            foreach (int distance in new[] { 2, 3, 6, 32 })
            {
                var spacing = new PcgPointSpacingIndex(100, 80, distance);
                var accepted = new List<Int2>(); var random = new PcgDeterministicRandom(441);
                for (int attempt = 0; attempt < 10000; attempt++)
                {
                    int x = random.NextInt(100), y = random.NextInt(80); bool expected = true;
                    foreach (Int2 point in accepted)
                    {
                        int dx = x - point.X, dy = y - point.Y;
                        if (dx * dx + dy * dy < distance * distance) { expected = false; break; }
                    }
                    Require(spacing.CanPlace(x, y) == expected, "Spatial buckets changed candidate acceptance.");
                    if (expected) { accepted.Add(new Int2(x, y)); spacing.Add(x, y); }
                }
                Require(!spacing.CanPlace(-1, 0) && !spacing.CanPlace(100, 0), "Spacing bounds.");
            }
            var boundary = new PcgPointSpacingIndex(16, 16, 4); boundary.Add(0, 0);
            Require(boundary.CanPlace(4, 0) && !boundary.CanPlace(3, 0), "Exact minimum distance must remain allowed.");
        }

        public static void NumericGuards()
        {
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 2f })
            {
                var r = PCGRequest.CreateForestDefault(); r.generatorSettings.forest.waterThreshold = value;
                Require(!PCGRequestValidator.Validate(r).IsValid, "Invalid water threshold accepted.");
                r = PCGRequest.CreateForestDefault(); r.visualSettings.trees.density = value;
                Require(!PCGRequestValidator.Validate(r).IsValid, "Invalid visual density accepted.");
            }
            var request = PCGRequest.CreateForestDefault(); request.generatorSettings.forest.noiseOctaves = 9;
            Require(!PCGRequestValidator.Validate(request).IsValid, "Unbounded noise octave count.");
            request = PCGRequest.CreateForestDefault(); request.generatorSettings.forest.vegetationMinDistance = float.PositiveInfinity;
            Require(!PCGRequestValidator.Validate(request).IsValid, "Infinite vegetation distance.");
            request = PCGRequest.CreateForestDefault(); request.visualSettings.trees.allowedTypes = new[] { "rock" };
            Require(!PCGRequestValidator.Validate(request).IsValid, "Cross-category visual type accepted.");
            request = PCGRequest.CreateDefault(); request.propSettings.allowedTypes = new[] { "Alien" };
            Require(!PCGRequestValidator.Validate(request).IsValid, "Unknown dungeon prop type accepted.");
            request = PCGRequest.CreateCaveDefault(); request.generatorSettings.cave.automataSteps = 13;
            Require(!PCGRequestValidator.Validate(request).IsValid, "Unbounded cellular iteration count.");
            request = PCGRequest.CreateCityDefault(); request.generatorSettings.city.hubMaxCount = 65;
            Require(!PCGRequestValidator.Validate(request).IsValid, "Unbounded city hub count.");
            request = PCGRequest.CreateDefault(); request.generatorSettings.dungeon.maxDepth = 13;
            Require(!PCGRequestValidator.Validate(request).IsValid, "Unbounded dungeon depth.");
        }

        public static void MapGuards()
        {
            foreach (int size in new[] { -1, 0, 1, 15, 501, int.MaxValue })
            {
                var r = PCGRequest.CreateForestDefault(mapWidth: size);
                Require(PCGRequestValidator.Validate(r).Code == "INVALID_MAP_SIZE", "Unsafe allocation size accepted.");
            }
            Require(PCGRequestValidator.Validate(PCGRequest.CreateForestDefault(mapWidth: 16, mapHeight: 500)).IsValid, "Valid boundary dimensions rejected.");
        }

        public static void LegacyDefaults()
        {
            var legacy = PCGRequest.CreateForestDefault(); legacy.schemaVersion = 2;
            legacy.generatorSettings.forest.elevationScale = 0; legacy.generatorSettings.forest.elevationFrequency = 0;
            Require(PCGRequestValidator.Validate(legacy).IsValid, "Legacy missing elevation defaults broken.");
            var current = PCGRequest.CreateForestDefault(); current.generatorSettings.forest.elevationFrequency = 0;
            Require(!PCGRequestValidator.Validate(current).IsValid, "Explicit invalid v3 frequency became a default.");
            Require(current.generatorSettings.forest.elevationFrequency == 0, "Invalid v3 request was silently repaired.");
        }

        public static void DungeonOutcomeDiagnostics()
        {
            var r = PCGRequest.CreateDefault(816, 64, 64);
            r.propSettings.enabled = true; r.propSettings.density = 1; r.propSettings.maxCount = 0;
            r.propSettings.allowedTypes = new[] { "Crate", "Pillar", "Torch" };
            var world = (DungeonWorldData)PCGGeneratorRegistry.Default.GetRequired(r).Generate(r);
            string hash = world.ComputeStableHash();
            Require(world.Props.Count == 0, "Zero cap compatibility changed.");
            Require(PCGGenerationDiagnostics.Inspect(r, world).Exists(d => d.code == "DUNGEON_PROP_ZERO_CAP"), "Missing zero-cap warning.");
            Require(world.ComputeStableHash() == hash && r.propSettings.maxCount == 0, "Diagnostics mutated generation.");
            r.propSettings.maxCount = 32;
            world = (DungeonWorldData)PCGGeneratorRegistry.Default.GetRequired(r).Generate(r);
            Require(world.Props.Count > 0 && world.Props.Count <= 32, "Positive prop cap does not produce props.");
            Require(!PCGGenerationDiagnostics.Inspect(r, world).Exists(d => d.code == "DUNGEON_PROP_ZERO_CAP"), "False zero-cap warning.");
            r.specialRooms.boss.enabled = true; r.specialRooms.boss.count = 1000;
            world = (DungeonWorldData)PCGGeneratorRegistry.Default.GetRequired(r).Generate(r);
            Require(PCGGenerationDiagnostics.Inspect(r, world).Exists(d => d.code == "SPECIAL_ROOM_COUNT_CAPPED"), "Missing special room cap warning.");
        }

        public static void NavigableEndpoints()
        {
            foreach (int seed in new[] { 0, 234, int.MinValue })
            foreach (PCGRequest r in new[] { PCGRequest.CreateDefault(seed,32,48), PCGRequest.CreateCaveDefault(seed,32,48), PCGRequest.CreateCityDefault(seed,32,48), PCGRequest.CreateForestDefault(seed,32,48), PCGRequest.CreateSwampDefault(seed,32,48), PCGRequest.CreateSnowfieldDefault(seed,32,48), PCGRequest.CreateDesertDefault(seed,32,48) })
            {
                var w = (IPCGNavigableWorldData)PCGGeneratorRegistry.Default.GetRequired(r).Generate(r);
                Require(w.IsWalkable(w.StartPosition.X,w.StartPosition.Y) && w.IsWalkable(w.ExitPosition.X,w.ExitPosition.Y), r.worldType + " endpoint blocked.");
                var visited = new bool[w.Width*w.Height]; var queue = new Queue<Int2>();
                queue.Enqueue(w.StartPosition); visited[w.StartPosition.Y*w.Width+w.StartPosition.X] = true;
                int[] dx = {0,1,0,-1}, dy = {-1,0,1,0};
                while(queue.Count>0)
                {
                    Int2 p=queue.Dequeue();
                    for(int d=0;d<4;d++) {int x=p.X+dx[d],y=p.Y+dy[d];if(x<0||y<0||x>=w.Width||y>=w.Height||!w.IsWalkable(x,y)||visited[y*w.Width+x])continue;visited[y*w.Width+x]=true;queue.Enqueue(new Int2(x,y));}
                }
                Require(visited[w.ExitPosition.Y*w.Width+w.ExitPosition.X], r.worldType + " exit disconnected.");
            }
        }
    }
}
