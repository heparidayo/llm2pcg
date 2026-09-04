using System;
using Llm2Pcg.Bridge;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Presentation;
using Llm2Pcg.Rendering;
using UnityEngine;

namespace Llm2Pcg.Demo
{
    /// <summary>
    /// Creates a complete, asset-free demonstration at runtime. The generated worlds use the
    /// package's built-in primitive/material fallbacks, so a fresh clone needs no art packs.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class PCGPublicDemoBootstrap : MonoBehaviour
    {
        private static readonly string[] WorldTypes =
        {
            PCGRequest.DungeonWorldType,
            PCGRequest.CaveWorldType,
            PCGRequest.ForestWorldType,
            PCGRequest.CityWorldType,
            PCGRequest.SwampWorldType,
            PCGRequest.SnowfieldWorldType,
            PCGRequest.DesertWorldType
        };

        [SerializeField] private int seed = 234;
        [SerializeField, Range(32, 128)] private int mapSize = 64;
        [SerializeField] private string initialWorldType = PCGRequest.DungeonWorldType;
        [SerializeField] private bool startLocalBridge = true;
        [SerializeField, Range(1, 65535)] private int bridgePort = 8088;

        private PCGDungeonGenerationController controller;
        private PCGServerBridge bridge;
        private string seedText;
        private string status = "Starting local demo...";

        private void Awake()
        {
            seedText = seed.ToString();
            EnsureCamera();
            EnsureLighting();
            CreatePipeline();
        }

        private void Start()
        {
            if (startLocalBridge)
            {
                bridge.ConfigureForLocalDemo(
                    bridgePort,
                    "http://127.0.0.1:3000",
                    "http://localhost:3000");
                bridge.StartServer();
            }

            Generate(initialWorldType);
        }

        private void CreatePipeline()
        {
            GameObject pipeline = new GameObject("LLM2PCG Runtime Pipeline");
            pipeline.transform.SetParent(transform, false);
            pipeline.SetActive(false);

            bridge = pipeline.AddComponent<PCGServerBridge>();
            pipeline.AddComponent<DungeonInstancedRenderer>();
            pipeline.AddComponent<CaveInstancedRenderer>();
            pipeline.AddComponent<BiomeInstancedRenderer>();
            controller = pipeline.AddComponent<PCGDungeonGenerationController>();
            pipeline.AddComponent<PCGDungeonCameraFramer>();
            pipeline.AddComponent<PCGFirstPersonTestDummy>();

            pipeline.SetActive(true);
        }

        private static void EnsureCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                camera = cameraObject.GetComponent<Camera>();
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.04f, 0.065f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f;
        }

        private static void EnsureLighting()
        {
            if (FindAnyObjectByType<Light>() != null)
                return;

            GameObject lightObject = new GameObject("Demo Sun", typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.95f, 0.86f);
            lightObject.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        }

        private void Generate(string worldType)
        {
            if (!int.TryParse(seedText, out seed))
            {
                status = "Seed must be a whole number.";
                return;
            }

            try
            {
                PCGRequest request = CreateRequest(worldType, seed, mapSize);
                controller.Generate(request);
                string hash = controller.LastGeneratedData.ComputeStableHash();
                status = worldType + "  |  seed " + seed + "  |  hash " + hash;
            }
            catch (Exception exception)
            {
                status = "Generation failed: " + exception.Message;
                Debug.LogException(exception, this);
            }
        }

        private static PCGRequest CreateRequest(string worldType, int requestSeed, int size)
        {
            switch (worldType)
            {
                case PCGRequest.CaveWorldType: return PCGRequest.CreateCaveDefault(requestSeed, size, size);
                case PCGRequest.ForestWorldType: return PCGRequest.CreateForestDefault(requestSeed, size, size);
                case PCGRequest.CityWorldType: return PCGRequest.CreateCityDefault(requestSeed, size, size);
                case PCGRequest.SwampWorldType: return PCGRequest.CreateSwampDefault(requestSeed, size, size);
                case PCGRequest.SnowfieldWorldType: return PCGRequest.CreateSnowfieldDefault(requestSeed, size, size);
                case PCGRequest.DesertWorldType: return PCGRequest.CreateDesertDefault(requestSeed, size, size);
                default: return PCGRequest.CreateDefault(requestSeed, size, size);
            }
        }

        private void OnGUI()
        {
            const float margin = 14f;
            GUILayout.BeginArea(new Rect(margin, margin, Mathf.Min(680f, Screen.width - margin * 2f), 134f), GUI.skin.box);
            GUILayout.Label("LLM2PCG — asset-free local demo");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed", GUILayout.Width(38f));
            seedText = GUILayout.TextField(seedText, GUILayout.Width(100f));
            for (int index = 0; index < WorldTypes.Length; index++)
            {
                string worldType = WorldTypes[index];
                if (GUILayout.Button(worldType, GUILayout.MinWidth(68f)))
                    Generate(worldType);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(status);
            GUILayout.Label("F1: first-person test  |  WASD / mouse: move  |  Web bridge: " +
                            (bridge != null && bridge.IsRunning ? "http://127.0.0.1:" + bridge.Port : "off"));
            GUILayout.EndArea();
        }
    }
}
