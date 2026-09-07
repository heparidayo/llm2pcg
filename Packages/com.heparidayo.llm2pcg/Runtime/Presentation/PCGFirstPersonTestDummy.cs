using Llm2Pcg.Contract;
using Llm2Pcg.Core;
using Llm2Pcg.Visuals;
using Llm2Pcg.Generators.Nature;
using Llm2Pcg.Rendering;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Llm2Pcg.Presentation
{
    /// <summary>
    /// One runtime test dummy that navigates pure PCG data. It does not create tile colliders or tile GameObjects.
    /// F1 toggles first-person mode; WASD moves; mouse looks; Escape releases the cursor.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PCGDungeonGenerationController))]
    public sealed class PCGFirstPersonTestDummy : MonoBehaviour
    {
        [SerializeField, Min(.1f)] private float moveSpeed = 6f;
        [SerializeField, Min(.1f)] private float sprintMultiplier = 1.8f;
        [SerializeField, Range(.05f, 1f)] private float collisionRadius = .28f;
        [SerializeField, Min(.1f)] private float maximumStepHeight = 1.25f;
        [SerializeField, Min(1f)] private float eyeHeight = 1.65f;
        [SerializeField, Range(.1f, 10f)] private float mouseSensitivity = 2f;
        [SerializeField] private PCGDungeonGenerationController generationController;
        [SerializeField] private PCGDungeonCameraFramer cameraFramer;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private PCGDemoOverlay demoOverlay;

        private GameObject dummy;
        private CharacterController characterController;
        private Transform originalCameraParent;
        private Vector3 originalCameraPosition;
        private Quaternion originalCameraRotation;
        private float pitch;
        private bool isFirstPerson;
        private readonly List<ForestTreeCollision> forestTreeCollisions = new List<ForestTreeCollision>();

        public bool IsFirstPerson => isFirstPerson;
        public Vector3 DummyPosition => dummy == null ? Vector3.zero : dummy.transform.position;

        private void Awake()
        {
            generationController = generationController ?? GetComponent<PCGDungeonGenerationController>();
            cameraFramer = cameraFramer ?? GetComponent<PCGDungeonCameraFramer>();
            targetCamera = targetCamera == null ? Camera.main : targetCamera;
            demoOverlay = demoOverlay ?? GetComponent<PCGDemoOverlay>();
            CreateDummy();
        }

        private void OnEnable()
        {
            if (generationController != null) generationController.AnyWorldGenerated += HandleWorldGenerated;
        }

        private void OnDisable()
        {
            if (generationController != null) generationController.AnyWorldGenerated -= HandleWorldGenerated;
            if (isFirstPerson) SetFirstPersonMode(false);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null) return;
            if (keyboard.f1Key.wasPressedThisFrame) SetFirstPersonMode(!isFirstPerson);
            if (!isFirstPerson) return;

            if (keyboard.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * .08f;
                ApplyMouseLook(delta.x, delta.y);
            }

            Vector2 input = new Vector2((keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f), (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
            bool sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            float speed = moveSpeed * (sprint ? sprintMultiplier : 1f);
            SimulateMove(input, speed * Time.deltaTime);
        }

        public void SetFirstPersonMode(bool enabled)
        {
            if (enabled == isFirstPerson) return;
            targetCamera = targetCamera == null ? Camera.main : targetCamera;
            if (enabled)
            {
                if (!(generationController?.LastGeneratedData is IPCGNavigableWorldData world) || targetCamera == null)
                {
                    Debug.LogWarning("Generate a navigable PCG world before entering first-person test mode.", this);
                    return;
                }
                originalCameraParent = targetCamera.transform.parent;
                originalCameraPosition = targetCamera.transform.position;
                originalCameraRotation = targetCamera.transform.rotation;
                if (cameraFramer != null) cameraFramer.SetFramingSuspended(true);
                RebuildForestTreeCollisions(world);
                PlaceAtStart(world);
                targetCamera.transform.SetParent(dummy.transform, false);
                targetCamera.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
                targetCamera.transform.localRotation = Quaternion.identity;
                pitch = 0f;
                isFirstPerson = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                isFirstPerson = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (targetCamera != null)
                {
                    targetCamera.transform.SetParent(originalCameraParent, true);
                    targetCamera.transform.SetPositionAndRotation(originalCameraPosition, originalCameraRotation);
                }
                if (cameraFramer != null)
                {
                    cameraFramer.SetFramingSuspended(false);
                    if (generationController?.LastGeneratedData != null) cameraFramer.FrameWorld(generationController.LastGeneratedData);
                }
            }
        }

        public bool SimulateMove(Vector2 input, float distance)
        {
            if (!isFirstPerson || !(generationController?.LastGeneratedData is IPCGNavigableWorldData world) || dummy == null) return false;
            Vector3 forward = dummy.transform.forward; forward.y = 0f; forward.Normalize();
            Vector3 right = dummy.transform.right; right.y = 0f; right.Normalize();
            Vector3 direction = forward * input.y + right * input.x;
            if (direction.sqrMagnitude > 1f) direction.Normalize();
            Vector3 candidate = dummy.transform.position + direction * distance;
            if (!CanOccupy(world, candidate, dummy.transform.position.y, collisionRadius, maximumStepHeight, forestTreeCollisions)) return false;
            int x = Mathf.RoundToInt(candidate.x), y = Mathf.RoundToInt(candidate.z);
            candidate.y = MovementSurfaceHeight(world, candidate.x, candidate.z, x, y);
            dummy.transform.position = candidate;
            return true;
        }

        public static bool CanOccupy(IPCGNavigableWorldData world, Vector3 candidate, float currentHeight, float radius, float maximumStep)
        {
            return CanOccupy(world, candidate, currentHeight, radius, maximumStep, null);
        }

        public static bool CanOccupy(IPCGNavigableWorldData world, Vector3 candidate, float currentHeight, float radius, float maximumStep, IReadOnlyList<ForestTreeCollision> treeCollisions)
        {
            if (world == null) return false;
            float[] offsets = { -radius, radius };
            for (int xIndex = 0; xIndex < offsets.Length; xIndex++)
            for (int zIndex = 0; zIndex < offsets.Length; zIndex++)
            {
                int x = Mathf.RoundToInt(candidate.x + offsets[xIndex]);
                int y = Mathf.RoundToInt(candidate.z + offsets[zIndex]);
                if (!world.IsWalkable(x, y) || Mathf.Abs(MovementSurfaceHeight(world, candidate.x + offsets[xIndex], candidate.z + offsets[zIndex], x, y) - currentHeight) > maximumStep) return false;
            }
            if (treeCollisions != null)
            {
                for (int i = 0; i < treeCollisions.Count; i++)
                {
                    ForestTreeCollision tree = treeCollisions[i];
                    float dx = candidate.x - tree.X, dz = candidate.z - tree.Z;
                    if (dx * dx + dz * dz < (tree.Radius + radius) * (tree.Radius + radius)) return false;
                }
            }
            else if (world is BiomeWorldData biome && NatureWorldTypes.IsForestFamily(biome.WorldType))
            {
                for (int i = 0; i < biome.Props.Count; i++)
                {
                    float dx = candidate.x - biome.Props[i].X, dz = candidate.z - biome.Props[i].Y;
                    if (dx * dx + dz * dz < (.35f + radius) * (.35f + radius)) return false;
                }
            }
            return true;
        }

        private static float MovementSurfaceHeight(IPCGNavigableWorldData world, float worldX, float worldZ, int cellX, int cellY)
        {
            if (world is BiomeWorldData biome && NatureWorldTypes.IsForestFamily(biome.WorldType))
                return ForestSurfaceMeshBuilder.SampleSurfaceHeight(biome, worldX, worldZ);
            if (world is BiomeWorldData city && city.WorldType == PCGRequest.CityWorldType)
                return CitySurfaceMeshBuilder.SampleSurfaceHeight(city, worldX, worldZ);
            if (world is CaveWorldData cave)
                return CaveSurfaceMeshBuilder.SampleSurfaceHeight(cave, worldX, worldZ);
            return world.GetSurfaceHeight(cellX, cellY);
        }

        private void ApplyMouseLook(float horizontal, float vertical)
        {
            dummy.transform.Rotate(0f, horizontal * mouseSensitivity, 0f);
            pitch = Mathf.Clamp(pitch - vertical * mouseSensitivity, -85f, 85f);
            targetCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void HandleWorldGenerated(IPCGWorldData generated)
        {
            if (generated is IPCGNavigableWorldData navigable)
            {
                RebuildForestTreeCollisions(navigable);
                if (isFirstPerson) PlaceAtStart(navigable);
            }
        }

        private void RebuildForestTreeCollisions(IPCGNavigableWorldData world)
        {
            forestTreeCollisions.Clear();
            if (!(world is BiomeWorldData biome)) return;
            if (generationController?.LastRequest?.presentationSettings?.propsEnabled == false) return;
            BiomeVisualProfile profile = VisualProfileLoader.Load(biome.WorldType);
            if (biome.WorldType == PCGRequest.CityWorldType)
            {
                forestTreeCollisions.AddRange(VisualWorldLayoutBuilder.BuildCityPropCollisions(biome, profile));
                return;
            }
            if (!NatureWorldTypes.IsForestFamily(biome.WorldType)) return;
            forestTreeCollisions.AddRange(VisualWorldLayoutBuilder.BuildNatureTreeCollisions(biome, profile, generationController?.LastRequest?.visualSettings));
        }

        private void PlaceAtStart(IPCGNavigableWorldData world)
        {
            Int2 start = world.StartPosition;
            float surfaceHeight = MovementSurfaceHeight(world, start.X, start.Y, start.X, start.Y);
            dummy.transform.SetPositionAndRotation(new Vector3(start.X, surfaceHeight, start.Y), Quaternion.identity);
            dummy.SetActive(true);
        }

        private void CreateDummy()
        {
            dummy = new GameObject("PCG First Person Test Dummy");
            dummy.transform.SetParent(transform, true);
            characterController = dummy.AddComponent<CharacterController>();
            characterController.height = 1.8f;
            characterController.radius = collisionRadius;
            characterController.center = new Vector3(0f, .9f, 0f);
            characterController.stepOffset = Mathf.Min(.5f, maximumStepHeight);
            dummy.SetActive(false);
        }

        private void OnGUI()
        {
            if (demoOverlay != null && !demoOverlay.IsVisible) return;
            GUIStyle style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft, fontSize = 14 };
            string text = isFirstPerson ? "FIRST PERSON TEST  |  WASD Move  •  Shift Sprint  •  Mouse Look  •  F1 Top View  •  Esc Cursor" : "F1  |  Enter first-person test mode after generating a world";
            GUI.Box(new Rect(12f, Screen.height - 42f, Mathf.Min(720f, Screen.width - 24f), 30f), text, style);
            if (isFirstPerson)
            {
                GUI.Label(new Rect(Screen.width * .5f - 8f, Screen.height * .5f - 12f, 16f, 24f), "+", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, normal = { textColor = Color.white } });
            }
        }
    }
}
