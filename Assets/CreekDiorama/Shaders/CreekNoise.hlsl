#ifndef CREEK_NOISE_INCLUDED
#define CREEK_NOISE_INCLUDED
#include "CreekClock.hlsl"
#include "CreekAnalyticAA.hlsl"
float CreekHash(float2 p) { p = frac(p * float2(123.34, 345.45)); p += dot(p,p+34.345); return frac(p.x*p.y); }
float CreekNoise(float2 p)
{
    float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
    return lerp(lerp(CreekHash(i),CreekHash(i+float2(1,0)),f.x),lerp(CreekHash(i+float2(0,1)),CreekHash(i+1),f.x),f.y);
}
float CreekFilteredNoise(float2 p) {
    float footprint=max(fwidth(p.x),fwidth(p.y));
    // Unresolved noise cells converge to their mean, not a changing hash sample.
    return lerp(CreekNoise(p),.5,smoothstep(.35,1.5,footprint));
}
#endif
