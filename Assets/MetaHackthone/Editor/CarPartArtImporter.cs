using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    // Turns the Blender kit in Art/CarParts (rebuilt by Source~/BuildCarPartsArt.py) into materials,
    // LOD prefabs and Resources/CarPartArt, the library the designer parts and road rollers read.
    public static class CarPartArtImporter
    {
        const string Kit="Assets/MetaHackthone/Art/CarParts";
        const string Models=Kit+"/Models", Materials=Kit+"/Materials", Prefabs=Kit+"/Prefabs", Grain=Kit+"/Textures/CarArt_WoodGrain.png";
        const string LibraryPath="Assets/MetaHackthone/Resources/CarPartArt.asset";
        const string PreviewPath="Docs/Images/CarPartsArt-Unity.png";

        static readonly (PartKind kind,string body,string parent,string piece)[] Visuals={
            (PartKind.DownhillSmallWheel,"tire","","SmallWheelTire"),(PartKind.DownhillSmallWheel,"hub","","SmallWheelHub"),
            (PartKind.DownhillLargeWheel,"tire","","LargeWheelTire"),(PartKind.DownhillLargeWheel,"hub","","LargeWheelHub"),
            (PartKind.DownhillCore,"body","","Core"),(PartKind.DownhillCube,"body","","Cube"),
            (PartKind.DownhillShortBlock,"body","","ShortBlock"),(PartKind.DownhillLongBlock,"body","","LongBlock"),
            (PartKind.DownhillTriangle,"body","","Triangle"),
            (PartKind.DownhillDrill,"body","","DrillHousing"),(PartKind.DownhillDrill,"body","Silver spiral bit","DrillBit")};
        static readonly (string prefab,string[] pieces)[] PartPrefabs={
            ("CarArt_SmallWheel",new[]{"SmallWheelTire","SmallWheelHub"}),("CarArt_LargeWheel",new[]{"LargeWheelTire","LargeWheelHub"}),
            ("CarArt_Core",new[]{"Core"}),("CarArt_Cube",new[]{"Cube"}),("CarArt_ShortBlock",new[]{"ShortBlock"}),
            ("CarArt_LongBlock",new[]{"LongBlock"}),("CarArt_Triangle",new[]{"Triangle"}),("CarArt_Drill",new[]{"DrillHousing","DrillBit"}),
            ("CarArt_Handwheel",new[]{"Handwheel"}),
            // Scenery that belongs to one level: Level 3 (wheelwright) and Level 4 (quarry).
            ("CarArt_Prop_TyreStack",new[]{"PropTyreStack"}),("CarArt_Prop_WheelSign",new[]{"PropWheelSign"}),
            ("CarArt_Prop_StoneBlocks",new[]{"PropStoneBlocks"}),("CarArt_Prop_OreCart",new[]{"PropOreCart"})};

        [MenuItem("MetaHackthone/Car Parts Art/Import Blender kit")]
        public static void Import()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(var folder in new[]{Materials,Prefabs}) if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Kit,Path.GetFileName(folder));
            var grain=ImportGrain();
            var materials=new Dictionary<string,Material> {
                // The grain map is a light multiplier, so the tint alone still reads as honey wood when an effect drops the map.
                ["CarArt_Wood"]=Lit("CarArt_Wood","#E4A856",.28f,0,grain),
                ["CarArt_WoodEdge"]=Lit("CarArt_WoodEdge","#8F5822",.22f,0),
                ["CarArt_Enamel"]=Lit("CarArt_Enamel","#296B78",.55f,0),
                ["CarArt_Rubber"]=Lit("CarArt_Rubber","#343D44",.18f,0),
                ["CarArt_Brass"]=Lit("CarArt_Brass","#B88A50",.55f,.45f),
                ["CarArt_Energy"]=Lit("CarArt_Energy","#61CFC7",.4f,0,null,"#3FA8A2"),
                ["CarArt_Cream"]=Lit("CarArt_Cream","#EDE3CC",.35f,0),
                ["CarArt_Steel"]=Lit("CarArt_Steel","#AEB6BC",.55f,.5f),
                ["CarArt_BumpPaint"]=Lit("CarArt_BumpPaint","#F2B632",.3f,0),
                ["CarArt_BumpBase"]=Lit("CarArt_BumpBase","#343D44",.18f,0),
                ["CarArt_Sandstone"]=Lit("CarArt_Sandstone","#D8AE6A",.12f,0),
                ["CarArt_SandstoneEdge"]=Lit("CarArt_SandstoneEdge","#A17B46",.1f,0)};
            AssetDatabase.SaveAssets();

            var meshes=new Dictionary<string,Mesh[]>(); var slots=new Dictionary<string,Material[]>();
            foreach(var file in Directory.GetFiles(Models,"*.fbx")) {
                var path=file.Replace('\\','/'); var piece=Path.GetFileNameWithoutExtension(path);
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);
                importer.globalScale=1; importer.useFileScale=true; importer.isReadable=true; importer.addCollider=false;
                importer.importNormals=ModelImporterNormals.Import; importer.importTangents=ModelImporterTangents.None;
                importer.importAnimation=false; importer.animationType=ModelImporterAnimationType.None; importer.importBlendShapes=false;
                importer.importCameras=false; importer.importLights=false; importer.meshCompression=ModelImporterMeshCompression.Off;
                importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
                foreach(var pair in materials) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material),pair.Key),pair.Value);
                importer.SaveAndReimport();
                var found=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToDictionary(m=>m.name);
                var lods=Enumerable.Range(0,3).Select(level=>found.TryGetValue($"{piece}_LOD{level}",out var mesh)?mesh:throw new InvalidOperationException($"{piece}: LOD{level} mesh missing")).ToArray();
                var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var used=model.GetComponentsInChildren<MeshRenderer>().First(r=>r.GetComponent<MeshFilter>().sharedMesh==lods[0]).sharedMaterials;
                foreach(var renderer in model.GetComponentsInChildren<MeshRenderer>())
                    if(!renderer.sharedMaterials.SequenceEqual(used)) throw new InvalidOperationException($"{piece}: material slots differ between LODs");
                if(used.Any(m=>!m || !materials.ContainsValue(m))) throw new InvalidOperationException($"{piece}: a material was not remapped to the kit");
                meshes[piece]=lods; slots[piece]=used;
            }
            VerifyAxes(meshes);

            var library=AssetDatabase.LoadAssetAtPath<CarPartArtLibrary>(LibraryPath);
            if(!library) { library=ScriptableObject.CreateInstance<CarPartArtLibrary>(); AssetDatabase.CreateAsset(library,LibraryPath); }
            library.Visuals=Visuals.Select(v=>new CarPartArtLibrary.Visual {Kind=v.kind,Body=v.body,Parent=v.parent,Lods=meshes[v.piece],Materials=slots[v.piece]}).ToArray();
            library.BumpMid=meshes["SpeedBumpMid"]; library.BumpEnd=meshes["SpeedBumpEnd"];
            library.BumpPaint=materials["CarArt_BumpPaint"]; library.BumpBase=materials["CarArt_BumpBase"];
            library.Handwheel=meshes["Handwheel"]; library.HandwheelMaterials=slots["Handwheel"];
            EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();

            foreach(var (prefab,pieces) in PartPrefabs) {
                var root=new GameObject(prefab);
                try {
                    var lods=new LOD[3];
                    for(int level=0;level<3;level++) lods[level]=new LOD(1f/(level+2),pieces.Select(piece=>{
                        var go=new GameObject($"{piece}_LOD{level}"); go.transform.SetParent(root.transform,false);
                        go.AddComponent<MeshFilter>().sharedMesh=meshes[piece][level];
                        var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterials=slots[piece]; return (Renderer)renderer;
                    }).ToArray());
                    var group=root.AddComponent<LODGroup>(); group.fadeMode=LODFadeMode.None; group.SetLODs(lods); group.RecalculateBounds();
                    // Scenery props are judged by a 20 cm detail unit: they are viewed from metres away, never held.
                    float unit=prefab.Contains("_Prop_")?.2f:BuildingGeometry.Unit;
                    lods[0].screenRelativeTransitionHeight=CarPartArtLibrary.ScreenHeight(library.Lod0Degrees,group.size,unit);
                    lods[1].screenRelativeTransitionHeight=CarPartArtLibrary.ScreenHeight(library.Lod1Degrees,group.size,unit);
                    if(prefab.Contains("_Prop_")) foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>()) { renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows=false; }
                    lods[2].screenRelativeTransitionHeight=.0002f;
                    group.SetLODs(lods);
                    PrefabUtility.SaveAsPrefabAsset(root,$"{Prefabs}/{prefab}.prefab");
                } finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            var sample=new GameObject("CarArt_SpeedBump_Sample");
            try {
                if(!CarPartArtLibrary.AddSpeedBump(sample.transform,SandboxRoller.BumpName,2.4f,.35f,0)) throw new InvalidOperationException("Speed bump could not be built from the library");
                PrefabUtility.SaveAsPrefabAsset(sample,$"{Prefabs}/CarArt_SpeedBump_Sample.prefab");
            } finally { UnityEngine.Object.DestroyImmediate(sample); }
            AssetDatabase.SaveAssets();
            Debug.Log($"CAR_PART_ART_IMPORT_OK visuals={library.Visuals.Length} triangles LOD0/1/2: "+string.Join(", ",meshes.Select(p=>$"{p.Key} {string.Join("/",p.Value.Select(m=>m.triangles.Length/3))}")));
        }

        // The Blender script writes Unity part space; a mirrored or rotated import must fail loudly.
        static void VerifyAxes(Dictionary<string,Mesh[]> meshes)
        {
            float u=BuildingGeometry.Unit; const float tolerance=.0002f;
            void Near(string what,float actual,float expected) { if(Mathf.Abs(actual-expected)>tolerance) throw new InvalidOperationException($"{what}: {actual:R} expected {expected:R}"); }
            foreach(var pair in meshes) for(int level=0;level<3;level++) {
                var b=pair.Value[level].bounds; string id=$"{pair.Key} LOD{level}";
                switch(pair.Key) {
                    case "DrillBit": Near(id+" tip",b.max.z,SandboxDrill.Tip(u,1).z); break;
                    case "DrillHousing": Near(id+" base",b.min.z,SandboxDrill.BaseCenter(u,1).z); if(b.max.z>SandboxDrill.BaseCenter(u,1).z+u+tolerance) throw new InvalidOperationException(id+" leaves the collider cube"); break;
                    case "Triangle":
                        Near(id+" back",b.min.z,-u); Near(id+" base",b.min.y,-u*.5f);
                        foreach(var vertex in pair.Value[level].vertices) if(vertex.y>u*.3f && vertex.z>-u*.3f) throw new InvalidOperationException(id+" slope faces the wrong way");
                        break;
                    case "SpeedBumpEnd": Near(id+" joint",b.min.x,0); Near(id+" end",b.max.x,.3f); Near(id+" top",b.max.y,1); break;
                    case "SpeedBumpMid": Near(id+" length",b.size.x,.8f); Near(id+" top",b.max.y,1); Near(id+" ground",b.min.y,0); break;
                    case "Handwheel": Near(id+" centre",b.center.x,0); if(b.size.y>.04f || b.size.x<.4f) throw new InvalidOperationException(id+" does not lie in XZ around the grab circle"); break;
                    case "LongBlock": Near(id+" length",b.size.z,3*u); Near(id+" width",b.size.x,u); break;
                    case "ShortBlock": Near(id+" length",b.size.z,2*u); Near(id+" width",b.size.x,u); break;
                    case "SmallWheelTire": case "LargeWheelTire": if(b.size.x>.23f/.38f*u+tolerance) throw new InvalidOperationException(id+" axle is not X"); break;
                }
            }
        }

        static Texture2D ImportGrain()
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(Grain);
            importer.textureType=TextureImporterType.Default; importer.sRGBTexture=true; importer.wrapMode=TextureWrapMode.Repeat;
            importer.mipmapEnabled=true; importer.anisoLevel=4; importer.maxTextureSize=512; importer.alphaSource=TextureImporterAlphaSource.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Grain);
        }
        static Material Lit(string name,string html,float smoothness,float metallic,Texture2D map=null,string emission=null)
        {
            string path=$"{Materials}/{name}.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")) {name=name}; AssetDatabase.CreateAsset(material,path); }
            ColorUtility.TryParseHtmlString(html,out var color);
            material.SetColor("_BaseColor",color); material.SetFloat("_Smoothness",smoothness); material.SetFloat("_Metallic",metallic);
            material.SetTexture("_BaseMap",map);
            if(emission!=null) {
                ColorUtility.TryParseHtmlString(emission,out var glow);
                material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor",glow); material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
            }
            material.enableInstancing=true;
            EditorUtility.SetDirty(material); return material;
        }

        // Three rows (LOD0, LOD1, LOD2) of the kit plus the speed bump, rendered without touching the open scene.
        [MenuItem("MetaHackthone/Car Parts Art/Capture preview")]
        public static void CapturePreview()
        {
            var preview=new PreviewRenderUtility(); var temporary=new List<UnityEngine.Object>();
            try {
                float spacing=.27f, x=0;
                var names=PartPrefabs.Select(p=>p.prefab).Append("CarArt_SpeedBump_Sample").ToArray();
                for(int column=0;column<names.Length;column++) {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/{names[column]}.prefab");
                    if(!prefab) throw new InvalidOperationException("Run Import Blender kit first: "+names[column]);
                    bool bump=names[column]=="CarArt_SpeedBump_Sample", wheel=names[column]=="CarArt_Handwheel", prop=names[column].Contains("_Prop_"); if(bump) x+=.2f;
                    for(int level=0;level<3;level++) {
                        var go=UnityEngine.Object.Instantiate(prefab); temporary.Add(go);
                        go.transform.position=new Vector3(x,bump||prop?0:wheel?.02f:BuildingGeometry.Unit*1.2f,level*.42f);
                        go.transform.rotation=Quaternion.Euler(0,bump?-20:-35,0); if(bump) go.transform.localScale=Vector3.one*.25f; if(wheel) go.transform.localScale=Vector3.one*.6f; if(prop) go.transform.localScale=Vector3.one*.35f;
                        // ForceLOD is ignored by the preview scene; show the wanted level's renderers directly.
                        foreach(var group in go.GetComponentsInChildren<LODGroup>()) {
                            var lods=group.GetLODs(); group.enabled=false;
                            for(int i=0;i<lods.Length;i++) foreach(var renderer in lods[i].renderers) renderer.enabled=i==level;
                        }
                        preview.AddSingleGO(go);
                    }
                    x+=spacing;
                }
                var ground=GameObject.CreatePrimitive(PrimitiveType.Plane); temporary.Add(ground);
                var sand=new Material(Shader.Find("Universal Render Pipeline/Lit")); temporary.Add(sand); sand.SetColor("_BaseColor",new Color(.80f,.72f,.58f)); sand.SetFloat("_Smoothness",.05f);
                ground.GetComponent<Renderer>().sharedMaterial=sand; ground.transform.position=new Vector3(x*.5f,0,.4f); ground.transform.localScale=Vector3.one*4f;
                preview.AddSingleGO(ground);
                var camera=preview.camera; camera.fieldOfView=15f; camera.nearClipPlane=.05f; camera.farClipPlane=40;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.93f,.90f,.82f);
                var target=new Vector3(x*.5f+.06f,.06f,.42f); camera.transform.position=target+new Vector3(.4f,3.1f,-4.4f); camera.transform.LookAt(target);
                preview.lights[0].intensity=1.25f; preview.lights[0].transform.rotation=Quaternion.Euler(50,-28,0);
                preview.lights[1].intensity=.55f; preview.lights[1].transform.rotation=Quaternion.Euler(-25,150,0);
                preview.ambientColor=new Color(.5f,.5f,.5f);
                preview.BeginStaticPreview(new Rect(0,0,2400,1100)); camera.Render(); var image=preview.EndStaticPreview(); temporary.Add(image);
                Directory.CreateDirectory(Path.GetDirectoryName(PreviewPath)); File.WriteAllBytes(PreviewPath,image.EncodeToPNG());
                Debug.Log("CAR_PART_ART_PREVIEW_OK "+PreviewPath);
            } finally {
                foreach(var item in temporary) if(item) UnityEngine.Object.DestroyImmediate(item);
                preview.Cleanup();
            }
        }

        // Rollers baked into a level before the speed bump existed still hold the gravel heap in the scene file
        // (the game swaps it on load). This writes the swap into the open scenes so the editor shows what the game shows.
        [MenuItem("MetaHackthone/Car Parts Art/Replace baked gravel with speed bumps (open scenes)")]
        public static void ReplaceBakedGravel()
        {
            int changed=0;
            foreach(var roller in UnityEngine.Object.FindObjectsByType<SandboxRoller>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
                if(!roller.Generated || !roller.Generated.transform.Find(SandboxRoller.GravelName)) continue;
                if(!roller.RefreshVisual()) continue;
                changed++; EditorUtility.SetDirty(roller.Generated);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(roller.gameObject.scene);
            }
            Debug.Log($"CAR_PART_ART_BUMPS rollers changed: {changed}. Save the scene to publish it to the game.");
        }

        // unity -batchmode -executeMethod PhysicalAssembly.Editor.CarPartArtImporter.ImportBatch (graphics needed for the preview)
        public static void ImportBatch()
        {
            try { Import(); CapturePreview(); EditorApplication.Exit(0); }
            catch(Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
