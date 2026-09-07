using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Presentation
{
    /// <summary>Scoped art direction for Forest derivatives; never changes the Forest baseline.</summary>
    internal sealed class NatureBiomePresentationState
    {
        private string activeWorld;
        private Camera camera;
        private Light light;
        private RenderPipelineAsset previousPipeline, pipeline;
        private CameraClearFlags clearFlags;
        private Color background, sky, equator, ground, fogColor, lightColor;
        private AmbientMode ambientMode;
        private bool fog;
        private FogMode fogMode;
        private float fogStart, fogEnd, intensity;
        private Quaternion rotation;

        public static bool Supports(string world) => world=="Desert" || world=="Snowfield" || world=="Swamp";

        public void Apply(Camera target,string world,bool firstPerson)
        {
            if(activeWorld!=null && (activeWorld!=world || camera!=target)) Restore();
            bool desert=world=="Desert", snow=world=="Snowfield";
            Color horizon=desert ? new Color(.76f,.69f,.55f) : snow ? new Color(.65f,.76f,.83f) : new Color(.28f,.40f,.37f);
            if(activeWorld==null)
            {
                camera=target;previousPipeline=QualitySettings.renderPipeline;
                clearFlags=camera.clearFlags;background=camera.backgroundColor;
                ambientMode=RenderSettings.ambientMode;sky=RenderSettings.ambientSkyColor;
                equator=RenderSettings.ambientEquatorColor;ground=RenderSettings.ambientGroundColor;
                fog=RenderSettings.fog;fogMode=RenderSettings.fogMode;fogColor=RenderSettings.fogColor;
                fogStart=RenderSettings.fogStartDistance;fogEnd=RenderSettings.fogEndDistance;
                light=RenderSettings.sun;
                if(light==null)
                    foreach(var candidate in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if(candidate.type==LightType.Directional && candidate.isActiveAndEnabled) {light=candidate;break;}
                if(light!=null) {lightColor=light.color;intensity=light.intensity;rotation=light.transform.rotation;}
                pipeline=Resources.Load<RenderPipelineAsset>("PCGPresentation/NatureBiomesPipeline");
                if(pipeline!=null) QualitySettings.renderPipeline=pipeline;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=horizon;
                RenderSettings.ambientMode=AmbientMode.Trilight;
                RenderSettings.ambientSkyColor=desert ? new Color(.68f,.73f,.74f) : snow ? new Color(.68f,.78f,.86f) : new Color(.47f,.59f,.53f);
                RenderSettings.ambientEquatorColor=desert ? new Color(.58f,.47f,.32f) : snow ? new Color(.47f,.59f,.69f) : new Color(.27f,.39f,.31f);
                RenderSettings.ambientGroundColor=desert ? new Color(.34f,.25f,.17f) : snow ? new Color(.32f,.41f,.49f) : new Color(.16f,.24f,.20f);
                if(light!=null)
                {
                    light.color=desert ? new Color(1f,.86f,.68f) : snow ? new Color(.91f,.96f,1f) : new Color(.90f,.96f,.80f);
                    light.intensity=desert ? 1.8f : snow ? 1.6f : 1.5f;
                    light.transform.rotation=Quaternion.Euler(desert?36:snow?32:48,-35,0);
                }
                activeWorld=world;
            }
            RenderSettings.fog=firstPerson;
            RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=horizon;
            RenderSettings.fogStartDistance=desert?35:snow?25:12;
            RenderSettings.fogEndDistance=desert?160:snow?120:85;
        }

        public void Restore()
        {
            if(activeWorld==null) return;
            if(QualitySettings.renderPipeline==pipeline) QualitySettings.renderPipeline=previousPipeline;
            if(camera!=null) {camera.clearFlags=clearFlags;camera.backgroundColor=background;}
            RenderSettings.ambientMode=ambientMode;RenderSettings.ambientSkyColor=sky;
            RenderSettings.ambientEquatorColor=equator;RenderSettings.ambientGroundColor=ground;
            RenderSettings.fog=fog;RenderSettings.fogMode=fogMode;RenderSettings.fogColor=fogColor;
            RenderSettings.fogStartDistance=fogStart;RenderSettings.fogEndDistance=fogEnd;
            if(light!=null) {light.color=lightColor;light.intensity=intensity;light.transform.rotation=rotation;}
            activeWorld=null;
        }
    }
}
