using System;
using System.Collections.Generic;
using System.Globalization;
using Llm2Pcg.Core;
using UnityEngine;

namespace Llm2Pcg.Visuals
{
    public readonly struct ResolvedVisualPlacement
    {
        public readonly string CategoryId;
        public readonly string VariantStableId;
        public readonly int X;
        public readonly int Y;
        public readonly int SemanticIndex;
        public readonly Matrix4x4 WorldMatrix;
        public readonly VisualVariant Variant;

        public ResolvedVisualPlacement(string categoryId, string variantStableId, int x, int y, int semanticIndex, Matrix4x4 worldMatrix, VisualVariant variant)
        {
            CategoryId = categoryId;
            VariantStableId = variantStableId;
            X = x;
            Y = y;
            SemanticIndex = semanticIndex;
            WorldMatrix = worldMatrix;
            Variant = variant;
        }
    }

    /// <summary>Pure deterministic visual selection. It never reads UnityEngine.Random, time, GUIDs, or collection iteration order.</summary>
    public static class VisualVariantResolver
    {
        private const int VisualStage = 3100;

        public static bool TryResolve(
            BiomeVisualProfile profile,
            string categoryId,
            int worldSeed,
            int x,
            int y,
            int semanticIndex,
            Vector3 worldPosition,
            Quaternion baseRotation,
            Vector3 targetSize,
            out ResolvedVisualPlacement placement)
        {
            return TryResolve(profile, categoryId, worldSeed, x, y, semanticIndex, worldPosition, baseRotation, targetSize, null, out placement);
        }

        public static bool TryResolve(
            BiomeVisualProfile profile,
            string categoryId,
            int worldSeed,
            int x,
            int y,
            int semanticIndex,
            Vector3 worldPosition,
            Quaternion baseRotation,
            Vector3 targetSize,
            string[] allowedTypes,
            out ResolvedVisualPlacement placement)
        {
            placement = default;
            VisualCategory category = profile == null ? null : profile.FindCategory(categoryId);
            List<VisualVariant> variants = GetSortedValidVariants(category, allowedTypes);
            if (variants.Count == 0) return false;

            uint hash = SeedPlacement(worldSeed, categoryId, x, y, semanticIndex);
            int totalWeight = 0;
            for (int index = 0; index < variants.Count; index++) totalWeight += Math.Max(1, variants[index].weight);
            int selected = (int)(hash % (uint)totalWeight);
            VisualVariant variant = variants[0];
            for (int index = 0; index < variants.Count; index++)
            {
                int weight = Math.Max(1, variants[index].weight);
                if (selected < weight) { variant = variants[index]; break; }
                selected -= weight;
            }

            hash = Mix(hash, 0xA341316Cu);
            float yawStep = variant.yawStep > 0f ? variant.yawStep : category.placementRules.yawStep;
            float yaw = 0f;
            if (category.placementRules.randomYaw && yawStep > 0f)
            {
                int steps = Math.Max(1, (int)Math.Round(360f / yawStep));
                yaw = (hash % (uint)steps) * yawStep;
            }

            hash = Mix(hash, 0xC8013EA4u);
            Vector2 scaleRange = variant.uniformScaleRange;
            if (scaleRange.x <= 0f || scaleRange.y <= 0f) scaleRange = category.placementRules.uniformScaleRange;
            float minimumScale = Math.Max(.001f, Math.Min(scaleRange.x, scaleRange.y));
            float maximumScale = Math.Max(minimumScale, Math.Max(scaleRange.x, scaleRange.y));
            float normalized = (hash & 0x00FFFFFFu) / 16777215f;
            Vector3 scale = Vector3.Scale(targetSize, Vector3.one * Mathf.Lerp(minimumScale, maximumScale, normalized));
            if (variant.fitMode == VisualFitMode.StretchToFootprint)
            {
                Vector3 source = variant.sourceBoundsSize;
                scale = new Vector3(
                    targetSize.x / Math.Max(.001f, source.x),
                    targetSize.y / Math.Max(.001f, source.y),
                    targetSize.z / Math.Max(.001f, source.z));
            }

            Quaternion rotation = baseRotation * Quaternion.Euler(0f, yaw, 0f);
            Vector3 position = worldPosition + Vector3.up * variant.verticalOffset;
            position += rotation * Vector3.Scale(variant.pivotOffset, scale);
            Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, scale);
            placement = new ResolvedVisualPlacement(categoryId, variant.stableId, x, y, semanticIndex, matrix, variant);
            return true;
        }

