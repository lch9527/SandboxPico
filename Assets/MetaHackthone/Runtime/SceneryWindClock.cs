using UnityEngine;

namespace PhysicalAssembly {
 // One shader clock for all baked wind data. No per-plant Update or material clones.
 public static class SceneryWindClock {
  public const string ShaderName="Creek Diorama/Animated Scenery";
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void Start(){Configure(1,1);Set(true,false,0);}
  public static void Configure(float strength,float speed){
   Shader.SetGlobalFloat("_SceneryWindStrength",Mathf.Clamp01(strength));
   Shader.SetGlobalFloat("_SceneryWindSpeed",Mathf.Clamp(speed,.25f,2));
  }
  public static void Set(bool enabled,bool preview,float seconds){
   Shader.SetGlobalFloat("_SceneryMotionEnabled",enabled?1:0);
   Shader.SetGlobalFloat("_SceneryPreview",preview?1:0);
   Shader.SetGlobalFloat("_SceneryPreviewTime",seconds);
  }
 }
}
