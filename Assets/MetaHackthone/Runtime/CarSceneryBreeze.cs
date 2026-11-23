using UnityEngine;

namespace PhysicalAssembly {
 // Cosmetic motion only; local pose follows campaign scaling and placement.
 [DisallowMultipleComponent]
 public sealed class CarSceneryBreeze : MonoBehaviour {
  public Vector3 Axis=Vector3.up;
  public float DegreesPerSecond;
  public float SwayDegrees=9, Frequency=.8f, Phase;
  Quaternion rest;
  public Quaternion EvaluateRotation(Quaternion pose,float seconds) => pose*Quaternion.AngleAxis(DegreesPerSecond*seconds+Mathf.Sin(seconds*Frequency+Phase)*SwayDegrees,Axis.normalized);
  void OnEnable(){rest=transform.localRotation;}
  void Update(){Apply(Time.time);}
  public void Apply(float seconds){transform.localRotation=EvaluateRotation(rest,seconds);}
  void OnDisable(){transform.localRotation=rest;}
 }
}