        public static List<VisualVariant> GetSortedValidVariants(VisualCategory category)
        {
            return GetSortedValidVariants(category, null);
        }

        public static List<VisualVariant> GetSortedValidVariants(VisualCategory category, string[] allowedTypes)
        {
            List<VisualVariant> result = new List<VisualVariant>();
            if (category?.variants == null) return result;
            for (int index = 0; index < category.variants.Length; index++)
            {
                VisualVariant variant = category.variants[index];
                if (variant != null && !string.IsNullOrWhiteSpace(variant.stableId) && variant.GetLodParts(0).Length > 0)
                    if (MatchesAnyAllowedType(variant, allowedTypes)) result.Add(variant);
            }
            result.Sort((left, right) => string.CompareOrdinal(left.stableId, right.stableId));
            return result;
        }

        public static bool MatchesAnyAllowedType(VisualVariant variant, string[] allowedTypes)
        {
            if (variant == null) return false;
            if (allowedTypes == null || allowedTypes.Length == 0) return true;
            string searchable = (variant.stableId ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            for (int index = 0; index < allowedTypes.Length; index++)
                if (MatchesCanonicalType(searchable, allowedTypes[index])) return true;
            return false;
        }

        private static bool MatchesCanonicalType(string searchable, string type)
        {
            switch (type)
            {
                case "cherry_blossom": return ContainsAny(searchable, "blossom", "cherry", "sakura");
                case "broadleaf": return ContainsAny(searchable, "broadleaf", "deciduous");
                case "conifer": return ContainsAny(searchable, "spruce", "pine", "fir", "conifer");
                case "willow": return searchable.Contains("willow");
                case "dead_tree": return ContainsAny(searchable, "deadtree", "deadwood");
                case "palm": return searchable.Contains("palm");
                case "cactus": return ContainsAny(searchable, "cactus", "cacti");
                case "rock": return searchable.Contains("rock");
                case "bush": return searchable.Contains("bush");
                case "reeds": return ContainsAny(searchable, "reed", "cattail");
                case "grass": return searchable.Contains("grass");
                case "flower": return ContainsAny(searchable, "flower", "meadow");
                case "plant": return searchable.Contains("plant");
                case "mushroom": return searchable.Contains("mushroom");
                case "stump": return searchable.Contains("stump");
                case "log": return searchable.Contains("log");
                case "branch": return searchable.Contains("branch");
                case "thorn": return searchable.Contains("thorn");
                case "lily_pad": return searchable.Contains("lilypad");
                case "water_lily": return searchable.Contains("waterlily");
                case "ice": return searchable.Contains("ice");
                default: return false;
            }
        }

        private static bool ContainsAny(string source, params string[] fragments)
        {
            for (int index = 0; index < fragments.Length; index++) if (source.Contains(fragments[index])) return true;
            return false;
        }

        private static uint SeedPlacement(int seed, string categoryId, int x, int y, int semanticIndex)
        {
            unchecked
            {
                uint hash = (uint)PcgSeed.Stage(seed, VisualStage);
                AppendString(ref hash, categoryId);
                AppendInt(ref hash, x);
                AppendInt(ref hash, y);
                AppendInt(ref hash, semanticIndex);
                return Mix(hash, 0x9E3779B9u);
            }
        }

        internal static void AppendString(ref uint hash, string value)
        {
            value = value ?? string.Empty;
            for (int index = 0; index < value.Length; index++)
            {
                hash ^= value[index];
                hash *= 16777619u;
            }
        }

        internal static void AppendInt(ref uint hash, int value)
        {
            unchecked
            {
                hash ^= (byte)value; hash *= 16777619u;
                hash ^= (byte)(value >> 8); hash *= 16777619u;
                hash ^= (byte)(value >> 16); hash *= 16777619u;
                hash ^= (byte)(value >> 24); hash *= 16777619u;
            }
        }

        private static uint Mix(uint value, uint salt)
        {
            unchecked
            {
                value ^= salt;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return value;
            }
        }
    }

