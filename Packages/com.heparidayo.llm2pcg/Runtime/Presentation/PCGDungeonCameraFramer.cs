using Llm2Pcg.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Presentation
{
    /// <summary>Fits generated worlds to the camera; City uses an oblique view and its own render quality.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PCGDungeonGenerationController))]
    public sealed class PCGDungeonCameraFramer : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField, Min(1f)] private float framingPadding = 1.15f;
        [SerializeField, Min(1f)] private float minimumCameraHeight = 20f;
        [SerializeField] private PCGDungeonGenerationController generationController;
        private RenderPipelineAsset previousPipeline, cityPipeline;
        private bool cityPresentationActive;
        private CameraClearFlags previousClearFlags;
        private Color previousBackground;
        private readonly CavePresentationState cavePresentation = new CavePresentationState();
        private bool caveWorldActive;
        private readonly NatureBiomePresentationState naturePresentation = new NatureBiomePresentationState();
        private string natureWorldActive;
        private readonly DungeonPresentationState dungeonPresentation = new DungeonPresentationState();
        private bool dungeonWorldActive;
        private readonly ForestPresentationState forestPresentation = new ForestPresentationState();
        private bool forestWorldActive;
        public bool IsFramingSuspended { get; private set; }

        // First-person navigation owns the camera pose, not the world's render quality.
        public void SetFramingSuspended(bool suspended)
        {
            IsFramingSuspended = suspended;
            if (forestWorldActive && targetCamera != null) forestPresentation.Apply(targetCamera, suspended);
            if (dungeonWorldActive && targetCamera != null) dungeonPresentation.Apply(targetCamera, suspended);
            if (caveWorldActive && targetCamera != null) cavePresentation.Apply(targetCamera, suspended);
            if (natureWorldActive != null && targetCamera != null) naturePresentation.Apply(targetCamera, natureWorldActive, suspended);
        }

        private void Awake()
        {
            generationController = generationController ?? GetComponent<PCGDungeonGenerationController>();
            targetCamera = targetCamera == null ? Camera.main : targetCamera;
        }

        private void OnEnable()
        {
            if (generationController != null)
                generationController.AnyWorldGenerated += FrameWorld;
        }

        private void Start()
        {
            if (generationController != null && generationController.LastGeneratedData != null)
                FrameWorld(generationController.LastGeneratedData);
        }

        private void OnDisable()
        {
            forestPresentation.Restore();
            forestWorldActive = false;
            dungeonPresentation.Restore();
            dungeonWorldActive = false;
            naturePresentation.Restore();
            natureWorldActive = null;
            cavePresentation.Restore();
            caveWorldActive = false;
            RestoreCityPresentation();
            if (generationController != null)
                generationController.AnyWorldGenerated -= FrameWorld;
        }

        public void FrameWorld(IPCGWorldData world)
        {
            if (world == null)
                return;

            targetCamera = targetCamera == null ? Camera.main : targetCamera;
            if (targetCamera == null)
            {
                Debug.LogWarning("Cannot frame dungeon because no camera is available.", this);
                return;
            }

            Vector3 center = new Vector3((world.Width - 1) * 0.5f, 0f, (world.Height - 1) * 0.5f);
            if (world.WorldType == "Forest")
            {
                dungeonPresentation.Restore(); dungeonWorldActive = false;
                naturePresentation.Restore(); natureWorldActive = null;
                cavePresentation.Restore(); caveWorldActive = false;
                RestoreCityPresentation();
                forestWorldActive = true;
                forestPresentation.Apply(targetCamera, IsFramingSuspended);
                float highest = 0;
                if (world is BiomeWorldData woodland)
                    foreach (float h in woodland.Elevations) highest = Mathf.Max(highest, h);
                if (!IsFramingSuspended) FrameCity(targetCamera, world.Width, world.Height, highest + 7f, framingPadding, 57f, -25f);
                return;
            }
            forestPresentation.Restore();
            forestWorldActive = false;
            if (world.WorldType == "Dungeon")
            {
                naturePresentation.Restore(); natureWorldActive = null;
                cavePresentation.Restore(); caveWorldActive = false;
                RestoreCityPresentation();
                dungeonWorldActive = true;
                dungeonPresentation.Apply(targetCamera, IsFramingSuspended);
                if (!IsFramingSuspended) FrameCity(targetCamera, world.Width, world.Height, 3f, framingPadding, 62f, -25f);
                return;
            }
            dungeonPresentation.Restore();
            dungeonWorldActive = false;
            if (NatureBiomePresentationState.Supports(world.WorldType))
            {
                cavePresentation.Restore();
                caveWorldActive = false;
                RestoreCityPresentation();
                natureWorldActive = world.WorldType;
                naturePresentation.Apply(targetCamera, world.WorldType, IsFramingSuspended);
                if (!IsFramingSuspended)
                {
                    float maximum = 0f;
                    if (world is BiomeWorldData terrain)
                        foreach (float h in terrain.Elevations) maximum = Mathf.Max(maximum,h);
                    FrameCity(targetCamera, world.Width, world.Height, maximum + 8f, framingPadding, 57f, -25f);
                }
                return;
            }
            naturePresentation.Restore();
            natureWorldActive = null;
            if (world.WorldType == "Cave")
            {
                RestoreCityPresentation();
                caveWorldActive = true;
                cavePresentation.Apply(targetCamera, IsFramingSuspended);
                if (!IsFramingSuspended) FrameCity(targetCamera, world.Width, world.Height, 5f, framingPadding, 64f, -25f);
                return;
            }
            cavePresentation.Restore();
            caveWorldActive = false;
            if (world.WorldType == "City")
            {
                ConfigureCityPresentation();
                if (IsFramingSuspended) return;
                float roofHeight = 1f;
                if (world is BiomeWorldData city)
                    foreach (float h in city.StructureHeights) roofHeight = Mathf.Max(roofHeight, h);
                FrameCity(targetCamera, world.Width, world.Height, roofHeight, framingPadding);
                return;
            }
            RestoreCityPresentation();
            if (IsFramingSuspended) return;
            if (targetCamera.orthographic)
            {
                targetCamera.orthographicSize = CalculateOrthographicSize(world.Width, world.Height, targetCamera.aspect, framingPadding);
                targetCamera.transform.SetPositionAndRotation(center + Vector3.up * minimumCameraHeight, Quaternion.Euler(90f, 0f, 0f));
                return;
            }

            float height = CalculatePerspectiveHeight(world.Width, world.Height, targetCamera.fieldOfView, targetCamera.aspect, framingPadding, minimumCameraHeight);
            targetCamera.transform.SetPositionAndRotation(center + Vector3.up * height, Quaternion.Euler(90f, 0f, 0f));
        }

        public static float CalculateOrthographicSize(int width, int height, float aspect, float padding)
        {
            float safeAspect = Mathf.Max(0.01f, aspect);
            return Mathf.Max(height * 0.5f, width * 0.5f / safeAspect) * Mathf.Max(1f, padding);
        }

        /// <summary>Fits all eight volume corners, including rooftops, for any aspect ratio.</summary>
        public static void FrameCity(Camera camera, int width, int height, float roofHeight, float padding, float elevation = 48f, float yaw = -35f)
        {
            var center = new Vector3((width - 1) * .5f, roofHeight * .5f, (height - 1) * .5f);
            var rotation = Quaternion.Euler(elevation, yaw, 0f);
            var inverse = Quaternion.Inverse(rotation);
            float tanY = Mathf.Tan(Mathf.Clamp(camera.fieldOfView, 1, 179) * .5f * Mathf.Deg2Rad);
            float tanX = tanY * Mathf.Max(.01f, camera.aspect);
            float distance = 20f, orthoSize = 1f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? -.5f : width - .5f,
                    (i & 2) == 0 ? 0f : roofHeight, (i & 4) == 0 ? -.5f : height - .5f);
                Vector3 local = inverse * (corner - center);
                distance = Mathf.Max(distance, Mathf.Max(Mathf.Abs(local.x) / tanX, Mathf.Abs(local.y) / tanY) * Mathf.Max(1f,padding) - local.z);
                orthoSize = Mathf.Max(orthoSize, Mathf.Abs(local.y), Mathf.Abs(local.x) / Mathf.Max(.01f,camera.aspect));
            }
            camera.orthographicSize = orthoSize * Mathf.Max(1f,padding);
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, distance + width + height + roofHeight);
            camera.transform.SetPositionAndRotation(center - rotation * Vector3.forward * distance, rotation);
        }

        private void ConfigureCityPresentation()
        {
            if (cityPresentationActive) return;
            previousPipeline = QualitySettings.renderPipeline;
            previousClearFlags = targetCamera.clearFlags;
            previousBackground = targetCamera.backgroundColor;
            cityPipeline = Resources.Load<RenderPipelineAsset>("PCGPresentation/CityPipeline");
            if (cityPipeline != null) QualitySettings.renderPipeline = cityPipeline;
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = new Color(.55f,.65f,.68f);
            cityPresentationActive = true;
        }

        private void RestoreCityPresentation()
        {
            if (!cityPresentationActive) return;
            if (QualitySettings.renderPipeline == cityPipeline) QualitySettings.renderPipeline = previousPipeline;
            if (targetCamera != null) { targetCamera.clearFlags = previousClearFlags; targetCamera.backgroundColor = previousBackground; }
            cityPresentationActive = false;
        }

        public static float CalculatePerspectiveHeight(int width, int height, float fieldOfView, float aspect, float padding, float minimumHeight)
        {
            float halfFieldOfViewRadians = Mathf.Clamp(fieldOfView, 1f, 179f) * Mathf.Deg2Rad * 0.5f;
            float tangent = Mathf.Tan(halfFieldOfViewRadians);
            float verticalHeight = height * 0.5f / tangent;
            float horizontalHeight = width * 0.5f / (tangent * Mathf.Max(0.01f, aspect));
            return Mathf.Max(minimumHeight, Mathf.Max(verticalHeight, horizontalHeight) * Mathf.Max(1f, padding));
        }
    }
}
