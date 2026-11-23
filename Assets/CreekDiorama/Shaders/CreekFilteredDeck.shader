Shader "Creek Diorama/Filtered Bridge Deck"
{
 Properties {
  _Wood0("Wood 0",Color)=(.64,.31,.09,1)
  _Wood1("Wood 1",Color)=(.78,.42,.14,1)
  _Wood2("Wood 2",Color)=(.89,.53,.22,1)
  _Wood3("Wood 3",Color)=(.72,.35,.105,1)
  _Wood4("Wood 4",Color)=(.82,.46,.18,1)
  _Pitch("Board spacing",Float)=.35
  _Width("Deck width",Float)=2.8
  _GapDuty("Seam width / board spacing",Range(.01,.3))=.1
  _GapShade("Seam brightness",Range(0,1))=.38
  _Smoothness("Smoothness",Range(0,1))=.25
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
    half4 _Wood0,_Wood1,_Wood2,_Wood3,_Wood4;
    float _Pitch,_Width,_GapDuty,_GapShade,_Smoothness;
   CBUFFER_END
   struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
   Varyings Vert(Attributes v) {
    Varyings o=(Varyings)0;UNITY_SETUP_INSTANCE_ID(v);UNITY_TRANSFER_INSTANCE_ID(v,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;
    o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.uv=v.uv;o.fog=ComputeFogFactor(p.positionCS.z);return o;
   }
   half3 BoardColor(float cell) {
    // The authored 0.8 m source module repeats 16 boards with five timber colors.
    float moduleCell=cell-floor(cell/16)*16;
    float index=moduleCell-floor(moduleCell/5)*5;
    return index<.5?_Wood0.rgb:index<1.5?_Wood1.rgb:index<2.5?_Wood2.rgb:index<3.5?_Wood3.rgb:_Wood4.rgb;
   }
   half4 Frag(Varyings i):SV_Target {
    UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float coordinate=i.uv.y/max(_Pitch,1e-5);
    // Differentiate the continuous coordinate BEFORE frac/floor. Integrating
    // coverage keeps subpixel seams faint instead of rounding each to a black pixel.
    float footprint=max(fwidth(coordinate),1e-5);
    float cell=floor(coordinate),fraction=frac(coordinate);
    float previous=saturate(.5-fraction/footprint),next=saturate(.5-(1-fraction)/footprint);
    half3 color=BoardColor(cell)*(1-previous-next)+BoardColor(cell-1)*previous+BoardColor(cell+1)*next;
    half3 mean=(_Wood0.rgb*4+_Wood1.rgb*3+_Wood2.rgb*3+_Wood3.rgb*3+_Wood4.rgb*3)/16;
    color=lerp(color,mean,smoothstep(1,3,footprint));
    float seam=CreekPeriodicFill(coordinate+_GapDuty*.5,_GapDuty,footprint);
    color*=lerp(1,_GapShade,seam);
    // Far LOD fasteners: coverage-filtered marks, not additional tiny meshes.
    float x=i.uv.x/max(_Width,1e-5);
    float across=min(abs(x-.08),abs(x-.92))*(_Width/_Pitch);
    float2 nailUV=float2(across,fraction-.5);
    float nailWidth=max(length(float2(fwidth(i.uv.x/_Pitch),footprint)),1e-5);
    float nail=saturate(.5+(.045-length(nailUV))/nailWidth)*min(1,.09/nailWidth);
    color=lerp(color,color*.45,nail);
    InputData data=(InputData)0;data.positionWS=i.positionWS;data.normalWS=normalize(i.normalWS);
    data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);data.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
    data.bakedGI=SampleSH(data.normalWS);data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);data.shadowMask=1;
    SurfaceData surface=(SurfaceData)0;surface.albedo=color;surface.alpha=1;surface.smoothness=_Smoothness;surface.occlusion=1;surface.normalTS=half3(0,0,1);
    half4 output=UniversalFragmentPBR(data,surface);output.rgb=MixFog(output.rgb,i.fog);return output;
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
