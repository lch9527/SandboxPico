using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
using static PhysicalAssembly.Editor.EnvironmentPolishAuthoring;

namespace PhysicalAssembly.Editor
{
    public static class TrayWaterRepair
    {
        public static void Fit(SandboxFactoryLevel factory,string key)
        {
            var tray=factory.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="01 Display base");
            var steel=tray.Find("Blue steel edge").GetComponent<MeshFilter>();
            var caps=SceneryAAAuthoring.TrayRimCaps(steel.sharedMesh);
            if(caps.Length!=8)throw new InvalidOperationException("Expected eight connected tray rim segments");
            float rim=caps.Min(b=>b.max.y),height=rim-.004f;
            float x=caps.Where(b=>b.size.z>.5f).Max(b=>b.min.x)+.001f;
            float z=caps.Where(b=>b.size.x>1).Max(b=>b.min.z)+.001f;
            float cut=.09f;
            Vector2[] outline={new(-x+cut,-z),new(x-cut,-z),new(x,-z+cut),new(x,z-cut),new(x-cut,z),new(-x+cut,z),new(-x,z-cut),new(-x,-z+cut)};
            var world=outline.Select(p=>steel.transform.TransformPoint(new Vector3(p.x,height,p.y))).ToArray();
            var river=factory.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="Living river surface"||f.name=="Freight creek");
            var before=river.GetComponent<Renderer>().bounds.center.y;
            var mesh=new Mesh{name="Water contained below tray rim"};mesh.vertices=world.Select(river.transform.InverseTransformPoint).ToArray();
            var indices=new List<int>();for(int i=1;i<world.Length-1;i++)indices.AddRange(new[]{0,i+1,i});mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            river.sharedMesh=Save(mesh,key+"-TrayWater");
            // A real opaque inner wall joins the water edge to the tray body. It is
            // outside the base's LOD group, so neither near nor distant LOD opens a gap.
            var old=tray.parent.Find("Watertight basin lining");if(old)Object.DestroyImmediate(old.gameObject);
            var lining=Group(tray.parent,"Watertight basin lining");var v=new List<Vector3>();indices.Clear();
            for(int i=0;i<outline.Length;i++){
                var a=outline[i];var b=outline[(i+1)%outline.Length];int n=v.Count;
                foreach(var p in new[]{new Vector3(a.x,.132f,a.y),new Vector3(b.x,.132f,b.y),new Vector3(b.x,rim-.001f,b.y),new Vector3(a.x,rim-.001f,a.y)})v.Add(lining.InverseTransformPoint(steel.transform.TransformPoint(p)));
                indices.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
            mesh=new Mesh{name="Closed inner basin walls"};mesh.SetVertices(v);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            RenderMesh(lining,"Inner tray wall",Save(mesh,key+"-TrayLining"),tray.Find("Deep shadow").GetComponent<Renderer>().sharedMaterial);
            var legacy=factory.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="Freight creek stone rim");if(legacy)legacy.gameObject.SetActive(false);
            float after=world[0].y,delta=after-before;
            foreach(var fall in factory.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name.StartsWith("Flowing curtain"))){
                var m=Object.Instantiate(fall.sharedMesh);var positions=m.vertices;var points=positions.Select(fall.transform.TransformPoint).ToArray();float low=points.Min(p=>p.y),high=points.Max(p=>p.y);
                for(int i=0;i<points.Length;i++){var p=points[i];p.y+=delta*(1-Mathf.InverseLerp(low,high,p.y));positions[i]=fall.transform.InverseTransformPoint(p);}m.vertices=positions;m.RecalculateNormals();m.RecalculateBounds();fall.sharedMesh=Save(m,key+"-Tray"+fall.name.Replace(" ",""));
                string number=fall.name.Split(' ').Last();foreach(string effect in new[]{"Impact ripples ","Airborne droplets "}){var t=fall.transform.parent.Find(effect+number);if(t)t.position+=Vector3.up*delta;}
            }
        }
        public static void GroundProps(SandboxFactoryLevel factory,string key)
        {
            var grounds=ContactPolishAuthoring.VisibleGround(factory.gameObject);
            var ground=new ContactPolishAuthoring.Surface(grounds);
            var mouths=factory.GetComponentsInChildren<Transform>().Where(t=>t.name=="Anchored waterfall source").SelectMany(t=>t.GetComponentsInChildren<Renderer>()).Select(r=>r.bounds).ToArray();
            var lanes=CarSceneryClearance.Lanes(factory);
            foreach(var dressing in factory.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="10 Botanical dressing").ToArray()){
                var source=dressing.Cast<Transform>().FirstOrDefault(t=>t.name.StartsWith("Editable placement"));var props=source?source.Find("Story props"):null;if(!props)continue;
                foreach(Transform prop in props){
                    var rr=prop.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();if(rr.Length==0)continue;
                    var bounds=rr[0].bounds;foreach(var r in rr)bounds.Encapsulate(r.bounds);var origin=prop.position;var relative=bounds.center-origin;
                    bool movable=prop.name=="Prop_Barrel"||prop.name=="Prop_Crate";bool placed=false;
                    for(int ring=0;ring<24&&!placed;ring++)for(int d=0;d<(ring==0?1:24)&&!placed;d++){
                        if(!movable&&ring>0)break;float a=d*Mathf.PI/12;var p=origin+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*ring*.045f;
                        float min=float.PositiveInfinity,max=float.NegativeInfinity;bool supported=true;
                        foreach(float dx in new[]{-.85f,0,.85f})foreach(float dz in new[]{-.85f,0,.85f}){
                            var q=p+relative+new Vector3(bounds.extents.x*dx,0,bounds.extents.z*dz);if(!ground.Height(q,out float y)){supported=false;break;}min=Mathf.Min(min,y);max=Mathf.Max(max,y);
                        }
                        if(!supported||max-min>.12f)continue;p.y=max+.004f-(bounds.min.y-origin.y);
                        var candidate=new Bounds(p+relative,bounds.size);
                        if(movable&&(mouths.Any(b=>b.Intersects(candidate))||CarSceneryClearance.Overlaps(candidate,lanes)))continue;
                        prop.position=p;placed=true;
                    }
                    if(movable&&!placed)throw new InvalidOperationException("No grounded clear prop placement: "+prop.name);
                }
                var target=dressing.Find("Render batches/Story props");foreach(Transform child in target.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
                int id=0;foreach(var group in props.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.GetComponent<Renderer>().enabled).GroupBy(m=>m.GetComponent<Renderer>().sharedMaterial)){
                    var m=new Mesh{indexFormat=IndexFormat.UInt32};m.CombineMeshes(group.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=target.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray(),true,true);
                    RenderMesh(target,group.Key.name,Save(m,key+"-GroundedProps"+id++),group.Key);
                }
            }
        }
        public static string Apply(int level)
        {
            bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
            try{
                var scene=EditorSceneManager.OpenScene($"Assets/MetaHackthone/Scenes/Level{level}.unity");var f=SandboxFactoryAuthoring.Find(scene);
                Repair(f,"Scene"+level);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
                var path=$"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab";var root=PrefabUtility.LoadPrefabContents(path);
                try{Repair(root.GetComponent<SandboxFactoryLevel>(),"Level"+level);PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
                AssetDatabase.SaveAssets();return "Tray and props repaired: "+level;
            }finally{SandboxTrackEditing.Suspended=suspended;}
        }
        public static string Review(int level)
        {
            bool suspended=SandboxTrackEditing.Suspended;SandboxTrackEditing.Suspended=true;
            var scene=EditorSceneManager.OpenScene($"Assets/MetaHackthone/Scenes/Level{level}.unity");
            var f=SandboxFactoryAuthoring.Find(scene);
            var hidden=f.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Horizon transition islands"||t.name=="Layered realistic cloud banks"||t==f.Layout.BuildingRoot||t.name.Contains("(editor only)")).ToArray();
            var states=hidden.Select(t=>t.gameObject.activeSelf).ToArray();
            try {
                f.Layout.BuildingRoot.gameObject.SetActive(false);
                EnvironmentPolishReview.PlayerView(f,$"TraySeal-Level{level}-Player");
                foreach(var t in hidden)t.gameObject.SetActive(false);
                var tray=f.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="01 Display base");
                var rr=tray.GetComponentsInChildren<Renderer>();var bounds=rr[0].bounds;foreach(var r in rr)bounds.Encapsulate(r.bounds);
                var direction=f.Layout.PlayerStartPose(SandboxLevelLayout.DefaultPlayerDistance).position-bounds.center;direction.y=0;direction=direction.normalized;direction.y=.22f;
                EnvironmentPolishReview.CaptureBounds(bounds,$"TraySeal-Level{level}-Edge",direction,1.12f);
                return "Reviewed "+level;
            }finally{for(int n=0;n<hidden.Length;n++)hidden[n].gameObject.SetActive(states[n]);EditorSceneManager.OpenScene(scene.path);SandboxTrackEditing.Suspended=suspended;}
        }
        static void Repair(SandboxFactoryLevel f,string key)
        {
            Fit(f,key);GroundProps(f,key);ContactPolishAuthoring.Roads(f,key);ContactPolishAuthoring.Foam(f,key);
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/MetaHackthone/Art/SceneLayouts/PlayerViewMarker.mat");
            foreach(var t in f.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Player view (editor only)"))if(t.TryGetComponent<Renderer>(out var r))r.sharedMaterial=material;
        }
    }
}
