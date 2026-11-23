using UnityEngine;
namespace PhysicalAssembly {
 [DisallowMultipleComponent]
 public sealed class SceneryWindSettings : MonoBehaviour {
  [Tooltip("Wind and rope movement strength. One is the authored maximum; zero stops deformation.")]
  [Range(0,1)] public float Strength=1;
  [Tooltip("Multiplier for the authored wind and rope animation speed.")]
  [Range(.25f,2)] public float Speed=1;
  public void Apply(){SceneryWindClock.Configure(Strength,Speed);}
  void OnEnable(){if(Application.isPlaying)Apply();}
  void OnValidate(){if(Application.isPlaying)Apply();}
 }
}
