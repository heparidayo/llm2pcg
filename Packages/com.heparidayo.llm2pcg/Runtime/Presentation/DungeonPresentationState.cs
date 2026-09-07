using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Presentation
{
    /// <summary>Reversible runtime-only lighting for the open-roof stone citadel presentation.</summary>
    internal sealed class DungeonPresentationState
    {
        private bool active;
        private Camera camera;
        private Light key;
        private RenderPipelineAsset previousPipeline, pipeline;
        private CameraClearFlags clearFlags;
        private Color background, sky, equator, ground, fogColor, lightColor;
        private AmbientMode ambientMode;
        private bool fog;
        private FogMode fogMode;
        private float fogStart, fogEnd, lightIntensity;
        private Quaternion lightRotation;

        public void Apply(Camera target, bool firstPerson)
        {
            if (!active)
            {
                camera=target; previousPipeline=QualitySettings.renderPipeline;
                clearFlags=camera.clearFlags; background=camera.backgroundColor;
                ambientMode=RenderSettings.ambientMode;sky=RenderSettings.ambientSkyColor;
                equator=RenderSettings.ambientEquatorColor;ground=RenderSettings.ambientGroundColor;
                fog=RenderSettings.fog;fogMode=RenderSettings.fogMode;fogColor=RenderSettings.fogColor;
                fogStart=RenderSettings.fogStartDistance;fogEnd=RenderSettings.fogEndDistance;
                key=RenderSettings.sun;
                if(key==null)
                    foreach(var candidate in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if(candidate.type==LightType.Directional && candidate.isActiveAndEnabled) { key=candidate;break; }
                if(key!=null) {lightColor=key.color;lightIntensity=key.intensity;lightRotation=key.transform.rotation;}
                pipeline=Resources.Load<RenderPipelineAsset>("PCGPresentation/DungeonCitadelPipeline");
                if(pipeline!=null) QualitySettings.renderPipeline=pipeline;
                camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.085f,.11f,.14f);
                RenderSettings.ambientMode=AmbientMode.Trilight;
                RenderSettings.ambientSkyColor=new Color(.43f,.50f,.56f);
                RenderSettings.ambientEquatorColor=new Color(.25f,.30f,.34f);
                RenderSettings.ambientGroundColor=new Color(.15f,.18f,.20f);
                if(key!=null) {key.color=new Color(.78f,.90f,1f);key.intensity=1.45f;key.transform.rotation=Quaternion.Euler(62, -32, 0);}
                active=true;
            }
            // Fog is for depth at eye level; overhead maps must remain readable at 128x128.
            RenderSettings.fog=firstPerson;
            RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogColor=new Color(.085f,.11f,.14f);
            RenderSettings.fogStartDistance=15;
            RenderSettings.fogEndDistance=75;
        }

        public void Restore()
        {
            if(!active) return;
            if(QualitySettings.renderPipeline==pipeline) QualitySettings.renderPipeline=previousPipeline;
            if(camera!=null) {camera.clearFlags=clearFlags;camera.backgroundColor=background;}
            RenderSettings.ambientMode=ambientMode;RenderSettings.ambientSkyColor=sky;
            RenderSettings.ambientEquatorColor=equator;RenderSettings.ambientGroundColor=ground;
            RenderSettings.fog=fog;RenderSettings.fogMode=fogMode;RenderSettings.fogColor=fogColor;
            RenderSettings.fogStartDistance=fogStart;RenderSettings.fogEndDistance=fogEnd;
            if(key!=null) {key.color=lightColor;key.intensity=lightIntensity;key.transform.rotation=lightRotation;}
            active=false;
        }
    }
}
