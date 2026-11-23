#ifndef CREEK_CLOUD_PUFF_INCLUDED
#define CREEK_CLOUD_PUFF_INCLUDED
#include "CreekClock.hlsl"
// Shared vertex animation for the cloud body and its fog shell, so both move together.
TEXTURE3D(_NoiseTex); SAMPLER(sampler_NoiseTex);
CBUFFER_START(UnityPerMaterial)
half4 _Tint,_ShadowTint,_SunTint;
float _SunRim,_Billow,_BillowScale,_RollSpeed,_Swell,_Drift,_Seed;
float _ShellInner,_ShellOuter,_ShellOpacity,_WispScale,_WispSpeed;
CBUFFER_END

float3 PuffNoiseCoord(float3 p) {
 float t=CreekTime()*_RollSpeed;
 return p*_BillowScale+_Seed*float3(.371,.113,.733)+float3(t,-t*.6,t*.3);
}
// Two octaves of billow noise that scroll and rise, so bumps roll across the surface and reform.
float PuffHeight(float3 p) {
 float3 uvw=PuffNoiseCoord(p);
 float t=CreekTime()*_RollSpeed;
 float broad=SAMPLE_TEXTURE3D_LOD(_NoiseTex,sampler_NoiseTex,uvw,0).r;
 float fine=SAMPLE_TEXTURE3D_LOD(_NoiseTex,sampler_NoiseTex,uvw*2.3+float3(-t*2.1,-t*.9,t*1.3),0).r;
 return broad*.7+fine*.3;
}
// The broad octave alone: one 3D read. The body uses it for the neighbour samples that rebuild its
// normal, where the fine octave is not worth two more reads per vertex on distant scenery.
float PuffBroad(float3 p) { return SAMPLE_TEXTURE3D_LOD(_NoiseTex,sampler_NoiseTex,PuffNoiseCoord(p),0).r; }
float PuffFine(float3 p) {
 float t=CreekTime()*_RollSpeed;
 return SAMPLE_TEXTURE3D_LOD(_NoiseTex,sampler_NoiseTex,PuffNoiseCoord(p)*2.3+float3(-t*2.1,-t*.9,t*1.3),0).r;
}
float PuffSwell(float2 mask) { return sin(CreekTime()*.07+mask.x*6.2832+_Seed)*.03*_Swell; }
// mask.x = lobe phase, mask.y = 0 on the flat base and 1 on the billowing top. height is PuffHeight or an estimate of it.
float3 PuffLift(float3 p,float3 n,float2 mask,float height,float shell) {
 return p+n*(((height-.4)*_Billow+PuffSwell(mask))*lerp(.2,1,mask.y)+shell);
}
float3 PuffDisplace(float3 p,float3 n,float2 mask,float shell) { return PuffLift(p,n,mask,PuffHeight(p),shell); }
float2 PuffSway() {
 float angle=sin(CreekTime()*.018+_Seed)*.035;
 return float2(sin(angle),cos(angle));
}
float3 PuffTurn(float3 v) {
 float2 sc=PuffSway();
 return float3(sc.y*v.x+sc.x*v.z,v.y,-sc.x*v.x+sc.y*v.z);
}
// Whole-cloud sway and drift in the world.
float3 PuffMove(float3 p) {
 float t=CreekTime();
 // A shared wind direction connects the depth layers. Seed offsets keep them from
 // moving in lockstep; bounded motion has no wrap/pop and fits the authored bounds.
 // A roughly three-minute drift cycle keeps the distant clouds in the background.
 float wind=sin(t*.04+_Seed*.08);
 float3 direction=normalize(mul((float3x3)GetWorldToObjectMatrix(),float3(1,0,.28)));
 return PuffTurn(p)+(direction*wind+float3(0,sin(t*.044+_Seed)*.18,0))*_Drift;
}
#endif
