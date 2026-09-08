Shader "LLM2PCG/SpatialV4VertexColor"
{
    Properties { _Color("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; half3 normalWS:TEXCOORD0; half4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.color=v.color*_Color; return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                Light light=GetMainLight();
                half shade=saturate(dot(normalize(i.normalWS),light.direction));
                return half4(i.color.rgb*(half3(.38,.38,.38)+light.color*shade*.62),1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
