Shader "Creek Diorama/Pastel Sky"
{
 Properties {
  _Zenith("Zenith",Color)=(.26,.48,.66,1)
  _Horizon("Horizon",Color)=(.73,.82,.83,1)
  _Ground("Below horizon",Color)=(.36,.51,.56,1)
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox"}
  Cull Off ZWrite Off
  Pass {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _Zenith, _Horizon, _Ground;
   CBUFFER_END
   struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct V { float4 positionCS:SV_POSITION; float3 direction:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
   V vert(A a) { V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS=TransformObjectToHClip(a.positionOS.xyz); o.direction=a.positionOS.xyz; return o; }
   half4 frag(V i):SV_Target {
    float3 d=normalize(i.direction); float y=d.y;
    half3 c=lerp(_Horizon.rgb,_Zenith.rgb,smoothstep(0,1,max(0,y)));
    c=lerp(c,_Ground.rgb,smoothstep(0,.65,max(0,-y)));
    // Broad daylight warmth; no sharp sun disc, texture, or post process.
    c+=half3(.045,.022,.002)*pow(saturate(dot(d,normalize(float3(-.5,.3,.8)))),8);
    return half4(c,1);
   }
   ENDHLSL
  }
 }
}
