// A part that exists only where the intro's light reaches. Close to the light it is solid and shaded from the
// light's side; toward the edge of the light's reach it fades out; beyond it nothing is drawn at all, so over
// a darkened room or passthrough there is not even a silhouette. The light is one point set through globals
// by SandboxIntroEffects: the blue spark while it flies, then the building core.
Shader "PhysicalAssembly/SandboxIntroLit" {
Properties {
    _BaseColor("Base colour",Color)=(.89,.6,.29,1)
}
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" } Pass {
Cull Back ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_instancing
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
half4 _BaseColor;
CBUFFER_END
float4 _SandboxIntroLight;      // xyz: world position of the light; w: how far it reaches, in metres
half4 _SandboxIntroLightTint;   // rgb: its colour; a: its strength, 0 to 1
struct A {float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO};
V vert(A a){V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionWS=TransformObjectToWorld(a.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(a.normalOS); return o;}
half4 frag(V i):SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float3 toLight=_SandboxIntroLight.xyz-i.positionWS; float d=length(toLight);
    half reach=saturate(1-d/max(.001,_SandboxIntroLight.w)); reach=reach*reach*(3-2*reach);
    // Faces turned to the light are bright, the far side keeps a little fill so the shape still reads.
    half toward=saturate(dot(normalize(i.normalWS),toLight/max(d,.0001)));
    half3 rgb=_BaseColor.rgb*lerp(half3(1,1,1),_SandboxIntroLightTint.rgb,.5)*(.4+.75*toward);
    return half4(rgb,saturate(reach*1.5)*_SandboxIntroLightTint.a);
}
ENDHLSL
} } }
