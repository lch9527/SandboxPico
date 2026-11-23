// Soft haze billboard for the intro orb. A few of these, large and overlapping, stand in for volumetric fog:
// no disc and no white core, only a smooth falloff, so stacked sprites read as one glowing cloud.
// Output is premultiplied like SandboxGlow, so the haze adds light over a darkened room or passthrough.
Shader "PhysicalAssembly/SandboxFog" {
Properties {
    _Intensity("Intensity",Range(0,3))=1
    _Opacity("Opacity",Range(0,1))=.35
}
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" } Pass {
Cull Off ZWrite Off ZTest LEqual Blend One OneMinusSrcAlpha
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_instancing
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
float _Intensity; float _Opacity;
CBUFFER_END
struct A {float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO};
V vert(A a){V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS=TransformObjectToHClip(a.positionOS.xyz); o.color=a.color; o.uv=a.uv; return o;}
half4 frag(V i):SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float2 p=i.uv*2-1;
    half f=saturate(1-length(p));
    // Smooth all the way to the edge of the quad, brightening toward the middle.
    half a=f*f*(3-2*f)*i.color.a;
    half3 rgb=lerp(i.color.rgb,half3(1,1,1),f*f*f*.35);
    return half4(rgb*a*_Intensity,a*_Opacity);
}
ENDHLSL
} } }
