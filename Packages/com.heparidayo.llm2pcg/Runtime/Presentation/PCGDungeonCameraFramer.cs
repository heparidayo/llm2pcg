using Llm2Pcg.Core;
using UnityEngine;

namespace Llm2Pcg.Presentation
{
    /// <summary>Frames each generated dungeon from above so every supported map size is immediately visible.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PCGDungeonGenerationController))]
    public sealed class PCGDungeonCameraFramer : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField, Min(1f)] private float framingPadding = 1.15f;
        [SerializeField, Min(1f)] private float minimumCameraHeight = 20f;
        [SerializeField] private PCGDungeonGenerationController generationController;

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
