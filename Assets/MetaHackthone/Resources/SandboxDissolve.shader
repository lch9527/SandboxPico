Shader "PhysicalAssembly/SandboxDissolve" {
Properties { _Dissolve("Dissolve",Range(0,1))=0 _Color("Body",Color)=(1,.66,.04,1) _Edge("Burning edge",Color)=(1,.96,.72,1) }
SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" } Pass {
Cull Back ZWrite On
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_instancing
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
float _Dissolve; half4 _Color; half4 _Edge;
CBUFFER_END
struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 positionCS:SV_POSITION;float3 positionOS:TEXCOORD0;half3 normalWS:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
V vert(A a){V o;UNITY_SETUP_INSTANCE_ID(a);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.positionOS=a.positionOS.xyz;o.normalWS=TransformObjectToWorldNormal(a.normalOS);return o;}
half4 frag(V i):SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float n=frac(sin(dot(floor(i.positionOS*120),float3(12.9898,78.233,39.425)))*43758.5453);
    clip(n-_Dissolve);
    // Molten gold body with simple top light so the shape still reads, and a white-hot rim where it is burning away.
    half shade=.72+.28*saturate(dot(normalize(i.normalWS),half3(.25,.93,.27)));
    half edge=1-saturate((n-_Dissolve)/.2);
    edge=edge*edge*saturate(_Dissolve*8);
    // Gold by default; a caller may tint the body and its edge (the intro's blue core).
    half3 body=_Color.rgb*shade;
    return half4(lerp(body,_Edge.rgb,edge),1);
}
ENDHLSL
} } }
