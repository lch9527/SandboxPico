using System;
using System.Linq;
using CloudGarden.Integration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Unity.Mathematics;
using Object = UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class BeautifulCornerDesignAuthoring
    {
        public const string ScenePath="Assets/MetaHackthone/Scenes/Level1.unity";
        public const string PrefabPath="Assets/MetaHackthone/Generated/Campaign/Level1.prefab";
        const string ArtPath="Assets/CreekDiorama/Prefabs/BeautifulCorner.prefab";
        const string SettingsPath="Assets/MetaHackthone/Generated/GravityGarageSettings.asset";

        // Explicit, one-time conversion. Never regenerate a designer's existing copy.
        public static void CreateCopy()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) throw new InvalidOperationException("The Beautiful Corner copy already exists. Open and edit it instead.");
            SandboxFactoryAuthoring.EnsureTemplate();
            if(!AssetDatabase.CopyAsset(SandboxFactoryAuthoring.TemplatePath,ScenePath)) throw new InvalidOperationException("Could not copy the Sandbox template.");
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            bool suspended=SandboxTrackEditing.Suspended; SandboxTrackEditing.Suspended=true;
            try {
                SceneManager.SetActiveScene(scene);
                SandboxFactoryAuthoring.MakeIndependent(scene,ScenePath);
                var factory=SandboxFactoryAuthoring.Find(scene);
                factory.Title="Beautiful Corner";
                factory.GetComponent<BuildingSceneLayout>().Downhill.SandboxFrame.localScale=Vector3.one;
                factory.TrackRoot.localScale=Vector3.one*factory.CourseScale;
                var art=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ArtPath),factory.TrackRoot);
                // Independent scene objects: editing this copy never applies overrides to the showcase.
                PrefabUtility.UnpackPrefabInstance(art,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                art.name="Beautiful Corner art - editable copy";
                const float artScale=16;
                art.transform.localPosition=Vector3.zero;
                art.transform.localRotation=Quaternion.identity;
                art.transform.localScale=Vector3.one*artScale;

                var track=SandboxFactoryAuthoring.AddTrack(factory,false);
                track.name="Beautiful Corner driving path";
                track.DeckStyle=SandboxDeckStyle.Plain; track.ShowGeneratedVisuals=false;
                track.Supports=false; track.Guardrails=false;
                track.DefaultWidth=.24f*artScale;
                track.SampleSpacing=.12f;
                // Bridge surface measured at its center, including both station approaches.
                // Art remains untouched; the spline supplies smooth collision and center assist.
                var points=new[]{new Vector2(1.35f,.67f),new Vector2(1.03f,.665f),new Vector2(.8f,.665f),
                    new Vector2(.7f,.628f),new Vector2(.6f,.56f),new Vector2(.5f,.502f),new Vector2(.4f,.453f),
                    new Vector2(.3f,.413f),new Vector2(.2f,.386f),new Vector2(.1f,.367f),new Vector2(0,.359f),
                    new Vector2(-.1f,.359f),new Vector2(-.2f,.369f),new Vector2(-.3f,.389f),new Vector2(-.4f,.42f),
                    new Vector2(-.5f,.46f),new Vector2(-.6f,.511f),new Vector2(-.7f,.571f),new Vector2(-.8f,.605f),
                    new Vector2(-1.12f,.605f)};
                var spline=new Spline();
                foreach(var p in points) spline.Add(new BezierKnot(new float3(p.x,p.y,-.03f)*artScale),TangentMode.AutoSmooth);
                track.Curve.Spline=spline;
                for(int i=0;i<spline.Count;i++) track.SetKnotWidth(i,track.DefaultWidth);
                factory.StartBinding=new SandboxTrackBinding {Track=track,Distance=.32f*artScale};
                factory.BindStart=true;
                factory.Finish.transform.localPosition=new Vector3(-1.04f,.67f,-.03f)*artScale;
                factory.Finish.transform.localRotation=Quaternion.identity;
                factory.Finish.size=new Vector3(.14f,.3f,.3f)*artScale;
                factory.TestBounds.transform.localPosition=Vector3.zero;
                factory.TestBounds.center=new Vector3(0,.5f,0)*artScale;
                factory.TestBounds.size=new Vector3(3.2f,1.8f,2.4f)*artScale;
                var car=AssetDatabase.LoadAssetAtPath<SandboxTestCarAsset>(SandboxFactorySamples.PresetFolder+"/Narrow car.asset");
                // Preserve the scene's independent car asset made by MakeIndependent.
                EditorUtility.CopySerialized(car,factory.TestCar); factory.TestCar.name="Beautiful Corner test car";
                EditorUtility.SetDirty(factory.TestCar);
                if(!SandboxTrackBaker.Rebuild(track,true)) throw new InvalidOperationException(track.ValidationError);
                SandboxTrackEditing.UpdateStart(factory);
                ConfigureCampaignCopy(factory);
                Save(factory);
            } finally {
                SandboxTrackEditing.Suspended=suspended;
                EditorSceneManager.CloseScene(scene,true);
                if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        public static void ConfigureCampaignCopy(SandboxFactoryLevel factory)
        {
            factory.UseStarterPalette=true;
            var layout=factory.GetComponent<BuildingSceneLayout>();
            var presentation=layout.Downhill.GetComponent<DownhillPresentation>();
            var art=factory.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Beautiful Corner art - editable copy");
            factory.Scenery=new[]{art};
            foreach(var track in factory.Tracks) { track.DeckStyle=SandboxDeckStyle.Plain; track.ShowGeneratedVisuals=false; }
            factory.StartBinding.Distance=(1.35f-1.08f)*16;
            var goal=art.Find("Anchor_Goal");
            factory.Finish.transform.SetParent(goal,true);
            factory.Finish.transform.localPosition=new Vector3(0,.08f,0);
            if(!factory.WorkbenchVisuals) {
                var table=art.Find("Atmosphere and assembly area/AssemblyBounds");
                table.SetParent(factory.transform,true);
                table.name="Beautiful Corner glowing workbench";
                var surface=table.Find("Assembly tabletop - shared footprint").GetComponent<Renderer>();
                var pad=presentation.FindGridElement("Centered grid pad").GetComponent<Renderer>();
                // Fit the existing functional build area, keeping all support and control objects.
                table.localScale=Vector3.Scale(table.localScale,new Vector3(.80f/surface.bounds.size.x,.22f/table.GetComponent<Renderer>().bounds.size.y,.70f/surface.bounds.size.z));
                table.position+=pad.bounds.center-surface.bounds.center;
                factory.WorkbenchVisuals=table;
            }
            // Copy the authored Gravity Garage palette instead of the Final Run factory palette.
            var game=SceneManager.GetSceneByPath("Assets/MetaHackthone/Scenes/DownhillSandbox.unity");
            bool opened=!game.isLoaded;
            if(opened) game=EditorSceneManager.OpenScene("Assets/MetaHackthone/Scenes/DownhillSandbox.unity",OpenSceneMode.Additive);
            try {
                var source=game.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<DownhillEntry>(true)).Single();
                var palette=new GameObject("Level 1 starter palette").transform; palette.SetParent(layout.Downhill.WorkbenchFrame,false);
                var old=layout.Spawns;
                layout.Spawns=source.Layout.Spawns.Select(spawn=> {
                    var copy=Object.Instantiate(spawn,palette);
                    copy.transform.SetPositionAndRotation(layout.transform.TransformPoint(source.Layout.transform.InverseTransformPoint(spawn.transform.position)),layout.transform.rotation*Quaternion.Inverse(source.Layout.transform.rotation)*spawn.transform.rotation);
                    return copy;
                }).ToArray();
                foreach(var spawn in old) if(spawn) Object.DestroyImmediate(spawn.gameObject);
                var entry=factory.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<DownhillEntry>(true)).Single();
                entry.Settings.SandboxDrillCourse=true;
                entry.Settings.Level25D=source.Settings.Level25D; entry.Settings.Level3D=source.Settings.Level3D;
                // Keep the plain starter layout for Design Mode's reset car as well.
                factory.TestCar.Assembly=SandboxFactorySamples.BuildVehicle("Narrow car");
                factory.Presets=new[]{factory.TestCar};
                EditorUtility.SetDirty(factory.TestCar); EditorUtility.SetDirty(entry.Settings);
            } finally { if(opened) EditorSceneManager.CloseScene(game,true); }
            SandboxTrackEditing.UpdateStart(factory);
            EditorSceneManager.MarkSceneDirty(factory.gameObject.scene);
        }

        public static void SaveCurrent()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before saving.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath) throw new InvalidOperationException("Open Level 1 - Beautiful Corner first.");
            Save(SandboxFactoryAuthoring.Find(scene));
        }
        static void Save(SandboxFactoryLevel factory)
        {
            if(!factory || !SandboxTrackEditing.Flush(factory.gameObject.scene,true) || !factory.Validate(out _))
                throw new InvalidOperationException("Fix track validation before saving Beautiful Corner.");
            foreach(var track in factory.Tracks) SandboxTrackBaker.Persist(track);
            if(!EditorSceneManager.SaveScene(factory.gameObject.scene)) throw new InvalidOperationException("Could not save the scene.");
            var prefab=SandboxFactoryCampaignSave.Export(factory,PrefabPath,"Assets/MetaHackthone/Generated/Campaign/Level1Meshes",DesignLevelCatalog.Level1Title);
            var settings=AssetDatabase.LoadAssetAtPath<DownhillSettings>(SettingsPath);
            if(!settings) throw new InvalidOperationException("Sandbox settings are missing.");
            settings.Level1=prefab;
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
        }
    }
}
