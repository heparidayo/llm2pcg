using System.Collections.Generic;
using Llm2Pcg.Visuals;
using UnityEngine;

namespace Llm2Pcg.Rendering
{
    /// <summary>At most eight shadowless lights, reused for nearby sconces; never one light per tile.</summary>
    internal sealed class DungeonTorchLightPool
    {
        private const int Capacity = 8;
        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly Light[] lights = new Light[Capacity];
        private readonly int[] selected = new int[Capacity];
        private Vector3 previousCamera;
        private bool dirty = true;
        public int ActiveCount { get; private set; }
        public void SetPlacements(IReadOnlyList<ResolvedVisualPlacement> placements)
        {
            positions.Clear();
            foreach (var p in placements)
                if (p.CategoryId == VisualCategoryIds.DungeonTorches)
                    positions.Add((Vector3)p.WorldMatrix.GetColumn(3) + Vector3.up * .4f);
            dirty = true;
        }
        public void Update(Camera camera, Transform owner)
        {
            if (!Application.isPlaying || camera == null) return;
            if (!dirty && (previousCamera - camera.transform.position).sqrMagnitude < .25f) return;
            previousCamera = camera.transform.position; dirty = false; ActiveCount = 0;
            for (int slot = 0; slot < Capacity; slot++)
            {
                int best = -1; float distance = 18f * 18f;
                for (int i = 0; i < positions.Count; i++)
                {
                    bool used = false;
                    for (int j = 0; j < slot; j++) if (selected[j] == i) { used = true; break; }
                    if (used) continue;
                    float candidate = (positions[i] - previousCamera).sqrMagnitude;
                    if (candidate < distance) { distance = candidate; best = i; }
                }
                selected[slot] = best;
                if (best >= 0 && lights[slot] == null)
                {
                    var host = new GameObject("Citadel torch light " + slot) { hideFlags = HideFlags.DontSave };
                    host.transform.SetParent(owner, false);
                    lights[slot] = host.AddComponent<Light>();
                    lights[slot].type = LightType.Point; lights[slot].shadows = LightShadows.None;
                    lights[slot].color = new Color(1f, .55f, .20f);
                    lights[slot].range = 6f; lights[slot].intensity = 3f;
                }
                if (lights[slot] == null) continue;
                lights[slot].enabled = best >= 0;
                if (best >= 0) { lights[slot].transform.position = positions[best]; ActiveCount++; }
            }
        }
        public void Clear()
        {
            positions.Clear(); dirty = true; ActiveCount = 0;
            foreach (var light in lights) if (light != null) light.enabled = false;
        }
        public void Dispose()
        {
            Clear();
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null)
                {
                    if (Application.isPlaying) Object.Destroy(lights[i].gameObject); else Object.DestroyImmediate(lights[i].gameObject);
                    lights[i] = null;
                }
        }
    }
}
