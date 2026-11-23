#ifndef CREEK_CLOCK_INCLUDED
#define CREEK_CLOCK_INCLUDED
float4 _CreekClock;
float CreekTime() { return lerp(_Time.y,_CreekClock.x,_CreekClock.y); }
#endif
