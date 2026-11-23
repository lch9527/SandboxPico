using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class CampaignLandingAuthoring
    {
        const string Folder="Assets/CreekDiorama/Meshes/EndpointIslands";
        public static void Apply()
        {
            Directory.CreateDirectory("Logs/CampaignLandings");var report=new StringBuilder();
            var setup=EditorSceneManager.GetSceneManagerSetup();bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
            try {
                for(int n=1;n<=5;n++) {
                    string path=$"Assets/MetaHackthone/Generated/Campaign/Level{n}.prefab";
                    var preflight=PrefabUtility.LoadPrefabContents(path);string runtimeIssues;
                    try {runtimeIssues=CampaignLandingAudit.Check(preflight.GetComponent<SandboxFactoryLevel>());}finally{PrefabUtility.UnloadPrefabContents(preflight);}
                    var scene=EditorSceneManager.OpenScene($"Assets/MetaHackthone/Scenes/Level{n}.unity");var f=SandboxFactoryAuthoring.Find(scene);
                    var before=CarSceneryAuthoring.GameplaySnapshot(f);Repair(f,n);
                    if(before!=CarSceneryAuthoring.GameplaySnapshot(f))throw new Exception($"Level {n} gameplay changed");
                    if(CampaignLandingAudit.Check(f).Length!=0)throw new Exception($"Level {n} scene: "+CampaignLandingAudit.Check(f));EditorSceneManager.SaveScene(scene);
                    var go=PrefabUtility.LoadPrefabContents(path);
                    try {f=go.GetComponent<SandboxFactoryLevel>();before=CarSceneryAuthoring.GameplaySnapshot(f);Repair(f,n,runtimeIssues);if(before!=CarSceneryAuthoring.GameplaySnapshot(f))throw new Exception("Prefab gameplay changed");
                        string errors=CampaignLandingAudit.Check(f);if(errors.Length!=0)throw new Exception($"Level {n} prefab: "+errors);PrefabUtility.SaveAsPrefabAsset(go,path);}
                    finally{PrefabUtility.UnloadPrefabContents(go);}
                    report.AppendLine($"Level {n}: scene and runtime prefab supported across road width and landing approach; gameplay unchanged.");
                }
                AssetDatabase.SaveAssets();File.WriteAllText("Logs/CampaignLandings/Validation.txt",report.ToString());Debug.Log("CAMPAIGN_LANDINGS_READY\n"+report);
            }finally{SandboxTrackEditing.Suspended=suspended;if(setup.Any(s=>s.isLoaded))EditorSceneManager.RestoreSceneManagerSetup(setup);}
            CampaignLandingAudit.Audit();CarSceneryClearance.AuditExisting();Review();
        }
        public static void Repair(SandboxFactoryLevel f,int level,string priorIssues=null)
        {
            string issues=CampaignLandingAudit.Check(f,out float excessHeight);issues=priorIssues??issues;if(issues.Length==0)return;
            if(level==1 && excessHeight>.15f)LowerFinishTerrace(f,excessHeight+.065f);
            QuarryIslandAuthoring.Dress(f,level,true,issues);
        }
        static void LowerFinishTerrace(SandboxFactoryLevel f,float distance)
        {
            // Level 1's legacy end terrace is higher than its published road. Deform only that
            // island's visual meshes into a lower shelf, preserving the base and opposite island.
            var creek=f.Scenery.First(t=>t.name.StartsWith("Beautiful Corner art"));
            var track=f.Tracks[0];var endpoint=creek.InverseTransformPoint(track.transform.TransformPoint(track.Samples[^1]));float side=Mathf.Sign(endpoint.x);
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            foreach(var filter in creek.GetComponentsInChildren<MeshFilter>()) {
                var renderer=filter.GetComponent<Renderer>();if(!renderer || !renderer.enabled)continue;
                if(renderer.sharedMaterials.Any(m=>m.shader.name.Contains("Water")||m.shader.name.Contains("Cloud")||m.shader.name.Contains("Portal")))continue;
                var source=filter.sharedMesh;var positions=source.vertices;var toCreek=creek.worldToLocalMatrix*filter.transform.localToWorldMatrix;var fromCreek=toCreek.inverse;bool changed=false;
                for(int i=0;i<positions.Length;i++) {
                    var p=toCreek.MultiplyPoint3x4(positions[i]);
                    float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.25f,.65f,p.x*side));
                    if(weight<=0 || p.y<=.17f)continue;
                    float drop=distance/creek.lossyScale.y;
                    float ratio=Mathf.Max(.15f,(.606f-.17f-drop)/(.606f-.17f));
                    // Preserve vertical ordering through the rocks instead of folding
                    // the lower faces with a steep smoothstep subtraction.
                    float compressed=p.y<=.606f?.17f+(p.y-.17f)*ratio:p.y-drop;
                    p.y=Mathf.Lerp(p.y,compressed,weight);positions[i]=fromCreek.MultiplyPoint3x4(p);changed=true;
                }
                if(!changed)continue;
                string sourcePath=AssetDatabase.GetAssetPath(source);string guid=AssetDatabase.AssetPathToGUID(sourcePath);
                string path=Folder+"/Level1 lowered terrace "+guid+".asset";
                var mesh=Object.Instantiate(source);mesh.name="Level 1 lowered finish terrace";mesh.vertices=positions;mesh.RecalculateNormals();mesh.RecalculateBounds();
                var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,path);
                filter.sharedMesh=mesh;EditorUtility.SetDirty(filter);
                if(PrefabUtility.IsPartOfPrefabInstance(filter))PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            }
            var art=f.Layout.Course.SandboxFrame.Find("Environment/"+CarSceneryAuthoring.GroupName);
            foreach(Transform t in art) {
                if(t.name=="Horizon transition islands" || QuarryIslandAuthoring.IsIsland(t.name))continue;
                if(creek.InverseTransformPoint(t.position).x*side<.65f)continue;
                t.position-=Vector3.up*distance;if(PrefabUtility.IsPartOfPrefabInstance(t))PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            }
        }
        public static void Review()
        {
            Directory.CreateDirectory("Logs/CampaignLandings");
            for(int n=1;n<=5;n++) {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/MetaHackthone/Generated/Campaign/Level{n}.prefab"));
                CarSceneryReview.Capture(go.GetComponent<SandboxFactoryLevel>(),$"Logs/CampaignLandings/Level{n}.png");
            }
        }
    }
}
