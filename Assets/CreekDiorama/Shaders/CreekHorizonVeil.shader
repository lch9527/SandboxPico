Shader "Creek Diorama/Soft Horizon Veil"
{
 Properties{_Horizon("Horizon",Color)=(.52,.68,.74,.88) _Zenith("Upper sky",Color)=(.25,.44,.61,.82)}
 SubShader{
 Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-100" "RenderType"="Transparent"}
 Pass{
 Cull Off ZWrite Off ZTest LEqual
 Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 half4 _Horizon,_Zenith;
 CBUFFER_END
 struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
 V vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o;}
 half4 frag(V i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);half4 c=lerp(_Horizon,_Zenith,smoothstep(.30,.90,i.uv.y));float edge=smoothstep(0,.10,i.uv.x)*smoothstep(0,.10,1-i.uv.x)*smoothstep(0,.14,i.uv.y)*smoothstep(0,.10,1-i.uv.y);c.a*=edge;return c;}
 ENDHLSL
 }
 }
}
