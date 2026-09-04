using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Visuals;
using Llm2Pcg.Generators.Nature;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Rendering
{
    /// <summary>Shared Forest/City GPU-instancing renderer. No generated tile owns a GameObject.</summary>
    [DisallowMultipleComponent]
    public sealed class BiomeInstancedRenderer : MonoBehaviour, IPCGWorldRenderer
    {
        private const int MaximumBatchSize = 1023;
        private const int GroundBatch = 0, WaterBatch = 1, PathBatch = 2, RoadBatch = 3, StructureBatch = 4, GreenBatch = 5;
        private readonly List<Matrix4x4[]>[] batches = { new List<Matrix4x4[]>(), new List<Matrix4x4[]>(), new List<Matrix4x4[]>(), new List<Matrix4x4[]>(), new List<Matrix4x4[]>(), new List<Matrix4x4[]>() };
        private readonly Material[] materials = new Material[6];
        private readonly InstancedRenderBatchBuilder visualBatches = new InstancedRenderBatchBuilder();
        private readonly List<ResolvedVisualPlacement> visualPlacements = new List<ResolvedVisualPlacement>();
        private readonly int[] lodPlacementCounts = new int[8];
        private readonly Material[] forestSurfaceMaterials = new Material[4];
        private readonly Material[] citySurfaceMaterials = new Material[5];
        private readonly Dictionary<string, Material[]> natureSurfaceMaterialSets = new Dictionary<string, Material[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> natureSurfaceAssetUsage = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly List<Material> ownedNatureSurfaceMaterials = new List<Material>();
        private int[] currentVisualLods = Array.Empty<int>();
        private Vector3 lastLodCameraPosition;
        private float lastLodFieldOfView = -1f;
        private float lastLodOrthographicSize = -1f;
        private bool lastLodOrthographic;
        private bool hasLodCameraState;
        private Mesh cube;
        private Mesh forestSurfaceMesh;
        private Mesh citySurfaceMesh;
        public string WorldType => "Biome";
        public int TotalInstanceCount { get; private set; }
        public int RenderSubmissionCount { get { int count = visualBatches.SubmissionCount + ForestSurfaceSubmissionCount + CitySurfaceSubmissionCount; for (int i = 0; i < batches.Length; i++) count += batches[i].Count; return count; } }
        public int VisualInstanceCount => visualBatches.InstanceCount;
        public int MaximumVisualSubmissionSize => visualBatches.MaximumSubmissionSize;
        public int VisualPlacementCount => visualPlacements.Count;
        public int PrimitiveInstanceCount { get; private set; }
        public long EstimatedVisualVertexCount => visualBatches.EstimatedVertexCount;
        public long Lod0EstimatedVisualVertexCount { get; private set; }
        public int LodRebuildCount { get; private set; }
        public int ForestSurfaceVertexCount { get; private set; }
        public int ForestSurfaceTriangleCount { get; private set; }
        public int ForestSurfaceSubmissionCount { get; private set; }
        public bool ForestSurfaceUsesAssetMaterials { get; private set; }
        public string ForestSurfaceHash { get; private set; } = "00000000";
        public int CitySurfaceVertexCount { get; private set; }
        public int CitySurfaceTriangleCount { get; private set; }
        public int CitySurfaceElevationEdgeCount { get; private set; }
        public int CitySurfaceRoadMarkingCount { get; private set; }
        public int CitySurfaceSubmissionCount { get; private set; }
        public bool CitySurfaceUsesAssetMaterials { get; private set; }
        public string CitySurfaceHash { get; private set; } = "00000000";
        public string VisualLayoutHash { get; private set; } = "00000000";
        public string PresentationGeometryMode { get; private set; } = PresentationGeometryModes.Surface;
        public bool PresentationPropsEnabled { get; private set; } = true;

        public int GetLodPlacementCount(int lodIndex) => lodIndex >= 0 && lodIndex < lodPlacementCounts.Length ? lodPlacementCounts[lodIndex] : 0;

        private void Awake()
        {
            cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Color[] colors = { new Color(.18f, .36f, .16f), new Color(.04f, .20f, .36f), new Color(.56f, .45f, .25f), new Color(.16f, .16f, .17f), new Color(.38f, .31f, .23f), new Color(.22f, .48f, .20f) };
            for (int i = 0; i < colors.Length; i++) { Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"); materials[i] = new Material(shader) { enableInstancing = true, color = colors[i] }; }
            citySurfaceMaterials[CitySurfaceMeshBuilder.SidewalkSubmesh] = Resources.Load<Material>("PCGSurfaceMaterials/CitySidewalk") ?? materials[GroundBatch];
            citySurfaceMaterials[CitySurfaceMeshBuilder.RoadSubmesh] = Resources.Load<Material>("PCGSurfaceMaterials/CityRoad") ?? materials[RoadBatch];
            citySurfaceMaterials[CitySurfaceMeshBuilder.ParkSubmesh] = Resources.Load<Material>("PCGSurfaceMaterials/CityPark") ?? materials[GreenBatch];
            citySurfaceMaterials[CitySurfaceMeshBuilder.CurbSubmesh] = Resources.Load<Material>("PCGSurfaceMaterials/CityCurb") ?? materials[GroundBatch];
            citySurfaceMaterials[CitySurfaceMeshBuilder.RoadMarkingSubmesh] = Resources.Load<Material>("PCGSurfaceMaterials/CityRoadMarking") ?? materials[PathBatch];
            CitySurfaceUsesAssetMaterials = true;
            for (int index = 0; index < citySurfaceMaterials.Length; index++) if (citySurfaceMaterials[index] == materials[GroundBatch] || citySurfaceMaterials[index] == materials[RoadBatch] || citySurfaceMaterials[index] == materials[GreenBatch] || citySurfaceMaterials[index] == materials[PathBatch]) CitySurfaceUsesAssetMaterials = false;
        }

        private void OnDestroy() { DestroyForestSurface(); DestroyCitySurface(); visualBatches.Dispose(); for (int i = 0; i < materials.Length; i++) if (materials[i] != null) Destroy(materials[i]); for (int i = 0; i < ownedNatureSurfaceMaterials.Count; i++) if (ownedNatureSurfaceMaterials[i] != null) Destroy(ownedNatureSurfaceMaterials[i]); }

        public void Render(BiomeWorldData world)
        {
            Render(world, null, PCGRequest.DefaultPresentation());
        }

        public void Render(BiomeWorldData world, VisualSettings visualSettings)
        {
            Render(world, visualSettings, PCGRequest.DefaultPresentation());
        }

        public void Render(BiomeWorldData world, VisualSettings visualSettings, PresentationSettings presentationSettings)
        {
            Clear();
            PresentationSettings presentation = presentationSettings ?? PCGRequest.DefaultPresentation();
            bool voxelGeometry = string.Equals(presentation.geometryMode, PresentationGeometryModes.Voxel, StringComparison.Ordinal);
            bool showProps = presentation.propsEnabled;
            PresentationGeometryMode = voxelGeometry ? PresentationGeometryModes.Voxel : PresentationGeometryModes.Surface;
            PresentationPropsEnabled = showProps;
            BiomeVisualProfile profile = VisualProfileLoader.Load(world.WorldType);
            bool useForestSurface = !voxelGeometry && NatureWorldTypes.IsForestFamily(world.WorldType);
            bool useCitySurface = !voxelGeometry && world.WorldType == PCGRequest.CityWorldType;
            if (useForestSurface)
            {
                string waterCategory = VisualWorldLayoutBuilder.NatureWaterCategory(world.WorldType);
                ConfigureNatureSurfaceMaterials(world.WorldType, ResolveWaterSurfaceMaterial(profile, waterCategory));
                ForestSurfaceMeshData surface = ForestSurfaceMeshBuilder.BuildData(world);
                forestSurfaceMesh = ForestSurfaceMeshBuilder.CreateMesh(surface);
                ForestSurfaceVertexCount = surface.VertexCount;
                ForestSurfaceTriangleCount = surface.TriangleCount;
                ForestSurfaceHash = surface.StableHash;
                for (int index = 0; index < surface.SubmeshTriangles.Length; index++) if (surface.SubmeshTriangles[index].Length > 0) ForestSurfaceSubmissionCount++;
            }
            if (useCitySurface)
            {
                CitySurfaceMeshData surface = CitySurfaceMeshBuilder.BuildData(world);
                citySurfaceMesh = CitySurfaceMeshBuilder.CreateMesh(surface);
                CitySurfaceVertexCount = surface.VertexCount;
                CitySurfaceTriangleCount = surface.TriangleCount;
                CitySurfaceElevationEdgeCount = surface.ElevationEdgeCount;
                CitySurfaceRoadMarkingCount = surface.RoadMarkingSegmentCount;
                CitySurfaceHash = surface.StableHash;
                for (int index = 0; index < surface.SubmeshTriangles.Length; index++) if (surface.SubmeshTriangles[index].Length > 0) CitySurfaceSubmissionCount++;
            }
            List<Matrix4x4>[] all = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                int index = y * world.Width + x;
                BiomeTile tile = world.Tiles[index];
                float elevation = world.Elevations[index];
                if (useForestSurface || useCitySurface) continue;
                if (tile == BiomeTile.Building)
                {
                    if (world.WorldType == PCGRequest.CityWorldType && showProps) continue;
                    float structureHeight = Math.Max(1f, world.StructureHeights[index]);
                    all[(int)tile].Add(Matrix4x4.TRS(new Vector3(x, elevation + structureHeight * .5f, y), Quaternion.identity, new Vector3(.9f, structureHeight, .9f)));
                }
                else
                {
                    float columnHeight = tile == BiomeTile.Water ? .16f : Math.Max(.2f, elevation + .2f);
                    float centerY = tile == BiomeTile.Water ? 0f : (elevation - .2f) * .5f;
                    all[(int)tile].Add(Matrix4x4.TRS(new Vector3(x, centerY, y), Quaternion.identity, new Vector3(1f, columnHeight, 1f)));
                }
            }
            if (showProps && world.WorldType == PCGRequest.ForestWorldType)
            {
                bool forestCatalogAvailable = VisualVariantResolver.GetSortedValidVariants(profile == null ? null : profile.FindCategory(VisualCategoryIds.ForestTrees)).Count > 0;
                visualPlacements.AddRange(VisualWorldLayoutBuilder.BuildForestTrees(world, profile, visualSettings));
                visualPlacements.AddRange(VisualWorldLayoutBuilder.BuildForestDecorations(world, profile, visualSettings));
                if (!forestCatalogAvailable)
                {
                    for (int i = 0; i < world.Props.Count; i++)
                    {
                        Int2 tree = world.Props[i]; float baseHeight = world.GetSurfaceHeight(tree.X, tree.Y); float height = 1.7f + StableHeight(world.Seed, tree.X, tree.Y) * .22f;
                        all[StructureBatch].Add(Matrix4x4.TRS(new Vector3(tree.X, baseHeight + height * .5f, tree.Y), Quaternion.identity, new Vector3(.28f, height, .28f)));
                        all[GreenBatch].Add(Matrix4x4.TRS(new Vector3(tree.X, baseHeight + height + .4f, tree.Y), Quaternion.identity, new Vector3(1.25f, .9f, 1.25f)));
                    }
                }
            }
            else if (showProps && NatureWorldTypes.IsNature(world.WorldType))
            {
                string primaryCategory = VisualWorldLayoutBuilder.NaturePrimaryCategory(world.WorldType);
                bool catalogAvailable = VisualVariantResolver.GetSortedValidVariants(profile == null ? null : profile.FindCategory(primaryCategory)).Count > 0;
                visualPlacements.AddRange(VisualWorldLayoutBuilder.BuildNaturePrimaryVegetation(world, profile, visualSettings));
                visualPlacements.AddRange(VisualWorldLayoutBuilder.BuildNatureDecorations(world, profile, visualSettings));
                if (!catalogAvailable) AddNaturePrimitiveFallback(world, all);
            }
            else if (showProps && world.WorldType == PCGRequest.CityWorldType)
            {
                List<CityStructurePlacement> structures = CityStructureExtractor.Extract(world);
                visualPlacements.AddRange(VisualWorldLayoutBuilder.BuildCityVisuals(world, profile, structures));
                HashSet<int> resolvedCenters = new HashSet<int>();
                for (int index = 0; index < visualPlacements.Count; index++)
                    if (visualPlacements[index].CategoryId.StartsWith("City/Buildings/", StringComparison.Ordinal)) resolvedCenters.Add(visualPlacements[index].Y * world.Width + visualPlacements[index].X);
                for (int index = 0; index < structures.Count; index++)
                {
                    CityStructurePlacement structure = structures[index];
                    if (resolvedCenters.Contains(structure.Center.Y * world.Width + structure.Center.X)) continue;
                    float centerX = structure.Footprint.X + (structure.Footprint.Width - 1) * .5f;
                    float centerZ = structure.Footprint.Y + (structure.Footprint.Height - 1) * .5f;
                    float elevation = world.GetSurfaceHeight(structure.Center.X, structure.Center.Y);
                    all[StructureBatch].Add(Matrix4x4.TRS(new Vector3(centerX, elevation + structure.Height * .5f, centerZ), Quaternion.identity, new Vector3(structure.Footprint.Width * .9f, structure.Height, structure.Footprint.Height * .9f)));
                }
            }
            VisualLayoutHash = showProps ? VisualLayoutHasher.Compute(profile, visualPlacements) : "00000000";
            for (int kind = 0; kind < all.Length; kind++) CreateBatches(all[kind], batches[kind]);
            PrimitiveInstanceCount = 0;
            for (int kind = 0; kind < all.Length; kind++) PrimitiveInstanceCount += all[kind].Count;
            if (forestSurfaceMesh != null) PrimitiveInstanceCount++;
            if (citySurfaceMesh != null) PrimitiveInstanceCount++;
            Lod0EstimatedVisualVertexCount = EstimateLodZeroVertices();
            currentVisualLods = new int[visualPlacements.Count];
            for (int index = 0; index < currentVisualLods.Length; index++) currentVisualLods[index] = -1;
            RefreshVisualLod(Camera.main, true);
        }

        void IPCGWorldRenderer.Render(IPCGWorldData world) { if (!(world is BiomeWorldData biome)) throw new ArgumentException("Biome renderer received incompatible world data.", nameof(world)); Render(biome); }
        public void Clear()
        {
            for (int i = 0; i < batches.Length; i++) batches[i].Clear();
            DestroyForestSurface();
            DestroyCitySurface();
            visualBatches.Clear(); visualPlacements.Clear();
            Array.Clear(lodPlacementCounts, 0, lodPlacementCounts.Length);
            currentVisualLods = Array.Empty<int>();
            VisualLayoutHash = "00000000"; TotalInstanceCount = 0; PrimitiveInstanceCount = 0;
            Lod0EstimatedVisualVertexCount = 0; LodRebuildCount = 0; hasLodCameraState = false;
            ForestSurfaceVertexCount = 0; ForestSurfaceTriangleCount = 0; ForestSurfaceSubmissionCount = 0; ForestSurfaceHash = "00000000";
            CitySurfaceVertexCount = 0; CitySurfaceTriangleCount = 0; CitySurfaceElevationEdgeCount = 0; CitySurfaceRoadMarkingCount = 0; CitySurfaceSubmissionCount = 0; CitySurfaceHash = "00000000";
            PresentationGeometryMode = PresentationGeometryModes.Surface; PresentationPropsEnabled = true;
        }

        /// <summary>Rebuilds only when a camera move actually changes at least one selected LOD tier.</summary>
        public bool RefreshVisualLod(Camera camera, bool force = false)
        {
            if (!force && camera != null && hasLodCameraState &&
                (camera.transform.position - lastLodCameraPosition).sqrMagnitude < .25f &&
                camera.orthographic == lastLodOrthographic &&
                Mathf.Approximately(camera.fieldOfView, lastLodFieldOfView) &&
                Mathf.Approximately(camera.orthographicSize, lastLodOrthographicSize)) return false;

            if (camera != null)
            {
                lastLodCameraPosition = camera.transform.position;
                lastLodFieldOfView = camera.fieldOfView;
                lastLodOrthographicSize = camera.orthographicSize;
                lastLodOrthographic = camera.orthographic;
                hasLodCameraState = true;
            }

            bool changed = force || currentVisualLods.Length != visualPlacements.Count;
            if (currentVisualLods.Length != visualPlacements.Count) currentVisualLods = new int[visualPlacements.Count];
            for (int index = 0; index < visualPlacements.Count; index++)
            {
                int selected = camera == null ? 0 : VisualLodSelector.SelectLodIndex(visualPlacements[index], camera);
                if (currentVisualLods[index] != selected) { currentVisualLods[index] = selected; changed = true; }
            }
            if (!changed) return false;

            visualBatches.Clear();
            Array.Clear(lodPlacementCounts, 0, lodPlacementCounts.Length);
            for (int index = 0; index < visualPlacements.Count; index++)
            {
                int lod = currentVisualLods[index];
                visualBatches.Add(visualPlacements[index], lod);
                if (lod >= 0 && lod < lodPlacementCounts.Length) lodPlacementCounts[lod]++;
            }
            visualBatches.Build();
            TotalInstanceCount = PrimitiveInstanceCount + visualBatches.InstanceCount;
            LodRebuildCount++;
            return true;
        }

        private long EstimateLodZeroVertices()
        {
            long total = 0;
            for (int placementIndex = 0; placementIndex < visualPlacements.Count; placementIndex++)
            {
                VisualRenderPart[] parts = visualPlacements[placementIndex].Variant?.GetLodParts(0);
                if (parts == null) continue;
                for (int partIndex = 0; partIndex < parts.Length; partIndex++)
                    if (parts[partIndex]?.mesh != null) total += parts[partIndex].mesh.vertexCount;
            }
            return total;
        }

        private void Update()
        {
            if (cube == null) return;
            RefreshVisualLod(Camera.main);
            SubmitForestSurface();
            SubmitCitySurface();
            for (int kind = 0; kind < batches.Length; kind++) for (int i = 0; i < batches[kind].Count; i++) Graphics.DrawMeshInstanced(cube, 0, materials[kind], batches[kind][i], batches[kind][i].Length, null, ShadowCastingMode.On, true, gameObject.layer);
            visualBatches.Submit(gameObject.layer);
        }

        private void SubmitForestSurface()
        {
            if (forestSurfaceMesh == null) return;
            for (int submesh = 0; submesh < forestSurfaceMesh.subMeshCount; submesh++)
            {
                if (forestSurfaceMesh.GetIndexCount(submesh) == 0) continue;
                bool isWaterSurface = submesh == ForestSurfaceMeshBuilder.WaterSurfaceSubmesh;
                RenderParams renderParams = new RenderParams(forestSurfaceMaterials[submesh])
                {
                    layer = gameObject.layer,
                    shadowCastingMode = isWaterSurface ? ShadowCastingMode.Off : ShadowCastingMode.On,
                    receiveShadows = !isWaterSurface,
                    worldBounds = forestSurfaceMesh.bounds
                };
                Graphics.RenderMesh(renderParams, forestSurfaceMesh, submesh, Matrix4x4.identity);
            }
        }

        private void DestroyForestSurface()
        {
            if (forestSurfaceMesh == null) return;
            if (Application.isPlaying) Destroy(forestSurfaceMesh); else DestroyImmediate(forestSurfaceMesh);
            forestSurfaceMesh = null;
        }

        private void SubmitCitySurface()
        {
            if (citySurfaceMesh == null) return;
            for (int submesh = 0; submesh < citySurfaceMesh.subMeshCount; submesh++)
            {
                if (citySurfaceMesh.GetIndexCount(submesh) == 0) continue;
                RenderParams renderParams = new RenderParams(citySurfaceMaterials[submesh])
                {
                    layer = gameObject.layer,
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true,
                    worldBounds = citySurfaceMesh.bounds
                };
                Graphics.RenderMesh(renderParams, citySurfaceMesh, submesh, Matrix4x4.identity);
            }
        }

        private void DestroyCitySurface()
        {
            if (citySurfaceMesh == null) return;
            if (Application.isPlaying) Destroy(citySurfaceMesh); else DestroyImmediate(citySurfaceMesh);
            citySurfaceMesh = null;
        }
        private void AddNaturePrimitiveFallback(BiomeWorldData world, List<Matrix4x4>[] all)
        {
            for (int i = 0; i < world.Props.Count; i++)
            {
                Int2 point = world.Props[i];
                float ground = world.GetSurfaceHeight(point.X, point.Y);
                float variation = StableHeight(world.Seed, point.X, point.Y);
                if (world.WorldType == PCGRequest.DesertWorldType)
                {
                    float height = 1.1f + variation * .18f;
                    all[GreenBatch].Add(Matrix4x4.TRS(new Vector3(point.X, ground + height * .5f, point.Y), Quaternion.identity, new Vector3(.28f, height, .28f)));
                    if ((i & 1) == 0) all[GreenBatch].Add(Matrix4x4.TRS(new Vector3(point.X + .22f, ground + height * .55f, point.Y), Quaternion.identity, new Vector3(.42f, .18f, .2f)));
                }
                else
                {
                    float height = world.WorldType == PCGRequest.SnowfieldWorldType ? 1.45f + variation * .18f : 1.65f + variation * .2f;
                    all[StructureBatch].Add(Matrix4x4.TRS(new Vector3(point.X, ground + height * .45f, point.Y), Quaternion.identity, new Vector3(.24f, height * .9f, .24f)));
                    all[GreenBatch].Add(Matrix4x4.TRS(new Vector3(point.X, ground + height, point.Y), Quaternion.identity, new Vector3(1.05f, world.WorldType == PCGRequest.SnowfieldWorldType ? .65f : .82f, 1.05f)));
                }
            }
        }

        private void ConfigureNatureSurfaceMaterials(string worldType, Material waterSurfaceMaterial)
        {
            if (!natureSurfaceMaterialSets.TryGetValue(worldType, out Material[] selected))
            {
                selected = CreateNatureSurfaceMaterials(worldType, waterSurfaceMaterial, out bool usesAssets);
                natureSurfaceMaterialSets.Add(worldType, selected);
                natureSurfaceAssetUsage.Add(worldType, usesAssets);
            }
            for (int i = 0; i < forestSurfaceMaterials.Length; i++) forestSurfaceMaterials[i] = selected[i];
            ForestSurfaceUsesAssetMaterials = natureSurfaceAssetUsage[worldType];
        }

        private Material[] CreateNatureSurfaceMaterials(string worldType, Material waterSurfaceMaterial, out bool usesAssets)
        {
            Color ground = new Color(.18f, .36f, .16f), path = new Color(.56f, .45f, .25f), underwater = new Color(.13f, .25f, .13f), water = new Color(.04f, .20f, .36f);
            if (worldType == PCGRequest.SwampWorldType) { ground = new Color(.18f,.27f,.12f); path = new Color(.36f,.28f,.16f); underwater = new Color(.11f,.16f,.08f); water = new Color(.08f,.22f,.20f); }
            else if (worldType == PCGRequest.SnowfieldWorldType) { ground = new Color(.78f,.88f,.93f); path = new Color(.55f,.65f,.70f); underwater = new Color(.30f,.55f,.70f); water = new Color(.48f,.74f,.88f); }
            else if (worldType == PCGRequest.DesertWorldType) { ground = new Color(.72f,.54f,.25f); path = new Color(.50f,.33f,.15f); underwater = new Color(.43f,.31f,.15f); water = new Color(.08f,.35f,.48f); }
            Material[] result = new Material[4];
            string[] suffixes = { "Ground", "Path", "Underwater", "Water" };
            Color[] colors = { ground, path, underwater, water };
            usesAssets = true;
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Resources.Load<Material>("PCGSurfaceMaterials/" + worldType + suffixes[i]);
                if (result[i] != null) continue;
                if (i == ForestSurfaceMeshBuilder.WaterSurfaceSubmesh && waterSurfaceMaterial != null)
                {
                    result[i] = new Material(waterSurfaceMaterial) { name = "PCG " + worldType + " Connected Water" };
                    ownedNatureSurfaceMaterials.Add(result[i]);
                    continue;
                }
                usesAssets = false;
                if (worldType == PCGRequest.ForestWorldType) result[i] = i == ForestSurfaceMeshBuilder.PathSubmesh ? materials[PathBatch] : i == ForestSurfaceMeshBuilder.WaterSurfaceSubmesh ? materials[WaterBatch] : materials[GroundBatch];
                else
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    result[i] = new Material(shader) { enableInstancing = true, color = colors[i] };
                    ownedNatureSurfaceMaterials.Add(result[i]);
                }
            }
            return result;
        }

        private static Material ResolveWaterSurfaceMaterial(BiomeVisualProfile profile, string categoryId)
        {
            List<VisualVariant> variants = VisualVariantResolver.GetSortedValidVariants(profile == null ? null : profile.FindCategory(categoryId));
            for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
            {
                VisualRenderPart[] parts = variants[variantIndex].GetLodParts(0);
                for (int partIndex = 0; partIndex < parts.Length; partIndex++)
                    if (parts[partIndex]?.material != null) return parts[partIndex].material;
            }
            return null;
        }
        private static void CreateBatches(List<Matrix4x4> source, List<Matrix4x4[]> destination) { for (int i = 0; i < source.Count; i += MaximumBatchSize) { int count = Math.Min(MaximumBatchSize, source.Count - i); Matrix4x4[] batch = new Matrix4x4[count]; source.CopyTo(i, batch, 0, count); destination.Add(batch); } }
        private static float StableHeight(int seed, int x, int y) { unchecked { uint value = (uint)(seed + x * 73856093 + y * 19349663); value ^= value >> 13; value *= 1274126177u; return (value % 5) * .55f; } }
    }
}