    public static class VisualLayoutHasher
    {
        public static string Compute(BiomeVisualProfile profile, IReadOnlyList<ResolvedVisualPlacement> placements)
        {
            List<ResolvedVisualPlacement> sorted = new List<ResolvedVisualPlacement>();
            if (placements != null) for (int index = 0; index < placements.Count; index++) sorted.Add(placements[index]);
            sorted.Sort(Compare);
            unchecked
            {
                uint hash = 2166136261u;
                VisualVariantResolver.AppendString(ref hash, profile == null ? string.Empty : profile.profileId);
                VisualVariantResolver.AppendInt(ref hash, profile == null ? 0 : profile.profileVersion);
                for (int index = 0; index < sorted.Count; index++)
                {
                    ResolvedVisualPlacement placement = sorted[index];
                    VisualVariantResolver.AppendString(ref hash, placement.CategoryId);
                    VisualVariantResolver.AppendInt(ref hash, placement.X);
                    VisualVariantResolver.AppendInt(ref hash, placement.Y);
                    VisualVariantResolver.AppendInt(ref hash, placement.SemanticIndex);
                    VisualVariantResolver.AppendString(ref hash, placement.VariantStableId);
                    AppendMatrix(ref hash, placement.WorldMatrix);
                    VisualRenderPart[] parts = placement.Variant?.GetLodParts(0);
                    if (parts == null) continue;
                    List<VisualRenderPart> sortedParts = new List<VisualRenderPart>();
                    for (int part = 0; part < parts.Length; part++) if (parts[part] != null) sortedParts.Add(parts[part]);
                    sortedParts.Sort((left, right) => string.CompareOrdinal(left.stableId, right.stableId));
                    for (int part = 0; part < sortedParts.Count; part++)
                    {
                        VisualRenderPart renderPart = sortedParts[part];
                        VisualVariantResolver.AppendString(ref hash, renderPart.stableId);
                        VisualVariantResolver.AppendString(ref hash, renderPart.mesh == null ? string.Empty : renderPart.mesh.name);
                        VisualVariantResolver.AppendInt(ref hash, renderPart.subMeshIndex);
                        VisualVariantResolver.AppendString(ref hash, renderPart.material == null ? string.Empty : renderPart.material.name);
                        AppendMatrix(ref hash, renderPart.LocalMatrix);
                    }
                }
                return hash.ToString("X8", CultureInfo.InvariantCulture);
            }
        }

        private static int Compare(ResolvedVisualPlacement left, ResolvedVisualPlacement right)
        {
            int value = string.CompareOrdinal(left.CategoryId, right.CategoryId);
            if (value != 0) return value;
            value = left.Y.CompareTo(right.Y); if (value != 0) return value;
            value = left.X.CompareTo(right.X); if (value != 0) return value;
            value = left.SemanticIndex.CompareTo(right.SemanticIndex); if (value != 0) return value;
            return string.CompareOrdinal(left.VariantStableId, right.VariantStableId);
        }

        private static void AppendMatrix(ref uint hash, Matrix4x4 matrix)
        {
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                VisualVariantResolver.AppendInt(ref hash, Mathf.RoundToInt(matrix[row, column] * 10000f));
        }
    }
}
