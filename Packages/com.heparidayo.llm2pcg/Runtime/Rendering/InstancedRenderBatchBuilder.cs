using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Llm2Pcg.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Rendering
{
    /// <summary>Stable mesh/submesh/material GPU-instancing batches. Placements are built once and only submitted per frame.</summary>
    public sealed class InstancedRenderBatchBuilder : IDisposable
    {
        public const int MaximumInstancesPerSubmission = 1023;
        private readonly Dictionary<RenderKey, List<Matrix4x4>> pending = new Dictionary<RenderKey, List<Matrix4x4>>();
        private readonly List<Submission> submissions = new List<Submission>();
        private readonly RuntimeInstancedMaterialCache materialCache = new RuntimeInstancedMaterialCache();

        public int InstanceCount { get; private set; }
        public int SubmissionCount => submissions.Count;
        public int MaximumSubmissionSize { get; private set; }
        public long EstimatedVertexCount { get; private set; }

        public void Clear()
        {
            pending.Clear();
            submissions.Clear();
            InstanceCount = 0;
            MaximumSubmissionSize = 0;
            EstimatedVertexCount = 0;
        }

        public void Add(ResolvedVisualPlacement placement)
        {
            Add(placement, 0);
        }

        public void Add(ResolvedVisualPlacement placement, int lodIndex)
        {
            VisualRenderPart[] parts = placement.Variant?.GetLodParts(lodIndex);
            if (parts == null) return;
            for (int index = 0; index < parts.Length; index++)
            {
                VisualRenderPart part = parts[index];
                if (part == null) continue;
                Add(part.mesh, part.subMeshIndex, part.material, placement.WorldMatrix * part.LocalMatrix, part.stableId);
            }
        }

        public void Add(Mesh mesh, int subMeshIndex, Material material, Matrix4x4 matrix, string stablePartId)
        {
            if (mesh == null || material == null || subMeshIndex < 0 || subMeshIndex >= mesh.subMeshCount) return;
            RenderKey key = new RenderKey(mesh, subMeshIndex, material, stablePartId ?? string.Empty);
            if (!pending.TryGetValue(key, out List<Matrix4x4> matrices))
            {
                matrices = new List<Matrix4x4>();
                pending.Add(key, matrices);
            }
            matrices.Add(matrix);
            InstanceCount++;
            EstimatedVertexCount += mesh.vertexCount;
        }

        public void Build()
        {
            submissions.Clear();
            MaximumSubmissionSize = 0;
            List<RenderKey> keys = new List<RenderKey>(pending.Keys);
            keys.Sort((left, right) => left.CompareTo(right));
            for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
            {
                RenderKey key = keys[keyIndex];
                List<Matrix4x4> matrices = pending[key];
                Material material = materialCache.Get(key.Material);
                for (int start = 0; start < matrices.Count; start += MaximumInstancesPerSubmission)
                {
                    int count = Math.Min(MaximumInstancesPerSubmission, matrices.Count - start);
                    InstanceData[] data = new InstanceData[count];
                    for (int index = 0; index < count; index++) data[index].objectToWorld = matrices[start + index];
                    submissions.Add(new Submission(key.Mesh, key.SubMeshIndex, material, data));
                    MaximumSubmissionSize = Math.Max(MaximumSubmissionSize, count);
                }
            }
        }

        public void Submit(int layer)
        {
            for (int index = 0; index < submissions.Count; index++)
            {
                Submission submission = submissions[index];
                if (submission.Mesh == null || submission.Material == null || submission.Instances.Length == 0) continue;
                RenderParams renderParams = new RenderParams(submission.Material)
                {
                    layer = layer,
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true
                };
                Graphics.RenderMeshInstanced(renderParams, submission.Mesh, submission.SubMeshIndex, submission.Instances);
            }
        }

        public void Dispose() => materialCache.Dispose();

        private readonly struct RenderKey : IComparable<RenderKey>
        {
            public readonly Mesh Mesh;
            public readonly int SubMeshIndex;
            public readonly Material Material;
            private readonly string stableId;
            private readonly string meshName;
            private readonly string materialName;

            public RenderKey(Mesh mesh, int subMeshIndex, Material material, string stableId)
            {
                Mesh = mesh;
                SubMeshIndex = subMeshIndex;
                Material = material;
                this.stableId = stableId;
                meshName = mesh == null ? string.Empty : mesh.name;
                materialName = material == null ? string.Empty : material.name;
            }

            public int CompareTo(RenderKey other)
            {
                int value = string.CompareOrdinal(stableId, other.stableId); if (value != 0) return value;
                value = string.CompareOrdinal(meshName, other.meshName); if (value != 0) return value;
                value = SubMeshIndex.CompareTo(other.SubMeshIndex); if (value != 0) return value;
                return string.CompareOrdinal(materialName, other.materialName);
            }

            public override bool Equals(object obj)
            {
                return obj is RenderKey other && Mesh == other.Mesh && SubMeshIndex == other.SubMeshIndex && Material == other.Material;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Mesh == null ? 0 : RuntimeHelpers.GetHashCode(Mesh);
                    hash = hash * 397 ^ SubMeshIndex;
                    hash = hash * 397 ^ (Material == null ? 0 : RuntimeHelpers.GetHashCode(Material));
                    return hash;
                }
            }
        }

        private readonly struct Submission
        {
            public readonly Mesh Mesh;
            public readonly int SubMeshIndex;
            public readonly Material Material;
            public readonly InstanceData[] Instances;
            public Submission(Mesh mesh, int subMeshIndex, Material material, InstanceData[] instances) { Mesh = mesh; SubMeshIndex = subMeshIndex; Material = material; Instances = instances; }
        }

        private struct InstanceData { public Matrix4x4 objectToWorld; }
    }

    internal sealed class RuntimeInstancedMaterialCache : IDisposable
    {
        private readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();

        public Material Get(Material source)
        {
            if (source == null) return null;
            bool requiresPcgOverride = PcgFoliageWindPolicy.RequiresOverride(source);
            if (source.enableInstancing && !requiresPcgOverride) return source;
            if (materials.TryGetValue(source, out Material copy) && copy != null) return copy;
            copy = new Material(source) { name = source.name + " (PCG Instanced)", enableInstancing = true, hideFlags = HideFlags.DontSave };
            if (requiresPcgOverride) PcgFoliageWindPolicy.Apply(copy);
            materials[source] = copy;
            return copy;
        }

        public void Dispose()
        {
            foreach (KeyValuePair<Material, Material> entry in materials)
                if (entry.Value != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(entry.Value);
                    else UnityEngine.Object.DestroyImmediate(entry.Value);
                }
            materials.Clear();
        }
    }

    /// <summary>
    /// Keeps third-party animated foliage readable when hundreds of instances are visible at once.
    /// The policy is applied only to runtime material copies, so imported vendor materials remain untouched.
    /// </summary>
    public static class PcgFoliageWindPolicy
    {
        public const string IdyllicVegetationShaderName = "Idyllic Fantasy Nature/Vegetation";
        public const float StableWindStrength = 0.045f;
        public const float StableWindSpeed = 0.22f;

        private static readonly int WindStrengthId = Shader.PropertyToID("_Wind_Strength");
        private static readonly int WindSpeedId = Shader.PropertyToID("_Wind_Speed");

        public static bool RequiresOverride(Material material)
        {
            return material != null && material.shader != null &&
                   string.Equals(material.shader.name, IdyllicVegetationShaderName, StringComparison.Ordinal);
        }

        public static bool Apply(Material material)
        {
            if (!RequiresOverride(material)) return false;
            if (material.HasProperty(WindStrengthId)) material.SetFloat(WindStrengthId, StableWindStrength);
            if (material.HasProperty(WindSpeedId)) material.SetFloat(WindSpeedId, StableWindSpeed);
            return true;
        }
    }
}
