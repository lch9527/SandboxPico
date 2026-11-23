using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PhysicalAssembly.Editor {
 public static class BridgeSeamReview {
  public static void CreateMaterial() {
   const string path="Assets/MetaHackthone/Resources/FilteredBridgeDeck.mat";
   var shader=Shader.Find("Creek Diorama/Filtered Bridge Deck");if(!shader)throw new System.Exception("Missing filtered bridge shader");
   var material=AssetDatabase.LoadAssetAtPath<Material>(path);
   if(!material){material=new Material(shader);AssetDatabase.CreateAsset(material,path);}
   material.shader=shader;
   for(int i=0;i<5;i++) {
    var source=AssetDatabase.LoadAssetAtPath<Material>($"Assets/CreekDiorama/Materials/Honey timber {i:00}.mat");
    material.SetColor("_Wood"+i,source.GetColor("_BaseColor"));
    if(i==2)material.SetFloat("_Smoothness",source.GetFloat("_Smoothness"));
   }
   material.enableInstancing=true;EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
   if(ShaderUtil.ShaderHasError(shader))throw new System.Exception("Filtered bridge shader has errors");
  }
  public static void Inspect() {
   Directory.CreateDirectory("Logs/BridgeSeams");var report=new StringBuilder();
   bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
   try {for(int level=1;level<=5;level++) {
    var root=PrefabUtility.LoadPrefabContents($"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab");
    try {var f=root.GetComponent<SandboxFactoryLevel>();
     foreach(var track in f.Tracks) {
      report.AppendLine($"LEVEL {level} track={track.name} style={track.DeckStyle} visible={track.ShowGeneratedVisuals} width={track.DefaultWidth} thickness={track.DeckThickness} scale={track.transform.lossyScale} module={(track.DeckModule?track.DeckModule.name:"none")}");
      if(track.Generated)foreach(var r in track.Generated.GetComponentsInChildren<MeshRenderer>(true))
       report.AppendLine($"  {r.name} enabled={r.enabled} active={r.gameObject.activeSelf} mats={string.Join(";",r.sharedMaterials.Select(m=>m?m.name:"none"))} mesh={r.GetComponent<MeshFilter>()?.sharedMesh?.name}");
     }
     foreach(var r in f.TrackRoot.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name.Contains("Timber")||r.name.Contains("plank")))report.AppendLine($"ART {r.name} {r.bounds}");
    }finally{PrefabUtility.UnloadPrefabContents(root);}
   }}finally{SandboxTrackEditing.Suspended=suspended;}
   File.WriteAllText("Logs/BridgeSeams/Inspect.txt",report.ToString());
  }
 }
}
