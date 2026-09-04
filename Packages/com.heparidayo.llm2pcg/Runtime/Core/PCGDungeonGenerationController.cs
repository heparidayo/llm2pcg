using System;
using System.IO;
using Llm2Pcg.Bridge;
using Llm2Pcg.Contract;
using Llm2Pcg.Rendering;
using UnityEngine;

namespace Llm2Pcg.Core
{
    /// <summary>Unity composition root: main-thread request handling, world generation, and presentation.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PCGServerBridge))]
    [RequireComponent(typeof(DungeonInstancedRenderer))]
    public sealed class PCGDungeonGenerationController : MonoBehaviour
    {
        [SerializeField] private PCGServerBridge bridge;
        [SerializeField] private DungeonInstancedRenderer dungeonRenderer;
        [SerializeField] private CaveInstancedRenderer caveRenderer;
        [SerializeField] private BiomeInstancedRenderer biomeRenderer;

        private readonly PCGGeneratorRegistry registry = PCGGeneratorRegistry.Default;
        public DungeonWorldData LastGeneratedWorld { get; private set; }
        public IPCGWorldData LastGeneratedData { get; private set; }
        public PCGRequest LastRequest { get; private set; }
        public event Action<DungeonWorldData> WorldGenerated;
        public event Action<IPCGWorldData> AnyWorldGenerated;
        public string SnapshotPath => Path.Combine(Application.persistentDataPath, "llm2pcg-last-generation.json");

        private void Awake()
        {
            bridge = bridge ?? GetComponent<PCGServerBridge>();
            dungeonRenderer = dungeonRenderer ?? GetComponent<DungeonInstancedRenderer>();
            caveRenderer = caveRenderer ?? GetComponent<CaveInstancedRenderer>();
            biomeRenderer = biomeRenderer ?? GetComponent<BiomeInstancedRenderer>();
        }

        private void OnEnable()
        {
            if (bridge != null)
            {
                bridge.RequestReceived += Generate;
                bridge.SaveRequested += SaveLastGeneration;
                bridge.LoadRequested += LoadLastGeneration;
            }
        }

        private void OnDisable()
        {
            if (bridge != null)
            {
                bridge.RequestReceived -= Generate;
                bridge.SaveRequested -= SaveLastGeneration;
                bridge.LoadRequested -= LoadLastGeneration;
            }
        }

        [ContextMenu("Generate Default Dungeon")]
        public void GenerateDefaultDungeon()
        {
            Generate(PCGRequest.CreateDefault());
        }

        public void Generate(PCGRequest request)
        {
            PCGValidationResult validation = PCGRequestValidator.Validate(request);
            if (!validation.IsValid)
                throw new ArgumentException(validation.Code + ": " + validation.Message, nameof(request));
            LastRequest = request;
            LastGeneratedData = registry.GetRequired(request).Generate(request);
            LastGeneratedWorld = LastGeneratedData as DungeonWorldData;
            dungeonRenderer.Clear();
            if (caveRenderer != null) caveRenderer.Clear();
            if (biomeRenderer != null) biomeRenderer.Clear();
            if (LastGeneratedWorld != null)
            {
                dungeonRenderer.Render(LastGeneratedWorld, request.presentationSettings);
                WorldGenerated?.Invoke(LastGeneratedWorld);
            }
            else if (LastGeneratedData is CaveWorldData cave && caveRenderer != null)
                caveRenderer.Render(cave, request.presentationSettings);
            else if (LastGeneratedData is BiomeWorldData biome && biomeRenderer != null)
                biomeRenderer.Render(biome, request.visualSettings, request.presentationSettings);
            else
                throw new InvalidOperationException("No renderer is configured for " + LastGeneratedData.WorldType + ".");
            AnyWorldGenerated?.Invoke(LastGeneratedData);
            Debug.Log("Generated " + LastGeneratedData.WorldType + " with seed " + request.seed + ", hash " + LastGeneratedData.ComputeStableHash() + ".", this);
        }

        [ContextMenu("Save Last Generation")]
        public void SaveLastGeneration()
        {
            if (LastRequest == null || LastGeneratedData == null)
            {
                Debug.LogWarning("No generated dungeon is available to save.", this);
                return;
            }
            try
            {
                File.WriteAllText(SnapshotPath, JsonUtility.ToJson(PCGGenerationSnapshot.Create(LastRequest, LastGeneratedData), true));
                Debug.Log("Saved deterministic PCG snapshot to " + SnapshotPath + ".", this);
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not save PCG snapshot: " + exception.Message, this);
            }
        }

        [ContextMenu("Load Last Generation")]
        public void LoadLastGeneration()
        {
            if (!File.Exists(SnapshotPath))
            {
                Debug.LogWarning("No PCG snapshot exists at " + SnapshotPath + ".", this);
                return;
            }
            try
            {
                PCGGenerationSnapshot snapshot = JsonUtility.FromJson<PCGGenerationSnapshot>(File.ReadAllText(SnapshotPath));
                if (snapshot == null || (snapshot.formatVersion != 1 && snapshot.formatVersion != PCGGenerationSnapshot.CurrentFormatVersion))
                    throw new InvalidDataException("Unsupported PCG snapshot format.");
                PCGValidationResult validation = PCGRequestValidator.Validate(snapshot.request);
                if (!validation.IsValid)
                    throw new InvalidDataException(validation.Code + ": " + validation.Message);
                Generate(snapshot.request);
                string generatedHash = LastGeneratedData.ComputeStableHash();
                if (!string.Equals(snapshot.worldHash, generatedHash, StringComparison.Ordinal))
                    throw new InvalidDataException("Saved hash " + snapshot.worldHash + " did not match regenerated hash " + generatedHash + ".");
                Debug.Log("Loaded deterministic PCG snapshot with hash " + generatedHash + ".", this);
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not load PCG snapshot: " + exception.Message, this);
            }
        }
    }
}
