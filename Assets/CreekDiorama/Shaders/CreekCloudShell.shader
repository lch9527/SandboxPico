Shader "Creek Diorama/Cloud Shell"
{
 // Soft haze around the cloud body: the same animated mesh drawn once more, inflated along its
 // normals, fading to nothing at the silhouette. Analytic, so no ray march and no dither.
 // It used to be two shells (inner and outer). These clouds are far and large on screen, so the second
 // transparent layer cost a full extra pass of overdraw per cloud for little visible gain: one shell
 // between the two old offsets, a little denser, now does the work.
 Properties {
 _NoiseTex("Billow noise",3D)="gray"{}
 _Tint("Lit colour",Color)=(1,1,1,1)
 _ShadowTint("Shadow colour",Color)=(.55,.63,.82,1)
 _ShellInner("Inner shell offset",Range(0,.1))=.035
 _ShellOuter("Outer shell offset",Range(0,.2))=.08
 _ShellOpacity("Haze opacity",Range(0,1))=.5
 _WispScale("Wisp frequency",Range(.5,8))=2.5
 _WispSpeed("Wisp speed",Range(0,.2))=.04
 _Billow("Billow height",Range(0,.2))=.075
 _BillowScale("Billow frequency",Range(.3,4))=1.5
 _RollSpeed("Billow roll speed",Range(0,.1))=.025
 _Swell("Lobe swell",Range(0,2))=1
 _Drift("Whole-cloud drift",Range(0,.2))=.12
 _Seed("Seed",Float)=0
 [HideInInspector]_SunTint("Sun rim colour",Color)=(1,.93,.8,1)
 [HideInInspector]_SunRim("Sun rim strength",Float)=.5
 }
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "CreekCloudPuff.hlsl"
 #include "CreekAnalyticAA.hlsl"
 struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V {float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0;float3 positionWS:TEXCOORD1;float3 uvw:TEXCOORD2;half height:TEXCOORD3;UNITY_VERTEX_OUTPUT_STEREO};
 V ShellVert(A i,float offset) {
 V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
 float3 n=normalize(i.normalOS);
 float3 p=PuffDisplace(i.positionOS.xyz,n,i.color.rg,offset);
 o.positionWS=TransformObjectToWorld(PuffMove(p));
 o.positionCS=TransformWorldToHClip(o.positionWS);
 o.normalWS=TransformObjectToWorldNormal(PuffTurn(n));
 float t=CreekTime()*_WispSpeed;
 o.uvw=i.positionOS.xyz*_WispScale+_Seed*float3(.53,.29,.17)+float3(t,-t*.7,t*.4);
 o.height=saturate(p.y*2.2+.5);
 return o;
 }
 half4 ShellFrag(V i,half strength) {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 float3 N=normalize(i.normalWS);
 float3 V=normalize(GetCameraPositionWS()-i.positionWS);
 // Dense facing the viewer, dissolving at the outline.
 half facing=saturate(dot(N,V));
 half wisp=SAMPLE_TEXTURE3D(_NoiseTex,sampler_NoiseTex,i.uvw).r;
 half alpha=CreekAASmooth(0,.75,facing-(1-wisp)*.35)*_ShellOpacity*strength;
 half light=saturate((dot(N,_MainLightPosition.xyz)*.5+.5)*.6+i.height*.45);
 half3 col=lerp(_ShadowTint.rgb,_Tint.rgb,light);
 return half4(col*alpha,alpha);
 }
 ENDHLSL
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent-100" "RenderType"="Transparent" "IgnoreProjector"="True"}
 // Premultiplied so framebuffer alpha stays correct over passthrough.
 Blend One OneMinusSrcAlpha
 ZWrite Off ZTest LEqual Cull Back
 Pass {
 Name "Haze"
 Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 V vert(A i){return ShellVert(i,lerp(_ShellInner,_ShellOuter,.65));}
 half4 frag(V i):SV_Target{return ShellFrag(i,1.05);}
 ENDHLSL
 }
 }
}
