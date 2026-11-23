Shader "Creek Diorama/Cloud Puff"
{
 // Solid cloud body: an opaque mesh whose billows are animated in the vertex stage.
 // Detail comes from geometry, so there is no ray march and no dither grain.
 Properties {
 _NoiseTex("Billow noise",3D)="gray"{}
 _Tint("Lit colour",Color)=(1,1,1,1)
 _ShadowTint("Shadow colour",Color)=(.55,.63,.82,1)
 _SunTint("Sun rim colour",Color)=(1,.93,.8,1)
 _SunRim("Sun rim strength",Range(0,1.5))=.5
 _Billow("Billow height",Range(0,.2))=.075
 _BillowScale("Billow frequency",Range(.3,4))=1.5
 _RollSpeed("Billow roll speed",Range(0,.1))=.025
 _Swell("Lobe swell",Range(0,2))=1
 _Drift("Whole-cloud drift",Range(0,.2))=.12
 _Seed("Seed",Float)=0
 [HideInInspector]_ShellInner("Shell inner",Float)=.035
 [HideInInspector]_ShellOuter("Shell outer",Float)=.08
 [HideInInspector]_ShellOpacity("Shell opacity",Float)=.5
 [HideInInspector]_WispScale("Wisp scale",Float)=2.5
 [HideInInspector]_WispSpeed("Wisp speed",Float)=.04
 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" "RenderType"="Opaque"}
 Pass {
 Tags {"LightMode"="UniversalForward"}
 ZWrite On Cull Back
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "CreekCloudPuff.hlsl"
 #include "CreekAnalyticAA.hlsl"
 struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;half4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V {float4 positionCS:SV_POSITION;float3 normalWS:TEXCOORD0;float3 positionWS:TEXCOORD1;float3 uvw:TEXCOORD2;half2 height:TEXCOORD3;UNITY_VERTEX_OUTPUT_STEREO};
 V vert(A i) {
 V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
 float3 n=normalize(i.normalOS);float2 mask=i.color.rg;
 // Four 3D reads per vertex instead of six: the position uses both octaves, the normal is rebuilt
 // from the broad octave alone at the vertex and at two neighbours.
 float broad=PuffBroad(i.positionOS.xyz);
 float3 p=PuffLift(i.positionOS.xyz,n,mask,broad*.7+PuffFine(i.positionOS.xyz)*.3,0);
 float3 t1=normalize(cross(n,abs(n.y)<.95?float3(0,1,0):float3(1,0,0)));
 float3 t2=cross(n,t1);
 float3 q=PuffLift(i.positionOS.xyz,n,mask,broad,0);
 float3 p1=PuffLift(i.positionOS.xyz+t1*.025,n,mask,PuffBroad(i.positionOS.xyz+t1*.025),0);
 float3 p2=PuffLift(i.positionOS.xyz+t2*.025,n,mask,PuffBroad(i.positionOS.xyz+t2*.025),0);
 float3 bumped=normalize(lerp(n,normalize(cross(p1-q,p2-q)),.85));
 o.positionWS=TransformObjectToWorld(PuffMove(p));
 o.positionCS=TransformWorldToHClip(o.positionWS);
 o.normalWS=TransformObjectToWorldNormal(PuffTurn(bumped));
 o.uvw=PuffNoiseCoord(i.positionOS.xyz)*3.1;
 o.height=half2(saturate(p.y*2.2+.5),i.color.b);
 return o;
 }
 half4 frag(V i):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 float3 N=normalize(i.normalWS);
 float3 V=normalize(GetCameraPositionWS()-i.positionWS);
 float3 L=_MainLightPosition.xyz;
 half fine=SAMPLE_TEXTURE3D(_NoiseTex,sampler_NoiseTex,i.uvw).r;
 half ndl=dot(N,L)*.5+.5+(fine-.5)*.16;
 // Two soft bands instead of a linear ramp: reads as layered cumulus.
 half band=CreekAASmooth(.22,.62,ndl)*.6+CreekAASmooth(.62,.9,ndl)*.4;
 half light=saturate(band*.8+i.height.x*.28-.1)*(1-i.height.y*.55);
 half3 col=lerp(_ShadowTint.rgb,_Tint.rgb,light);
 // Crowns catch a little of the sun's warmth and bellies sink toward the shadow colour, so each
 // cloud reads as a lit volume rather than a flat pale shape.
 col=lerp(col,col*_SunTint.rgb*1.05,i.height.x*i.height.x*.4);
 col=lerp(col,_ShadowTint.rgb*.94,(1-i.height.x)*(1-i.height.x)*.3);
 half fresnel=1-saturate(dot(N,V));fresnel*=fresnel*fresnel;
 half towardSun=saturate(dot(-V,L));towardSun*=towardSun;
 col+=_Tint.rgb*fresnel*.16+_SunTint.rgb*fresnel*towardSun*towardSun*_SunRim;
 return half4(saturate(col),1);
 }
 ENDHLSL
 }
 }
}
