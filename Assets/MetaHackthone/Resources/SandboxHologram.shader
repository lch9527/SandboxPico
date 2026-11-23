// The core's building light, in the manner of a construction blueprint: real 3D meshes drawn as see-through
// light. Used two ways by the first-entry intro (SandboxIntroEffects):
//   - the blueprint: a copy of each part, standing where the part will go, before the core pulls it in
//     (_Fill a little above 0: faint body, glowing edges, bands of light running up through it);
//   - the grab: laid over a real part, as an extra material, while the core's tether holds it (_Fill 0: only
//     the glowing edges and the bands, so the part itself shows through).
// Light is added over what is behind (premultiplied, alpha kept low), so over passthrough it reads as light in
// the room rather than as a dark shape. Faces at a glancing angle to the eye glow most; both sides are drawn so
// the far edges of a blueprint show through its near faces.
Shader "PhysicalAssembly/SandboxHologram" {
Properties {
    _Glow("Glow",Color)=(.25,.7,1,1)
    _Alpha("Strength",Range(0,2))=1
    _Fill("Body",Range(0,1))=.12
    _Edge("Edge sharpness",Range(.5,8))=2.5
    _Lines("Bands per metre",Float)=60
    _LineSpeed("Bands: metres a second",Float)=.05
}
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" } Pass {
Cull Off ZWrite Off ZTest LEqual Blend One OneMinusSrcAlpha
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_instancing
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
half4 _Glow; half _Alpha; half _Fill; half _Edge; float _Lines; float _LineSpeed;
CBUFFER_END
struct A {float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO};
V vert(A a){V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    o.positionWS=TransformObjectToWorld(a.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(a.normalOS); return o;}
half4 frag(V i):SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half3 view=normalize(GetWorldSpaceViewDir(i.positionWS));
    half edge=pow(1-abs(dot(normalize(i.normalWS),view)),_Edge);
    // Thin bands of light climbing through the part, as if it were being drawn in layers.
    half band=pow(saturate(.5+.5*sin((i.positionWS.y*_Lines-_Time.y*_LineSpeed*_Lines)*6.2832)),12);
    half light=(edge*1.4+_Fill+band*.35)*_Alpha;
    return half4(_Glow.rgb*light,saturate(_Fill*_Alpha*.6));
}
ENDHLSL
} } }
