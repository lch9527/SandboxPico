using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAssembly.Editor
{
    // One-shot migration for the 2026-11 sandbox pass. Safe to run again: every step checks its own state.
    //  - Level 2/3 authoring files follow the new campaign order (Crab Bridge = 2, Wheel Size = 3).
    //  - Level 5's drop-bridge target moves to building layer 4, where the default car's drill reaches it.
    //  - Drop-bridge targets and rollers are regenerated (stone target, gravel look).
    //  - Every level gets a Player Start marker under its Building Area.
    //  - All five levels are saved to the game prefabs.
    public static class SandboxImprovementsAuthoring
    {
        const string Root="Assets/MetaHackthone/";
        [MenuItem("MetaHackthone/Level Factory/Apply Sandbox Improvements (all levels)")]
        public static void ApplyAll()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var original=SceneManager.GetActiveScene().path;
            SwapLevel2And3Files();
            for(int n=1;n<=5;n++) UpdateLevel(n);
            UpdateSampleScene(Root+"Scenes/FactoryFinalRun.unity");
            AssetDatabase.SaveAssets();
            if(!string.IsNullOrEmpty(original) && AssetDatabase.LoadAssetAtPath<SceneAsset>(original)) EditorSceneManager.OpenScene(original);
            Debug.Log("METAHACKTHONE_SANDBOX_IMPROVEMENTS_APPLIED");
        }
        static SandboxCourseId PrefabCourse(string path)
        {
            var level=AssetDatabase.LoadAssetAtPath<SandboxFactoryLevel>(path);
            if(!level || !level.Layout) throw new InvalidOperationException("Missing campaign prefab layout: "+path);
            return level.Layout.CourseId;
        }
        public static void SwapLevel2And3Files()
        {
            // File names follow the displayed campaign number. Moves keep GUIDs, so settings references survive.
            if(PrefabCourse(Root+"Generated/Campaign/Level2.prefab")==SandboxCourseCatalog.Courses[1]) return;
            foreach(var (folder,name,extension) in new[]{("Scenes/","Level{0}",".unity"),("Scenes/","Level{0}_Factory",""),
                ("Generated/Campaign/","Level{0}",".prefab"),("Generated/Campaign/","Level{0}Meshes","")}) {
                string a=Root+folder+string.Format(name,2)+extension, b=Root+folder+string.Format(name,3)+extension, temp=Root+folder+string.Format(name,"23swap")+extension;
                if(!AssetDatabase.LoadMainAssetAtPath(a) || !AssetDatabase.LoadMainAssetAtPath(b)) continue;
                Move(a,temp); Move(b,a); Move(temp,b);
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            if(PrefabCourse(Root+"Generated/Campaign/Level2.prefab")!=SandboxCourseCatalog.Courses[1]) throw new InvalidOperationException("Level 2/3 swap did not complete.");
        }
        static void Move(string from,string to) { var error=AssetDatabase.MoveAsset(from,to); if(!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error); }

        static void UpdateLevel(int n)
        {
            var path=Root+"Scenes/Level"+n+".unity";
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            var factory=SandboxFactoryAuthoring.Find(scene);
            if(!factory) throw new InvalidOperationException("No factory level in "+path);
            var id=SandboxCourseCatalog.Courses[n-1];
            if(factory.Layout) {
                if(factory.Layout.CourseId!=id) throw new InvalidOperationException($"{path} holds {factory.Layout.CourseId}, expected {id}.");
                factory.Layout.name="Level "+n+" - "+SandboxCourseCatalog.Title(id);
                SandboxPlayerStartAuthoring.Ensure(factory.Layout);
                EditorUtility.SetDirty(factory.Layout);
            }
            if(id==SandboxCourseId.FactoryLevel4) foreach(var bridge in factory.Puzzles.OfType<SandboxDropBridge>()) PassableByDefaultCar(bridge);
            RegeneratePuzzles(factory);
            EditorSceneManager.MarkSceneDirty(scene);
            if(n==1) SandboxFactoryCampaignSave.SaveLevel1();
            else if(n==5) SandboxFactoryCampaignSave.SaveLevel4();
            else FiveCourseAuthoring.Save(n);
            EditorSceneManager.SaveScene(scene);
        }
        static void UpdateSampleScene(string path)
        {
            if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(path)) return;
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            var factory=SandboxFactoryAuthoring.Find(scene); if(!factory) return;
            foreach(var bridge in factory.Puzzles.OfType<SandboxDropBridge>()) PassableByDefaultCar(bridge);
            RegeneratePuzzles(factory);
            EditorSceneManager.SaveScene(scene);
        }
        // Level 5's default car ("Combined wide high drill") must be able to pass:
        //  - its drill rides on building layer 4, so the stone sits on layer 4;
        //  - the roller before the bridge launches the car (~4.5 units of flight), so the stone sits
        //    4.5 units before the bridge entry, after the landing, where the drill meets it on the ground;
        //  - 0.6 wide tolerates small lateral drift without lowering the stone onto the front blocks;
        //  - the hanging rod drops with the stone so the drill's support block can pass underneath.
        public const float FinalRunTargetApproach=4.5f, FinalRunTargetSize=.6f;
        static void PassableByDefaultCar(SandboxDropBridge bridge)
        {
            bridge.TargetLayer=4; bridge.TargetSize=FinalRunTargetSize; bridge.ReleaseSuspensionWithTarget=true;
            bridge.TargetApproach=FinalRunTargetApproach; bridge.TargetDistance=bridge.Distance-bridge.Length*.5f-FinalRunTargetApproach;
            EditorUtility.SetDirty(bridge);
        }
        static void RegeneratePuzzles(SandboxFactoryLevel factory)
        {
            foreach(var puzzle in factory.Puzzles) if(puzzle is SandboxDropBridge || puzzle is SandboxRoller) { puzzle.GeometrySignature=""; EditorUtility.SetDirty(puzzle); }
            foreach(var track in factory.Tracks.Where(t=>t))
                if(!SandboxTrackBaker.Rebuild(track,true)) throw new InvalidOperationException(track.name+": "+track.ValidationError);
        }
    }
}
