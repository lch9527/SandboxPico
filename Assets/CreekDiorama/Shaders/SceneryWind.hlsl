#ifndef CREEK_SCENERY_WIND
#define CREEK_SCENERY_WIND
float _SceneryMotionEnabled, _SceneryPreview, _SceneryPreviewTime;
float _SceneryWindStrength, _SceneryWindSpeed;
// uv3: anchor height, maximum shear, angular frequency, phase.
// uv4: rope flag, endpoint x, span, maximum displacement.
void SceneryDeform(inout float3 p,inout float3 n,float4 wind,float4 rope) {
 float time=lerp(_Time.y,_SceneryPreviewTime,_SceneryPreview)*_SceneryWindSpeed;
 float enabled=_SceneryMotionEnabled*_SceneryWindStrength;
 float wave=sin(time*wind.z+wind.w)*enabled;
 if(wind.y==0 && rope.x>.5) {
  float t=saturate((p.x-rope.y)/max(rope.z,.00001));
  p.z+=sin(t*PI)*rope.w*wave;
  n.x-=cos(t*PI)*PI*rope.w/max(rope.z,.00001)*wave*n.z;
 } else {
  float sx=wind.y*wave;
  float sz=wind.y*.35*sin(time*wind.z*.83+wind.w)*enabled;
  float h=p.y-wind.x;
  p.x+=h*sx;p.z+=h*sz;
  n.y-=sx*n.x+sz*n.z;
 }
 n=normalize(n);
}
#endif
