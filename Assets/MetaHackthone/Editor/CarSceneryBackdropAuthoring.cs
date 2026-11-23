using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    // Palette-baked distant geometry: a single opaque draw per layer, no fog/depth texture.
    public static class CarSceneryBackdropAuthoring
    {
        const string Root="Assets/CreekDiorama";
        const string AssetsPath=Root+"/Meshes/Backdrop";
        const string Kit=Root+"/Prefabs/CarScenery/Transitions";
        static readonly string[] Clusters={"Meadow verge","Mooring supplies","Workshop supplies","Quarry scree","Freight garden"};
        static Material horizon;
        static Transform Group(Transform parent,string name) { var t=new GameObject(name).transform;t.SetParent(parent,false);return t; }
        static Transform Plant(Transform parent,string name,Vector3 p,Vector3 scale,float yaw=0)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/"+(name=="PineTree"?"":"Botanical/")+name+".prefab");
            var t=((GameObject)PrefabUtility.InstantiatePrefab(asset,parent)).transform;
            t.localPosition=p;t.localRotation=Quaternion.Euler(0,yaw,0);t.localScale=scale;return t;
        }
        static Material Material(string path,string shader)
        {
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}return m;
        }
        static void FitRock(Transform t,Vector3 size)
        {
            var bounds=new Bounds();bool first=true;
            foreach(var f in t.GetComponentsInChildren<MeshFilter>()) {
                var b=CarSceneryClearance.TransformBounds(f.sharedMesh.bounds,t.worldToLocalMatrix*f.transform.localToWorldMatrix);
                if(first){bounds=b;first=false;}else bounds.Encapsulate(b);
            }
            t.localScale=new Vector3(size.x/bounds.size.x,size.y/bounds.size.y,size.z/bounds.size.z);
            t.localPosition-=t.localRotation*Vector3.Scale(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z),t.localScale);
        }
        public static void BuildKit()
        {
            Directory.CreateDirectory(AssetsPath);Directory.CreateDirectory(Kit);AssetDatabase.Refresh();
            horizon=Material(Root+"/Materials/Horizon palette.mat","Creek Diorama/Horizon Palette");
            var sky=Material("Assets/MetaHackthone/Resources/CarCampaignSky.mat","Creek Diorama/Pastel Sky");
            sky.SetColor("_Zenith",new Color(.26f,.48f,.66f));sky.SetColor("_Horizon",new Color(.73f,.82f,.83f));sky.SetColor("_Ground",new Color(.36f,.51f,.56f));EditorUtility.SetDirty(sky);
            for(int n=0;n<5;n++) {
                var root=new GameObject(Clusters[n]);
                try {
                    for(int i=0;i<5;i++) {
                        float x=(i-2)*.085f,z=Mathf.Sin(i*2.3f+n)*.075f;
                        Plant(root.transform,i%2==0?"Grass_Soft_Tuft":"Rock_Pebble_Cluster",new Vector3(x,0,z),Vector3.one*(.65f+i*.09f),i*77);
                    }
                    Plant(root.transform,n==1?"Fern_Rosette":n==3?"Rock_Stratum_C":"Flowers_Daisies",new Vector3(-.12f,.01f,.07f),Vector3.one*.75f,25);
                    Plant(root.transform,n==1?"Prop_Barrel":n==2?"Prop_Crate":n==3?"Rock_River_Boulder":n==4?"Shrub_Broadleaf":"Flowers_Lupin",new Vector3(.10f,.01f,.06f),Vector3.one*(n==3?.6f:.8f),-20);
                    if(n==1 || n==2 || n==4)Plant(root.transform,"Prop_Lantern",new Vector3(-.04f,.015f,-.07f),Vector3.one*.7f);
                    PrefabUtility.SaveAsPrefabAsset(root,Kit+"/"+Clusters[n]+" Editable.prefab");
                    // Runtime version shares one vertex-color material; editable originals remain available.
                    Bake(root.transform,"Cluster "+n,Color.white,0,true);
                    PrefabUtility.SaveAsPrefabAsset(root,Kit+"/"+Clusters[n]+".prefab");
                }finally{Object.DestroyImmediate(root);}
            }
        }
        public static void Dress(SandboxFactoryLevel f,Transform art,int level,Transform creek)
        {
            if(!horizon)horizon=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Horizon palette.mat");
            // Small edge clusters sit on the existing terrace shelves, never the driving surface.
            if(creek) {
                Vector3[] positions={new(.94f,.68f,.05f),new(1.16f,.68f,-.35f),new(-.91f,.615f,.32f),new(-1.18f,.615f,-.19f)};
                for(int i=0;i<positions.Length;i++)PutCluster(art,level,positions[i],.6f+i*.07f,i*103);
            } else {
                foreach(Transform terrace in art.Cast<Transform>().Where(t=>t.name.StartsWith("Freight terrace")).ToArray())
                    PutCluster(terrace,level,new Vector3(-.9f,.04f,.7f),5,-35);
            }
            // A world-upright composition relative to the actual course and player viewing direction.
            var trackBounds=new Bounds(f.Start.position,Vector3.zero);
            foreach(var track in f.Tracks)foreach(var p in track.Samples)trackBounds.Encapsulate(track.transform.TransformPoint(p));
            float span=Mathf.Max(trackBounds.size.x,trackBounds.size.z,1);
            // Use the published spawn as the composition reference for both authoring and runtime copies.
            // Some standalone scenes intentionally keep a different editor player marker.
            var published=AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>($"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab");
            var view=f.transform.TransformPoint(published.transform.InverseTransformPoint(published.Layout.PlayerStart.position));
            Vector3 forward=trackBounds.center-view;forward.y=0;forward.Normalize();
            if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
            var anchor=new Vector3(trackBounds.center.x,trackBounds.min.y-.10f*span,trackBounds.center.z);
            var backdrop=Group(art,"Horizon transition islands");
            backdrop.SetPositionAndRotation(anchor,Quaternion.LookRotation(forward));
            backdrop.localScale=Vector3.one*(span/art.lossyScale.x);
            // Two opaque baked meshes. Receding colors echo cloud shadows and pastel sky.
            for(int layer=0;layer<2;layer++) {
                var bank=Group(backdrop,layer==0?"Sage pine islands":"Blue limestone distance");
                int count=layer==0?5:7;
                for(int i=0;i<count;i++) {
                    float x=(i-(count-1)*.5f)*(layer==0?.54f:.67f)+.10f*Mathf.Sin(i*3.1f+level);
                    float z=(layer==0?1.15f:2.1f)+.24f*Mathf.Sin(i*2+level);
                    float y=(layer==0?-.17f:.05f)+.12f*Mathf.Sin(i*1.7f+level*.5f);
                    var island=Group(bank,"Island "+i);island.localPosition=new Vector3(x,y,z);
                    float width=(layer==0?.30f:.52f)*(1+.15f*Mathf.Sin(i*3.2f+level));
                    // Normalize each stratum's actual bounds so variants overlap instead of floating apart.
                    for(int j=0;j<3;j++) {
                        var rock=Plant(island,"Rock_Stratum_"+(char)('A'+(i+j)%3),new Vector3(j*.006f,-.18f+j*.12f,0),Vector3.one,i*43+j*12);
                        FitRock(rock,new Vector3(width*(.85f+j*.1f),.16f,width*(.65f+j*.07f)));
                    }
                    Plant(island,"Moss_Overhang",new Vector3(.01f,.215f,0),Vector3.one*(width*2.9f),i*32);
                    for(int t=0;t<3;t++)Plant(island,"PineTree",new Vector3((t-1)*width*.28f,.22f,(t%2)*.035f),Vector3.one*(width*(t==1?1.5f:1.1f)),t*75+i*17);
                }
                Bake(bank,"Level "+level+" horizon "+layer,layer==0?new Color(.52f,.66f,.66f):new Color(.67f,.76f,.80f),layer==0?.38f:.76f,true);
            }
            // Same resource on scenes and prefabs, no camera or global fog is stored in the level.
        }
        static void PutCluster(Transform parent,int level,Vector3 position,float scale,float yaw)
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(Kit+"/"+Clusters[level-1]+".prefab");
            var t=((GameObject)PrefabUtility.InstantiatePrefab(asset,parent)).transform;t.localPosition=position;t.localScale=Vector3.one*scale;t.localRotation=Quaternion.Euler(0,yaw,0);
        }
        static void Bake(Transform root,string id,Color haze,float hazeAmount,bool shade)
        {
            var vertices=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>()) {
                var mesh=filter.sharedMesh;var positions=mesh.vertices;var normals=mesh.normals;
                var matrix=root.worldToLocalMatrix*filter.transform.localToWorldMatrix;var normalMatrix=matrix.inverse.transpose;
                var materials=filter.GetComponent<Renderer>().sharedMaterials;
                for(int sub=0;sub<mesh.subMeshCount;sub++) {
                    var m=materials[Mathf.Min(sub,materials.Length-1)];var baseColor=m.HasProperty("_BaseColor")?m.GetColor("_BaseColor"):m.HasProperty("_Color")?m.GetColor("_Color"):Color.white;
                    int offset=vertices.Count;
                    for(int v=0;v<positions.Length;v++) {
                        var p=matrix.MultiplyPoint3x4(positions[v]);vertices.Add(p);
                        var normal=normals.Length==positions.Length?normalMatrix.MultiplyVector(normals[v]).normalized:Vector3.up;
                        float light=shade?.7f+.3f*Mathf.Max(0,Vector3.Dot(normal,new Vector3(-.4f,.85f,-.3f).normalized)):1;
                        Color c=Color.Lerp(baseColor*light,haze,hazeAmount);c.a=1;colors.Add(c.linear);
                    }
                    foreach(int t in mesh.GetTriangles(sub))triangles.Add(offset+t);
                }
            }
            var result=new Mesh{name=id,indexFormat=IndexFormat.UInt32};result.SetVertices(vertices);result.SetColors(colors);result.SetTriangles(triangles,0);result.RecalculateBounds();
            string path=AssetsPath+"/"+id+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(old){EditorUtility.CopySerialized(result,old);Object.DestroyImmediate(result);result=old;EditorUtility.SetDirty(result);}else AssetDatabase.CreateAsset(result,path);
            foreach(Transform t in root.Cast<Transform>().ToArray())Object.DestroyImmediate(t.gameObject);
            var part=Group(root,"Palette mesh");part.gameObject.AddComponent<MeshFilter>().sharedMesh=result;
            var renderer=part.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=id.StartsWith("Cluster ")?SceneryMotionRepair.GroundMaterial():horizon;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            Debug.Log($"BACKDROP_MESH {id}: {triangles.Count/3} triangles, 1 opaque renderer");
        }
    }
}
