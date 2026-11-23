#ifndef CREEK_ANALYTIC_AA_INCLUDED
#define CREEK_ANALYTIC_AA_INCLUDED
// Fragment-stage filtering in screen space. Derivatives must be taken BEFORE
// abs/frac/threshold operations; otherwise thin lines disappear or shimmer.
float CreekAASmooth(float low, float high, float value) {
    float pixel = max(fwidth(value), 1e-5) * .5;
    return smoothstep(low - pixel, high + pixel, value);
}
float CreekAAStep(float threshold, float value) {
    float pixel = max(fwidth(value), 1e-5);
    return saturate(.5 + (value - threshold) / pixel);
}
float CreekBand(float coordinate, float halfWidth) {
    float pixel = max(fwidth(coordinate), 1e-5);
    // Integrate both edges over a pixel; a subpixel line loses intensity instead
    // of being widened to a full-bright pixel. Keeps its average coverage.
    return saturate((halfWidth-coordinate)/pixel+.5)
         - saturate((-halfWidth-coordinate)/pixel+.5);
}
float CreekStripeIntegral(float x, float duty) {
    return floor(x)*duty + min(frac(x), duty);
}
float CreekPeriodicFill(float coordinate, float duty, float footprint) {
    float pixel=max(footprint,1e-5);
    return saturate((CreekStripeIntegral(coordinate+pixel*.5,duty)
                    -CreekStripeIntegral(coordinate-pixel*.5,duty))/pixel);
}
float CreekPeriodicFill(float coordinate, float duty) {
    return CreekPeriodicFill(coordinate,duty,fwidth(coordinate));
}
float CreekPeriodicLine(float coordinate, float halfWidth) {
    halfWidth=clamp(halfWidth,0,.5);
    return CreekPeriodicFill(coordinate+halfWidth,halfWidth*2);
}
float CreekPhaseVisibility(float phase) {
    float footprint=max(abs(ddx(phase)),abs(ddy(phase)));
    return 1-smoothstep(1.0,3.14159265,footprint);
}
float CreekFilteredSin(float phase) { return sin(phase)*CreekPhaseVisibility(phase); }
float CreekSilkPulse(float phase) {
    // Integrate the sharp pulse over a 2x2 footprint within this fragment.
    // This is arithmetic only (no extra texture fetches or rendering passes).
    float2 delta=float2(ddx(phase),ddy(phase))*.25;
    float4 samples=phase+float4(-delta.x-delta.y,delta.x-delta.y,-delta.x+delta.y,delta.x+delta.y);
    float4 pulse=saturate(.5+.5*sin(samples));
    float4 squared=pulse*pulse;
    float filtered=dot(squared*squared*pulse,float4(.25,.25,.25,.25));
    float footprint=max(abs(ddx(phase)),abs(ddy(phase)));
    return lerp(filtered,.24609375,smoothstep(1.25,3.14159265,footprint));
}
#endif
