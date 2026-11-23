Shader "MetaHackthone/Victory Particles" {
 Properties{_Soft("Soft sparks",Float)=1}
 SubShader{Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20"}
 Pass{Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha Cull Off ZWrite Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float _Soft;
 CBUFFER_END
 struct A{float4 positionOS:POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V{float4 positionCS:SV_POSITION;half4 color:COLOR;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
 V vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.color=i.color;o.uv=i.uv;return o;}
 half4 frag(V i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);float2 p=i.uv*2-1;float soft=saturate(1-dot(p,p));float paper=(1-smoothstep(.72,.95,abs(p.x)))*(1-smoothstep(.38,.6,abs(p.y)));return half4(i.color.rgb,i.color.a*lerp(paper,soft*soft,_Soft));}
 ENDHLSL
 }}
}
