using System;
using UnityEngine;

namespace PhysicalAssembly {
 // Offline-built visual alternatives. Original meshes remain untouched and movable.
 public sealed class SceneryDetailCatalog:ScriptableObject {
  [Serializable] public class Entry {
   public Mesh Source,Filtered;public float ReferenceSize=.12f;
  }
  [Serializable] public class BaseEntry {
   public Mesh SteelSource,BasaltSource,RepairedBasalt,Facade,Rim,Inset;public Material FacadeMaterial,RimMaterial,InsetMaterial;
   public float ReferenceSize=.5f;
  }
  public Entry[] Foliage=Array.Empty<Entry>();
  public BaseEntry[] Bases=Array.Empty<BaseEntry>();
  [HideInInspector] public string AuthoringFingerprint;
 }
}
