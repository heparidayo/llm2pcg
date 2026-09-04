using System;
using UnityEngine;

namespace Llm2Pcg.Visuals
{
    /// <summary>Camera-dependent LOD choice that never changes deterministic placement or layout hashes.</summary>
    public static class VisualLodSelector
    {
        public static int SelectLodIndex(VisualVariant variant, float relativeScreenHeight)
        {
            if (variant?.lodTiers == null || variant.lodTiers.Length == 0) return 0;
            float height = Mathf.Max(0f, relativeScreenHeight);
            for (int index = 0; index < variant.lodTiers.Length - 1; index++)
            {
                VisualLodTier tier = variant.lodTiers[index];
                float threshold = tier == null ? 0f : Mathf.Max(0f, tier.screenRelativeTransitionHeight);
                if (height >= threshold) return index;
            }
            return variant.lodTiers.Length - 1;
        }

        public static int SelectLodIndex(ResolvedVisualPlacement placement, Camera camera)
        {
            if (camera == null || placement.Variant == null) return 0;
            return SelectLodIndex(placement.Variant, RelativeScreenHeight(placement, camera));
        }

        public static float RelativeScreenHeight(ResolvedVisualPlacement placement, Camera camera)
        {
            if (camera == null || placement.Variant == null) return float.PositiveInfinity;
            VisualVariant variant = placement.Variant;
            Vector3 scale = placement.WorldMatrix.lossyScale;
            float maximumScale = Math.Max(Math.Abs(scale.x), Math.Max(Math.Abs(scale.y), Math.Abs(scale.z)));
            float size = Math.Max(.001f, variant.lodSize > 0f ? variant.lodSize : Math.Max(variant.sourceBoundsSize.x, Math.Max(variant.sourceBoundsSize.y, variant.sourceBoundsSize.z)));
            size *= Math.Max(.001f, maximumScale) * Math.Max(.01f, QualitySettings.lodBias);
            if (camera.orthographic) return size / Math.Max(.001f, camera.orthographicSize * 2f);

            Vector3 center = placement.WorldMatrix.MultiplyPoint3x4(variant.lodReferencePoint);
            float distance = Math.Max(.001f, Vector3.Distance(camera.transform.position, center));
            float halfFov = Mathf.Deg2Rad * Mathf.Clamp(camera.fieldOfView, 1f, 179f) * .5f;
            return size / Math.Max(.001f, 2f * distance * Mathf.Tan(halfFov));
        }
    }
}
