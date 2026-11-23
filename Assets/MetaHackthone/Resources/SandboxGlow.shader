// Soft glow for sandbox feedback. One shader serves both billboards and lines:
// _Line 0 shapes a round sparkle from the quad UV, _Line 1 shapes a soft band across a LineRenderer.
// Output is premultiplied so the glow adds light but still reads over bright surfaces and passthrough.
Shader "PhysicalAssembly/SandboxGlow" {
Properties {
    _Line("Line (0 sprite, 1 line)",Float)=0
    _Intensity("Intensity",Range(0,3))=1.3
    // High enough that gold still reads over a bright sky instead of washing out to white.
    _Opacity("Opacity",Range(0,1))=.8
    _Flare("Flare",Range(0,1))=.45
}
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" } Pass {
Cull Off ZWrite Off ZTest LEqual Blend One OneMinusSrcAlpha
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_instancing
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
float _Line; float _Intensity; float _Opacity; float _Flare;
CBUFFER_END
struct A {float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO};
V vert(A a){V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS=TransformObjectToHClip(a.positionOS.xyz); o.color=a.color; o.uv=a.uv; return o;}
half4 frag(V i):SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half body, core;
    if(_Line>.5) {
        half f=saturate(1-abs(i.uv.y*2-1));
        // A solid band inside a soft skirt, so the ring keeps a visible body as it thins.
        body=f*f*.6+smoothstep(.35,.85,f)*.7; core=smoothstep(.6,1,f);
    } else {
        float2 p=i.uv*2-1;
        half f=saturate(1-length(p));
        // A short, fat four-point glint (an astroid) instead of a hairline cross: it reads as a shape
        // at headset resolution and does not shimmer. Faint background glows (low alpha) stay plain discs.
        half star=saturate(1-pow(abs(p.x),.62)-pow(abs(p.y),.62));
        half solid=smoothstep(.3,.65,i.color.a);
        half glint=smoothstep(0,.3,star)*solid;
        // A coloured disc inside a wide soft halo: the disc carries the colour, the halo carries the glow.
        // Faint glows drop the disc and keep only the smooth falloff, so overlapping ones merge into one shape.
        body=f*f*lerp(1.15,.6,solid)+smoothstep(.45,.9,f)*.6*solid+glint*_Flare; core=(smoothstep(.72,1,f)+glint*.35)*solid;
    }
    half a=saturate(body)*i.color.a;
    half3 rgb=lerp(i.color.rgb,half3(1,1,1),saturate(core)*.55);
    return half4(rgb*a*_Intensity,a*_Opacity);
}
ENDHLSL
} } }
