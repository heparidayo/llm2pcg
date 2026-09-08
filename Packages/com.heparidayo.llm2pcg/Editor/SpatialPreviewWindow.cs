using System;
using System.Linq;
using Llm2Pcg.Bridge;
using Llm2Pcg.Core.V4;
using Llm2Pcg.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Llm2Pcg.Editor
{
    // Primitive-only, disposable preview; no source art or prepared fixtures required.
    public sealed class SpatialPreviewWindow : EditorWindow
    {
        [Serializable] private sealed class Reply
        {
            public bool ok=true,experimental=true;
            public string target="unity-editor-preview",worldHash,semanticHash;
            public Rendering rendering;
            public Condition[] constraints;
        }
        [Serializable] private sealed class Rendering
        {public bool completed=true;public string profile,visualHash;public SpatialV4RenderCount[] counts;}
        [Serializable] private sealed class Condition
        {public string id,status;public int cellCount,borderContacts,centerContacts;}
        private SpatialV4HttpBridge bridge;
        private Scene scene;
        private Camera camera;
        private RenderTexture texture;
        private string message="Waiting for a resolved request from /spatial-v4.";

        [MenuItem("Tools/LLM2PCG/Experimental v4/Start HTTP Preview")]
        public static void Open()
        {
            var window=GetWindow<SpatialPreviewWindow>("Spatial v4 Preview");
            window.minSize=new Vector2(640,480);window.StartHttp();window.Show();
        }
        private void StartHttp()
        {
            if(bridge!=null&&bridge.IsRunning)return;
            bridge?.Dispose();bridge=new SpatialV4HttpBridge(Generate);
            try {bridge.Start();EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
            catch(Exception e){bridge.Dispose();bridge=null;message=e.Message;}
        }
        private void Pump()=>bridge?.Pump();
        private string Generate(SpatialRequest request)
        {
            var world=SpatialGenerator.Generate(request);
            Cleanup();
            try
            {
                scene=EditorSceneManager.NewPreviewScene();
                var root=new GameObject("Spatial v4 primitive preview");SceneManager.MoveGameObjectToScene(root,scene);
                var renderer=root.AddComponent<SpatialV4WorldRenderer>();renderer.Build(request,world);
                var sunObject=new GameObject("Preview sun");SceneManager.MoveGameObjectToScene(sunObject,scene);
                var sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.15f;sun.transform.rotation=Quaternion.Euler(50,-35,0);
                var cameraObject=new GameObject("Preview camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
                camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.1f,.14f,.18f);
                float span=Mathf.Max(world.width,world.height),height=world.elevationUnits.Max()*.25f;
                var center=new Vector3((world.width-1)*.5f,height*.3f,(world.height-1)*.5f);
                camera.orthographic=true;camera.orthographicSize=span*.59f+height*.2f;camera.nearClipPlane=.1f;camera.farClipPlane=3000;
                camera.transform.position=center+new Vector3(-span*.75f,span*1.3f,-span*.9f);camera.transform.rotation=Quaternion.LookRotation(center-camera.transform.position);
                texture=new RenderTexture(1280,900,24);texture.Create();camera.targetTexture=texture;camera.Render();
                message=world.worldType+" | Seed "+world.seed+" | "+renderer.ProfileVersion+"\nWorld "+world.worldHash+"\nSemantic "+world.semanticHash;
                Repaint();
                return JsonUtility.ToJson(new Reply {worldHash=world.worldHash,semanticHash=world.semanticHash,
                    rendering=new Rendering {profile=renderer.ProfileVersion,visualHash=renderer.VisualHash,counts=renderer.Counts},
                    constraints=world.constraints.Select(c=>new Condition{id=c.id,status=c.status,cellCount=c.cellCount,borderContacts=c.borderContacts,centerContacts=c.centerContacts}).ToArray()});
            }
            catch {Cleanup();throw;}
        }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Forest / Desert / Snowfield / Swamp. Primitive profile, no external assets. Loopback port 8089, at most 256×256. Close this window to stop. Your working scene is not modified.",MessageType.Info);
            if(bridge==null||!bridge.IsRunning) {if(GUILayout.Button("Start HTTP preview"))StartHttp();}
            EditorGUILayout.HelpBox(message,MessageType.None);
            var rect=GUILayoutUtility.GetRect(100,10000,100,10000);if(texture!=null)GUI.DrawTexture(rect,texture,ScaleMode.ScaleToFit,false);
        }
        private void Cleanup()
        {
            if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);scene=default;camera=null;
            if(texture!=null){texture.Release();DestroyImmediate(texture);texture=null;}
        }
        private void OnDisable(){EditorApplication.update-=Pump;bridge?.Dispose();bridge=null;Cleanup();}
    }
}
