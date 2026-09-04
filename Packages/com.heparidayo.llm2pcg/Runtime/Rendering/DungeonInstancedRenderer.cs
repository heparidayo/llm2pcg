using System;
using System.Collections.Generic;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Rendering
{
    /// <summary>Renders generated data with GPU instancing. It never creates a GameObject per dungeon tile.</summary>
    [DisallowMultipleComponent]
    public sealed class DungeonInstancedRenderer : MonoBehaviour, IPCGWorldRenderer
    {
        private const int MaximumInstancesPerSubmission = 1023;

        [SerializeField, Min(0.1f)] private float tileSize = 1f;
        [SerializeField, Min(0.1f)] private float wallHeight = 2.6f;
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material corridorMaterial;
        [SerializeField] private Material wallMaterial;
        [SerializeField] private Material trimMaterial;
        [SerializeField] private Material doorwayMaterial;
        [SerializeField] private Material startMaterial;
        [SerializeField] private Material exitMaterial;
        [SerializeField] private Material bossMaterial;
        [SerializeField] private Material treasureMaterial;
        [SerializeField] private Material shopMaterial;
        [SerializeField] private Material secretMaterial;
        [SerializeField] private Material pillarMaterial;
        [SerializeField] private Material crateMaterial;
        [SerializeField] private Material crystalMaterial;
        [SerializeField] private Material torchMaterial;

        private Mesh cubeMesh;
        private Mesh pillarMesh;
        private Mesh crateMesh;
        private Mesh crystalMesh;
        private Mesh torchMesh;
        private Mesh dungeonSurfaceMesh;
        private DungeonSurfaceMeshData dungeonSurfaceData;
        private readonly List<InstanceData[]> floorBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> corridorBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> wallBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> startBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> exitBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> bossBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> treasureBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> shopBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> secretBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> pillarBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> crateBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> crystalBatches = new List<InstanceData[]>();
        private readonly List<InstanceData[]> torchBatches = new List<InstanceData[]>();
        private readonly InstancedRenderBatchBuilder visualBatches = new InstancedRenderBatchBuilder();
        private readonly List<ResolvedVisualPlacement> visualPlacements = new List<ResolvedVisualPlacement>();
        private int fallbackPropInstanceCount;

        public int FloorInstanceCount { get; private set; }
        public int CorridorInstanceCount { get; private set; }
        public int WallInstanceCount { get; private set; }
        public int ExteriorWallInstanceCount { get; private set; }
        public int StartMarkerCount { get; private set; }
        public int ExitMarkerCount { get; private set; }
        public int BossMarkerCount { get; private set; }
        public int TreasureMarkerCount { get; private set; }
        public int ShopMarkerCount { get; private set; }
        public int SecretMarkerCount { get; private set; }
        public int PillarPropCount { get; private set; }
        public int CratePropCount { get; private set; }
        public int CrystalPropCount { get; private set; }
        public int TorchPropCount { get; private set; }
        public int PropInstanceCount => PillarPropCount + CratePropCount + CrystalPropCount + TorchPropCount;
        public int TotalInstanceCount => (dungeonSurfaceMesh == null ? 0 : 1) + VoxelGeometryInstanceCount + StartMarkerCount + ExitMarkerCount + BossMarkerCount + TreasureMarkerCount + ShopMarkerCount + SecretMarkerCount + fallbackPropInstanceCount + visualBatches.InstanceCount;
        public int RenderSubmissionCount => DungeonSurfaceSubmissionCount + floorBatches.Count + corridorBatches.Count + wallBatches.Count + startBatches.Count + exitBatches.Count + bossBatches.Count + treasureBatches.Count + shopBatches.Count + secretBatches.Count + pillarBatches.Count + crateBatches.Count + crystalBatches.Count + torchBatches.Count + visualBatches.SubmissionCount;
        public int VisualInstanceCount => visualBatches.InstanceCount;
        public int DungeonArchitectureInstanceCount { get; private set; }
        public int VoxelGeometryInstanceCount { get; private set; }
        public string PresentationGeometryMode { get; private set; } = PresentationGeometryModes.Surface;
        public bool PresentationPropsEnabled { get; private set; } = true;
        public int MaximumVisualSubmissionSize => visualBatches.MaximumSubmissionSize;
        public int DungeonSurfaceVertexCount { get; private set; }
        public int DungeonSurfaceTriangleCount { get; private set; }
        public int DungeonSurfaceBoundaryEdgeCount { get; private set; }
        public int DungeonSurfaceDoorwayCount { get; private set; }
        public int DungeonSurfaceCornerCount { get; private set; }
        public int DungeonSurfaceSubmissionCount { get; private set; }
        public bool DungeonSurfaceUsesAssetMaterials { get; private set; }
        public string DungeonSurfaceHash { get; private set; } = "00000000";
        public string VisualLayoutHash { get; private set; } = "00000000";
        public string WorldType => Contract.PCGRequest.DungeonWorldType;

        void IPCGWorldRenderer.Render(IPCGWorldData world)
        {
            if (!(world is DungeonWorldData dungeon)) throw new ArgumentException("Dungeon renderer received incompatible world data.", nameof(world));
            Render(dungeon);
        }

        private void Awake()
        {
            cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            pillarMesh = LoadPrefabMesh("PCGProps/Pillar");
            crateMesh = LoadPrefabMesh("PCGProps/Crate");
            crystalMesh = LoadPrefabMesh("PCGProps/Crystal");
            torchMesh = LoadPrefabMesh("PCGProps/Torch");
            LoadSurfaceMaterials();
            CreateFallbackMaterialsIfNeeded();
        }

        private void OnDestroy()
        {
            visualBatches.Dispose();
            DestroyDungeonSurface();
            DestroyFallbackMaterial(floorMaterial);
            DestroyFallbackMaterial(corridorMaterial);
            DestroyFallbackMaterial(wallMaterial);
            DestroyFallbackMaterial(trimMaterial);
            DestroyFallbackMaterial(doorwayMaterial);
            DestroyFallbackMaterial(startMaterial);
            DestroyFallbackMaterial(exitMaterial);
            DestroyFallbackMaterial(bossMaterial);
            DestroyFallbackMaterial(treasureMaterial);
            DestroyFallbackMaterial(shopMaterial);
            DestroyFallbackMaterial(secretMaterial);
            DestroyFallbackMaterial(pillarMaterial);
            DestroyFallbackMaterial(crateMaterial);
            DestroyFallbackMaterial(crystalMaterial);
            DestroyFallbackMaterial(torchMaterial);
        }

        private void Update()
        {
            RenderDungeonSurface();
            RenderBatches(floorBatches, floorMaterial);
            RenderBatches(corridorBatches, corridorMaterial);
            RenderBatches(wallBatches, wallMaterial);
            RenderBatches(startBatches, startMaterial);
            RenderBatches(exitBatches, exitMaterial);
            RenderBatches(bossBatches, bossMaterial);
            RenderBatches(treasureBatches, treasureMaterial);
            RenderBatches(shopBatches, shopMaterial);
            RenderBatches(secretBatches, secretMaterial);
            RenderBatches(pillarBatches, pillarMaterial, pillarMesh);
            RenderBatches(crateBatches, crateMaterial, crateMesh);
            RenderBatches(crystalBatches, crystalMaterial, crystalMesh);
            RenderBatches(torchBatches, torchMaterial, torchMesh);
            visualBatches.Submit(gameObject.layer);
        }

        public void Render(DungeonWorldData dungeon)
        {
            Render(dungeon, PCGRequest.DefaultPresentation());
        }

        public void Render(DungeonWorldData dungeon, PresentationSettings presentationSettings)
        {
            if (dungeon == null)
                throw new ArgumentNullException(nameof(dungeon));

            PresentationSettings presentation = presentationSettings ?? PCGRequest.DefaultPresentation();
            bool voxelGeometry = string.Equals(presentation.geometryMode, PresentationGeometryModes.Voxel, StringComparison.Ordinal);
            bool showProps = presentation.propsEnabled;
            PresentationGeometryMode = voxelGeometry ? PresentationGeometryModes.Voxel : PresentationGeometryModes.Surface;
            PresentationPropsEnabled = showProps;

            int floorCount = 0;
            int corridorCount = 0;
            List<InstanceData> floors = new List<InstanceData>();
            List<InstanceData> corridors = new List<InstanceData>();
            List<InstanceData> walls = new List<InstanceData>();
            List<InstanceData> starts = new List<InstanceData> { CreateMarkerInstance(dungeon.StartPosition) };
            List<InstanceData> exits = new List<InstanceData> { CreateMarkerInstance(dungeon.ExitPosition) };
            List<InstanceData> bosses = new List<InstanceData>();
            List<InstanceData> treasures = new List<InstanceData>();
            List<InstanceData> shops = new List<InstanceData>();
            List<InstanceData> secrets = new List<InstanceData>();
            List<InstanceData> pillars = new List<InstanceData>();
            List<InstanceData> crates = new List<InstanceData>();
            List<InstanceData> crystals = new List<InstanceData>();
            List<InstanceData> torches = new List<InstanceData>();
            BiomeVisualProfile profile = VisualProfileLoader.Load(Contract.PCGRequest.DungeonWorldType);
            visualBatches.Clear();
            visualPlacements.Clear();
            List<ResolvedVisualPlacement> architecture = showProps
                ? DungeonArchitectureLayoutBuilder.Build(dungeon, profile, tileSize, wallHeight)
                : new List<ResolvedVisualPlacement>();
            for (int index = 0; index < architecture.Count; index++)
            {
                visualPlacements.Add(architecture[index]);
                visualBatches.Add(architecture[index]);
            }
            DungeonArchitectureInstanceCount = architecture.Count;
            if (voxelGeometry)
            {
                DestroyDungeonSurface();
                ResetDungeonSurfaceMetrics();
            }
            else
            {
                DungeonSurfaceBuildOptions surfaceOptions = new DungeonSurfaceBuildOptions(
                    !showProps || !DungeonArchitectureLayoutBuilder.HasVariants(profile, VisualCategoryIds.DungeonRoomFloors),
                    !showProps || !DungeonArchitectureLayoutBuilder.HasVariants(profile, VisualCategoryIds.DungeonCorridorFloors),
                    !showProps || !DungeonArchitectureLayoutBuilder.HasVariants(profile, VisualCategoryIds.DungeonStraightWalls),
                    !showProps || !DungeonArchitectureLayoutBuilder.HasVariants(profile, VisualCategoryIds.DungeonWallTrims),
                    !showProps || !DungeonArchitectureLayoutBuilder.HasVariants(profile, VisualCategoryIds.DungeonDoorways));
                RebuildDungeonSurface(dungeon, surfaceOptions);
            }
            int pillarCount = 0, crateCount = 0, crystalCount = 0, torchCount = 0;
            for (int y = 0; y < dungeon.Height; y++)
            for (int x = 0; x < dungeon.Width; x++)
            {
                DungeonCell cell = dungeon.GetCell(x, y);
                if (cell == DungeonCell.Floor)
                {
                    floorCount++;
                    if (voxelGeometry) floors.Add(CreateTileInstance(x, y, .18f));
                }
                else if (cell == DungeonCell.Corridor)
                {
                    corridorCount++;
                    if (voxelGeometry) corridors.Add(CreateTileInstance(x, y, .18f));
                }
                else if (voxelGeometry && HasWalkableNeighbour(dungeon, x, y))
                    walls.Add(CreateWallInstance(x, y));
            }
            for (int index = 0; index < dungeon.BossRoomIndices.Count; index++)
                bosses.Add(CreateMarkerInstance(dungeon.Rooms[dungeon.BossRoomIndices[index]].Center));
            for (int index = 0; index < dungeon.TreasureRoomIndices.Count; index++)
                treasures.Add(CreateMarkerInstance(dungeon.Rooms[dungeon.TreasureRoomIndices[index]].Center));
            for (int index = 0; index < dungeon.ShopRoomIndices.Count; index++)
                shops.Add(CreateMarkerInstance(dungeon.Rooms[dungeon.ShopRoomIndices[index]].Center));
            for (int index = 0; index < dungeon.SecretRoomIndices.Count; index++)
                secrets.Add(CreateMarkerInstance(dungeon.Rooms[dungeon.SecretRoomIndices[index]].Center));
            for (int index = 0; showProps && index < dungeon.Props.Count; index++)
            {
                DungeonPropPlacement prop = dungeon.Props[index];
                if (prop.Type == DungeonPropType.Pillar) pillarCount++; else if (prop.Type == DungeonPropType.Crate) crateCount++; else if (prop.Type == DungeonPropType.Crystal) crystalCount++; else torchCount++;
                Matrix4x4 fallbackMatrix = CreatePropMatrix(prop.Position, prop.Type, prop.WallNormal);
                Vector3 position = fallbackMatrix.GetColumn(3);
                if (VisualVariantResolver.TryResolve(profile, VisualWorldLayoutBuilder.DungeonCategory(prop.Type), dungeon.Seed, prop.Position.X, prop.Position.Y, index, position, fallbackMatrix.rotation, fallbackMatrix.lossyScale, out ResolvedVisualPlacement placement))
                {
                    visualPlacements.Add(placement);
                    visualBatches.Add(placement);
                    continue;
                }
                if (prop.Type == DungeonPropType.Pillar) pillars.Add(new InstanceData { objectToWorld = fallbackMatrix });
                else if (prop.Type == DungeonPropType.Crate) crates.Add(new InstanceData { objectToWorld = fallbackMatrix });
                else if (prop.Type == DungeonPropType.Crystal) crystals.Add(new InstanceData { objectToWorld = fallbackMatrix });
                else torches.Add(new InstanceData { objectToWorld = fallbackMatrix });
            }
            visualBatches.Build();
            VisualLayoutHash = showProps ? VisualLayoutHasher.Compute(profile, visualPlacements) : "00000000";

            ReplaceBatches(floorBatches, floors);
            ReplaceBatches(corridorBatches, corridors);
            ReplaceBatches(wallBatches, walls);
            ReplaceBatches(startBatches, starts);
            ReplaceBatches(exitBatches, exits);
            ReplaceBatches(bossBatches, bosses);
            ReplaceBatches(treasureBatches, treasures);
            ReplaceBatches(shopBatches, shops);
            ReplaceBatches(secretBatches, secrets);
            ReplaceBatches(pillarBatches, pillars);
            ReplaceBatches(crateBatches, crates);
            ReplaceBatches(crystalBatches, crystals);
            ReplaceBatches(torchBatches, torches);
            FloorInstanceCount = floorCount;
            CorridorInstanceCount = corridorCount;
            WallInstanceCount = dungeonSurfaceData == null ? 0 : dungeonSurfaceData.BoundaryEdgeCount;
            ExteriorWallInstanceCount = dungeonSurfaceData == null ? 0 : dungeonSurfaceData.ExteriorBoundaryEdgeCount;
            StartMarkerCount = starts.Count;
            ExitMarkerCount = exits.Count;
            BossMarkerCount = bosses.Count;
            TreasureMarkerCount = treasures.Count;
            ShopMarkerCount = shops.Count;
            SecretMarkerCount = secrets.Count;
            PillarPropCount = pillarCount;
            CratePropCount = crateCount;
            CrystalPropCount = crystalCount;
            TorchPropCount = torchCount;
            fallbackPropInstanceCount = pillars.Count + crates.Count + crystals.Count + torches.Count;
            VoxelGeometryInstanceCount = floors.Count + corridors.Count + walls.Count;
        }

        public void Clear()
        {
            floorBatches.Clear();
            corridorBatches.Clear();
            wallBatches.Clear();
            startBatches.Clear();
            exitBatches.Clear();
            bossBatches.Clear();
            treasureBatches.Clear();
            shopBatches.Clear();
            secretBatches.Clear();
            pillarBatches.Clear();
            crateBatches.Clear();
            crystalBatches.Clear();
            torchBatches.Clear();
            visualBatches.Clear();
            visualPlacements.Clear();
            DestroyDungeonSurface();
            FloorInstanceCount = 0;
            CorridorInstanceCount = 0;
            WallInstanceCount = 0;
            ExteriorWallInstanceCount = 0;
            StartMarkerCount = 0;
            ExitMarkerCount = 0;
            BossMarkerCount = 0;
            TreasureMarkerCount = 0;
            ShopMarkerCount = 0;
            SecretMarkerCount = 0;
            PillarPropCount = 0;
            CratePropCount = 0;
            CrystalPropCount = 0;
            TorchPropCount = 0;
            fallbackPropInstanceCount = 0;
            DungeonArchitectureInstanceCount = 0;
            VoxelGeometryInstanceCount = 0;
            PresentationGeometryMode = PresentationGeometryModes.Surface;
            PresentationPropsEnabled = true;
            DungeonSurfaceVertexCount = 0;
            DungeonSurfaceTriangleCount = 0;
            DungeonSurfaceBoundaryEdgeCount = 0;
            DungeonSurfaceDoorwayCount = 0;
            DungeonSurfaceCornerCount = 0;
            DungeonSurfaceSubmissionCount = 0;
            DungeonSurfaceUsesAssetMaterials = false;
            DungeonSurfaceHash = "00000000";
            VisualLayoutHash = "00000000";
        }

        private InstanceData CreateTileInstance(int x, int y, float height)
        {
            Vector3 position = new Vector3(x * tileSize, 0f, y * tileSize);
            Vector3 scale = new Vector3(tileSize, height, tileSize);
            return new InstanceData { objectToWorld = Matrix4x4.TRS(position, Quaternion.identity, scale) };
        }

        private InstanceData CreateWallInstance(int x, int y)
        {
            Vector3 position = new Vector3(x * tileSize, wallHeight * 0.5f, y * tileSize);
            Vector3 scale = new Vector3(tileSize, wallHeight, tileSize);
            return new InstanceData { objectToWorld = Matrix4x4.TRS(position, Quaternion.identity, scale) };
        }

        private InstanceData CreateMarkerInstance(Int2 point)
        {
            Vector3 position = new Vector3(point.X * tileSize, 1.5f, point.Y * tileSize);
            Vector3 scale = new Vector3(tileSize * 1.35f, 3f, tileSize * 1.35f);
            return new InstanceData { objectToWorld = Matrix4x4.TRS(position, Quaternion.identity, scale) };
        }

        private InstanceData CreatePropInstance(Int2 point, DungeonPropType type, Int2 wallNormal = default)
        {
            return new InstanceData { objectToWorld = CreatePropMatrix(point, type, wallNormal) };
        }

        private Matrix4x4 CreatePropMatrix(Int2 point, DungeonPropType type, Int2 wallNormal = default)
        {
            Vector3 position = new Vector3(point.X * tileSize, 0.5f, point.Y * tileSize);
            Vector3 scale;
            Quaternion rotation;
            float variation = GetPropVariation(point, type);
            if (type == DungeonPropType.Pillar)
            {
                position.y = 0.9f;
                scale = new Vector3(tileSize * 0.24f, 1.6f + variation * 0.45f, tileSize * 0.24f);
                rotation = Quaternion.Euler(0f, variation * 360f, 0f);
            }
            else if (type == DungeonPropType.Crate)
            {
                position.y = 0.42f;
                float size = tileSize * (0.58f + variation * 0.20f);
                scale = new Vector3(size, 0.64f + variation * 0.28f, size);
                rotation = Quaternion.Euler(0f, variation * 360f, 0f);
            }
            else if (type == DungeonPropType.Crystal)
            {
                position.y = 0.65f;
                float size = tileSize * (0.30f + variation * 0.16f);
                scale = new Vector3(size, 0.9f + variation * 0.7f, size);
                rotation = Quaternion.Euler(0f, variation * 360f, 0f);
            }
            else
            {
                position = new Vector3((point.X + wallNormal.X * 0.36f) * tileSize, 1.05f, (point.Y + wallNormal.Y * 0.36f) * tileSize);
                scale = new Vector3(tileSize * 0.16f, 0.72f + variation * 0.24f, tileSize * 0.16f);
                rotation = Quaternion.LookRotation(new Vector3(-wallNormal.X, 0f, -wallNormal.Y));
            }
            return Matrix4x4.TRS(position, rotation, scale);
        }

        private static float GetPropVariation(Int2 point, DungeonPropType type)
        {
            unchecked
            {
                uint value = (uint)(point.X * 73856093) ^ (uint)(point.Y * 19349663) ^ (uint)((int)type * 83492791);
                value ^= value >> 16;
                return (value & 1023u) / 1023f;
            }
        }

        private static bool HasWalkableNeighbour(DungeonWorldData dungeon, int x, int y)
        {
            return dungeon.IsWalkable(x + 1, y) || dungeon.IsWalkable(x - 1, y) ||
                   dungeon.IsWalkable(x, y + 1) || dungeon.IsWalkable(x, y - 1);
        }

        /// <summary>Creates a visible outer shell where a walkable cell touches the map boundary.</summary>
        private int AddExteriorWalls(DungeonWorldData dungeon, List<InstanceData> walls)
        {
            int countBefore = walls.Count;
            for (int y = 0; y < dungeon.Height; y++)
            for (int x = 0; x < dungeon.Width; x++)
            {
                if (!dungeon.IsWalkable(x, y))
                    continue;
                if (x == 0) walls.Add(CreateWallInstance(-1, y));
                if (x == dungeon.Width - 1) walls.Add(CreateWallInstance(dungeon.Width, y));
                if (y == 0) walls.Add(CreateWallInstance(x, -1));
                if (y == dungeon.Height - 1) walls.Add(CreateWallInstance(x, dungeon.Height));
            }
            return walls.Count - countBefore;
        }

        public static int CountExteriorBoundaryWalls(DungeonWorldData dungeon)
        {
            if (dungeon == null)
                throw new ArgumentNullException(nameof(dungeon));

            int count = 0;
            for (int y = 0; y < dungeon.Height; y++)
            for (int x = 0; x < dungeon.Width; x++)
            {
                if (!dungeon.IsWalkable(x, y))
                    continue;
                if (x == 0) count++;
                if (x == dungeon.Width - 1) count++;
                if (y == 0) count++;
                if (y == dungeon.Height - 1) count++;
            }
            return count;
        }

        private static void ReplaceBatches(List<InstanceData[]> destination, List<InstanceData> source)
        {
            destination.Clear();
            for (int start = 0; start < source.Count; start += MaximumInstancesPerSubmission)
            {
                int length = Math.Min(MaximumInstancesPerSubmission, source.Count - start);
                InstanceData[] batch = new InstanceData[length];
                source.CopyTo(start, batch, 0, length);
                destination.Add(batch);
            }
        }

        private void LoadSurfaceMaterials()
        {
            floorMaterial = floorMaterial == null ? Resources.Load<Material>("PCGSurfaceMaterials/DungeonRoomFloor") : floorMaterial;
            corridorMaterial = corridorMaterial == null ? Resources.Load<Material>("PCGSurfaceMaterials/DungeonCorridorFloor") : corridorMaterial;
            wallMaterial = wallMaterial == null ? Resources.Load<Material>("PCGSurfaceMaterials/DungeonWall") : wallMaterial;
            trimMaterial = trimMaterial == null ? Resources.Load<Material>("PCGSurfaceMaterials/DungeonTrim") : trimMaterial;
            doorwayMaterial = doorwayMaterial == null ? Resources.Load<Material>("PCGSurfaceMaterials/DungeonDoorway") : doorwayMaterial;
            DungeonSurfaceUsesAssetMaterials = floorMaterial != null && corridorMaterial != null && wallMaterial != null && trimMaterial != null && doorwayMaterial != null;
        }

        private void RebuildDungeonSurface(DungeonWorldData dungeon, DungeonSurfaceBuildOptions options)
        {
            DestroyDungeonSurface();
            DungeonSurfaceUsesAssetMaterials = IsPersistentMaterial(floorMaterial) && IsPersistentMaterial(corridorMaterial) && IsPersistentMaterial(wallMaterial) && IsPersistentMaterial(trimMaterial) && IsPersistentMaterial(doorwayMaterial);
            dungeonSurfaceData = DungeonSurfaceMeshBuilder.BuildData(dungeon, tileSize, wallHeight, options);
            dungeonSurfaceMesh = DungeonSurfaceMeshBuilder.CreateMesh(dungeonSurfaceData);
            DungeonSurfaceVertexCount = dungeonSurfaceData.VertexCount;
            DungeonSurfaceTriangleCount = dungeonSurfaceData.TriangleCount;
            DungeonSurfaceBoundaryEdgeCount = dungeonSurfaceData.BoundaryEdgeCount;
            DungeonSurfaceDoorwayCount = dungeonSurfaceData.DoorwayCount;
            DungeonSurfaceCornerCount = dungeonSurfaceData.CornerCount;
            DungeonSurfaceHash = dungeonSurfaceData.StableHash;
            DungeonSurfaceSubmissionCount = 0;
            Material[] materials = { floorMaterial, corridorMaterial, wallMaterial, trimMaterial, doorwayMaterial };
            for (int index = 0; index < dungeonSurfaceData.SubmeshTriangles.Length; index++)
                if (dungeonSurfaceData.SubmeshTriangles[index].Length > 0 && materials[index] != null) DungeonSurfaceSubmissionCount++;
        }

        private void ResetDungeonSurfaceMetrics()
        {
            DungeonSurfaceVertexCount = 0;
            DungeonSurfaceTriangleCount = 0;
            DungeonSurfaceBoundaryEdgeCount = 0;
            DungeonSurfaceDoorwayCount = 0;
            DungeonSurfaceCornerCount = 0;
            DungeonSurfaceSubmissionCount = 0;
            DungeonSurfaceUsesAssetMaterials = false;
            DungeonSurfaceHash = "00000000";
        }

        private void RenderDungeonSurface()
        {
            if (dungeonSurfaceMesh == null) return;
            Material[] materials = { floorMaterial, corridorMaterial, wallMaterial, trimMaterial, doorwayMaterial };
            for (int submesh = 0; submesh < materials.Length; submesh++)
            {
                if (materials[submesh] == null || dungeonSurfaceMesh.GetIndexCount(submesh) == 0) continue;
                RenderParams renderParams = new RenderParams(materials[submesh])
                {
                    layer = gameObject.layer,
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true,
                    worldBounds = dungeonSurfaceMesh.bounds
                };
                Graphics.RenderMesh(renderParams, dungeonSurfaceMesh, submesh, Matrix4x4.identity);
            }
        }

        private void DestroyDungeonSurface()
        {
            if (dungeonSurfaceMesh != null)
            {
                if (Application.isPlaying) Destroy(dungeonSurfaceMesh); else DestroyImmediate(dungeonSurfaceMesh);
            }
            dungeonSurfaceMesh = null;
            dungeonSurfaceData = null;
        }

        private static bool IsPersistentMaterial(Material material)
        {
            return material != null && (material.hideFlags & HideFlags.DontSave) == 0;
        }

        private void RenderBatches(List<InstanceData[]> batches, Material material, Mesh mesh = null)
        {
            mesh = mesh ?? cubeMesh;
            if (mesh == null || material == null)
                return;

            RenderParams parameters = new RenderParams(material)
            {
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true
            };
            for (int index = 0; index < batches.Count; index++)
                Graphics.RenderMeshInstanced(parameters, mesh, 0, batches[index]);
        }

        private static Mesh LoadPrefabMesh(string resourcePath)
        {
            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            MeshFilter filter = prefab == null ? null : prefab.GetComponent<MeshFilter>();
            return filter == null ? null : filter.sharedMesh;
        }

        private void CreateFallbackMaterialsIfNeeded()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("No compatible Lit shader found for the PCG renderer.", this);
                return;
            }

            // UnityEngine.Object can be a destroyed "fake null" after leaving Play Mode.
            // Use Unity's equality operator rather than C# null coalescing so it is recreated.
            if (floorMaterial == null)
                floorMaterial = CreateFallbackMaterial(shader, new Color(0.38f, 0.42f, 0.46f));
            if (corridorMaterial == null)
                corridorMaterial = CreateFallbackMaterial(shader, new Color(0.65f, 0.55f, 0.35f));
            if (wallMaterial == null)
                wallMaterial = CreateFallbackMaterial(shader, new Color(0.16f, 0.19f, 0.23f));
            if (trimMaterial == null)
                trimMaterial = CreateFallbackMaterial(shader, new Color(0.34f, 0.37f, 0.40f));
            if (doorwayMaterial == null)
                doorwayMaterial = CreateFallbackMaterial(shader, new Color(0.42f, 0.31f, 0.20f));
            if (startMaterial == null)
                startMaterial = CreateFallbackMaterial(shader, new Color(0.20f, 0.78f, 0.36f));
            if (exitMaterial == null)
                exitMaterial = CreateFallbackMaterial(shader, new Color(0.30f, 0.55f, 0.96f));
            if (bossMaterial == null)
                bossMaterial = CreateFallbackMaterial(shader, new Color(0.88f, 0.20f, 0.22f));
            if (treasureMaterial == null)
                treasureMaterial = CreateFallbackMaterial(shader, new Color(0.96f, 0.72f, 0.18f));
            if (shopMaterial == null)
                shopMaterial = CreateFallbackMaterial(shader, new Color(0.72f, 0.38f, 0.92f));
            if (secretMaterial == null)
                secretMaterial = CreateFallbackMaterial(shader, new Color(0.22f, 0.80f, 0.74f));
            if (pillarMaterial == null)
                pillarMaterial = CreateFallbackMaterial(shader, new Color(0.48f, 0.44f, 0.38f));
            if (crateMaterial == null)
                crateMaterial = CreateFallbackMaterial(shader, new Color(0.50f, 0.27f, 0.10f));
            if (crystalMaterial == null)
                crystalMaterial = CreateFallbackMaterial(shader, new Color(0.42f, 0.80f, 0.96f));
            if (torchMaterial == null)
                torchMaterial = CreateFallbackMaterial(shader, new Color(1.0f, 0.42f, 0.08f));
        }

        private static Material CreateFallbackMaterial(Shader shader, Color color)
        {
            Material material = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            material.enableInstancing = true;
            return material;
        }

        private static void DestroyFallbackMaterial(Material material)
        {
            if (material != null && (material.hideFlags & HideFlags.DontSave) != 0)
                Destroy(material);
        }

        private struct InstanceData
        {
            public Matrix4x4 objectToWorld;
        }
    }
}
