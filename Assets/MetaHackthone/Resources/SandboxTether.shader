// The core's tether: a 3D tube of light from the core to a part it is pulling in (mesh built each frame by
// SandboxIntroEffects). Brightest along its middle as seen from the eye, softer at its rim, with pulses of
// light running down it toward the core. uv.x runs 0 at the core to 1 at the part; uv.y goes around the tube.
// Light is added over what is behind (premultiplied, alpha 0), so over passthrough it reads as light in the room.
Shader "PhysicalAssembly/SandboxTether" {
Properties {
    _Glow("Glow",Color)=(.3,.75,1,1)
    _Alpha("Strength",Range(0,2))=1
    _Pulses("Pulses along it",Float)=5
    _PulseSpeed("Pulses: lengths a second",Float)=2.5
}
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" } Pass {
Cull Back ZWrite Off ZTest LEqual Blend One OneMinusSrcAlpha
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_instancing
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
half4 _Glow; half _Alpha; float _Pulses; float _PulseSpeed;
CBUFFER_END
struct A {float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO};
V vert(A a){V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.positionWS=TransformObjectToWorld(a.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(a.normalOS); o.uv=a.uv; return o;}
half4 frag(V i):SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half3 view=normalize(GetWorldSpaceViewDir(i.positionWS));
    half facing=abs(dot(normalize(i.normalWS),view));
    half body=pow(facing,1.5);
    // Pulses travel from the part to the core: the core is drawing it in.
    half pulse=pow(saturate(.5+.5*sin((i.uv.x*_Pulses+_Time.y*_PulseSpeed)*6.2832)),5);
    // Fades in from the core and out at the part's end, so neither end is a hard cut.
    half ends=smoothstep(0,.06,i.uv.x)*smoothstep(1,.9,i.uv.x);
    half light=(body*.55+pulse*body*1.1+(1-facing)*.15)*ends*_Alpha;
    return half4(lerp(_Glow.rgb,half3(1,1,1),pulse*body*.35)*light,0);
}
ENDHLSL
} } }
