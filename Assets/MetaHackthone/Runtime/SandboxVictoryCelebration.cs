using UnityEngine;
using UnityEngine.Rendering;
namespace PhysicalAssembly {
 // Two bounded, unlit particle batches. No lights, collisions, trails or per-particle GameObjects.
 public sealed class SandboxVictoryCelebration:MonoBehaviour {
  public const int SparkCount=160,PaperCount=96;public const float Duration=7;
  public bool Playing{get;private set;} public float Age{get;private set;}
  ParticleSystem sparks,paper;Material sparkMaterial,paperMaterial;
  readonly ParticleSystem.Particle[] s=new ParticleSystem.Particle[SparkCount],c=new ParticleSystem.Particle[PaperCount];
  Vector3 centre,right,forward;float span;
  static readonly Color[] Colors={new(1,.72f,.15f),new(.15f,.95f,1),new(1,.22f,.49f),new(.65f,.40f,1),new(.5f,1,.25f)};
  public void Play(DownhillController level,Vector3 origin){
   var bounds=new Bounds(origin,Vector3.one*.5f);
   if(level && level.FactoryLevel){bool first=true;foreach(var track in level.FactoryLevel.Tracks)foreach(var p in track.Samples){var q=track.transform.TransformPoint(p);if(first){bounds=new Bounds(q,Vector3.zero);first=false;}else bounds.Encapsulate(q);}}
   var rotation=level && level.ActiveLayout?level.ActiveLayout.PlayerStartPose(SandboxLevelLayout.DefaultPlayerDistance).rotation:Quaternion.identity;
   right=rotation*Vector3.right;forward=rotation*Vector3.forward;span=Mathf.Clamp(bounds.size.magnitude,.8f,8);
   centre=new Vector3(bounds.center.x,bounds.max.y,bounds.center.z);Age=0;Playing=true;
   if(!sparks){sparks=Create("Victory fireworks",SparkCount,1,out sparkMaterial);paper=Create("Victory confetti",PaperCount,0,out paperMaterial);}
   sparks.gameObject.SetActive(true);paper.gameObject.SetActive(true);sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);paper.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);sparks.Play();paper.Play();Advance(0);
  }
  ParticleSystem Create(string name,int budget,float soft,out Material material){
   var go=new GameObject(name);go.transform.SetParent(transform,false);var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
   var main=ps.main;main.duration=Duration+1;main.maxParticles=budget;main.playOnAwake=false;main.loop=false;main.simulationSpace=ParticleSystemSimulationSpace.World;var emission=ps.emission;emission.enabled=false;
   material=new Material(Resources.Load<Shader>("VictoryParticles"));material.SetFloat("_Soft",soft);
   var r=ps.GetComponent<ParticleSystemRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;ps.Play();return ps;
  }
  static float Hash(int i,int salt){uint x=(uint)(i*374761393+salt*668265263);x=(x^(x>>13))*1274126177u;return ((x^(x>>16))&0xffffff)/16777216f;}
  static void Set(ref ParticleSystem.Particle p,Vector3 position,float size,Color color,float angle){p.position=position;p.startSize=size;p.startColor=color;p.rotation=angle;p.startLifetime=10;p.remainingLifetime=10;}
  void Update(){if(Playing)Advance(Time.unscaledDeltaTime);}
  public void Advance(float dt){
   if(!Playing)return;Age+=dt;if(Age>=Duration){Stop();return;}
   for(int i=0;i<SparkCount;i++){
    int shell=i/32,j=i%32;float t=Age-shell*.62f;float alpha=0;Vector3 pos=centre;
    var basePoint=centre+right*((shell-2)*span*.16f)+forward*(span*(.12f+.08f*(shell%2)));
    if(t>=0&&t<.65f){pos=basePoint+Vector3.up*(span*(.02f+.3f*t/.65f));alpha=j<3?1:0;}
    else if(t>=.65f&&t<2.5f){float a=t-.65f,z=1-2*(j+.5f)/32,theta=j*2.399963f+shell;var direction=new Vector3(Mathf.Sqrt(1-z*z)*Mathf.Cos(theta),z,Mathf.Sqrt(1-z*z)*Mathf.Sin(theta));pos=basePoint+Vector3.up*span*.32f+direction*span*.18f*(1-Mathf.Exp(-a*2))-Vector3.up*(a*a*span*.025f);alpha=Mathf.Pow(1-a/1.85f,.75f);}
    var color=Colors[shell];color.a=alpha;Set(ref s[i],pos,span*.012f,color,0);
   }
   for(int i=0;i<PaperCount;i++){
    float t=Age-.7f-Hash(i,2)*1.4f;float life=3.5f+Hash(i,8)*.8f;var pos=centre+right*((Hash(i,3)-.5f)*span*.95f)+forward*((Hash(i,4)-.5f)*span*.35f)+Vector3.up*span*.32f;
    float alpha=0;if(t>=0&&t<life){pos+=right*(Mathf.Sin(t*3+Hash(i,6)*6)*span*.025f)+Vector3.up*span*(.10f*t-.055f*t*t);alpha=Mathf.Min(1,(life-t)*1.5f);}
    var color=Colors[i%Colors.Length];color.a=alpha;Set(ref c[i],pos,span*(.008f+Hash(i,7)*.006f),color,Hash(i,5)*360+t*150);
   }
   sparks.SetParticles(s,s.Length);paper.SetParticles(c,c.Length);
  }
  public void Stop(){Playing=false;if(sparks){sparks.Clear();sparks.gameObject.SetActive(false);}if(paper){paper.Clear();paper.gameObject.SetActive(false);}}
  void OnDisable()=>Stop();void OnDestroy(){if(sparkMaterial)Destroy(sparkMaterial);if(paperMaterial)Destroy(paperMaterial);}
 }
}
