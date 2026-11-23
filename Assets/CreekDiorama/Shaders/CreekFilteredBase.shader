Shader "Creek Diorama/Filtered Display Base"
{
 Properties {
  _BaseColor("Enamel",Color)=(.35,.4,.45,1)
  _Copper("Copper band",Color)=(.7,.5,.3,1)
  _Steel("Steel trim",Color)=(.48,.55,.57,1)
  _Dark("Recess",Color)=(.2,.25,.28,1)
  _Amber("Lamp",Color)=(1,.6,.2,1)
  _Unit("Authoring unit",Float)=.19073
 }

 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
  Pass {
   Name "ForwardLit"
   Tags {"LightMode"="UniversalForward"}
   Cull Back ZWrite On
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_instancing
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS
   #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Assets/CreekDiorama/Shaders/CreekAnalyticAA.hlsl"
   CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor,_Copper,_Steel,_Dark,_Amber;
    float _Unit;
   CBUFFER_END
   struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float3 local:TEXCOORD2;half fog:TEXCOORD3;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
   Varyings Vert(Attributes v) {
    Varyings o=(Varyings)0;UNITY_SETUP_INSTANCE_ID(v);UNITY_TRANSFER_INSTANCE_ID(v,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;
    o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.local=v.positionOS.xyz;o.fog=ComputeFogFactor(p.positionCS.z);return o;
   }
   half4 Frag(Varyings i):SV_Target {
    UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float3 p=i.local/max(_Unit,1e-5);
    // One opaque surface: integrate trim/decoration coverage instead of layering
    // nearly coplanar strips, rivets and lamp sockets over the enamel.
    float copper=CreekBand(p.y-.30,.08);
    float steel=CreekAAStep(.69,p.y);
    half3 color=lerp(_BaseColor.rgb,_Copper.rgb,copper);
    color=lerp(color,_Steel.rgb,steel);
    float front=CreekAAStep(3.64,p.z);
    float cycle=p.x/2.9+.5;
    float x=(frac(cycle)-.5)*2.9;
    float pixelX=max(fwidth(p.x),1e-5);
    float socket=(saturate((.535-x)/pixelX+.5)-saturate((-.535-x)/pixelX+.5))*CreekBand(p.y-.49,.12)*front;
    float lamp=(saturate((.395-x)/pixelX+.5)-saturate((-.395-x)/pixelX+.5))*CreekBand(p.y-.49,.045)*front;
    float strap=CreekPeriodicFill((p.x-.59)/2.9+.075/2.9,.15/2.9,fwidth(p.x/2.9));
    strap+=CreekPeriodicFill((p.x+.59)/2.9+.075/2.9,.15/2.9,fwidth(p.x/2.9));
    strap*=CreekBand(p.y-.46,.31)*front;
    color=lerp(color,_Dark.rgb,socket*.7);
    color=lerp(color,_Steel.rgb,saturate(strap)*.7);
    color=lerp(color,_Amber.rgb,lamp);
    InputData data=(InputData)0;data.positionWS=i.positionWS;data.normalWS=normalize(i.normalWS);
    data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);data.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
    data.bakedGI=SampleSH(data.normalWS);data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);data.shadowMask=1;
    SurfaceData surface=(SurfaceData)0;surface.albedo=color;surface.alpha=1;surface.smoothness=.18;surface.metallic=.15;surface.emission=_Amber.rgb*lamp*.3;surface.occlusion=1;surface.normalTS=half3(0,0,1);
    half4 output=UniversalFragmentPBR(data,surface);output.rgb=MixFog(output.rgb,i.fog);return output;
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
