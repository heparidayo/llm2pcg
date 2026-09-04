using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;

namespace Llm2Pcg.Visuals
{
    public enum CityStructureClass : byte { LowRise, MidRise, HighRise }

    public readonly struct CityStructurePlacement
    {
        public readonly IntRect Footprint;
        public readonly Int2 Center;
        public readonly float Height;
        public readonly CityStructureClass StructureClass;

        public CityStructurePlacement(IntRect footprint, Int2 center, float height, CityStructureClass structureClass)
        {
            Footprint = footprint; Center = center; Height = height; StructureClass = structureClass;
        }
    }

    /// <summary>Derives one stable render placement per generator-provided City building center without altering world data.</summary>
    public static class CityStructureExtractor
    {
        public static List<CityStructurePlacement> Extract(BiomeWorldData world)
        {
            List<Candidate> candidates = new List<Candidate>();
            if (world == null || !string.Equals(world.WorldType, PCGRequest.CityWorldType, StringComparison.Ordinal)) return new List<CityStructurePlacement>();
            List<Int2> centers = new List<Int2>();
            for (int index = 0; index < world.Props.Count; index++) centers.Add(world.Props[index]);
            centers.Sort((left, right) => { int value = left.Y.CompareTo(right.Y); return value != 0 ? value : left.X.CompareTo(right.X); });
            bool[] claimed = new bool[world.Tiles.Length];
            for (int index = 0; index < centers.Count; index++)
            {
                Int2 center = centers[index];
                if (!world.IsInBounds(center.X, center.Y)) continue;
                int centerIndex = center.Y * world.Width + center.X;
                if (world.Tiles[centerIndex] != BiomeTile.Building || claimed[centerIndex]) continue;
                float height = world.StructureHeights[centerIndex];
                int minX = center.X, maxX = center.X, minY = center.Y, maxY = center.Y;
                while (minX > 0 && IsSameBuilding(world, minX - 1, center.Y, height)) minX--;
                while (maxX + 1 < world.Width && IsSameBuilding(world, maxX + 1, center.Y, height)) maxX++;
                while (minY > 0 && RowMatches(world, minX, maxX, minY - 1, height)) minY--;
                while (maxY + 1 < world.Height && RowMatches(world, minX, maxX, maxY + 1, height)) maxY++;
                IntRect footprint = new IntRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
                if (!RectangleMatches(world, footprint, height) || OverlapsClaimed(world.Width, footprint, claimed)) continue;
                Claim(world.Width, footprint, claimed);
                candidates.Add(new Candidate(footprint, center, height));
            }

            float minimum = float.MaxValue, maximum = float.MinValue;
            for (int index = 0; index < candidates.Count; index++) { minimum = Math.Min(minimum, candidates[index].Height); maximum = Math.Max(maximum, candidates[index].Height); }
            List<CityStructurePlacement> result = new List<CityStructurePlacement>(candidates.Count);
            for (int index = 0; index < candidates.Count; index++)
            {
                Candidate candidate = candidates[index];
                float normalized = maximum <= minimum ? 0f : (candidate.Height - minimum) / (maximum - minimum);
                CityStructureClass structureClass = normalized < 1f / 3f ? CityStructureClass.LowRise : normalized < 2f / 3f ? CityStructureClass.MidRise : CityStructureClass.HighRise;
                result.Add(new CityStructurePlacement(candidate.Footprint, candidate.Center, candidate.Height, structureClass));
            }
            return result;
        }

        public static string Category(CityStructureClass structureClass)
        {
            if (structureClass == CityStructureClass.LowRise) return VisualCategoryIds.CityLowRise;
            if (structureClass == CityStructureClass.MidRise) return VisualCategoryIds.CityMidRise;
            return VisualCategoryIds.CityHighRise;
        }

        private static bool IsSameBuilding(BiomeWorldData world, int x, int y, float height)
        {
            int index = y * world.Width + x;
            return world.Tiles[index] == BiomeTile.Building && Math.Abs(world.StructureHeights[index] - height) < .001f;
        }
        private static bool RowMatches(BiomeWorldData world, int minX, int maxX, int y, float height) { for (int x = minX; x <= maxX; x++) if (!IsSameBuilding(world, x, y, height)) return false; return true; }
        private static bool RectangleMatches(BiomeWorldData world, IntRect rectangle, float height) { for (int y = rectangle.Y; y < rectangle.YMax; y++) for (int x = rectangle.X; x < rectangle.XMax; x++) if (!IsSameBuilding(world, x, y, height)) return false; return true; }
        private static bool OverlapsClaimed(int width, IntRect rectangle, bool[] claimed) { for (int y = rectangle.Y; y < rectangle.YMax; y++) for (int x = rectangle.X; x < rectangle.XMax; x++) if (claimed[y * width + x]) return true; return false; }
        private static void Claim(int width, IntRect rectangle, bool[] claimed) { for (int y = rectangle.Y; y < rectangle.YMax; y++) for (int x = rectangle.X; x < rectangle.XMax; x++) claimed[y * width + x] = true; }

        private readonly struct Candidate
        {
            public readonly IntRect Footprint; public readonly Int2 Center; public readonly float Height;
            public Candidate(IntRect footprint, Int2 center, float height) { Footprint = footprint; Center = center; Height = height; }
        }
    }
}
