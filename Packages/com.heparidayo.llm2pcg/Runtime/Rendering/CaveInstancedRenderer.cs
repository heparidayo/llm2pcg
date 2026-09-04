using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Rendering
{
    /// <summary>Continuous cave surface plus bounded GPU-instanced visual details; no generated tile owns a GameObject.</summary>
    [DisallowMultipleComponent]
    public sealed class CaveInstancedRenderer : MonoBehaviour, IPCGWorldRenderer
    {
        private const int MaximumBatchSize = 1023;
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material rockMaterial;
        [SerializeField] private Material wallMaterial;
        [SerializeField, Min(.1f)] private float tileSize = 1f;
        private readonly Material[] surfaceMaterials = new Material[3];
        private readonly InstancedRenderBatchBuilder visualBatches = new InstancedRenderBatchBuilder();
        private readonly List<ResolvedVisualPlacement> visualPlacements = new List<ResolvedVisualPlacement>();
        private readonly List<Material> fallbackMaterials = new List<Material>();
        private readonly List<Matrix4x4[]> voxelFloorBatches = new List<Matrix4x4[]>();
        private readonly List<Matrix4x4[]> voxelRockBatches = new List<Matrix4x4[]>();
        private Mesh cubeMesh;
        private Mesh caveSurfaceMesh;

        public string WorldType => PCGRequest.CaveWorldType;
        public int TotalInstanceCount { get; private set; }
        public int RenderSubmissionCount => CaveSurfaceSubmissionCount + voxelFloorBatches.Count + voxelRockBatches.Count + visualBatches.SubmissionCount;
        public int VisualInstanceCount => visualBatches.InstanceCount;
        public int MaximumVisualSubmissionSize => visualBatches.MaximumSubmissionSize;
        public int CaveSurfaceVertexCount { get; private set; }
        public int CaveSurfaceTriangleCount { get; private set; }
        public int CaveSurfaceBoundaryEdgeCount { get; private set; }
        public int CaveSurfaceSubmissionCount { get; private set; }
        public bool CaveSurfaceUsesAssetMaterials { get; private set; }
        public string CaveSurfaceHash { get; private set; } = "00000000";
        public string VisualLayoutHash { get; private set; } = "00000000";
        public int VoxelGeometryInstanceCount { get; private set; }
        public string PresentationGeometryMode { get; private set; } = PresentationGeometryModes.Surface;
        public bool PresentationPropsEnabled { get; private set; } = true;

        private void Awake()
        {
            cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Material fallbackFloor = CreateFallback(new Color(.24f, .25f, .24f));
            Material fallbackRock = CreateFallback(new Color(.20f, .22f, .24f));
            Material fallbackWall = CreateFallback(new Color(.16f, .18f, .20f));
            surfaceMaterials[CaveSurfaceMeshBuilder.FloorSubmesh] = floorMaterial != null ? floorMaterial : Resources.Load<Material>("PCGSurfaceMaterials/CaveFloor") ?? fallbackFloor;
            surfaceMaterials[CaveSurfaceMeshBuilder.RockSubmesh] = rockMaterial != null ? rockMaterial : Resources.Load<Material>("PCGSurfaceMaterials/CaveRock") ?? fallbackRock;
            surfaceMaterials[CaveSurfaceMeshBuilder.WallSubmesh] = wallMaterial != null ? wallMaterial : Resources.Load<Material>("PCGSurfaceMaterials/CaveWall") ?? fallbackWall;
            CaveSurfaceUsesAssetMaterials = surfaceMaterials[0] != fallbackFloor && surfaceMaterials[1] != fallbackRock && surfaceMaterials[2] != fallbackWall;
        }

        private void OnDestroy()
        {
            DestroyCaveSurface();
            visualBatches.Dispose();
            for (int index = 0; index < fallbackMaterials.Count; index++) if (fallbackMaterials[index] != null) Destroy(fallbackMaterials[index]);
        }

        public void Render(CaveWorldData world)
        {
            Render(world, PCGRequest.DefaultPresentation());
        }

        public void Render(CaveWorldData world, PresentationSettings presentationSettings)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            Clear();
            if (!Mathf.Approximately(tileSize, 1f)) Debug.LogWarning("Cave surface mesh uses unit PCG coordinates; tileSize values other than 1 are retained only for visual detail compatibility.", this);

            PresentationSettings presentation = presentationSettings ?? PCGRequest.DefaultPresentation();
            bool voxelGeometry = string.Equals(presentation.geometryMode, PresentationGeometryModes.Voxel, StringComparison.Ordinal);
            bool showProps = presentation.propsEnabled;
            PresentationGeometryMode = voxelGeometry ? PresentationGeometryModes.Voxel : PresentationGeometryModes.Surface;
            PresentationPropsEnabled = showProps;

            if (voxelGeometry)
                BuildVoxelGeometry(world);
            else
            {
                CaveSurfaceMeshData surface = CaveSurfaceMeshBuilder.BuildData(world);
                caveSurfaceMesh = CaveSurfaceMeshBuilder.CreateMesh(surface);
                CaveSurfaceVertexCount = surface.VertexCount;
                CaveSurfaceTriangleCount = surface.TriangleCount;
                CaveSurfaceBoundaryEdgeCount = surface.BoundaryEdgeCount;
                CaveSurfaceHash = surface.StableHash;
                for (int index = 0; index < surface.SubmeshTriangles.Length; index++) if (surface.SubmeshTriangles[index].Length > 0) CaveSurfaceSubmissionCount++;
            }

            BiomeVisualProfile profile = VisualProfileLoader.Load(PCGRequest.CaveWorldType);
            if (showProps) visualPlacements.AddRange(VisualWorldLayoutBuilder.BuildCaveDetails(world, profile, tileSize));
            for (int index = 0; index < visualPlacements.Count; index++) visualBatches.Add(visualPlacements[index]);
            visualBatches.Build();
            VisualLayoutHash = showProps ? VisualLayoutHasher.Compute(profile, visualPlacements) : "00000000";
            TotalInstanceCount = (caveSurfaceMesh == null ? 0 : 1) + VoxelGeometryInstanceCount + visualBatches.InstanceCount;
        }

        void IPCGWorldRenderer.Render(IPCGWorldData world)
        {
            if (!(world is CaveWorldData cave)) throw new ArgumentException("Cave renderer received incompatible world data.", nameof(world));
            Render(cave);
        }

        public void Clear()
        {
            DestroyCaveSurface();
            visualBatches.Clear();
            visualPlacements.Clear();
            voxelFloorBatches.Clear();
            voxelRockBatches.Clear();
            VisualLayoutHash = "00000000";
            CaveSurfaceHash = "00000000";
            CaveSurfaceVertexCount = 0;
            CaveSurfaceTriangleCount = 0;
            CaveSurfaceBoundaryEdgeCount = 0;
            CaveSurfaceSubmissionCount = 0;
            TotalInstanceCount = 0;
            VoxelGeometryInstanceCount = 0;
            PresentationGeometryMode = PresentationGeometryModes.Surface;
            PresentationPropsEnabled = true;
        }

        private void Update()
        {
            SubmitCaveSurface();
            SubmitVoxelBatches(voxelFloorBatches, surfaceMaterials[CaveSurfaceMeshBuilder.FloorSubmesh]);
            SubmitVoxelBatches(voxelRockBatches, surfaceMaterials[CaveSurfaceMeshBuilder.RockSubmesh]);
            visualBatches.Submit(gameObject.layer);
        }

        private void BuildVoxelGeometry(CaveWorldData world)
        {
            List<Matrix4x4> floors = new List<Matrix4x4>();
            List<Matrix4x4> rocks = new List<Matrix4x4>();
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                if (world.IsWalkable(x, y))
                    floors.Add(Matrix4x4.TRS(new Vector3(x * tileSize, -.09f, y * tileSize), Quaternion.identity, new Vector3(tileSize, .18f, tileSize)));
                else
                    rocks.Add(Matrix4x4.TRS(new Vector3(x * tileSize, 1.2f, y * tileSize), Quaternion.identity, new Vector3(tileSize, 2.4f, tileSize)));
            }
            CreateBatches(floors, voxelFloorBatches);
            CreateBatches(rocks, voxelRockBatches);
            VoxelGeometryInstanceCount = floors.Count + rocks.Count;
        }

        private void SubmitVoxelBatches(List<Matrix4x4[]> batches, Material material)
        {
            if (cubeMesh == null || material == null) return;
            for (int index = 0; index < batches.Count; index++)
                Graphics.DrawMeshInstanced(cubeMesh, 0, material, batches[index], batches[index].Length, null, ShadowCastingMode.On, true, gameObject.layer);
        }

        private static void CreateBatches(List<Matrix4x4> source, List<Matrix4x4[]> destination)
        {
            for (int start = 0; start < source.Count; start += MaximumBatchSize)
            {
                int length = Math.Min(MaximumBatchSize, source.Count - start);
                Matrix4x4[] batch = new Matrix4x4[length];
                source.CopyTo(start, batch, 0, length);
                destination.Add(batch);
            }
        }

        private void SubmitCaveSurface()
        {
            if (caveSurfaceMesh == null) return;
            for (int submesh = 0; submesh < caveSurfaceMesh.subMeshCount; submesh++)
            {
                if (caveSurfaceMesh.GetIndexCount(submesh) == 0 || surfaceMaterials[submesh] == null) continue;
                RenderParams renderParams = new RenderParams(surfaceMaterials[submesh])
                {
                    layer = gameObject.layer,
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true,
                    worldBounds = caveSurfaceMesh.bounds
                };
                Graphics.RenderMesh(renderParams, caveSurfaceMesh, submesh, Matrix4x4.identity);
            }
        }

        private void DestroyCaveSurface()
        {
            if (caveSurfaceMesh == null) return;
            if (Application.isPlaying) Destroy(caveSurfaceMesh); else DestroyImmediate(caveSurfaceMesh);
            caveSurfaceMesh = null;
        }

        private Material CreateFallback(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader) { enableInstancing = true, color = color };
            fallbackMaterials.Add(material);
            return material;
        }
    }
}
