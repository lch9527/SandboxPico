using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class QuarryIslandAuthoring
    {
        const string Root="Assets/CreekDiorama";
        public const string IslandPrefix="Quarry endpoint island";
        public static string Prefix(int level)=>level==4?IslandPrefix:$"Campaign endpoint island {level}";
        public static bool IsIsland(string name)=>name.StartsWith(IslandPrefix)||name.StartsWith("Campaign endpoint island ");
        public static bool IsGround(string name)=>name.StartsWith("Quarry island ground ")||name.StartsWith("Campaign island ground ");
        public static void Apply()
        {
            Directory.CreateDirectory("Logs/QuarryIslands");
            var setup=EditorSceneManager.GetSceneManagerSetup();bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
            try {
                var scene=EditorSceneManager.OpenScene("Assets/MetaHackthone/Scenes/Level4.unity");var f=SandboxFactoryAuthoring.Find(scene);
                var before=CarSceneryAuthoring.GameplaySnapshot(f);Dress(f);if(before!=CarSceneryAuthoring.GameplaySnapshot(f))throw new Exception("Gameplay changed");EditorSceneManager.SaveScene(scene);
                const string path="Assets/MetaHackthone/Generated/Campaign/Level4.prefab";
                var go=PrefabUtility.LoadPrefabContents(path);
                try {f=go.GetComponent<SandboxFactoryLevel>();before=CarSceneryAuthoring.GameplaySnapshot(f);Dress(f);if(before!=CarSceneryAuthoring.GameplaySnapshot(f))throw new Exception("Prefab gameplay changed");PrefabUtility.SaveAsPrefabAsset(go,path);}
                finally{PrefabUtility.UnloadPrefabContents(go);}
                AssetDatabase.SaveAssets();Debug.Log("QUARRY_ISLANDS_READY: two anchored islands; gameplay unchanged; road clearance verified.");
            }finally{SandboxTrackEditing.Suspended=suspended;if(setup.Any(s=>s.isLoaded))EditorSceneManager.RestoreSceneManagerSetup(setup);}
            Audit();
        }
        public static void Dress(SandboxFactoryLevel f,int level=4,bool onlyMissing=false,string requestedIssues=null)
        {
            var art=f.Layout.Course.SandboxFrame.Find("Environment/"+CarSceneryAuthoring.GroupName);
            string failures=requestedIssues??(onlyMissing?CampaignLandingAudit.Check(f):"Start Finish");
            if(failures.Length==0)return;
            var track=f.Tracks[0];var samples=track.Samples;float width=track.DefaultWidth*track.transform.lossyScale.x;
            var lanes=CarSceneryClearance.Lanes(f);
            for(int side=0;side<2;side++) {
                string sideName=side==0?"Start":"Finish";if(!failures.Contains(sideName))continue;
                var previous=art.Find(Prefix(level)+" "+sideName);if(previous)Object.DestroyImmediate(previous.gameObject);
                int i=side==0?0:samples.Length-1,j=side==0?Mathf.Min(8,samples.Length-1):Mathf.Max(0,samples.Length-9);
                var p=track.transform.TransformPoint(samples[i]);var inward=track.transform.TransformPoint(samples[j])-p;
                var outward=-inward;float slope=outward.y/Mathf.Max(new Vector2(outward.x,outward.z).magnitude,.001f);outward.y=0;
                var island=new GameObject(Prefix(level)+" "+sideName).transform;island.SetParent(art,false);island.SetPositionAndRotation(p,Quaternion.LookRotation(outward));island.localScale=Vector3.one/art.lossyScale.x;
                var creek=f.Scenery.FirstOrDefault(t=>t.name.StartsWith("Beautiful Corner art"));
                var river=art.Find("Freight creek");
                float floor=creek?creek.position.y+.2f:river?river.GetComponent<Renderer>().bounds.center.y-.1f:p.y-width*3;
                float height=Mathf.Max(width*1.8f,p.y-floor),rx=width*1.65f,rz=width*1.8f,offset=width*.45f;
                var stone=MakeGround(island,level,side,false,rx,rz,offset,height,slope);
                var turf=MakeGround(island,level,side,true,rx,rz,offset,height,slope);
                // Slightly lower the visual foundation if spline curvature deviates from the end tangent.
                int steps=0;while(new[]{stone,turf}.Any(m=>CarSceneryClearance.MeshOverlaps(m,lanes))) {
                    stone.transform.localPosition-=Vector3.up*.01f;turf.transform.localPosition-=Vector3.up*.01f;
                    if(++steps>30)throw new Exception("Endpoint island cannot fit below the road");
                }
                float drop=turf.transform.localPosition.y;
                // Retire a lower-terrace landmark if the new landing would bury it.
                foreach(Transform old in art.Cast<Transform>().Where(t=>t.name==CarSceneryAuthoring.Landmarks[level-1]).ToArray())
                    if(old.GetComponentsInChildren<Renderer>().Any(r=>r.bounds.Intersects(stone.GetComponent<Renderer>().bounds)))Object.DestroyImmediate(old.gameObject);
                void Place(string name,float x,float z,float scale,bool kit=false) {
                    string path=Root+"/Prefabs/"+(kit?"CarScenery/":"Botanical/")+name+".prefab";
                    var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);var obj=(GameObject)PrefabUtility.InstantiatePrefab(asset,island);
                    obj.transform.localScale=Vector3.one*scale;obj.transform.localPosition=new Vector3(x,-.055f+slope*(level==5?Mathf.Min(z,0):z)+drop,z);
                    obj.transform.localRotation=Quaternion.Euler(0,side==0?25:-35,0);
                    if(obj.GetComponentsInChildren<Renderer>().Any(r=>CarSceneryClearance.Overlaps(CarSceneryClearance.SweptBounds(r),lanes)))Object.DestroyImmediate(obj);
                }
                Place("Pine_Branching",-rx*.75f,offset+rz*.12f,width*1.8f);
                Place("Tree_Round_Canopy",rx*.75f,offset+rz*.12f,width*1.7f);
                Place(side==0?CarSceneryAuthoring.Landmarks[level-1]:"Trail_Rest_Stop",0,offset+rz*.55f,width*1.8f,true);
                Place("Creek_Wind_Pennant",-rx*.65f,offset-rz*.33f,width*.6f,true);
                Place("Prop_Crate",rx*.32f,offset+rz*.62f,width*.6f);
                for(int k=0;k<8;k++) {
                    float a=k*Mathf.PI/4;float x=Mathf.Cos(a)*rx*.75f,z=offset+Mathf.Sin(a)*rz*.72f;
                    Place(k%2==0?"Fern_Rosette":"Flowers_Daisies",x,z,width*.85f);
                }
                for(int k=0;k<4;k++)Place("Ivy_Cascade",(k<2?-1:1)*rx*.86f,offset+(k%2==0?-.35f:.36f)*rz,width*1.8f);
                foreach(var r in island.GetComponentsInChildren<Renderer>())if(CarSceneryClearance.Intrudes(r,lanes))throw new Exception("Island blocks road: "+r.name);
            }
        }
        static MeshFilter MakeGround(Transform parent,int level,int id,bool grass,float rx,float rz,float offset,float height,float slope)
        {
            const int count=16;var v=new List<Vector3>();var triangles=new List<int>();
            Vector3 Ring(int k,float radius,float y) { float a=k*Mathf.PI*2/count;float wobble=1+.055f*Mathf.Sin(k*2.7f+id);float x=Mathf.Cos(a)*rx*radius*wobble,z=offset+Mathf.Sin(a)*rz*radius*wobble;return new Vector3(x,y+slope*z,z); }
            void Emit(Vector3 a,Vector3 b,Vector3 c) {
                if(level==5){a.y-=slope*Mathf.Max(a.z,0);b.y-=slope*Mathf.Max(b.z,0);c.y-=slope*Mathf.Max(c.z,0);}
                int n=v.Count;v.AddRange(new[]{a,b,c});triangles.AddRange(new[]{n,n+1,n+2});
            }
            void Triangle(Vector3 a,Vector3 b,Vector3 c) {
                // End the steep driving gradient at the landing line, with a flat departure shelf behind it.
                // Split crossing faces first so the turf and cliff share an exact crease at z=0.
                if(level!=5 || Mathf.Min(a.z,Mathf.Min(b.z,c.z))>=0 || Mathf.Max(a.z,Mathf.Max(b.z,c.z))<=0){Emit(a,b,c);return;}
                foreach(float side in new[]{-1f,1f}) {
                    var input=new[]{a,b,c};var polygon=new List<Vector3>();
                    for(int k=0;k<3;k++) {
                        var p=input[k];var q=input[(k+1)%3];bool inside=p.z*side>=0,next=q.z*side>=0;
                        if(inside)polygon.Add(p);if(inside!=next)polygon.Add(Vector3.Lerp(p,q,p.z/(p.z-q.z)));
                    }
                    for(int k=1;k<polygon.Count-1;k++)Emit(polygon[0],polygon[k],polygon[k+1]);
                }
            }
            if(grass)for(int k=0;k<count;k++)Triangle(new Vector3(0,-.055f+slope*offset,offset),Ring(k+1,.98f,-.055f),Ring(k,.98f,-.055f));
            else {
                float[] radii={.63f,.82f,.94f,1.02f,1f};float[] ys={-height,-height*.69f,-height*.38f,-.19f,-.065f};
                for(int band=0;band<4;band++)for(int k=0;k<count;k++) {
                    var a=Ring(k,radii[band],ys[band]);var b=Ring(k+1,radii[band],ys[band]);var c=Ring(k+1,radii[band+1],ys[band+1]);var d=Ring(k,radii[band+1],ys[band+1]);Triangle(a,c,b);Triangle(a,d,c);
                }
            }
            var mesh=new Mesh{name=(level==4?"Quarry island ":$"Level {level} island ")+id+(grass?" turf":" bedrock")};mesh.SetVertices(v);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            string folder=Root+"/Meshes/"+(level==4?"QuarryIslands":"EndpointIslands");Directory.CreateDirectory(folder);AssetDatabase.Refresh();string path=folder+"/"+mesh.name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(old){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);mesh=old;EditorUtility.SetDirty(mesh);}else AssetDatabase.CreateAsset(mesh,path);
            var go=new GameObject((level==4?"Quarry":"Campaign")+" island ground "+(grass?"turf":"bedrock"));go.transform.SetParent(parent,false);var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+(grass?"Botanical moss":"Botanical limestone")+".mat");return filter;
        }
        public static void Audit()
        {
            Directory.CreateDirectory("Logs/QuarryIslands");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MetaHackthone/Generated/Campaign/Level4.prefab"));
            var f=go.GetComponent<SandboxFactoryLevel>();var s=new StringBuilder();
            s.AppendLine($"Start {f.Start.position:F5}, finish {f.Finish.transform.position:F5}, player {f.Layout.PlayerStart.position:F5}");
            foreach(var t in f.Tracks)s.AppendLine($"{t.name}: scale {t.transform.lossyScale}, width {t.DefaultWidth}, first {t.transform.TransformPoint(t.Samples[0]):F5}, last {t.transform.TransformPoint(t.Samples[^1]):F5}");
            foreach(var t in f.Scenery)s.AppendLine($"Scenery {t.name}: pos {t.position}, scale {t.lossyScale}");
            File.WriteAllText("Logs/QuarryIslands/Geometry.txt",s.ToString());
            CarSceneryReview.Capture(f,"Logs/QuarryIslands/Overview.png");
        }
    }
}
