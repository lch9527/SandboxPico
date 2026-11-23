Shader "Creek Diorama/Horizon Palette"
{
 // Distant floating islands baked to vertex colours (haze and lighting included). Still one opaque,
 // textureless, unlit pass; the vertex stage adds a slow float and the colour gets a vertical grade.
 // _Brightness 1 leaves the result untouched. The sandbox intro lowers it to bring the horizon out of darkness.
 Properties {
  _Brightness("Brightness",Range(0,1))=1
  _FloatHeight("Float: rise and fall (world metres)",Range(0,1))=0
  _FloatSpeed("Float: radians a second",Range(0,2))=.55
  _FloatSpread("Float: phase change per world metre",Range(0,.2))=.05
  _Haze("Haze the undersides sink into",Color)=(.62,.75,.88,1)
  _HazeAmount("Haze at the lowest point",Range(0,1))=.34
  _SunLift("Warm lift at the highest point",Range(0,.5))=.16
  _GradeLow("Grade: object height of the lowest point",Float)=-.44
  _GradeHigh("Grade: object height of the highest point",Float)=.74
 }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
  Pass {
   Tags { "LightMode"="UniversalForward" }
   Cull Back ZWrite On
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half _Brightness; float _FloatHeight; float _FloatSpeed; float _FloatSpread; half4 _Haze; half _HazeAmount; half _SunLift; float _GradeLow; float _GradeHigh;
   CBUFFER_END
   struct A { float4 positionOS:POSITION; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct V { float4 positionCS:SV_POSITION; half4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
   V vert(A a) {
    V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 world=TransformObjectToWorld(a.positionOS.xyz);
    // Every island of a bank is baked into one mesh, so the float is a slow wave across the bank: the
    // phase changes little within one island (it rises and falls almost rigidly, with a slight rock)
    // and a lot between islands, so neighbours are never in step. One sine per vertex, no scripts.
    world.y+=sin(_Time.y*_FloatSpeed+dot(world.xz,float2(1,.62))*_FloatSpread)*_FloatHeight;
    o.positionCS=TransformWorldToHClip(world);
    // Vertical grade, done per vertex: undersides fall away into the sky's haze, crowns warm slightly.
    half height=saturate((a.positionOS.y-_GradeLow)/max(_GradeHigh-_GradeLow,1e-4));
    half3 colour=lerp(_Haze.rgb,a.color.rgb,1-_HazeAmount*(1-height)*(1-height));
    colour*=1+_SunLift*height*half3(1,.86,.6);
    o.color=half4(colour,a.color.a);
    return o;
   }
   half4 frag(V i):SV_Target { return half4(i.color.rgb*_Brightness,1); }
   ENDHLSL
  }
 }
}
