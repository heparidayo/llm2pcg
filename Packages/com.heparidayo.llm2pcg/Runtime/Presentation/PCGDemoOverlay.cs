using Llm2Pcg.Bridge;
using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Llm2Pcg.Generators.Nature;

namespace Llm2Pcg.Presentation
{
    /// <summary>Runtime Canvas status panel for demonstrating the local PCG pipeline.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PCGDungeonGenerationController))]
    [RequireComponent(typeof(DungeonInstancedRenderer))]
    public sealed class PCGDemoOverlay : MonoBehaviour
    {
        [SerializeField] private bool isVisible = true;
        [SerializeField] private PCGDungeonGenerationController generationController;
        [SerializeField] private DungeonInstancedRenderer dungeonRenderer;
        [SerializeField] private CaveInstancedRenderer caveRenderer;
        [SerializeField] private BiomeInstancedRenderer biomeRenderer;
        [SerializeField] private PCGServerBridge bridge;

        private Canvas canvas;
        private GraphicRaycaster raycaster;
        private Image bridgeIndicator;
        private Text bridgeLabel;
        private Text generationLabel;
        private Text metricsLabel;
        private Text legendLabel;
        private Text footerLabel;

        public bool IsVisible => isVisible;

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            ApplyVisibility();
        }

        private void Awake()
        {
            generationController = generationController ?? GetComponent<PCGDungeonGenerationController>();
            dungeonRenderer = dungeonRenderer ?? GetComponent<DungeonInstancedRenderer>();
            caveRenderer = caveRenderer ?? GetComponent<CaveInstancedRenderer>();
            biomeRenderer = biomeRenderer ?? GetComponent<BiomeInstancedRenderer>();
            bridge = bridge ?? GetComponent<PCGServerBridge>();
            CreateUi();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame) SetVisible(!isVisible);

            ApplyVisibility();
            if (isVisible) RefreshLabels();
        }

        private void ApplyVisibility()
        {
            if (canvas == null) return;
            canvas.enabled = isVisible;
            if (raycaster != null) raycaster.enabled = isVisible;
        }

        private void CreateUi()
        {
            GameObject canvasObject = new GameObject("PCG Demo Status Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 0.5f;
            canvas.sortingOrder = 100;
            raycaster = canvasObject.GetComponent<GraphicRaycaster>();

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            RectTransform panel = CreateImage(canvasObject.transform, "Panel", new Color(0.035f, 0.055f, 0.085f, 0.90f));
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(24f, -24f);
            panel.sizeDelta = new Vector2(640f, 425f);

            CreateText(panel, "Title", "LLM2PCG  |  WORLD GENERATOR", 24, new Color(0.70f, 0.86f, 1f), new Vector2(22f, -20f), new Vector2(500f, 34f), FontStyle.Bold);
            bridgeIndicator = CreateImage(panel, "Bridge Indicator", new Color(0.9f, 0.3f, 0.3f)).GetComponent<Image>();
            RectTransform indicatorRect = bridgeIndicator.rectTransform;
            indicatorRect.anchorMin = indicatorRect.anchorMax = new Vector2(0f, 1f);
            indicatorRect.pivot = new Vector2(0f, 1f);
            indicatorRect.anchoredPosition = new Vector2(23f, -71f);
            indicatorRect.sizeDelta = new Vector2(14f, 14f);
            bridgeLabel = CreateText(panel, "Bridge", "", 17, Color.white, new Vector2(46f, -62f), new Vector2(460f, 28f), FontStyle.Bold);
            generationLabel = CreateText(panel, "Generation", "", 17, new Color(0.91f, 0.95f, 1f), new Vector2(23f, -108f), new Vector2(484f, 58f), FontStyle.Normal);
            metricsLabel = CreateText(panel, "Metrics", "", 16, new Color(0.82f, 0.89f, 0.96f), new Vector2(23f, -174f), new Vector2(590f, 120f), FontStyle.Normal);
            legendLabel = CreateText(panel, "Legend", "", 15, Color.white, new Vector2(23f, -310f), new Vector2(590f, 48f), FontStyle.Bold);
            footerLabel = CreateText(panel, "Footer", "Status panel available during Play Mode", 14, new Color(0.62f, 0.69f, 0.78f), new Vector2(23f, -384f), new Vector2(590f, 24f), FontStyle.Normal);

            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (bridgeLabel == null)
                return;

            bool bridgeRunning = bridge != null && bridge.IsRunning;
            bridgeIndicator.color = bridgeRunning ? new Color(0.20f, 0.78f, 0.36f) : new Color(0.88f, 0.20f, 0.22f);
            bridgeLabel.text = bridgeRunning
                ? "BRIDGE ONLINE  |  http://127.0.0.1:" + bridge.Port + "  |  Queue " + bridge.PendingRequestCount
                : "BRIDGE OFFLINE  |  Enter Play Mode to accept requests";

            if (bridge != null && bridge.LastResponseIsError)
            {
                generationLabel.text = "LAST REQUEST FAILED  |  " + bridge.LastResponseCode + "\n" + bridge.LastResponseMessage;
                metricsLabel.text = "The previous generated world remains visible. Correct the request and try again.";
                legendLabel.text = "Markers  •  Start  •  Exit  •  Boss";
                footerLabel.text = "HTTP " + bridge.LastResponseStatusCode + "  |  Error response is shown above";
                return;
            }

            IPCGWorldData generated = generationController == null ? null : generationController.LastGeneratedData;
            if (generated == null)
            {
                generationLabel.text = "WAITING FOR GENERATION REQUEST\nWeb, MCP, or Generate Default Dungeon";
                metricsLabel.text = "No world data yet.";
                legendLabel.text = "Markers  •  Start  •  Exit  •  Boss";
                footerLabel.text = "Last HTTP: " + (bridge == null ? "not available" : bridge.LastResponseCode);
                return;
            }

            string presentationLabel = PresentationLabel(generationController.LastRequest?.presentationSettings);

            if (!(generated is DungeonWorldData world))
            {
                int instances = generated is CaveWorldData ? (caveRenderer == null ? 0 : caveRenderer.TotalInstanceCount) : (biomeRenderer == null ? 0 : biomeRenderer.TotalInstanceCount);
                generationLabel.text = "GENERATED " + generated.WorldType.ToUpperInvariant() + "  |  Seed " + generated.Seed + "  |  Hash " + generated.ComputeStableHash() + "\nMAP " + generated.Width + " x " + generated.Height + "  |  " + generated.GeneratorVersion + "  |  " + presentationLabel;
                if (NatureWorldTypes.IsForestFamily(generated.WorldType) && biomeRenderer != null)
                    metricsLabel.text = "GPU INSTANCING  |  " + instances + " instances  |  " + biomeRenderer.RenderSubmissionCount + " submissions\nLOD0/1/2/3  |  " + biomeRenderer.GetLodPlacementCount(0) + "/" + biomeRenderer.GetLodPlacementCount(1) + "/" + biomeRenderer.GetLodPlacementCount(2) + "/" + biomeRenderer.GetLodPlacementCount(3) + " placements\nVERTEX WORKLOAD  |  " + biomeRenderer.EstimatedVisualVertexCount + " / LOD0 " + biomeRenderer.Lod0EstimatedVisualVertexCount + "\nSURFACE MESH  |  " + biomeRenderer.ForestSurfaceVertexCount + " vertices  •  " + biomeRenderer.ForestSurfaceTriangleCount + " triangles  •  " + biomeRenderer.ForestSurfaceHash + "\nNo generated tile GameObjects";
                else if (generated.WorldType == "Cave" && caveRenderer != null)
                    metricsLabel.text = "GPU INSTANCING  |  " + instances + " detail instances  |  " + caveRenderer.RenderSubmissionCount + " submissions\nCAVE SURFACE  |  " + caveRenderer.CaveSurfaceVertexCount + " vertices  •  " + caveRenderer.CaveSurfaceTriangleCount + " triangles\nBOUNDARY WALLS  |  " + caveRenderer.CaveSurfaceBoundaryEdgeCount + " edges  •  Hash " + caveRenderer.CaveSurfaceHash + "\nASSET MATERIALS  |  " + (caveRenderer.CaveSurfaceUsesAssetMaterials ? "Floor / rock / wall loaded" : "Runtime fallback") + "\nNo generated tile GameObjects";
                else if (generated.WorldType == "City" && biomeRenderer != null)
                    metricsLabel.text = "GPU INSTANCING  |  " + instances + " instances  |  " + biomeRenderer.RenderSubmissionCount + " submissions\nCITY SURFACE  |  " + biomeRenderer.CitySurfaceVertexCount + " vertices  •  " + biomeRenderer.CitySurfaceTriangleCount + " triangles\nCURB / ROAD LINES  |  " + biomeRenderer.CitySurfaceElevationEdgeCount + " / " + biomeRenderer.CitySurfaceRoadMarkingCount + " segments\nSURFACE HASH  |  " + biomeRenderer.CitySurfaceHash + "  •  " + (biomeRenderer.CitySurfaceUsesAssetMaterials ? "Asset materials" : "Runtime fallback") + "\nNo generated tile GameObjects";
                else
                    metricsLabel.text = "GPU INSTANCING  |  " + instances + " instances\nNo generated tile GameObjects";
                legendLabel.text = generated.WorldType == "Cave" ? "Rock  •  Floor  •  Tunnel" : NatureWorldTypes.IsForestFamily(generated.WorldType) ? "Ground  •  Water / Ice  •  Path  •  Vegetation" : "Road  •  Building  •  Park  •  Ground";
                footerLabel.text = "Last HTTP: " + (bridge == null ? "not available" : bridge.LastResponseStatusCode + " " + bridge.LastResponseCode);
                return;
            }

            generationLabel.text = "GENERATED  |  Seed " + world.Seed + "  |  Hash " + world.ComputeStableHash() + "\nMAP " + world.Width + " x " + world.Height + "  |  Rooms " + world.Rooms.Count + "  |  " + presentationLabel;
            metricsLabel.text = "RENDER  |  " + dungeonRenderer.TotalInstanceCount + " instances  |  " + dungeonRenderer.RenderSubmissionCount + " submissions\nDUNGEON SURFACE  |  " + dungeonRenderer.DungeonSurfaceVertexCount + " vertices  •  " + dungeonRenderer.DungeonSurfaceTriangleCount + " triangles  •  Hash " + dungeonRenderer.DungeonSurfaceHash + "\nBOUNDARY / DOOR / CORNER  |  " + dungeonRenderer.DungeonSurfaceBoundaryEdgeCount + " / " + dungeonRenderer.DungeonSurfaceDoorwayCount + " / " + dungeonRenderer.DungeonSurfaceCornerCount + "\nASSET KIT  |  " + dungeonRenderer.DungeonArchitectureInstanceCount + " architecture instances  •  " + (dungeonRenderer.DungeonSurfaceUsesAssetMaterials ? "Asset materials" : "Runtime fallback") + "  •  Props " + dungeonRenderer.PropInstanceCount + "\nNo generated tile GameObjects";
            legendLabel.text = "<color=#33C75C>■</color> Start " + dungeonRenderer.StartMarkerCount + "  <color=#4D8CFF>■</color> Exit " + dungeonRenderer.ExitMarkerCount + "  <color=#E03338>■</color> Boss " + dungeonRenderer.BossMarkerCount + "  <color=#F5B82E>■</color> Treasure " + dungeonRenderer.TreasureMarkerCount + "\n<color=#B76BEF>■</color> Shop " + dungeonRenderer.ShopMarkerCount + "  <color=#38C8BC>■</color> Secret " + dungeonRenderer.SecretMarkerCount + "  |  Props P/C/C/T " + dungeonRenderer.PillarPropCount + "/" + dungeonRenderer.CratePropCount + "/" + dungeonRenderer.CrystalPropCount + "/" + dungeonRenderer.TorchPropCount;
            footerLabel.text = "Last HTTP: " + (bridge == null ? "not available" : bridge.LastResponseStatusCode + " " + bridge.LastResponseCode);
        }

        private static string PresentationLabel(PresentationSettings settings)
        {
            PresentationSettings resolved = settings ?? PCGRequest.DefaultPresentation();
            return (resolved.geometryMode == PresentationGeometryModes.Voxel ? "VOXEL" : "SURFACE") + (resolved.propsEnabled ? " + PROPS" : " / NO PROPS");
        }

        private static RectTransform CreateImage(Transform parent, string name, Color color)
        {
            GameObject objectInstance = new GameObject(name, typeof(RectTransform), typeof(Image));
            objectInstance.transform.SetParent(parent, false);
            Image image = objectInstance.GetComponent<Image>();
            image.color = color;
            return objectInstance.GetComponent<RectTransform>();
        }

        private static Text CreateText(Transform parent, string name, string value, int fontSize, Color color, Vector2 anchoredPosition, Vector2 size, FontStyle style)
        {
            GameObject objectInstance = new GameObject(name, typeof(RectTransform), typeof(Text));
            objectInstance.transform.SetParent(parent, false);
            RectTransform rect = objectInstance.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Text text = objectInstance.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = value;
            return text;
        }
    }
}
