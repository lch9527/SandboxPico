using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class SandboxLevelLayoutAuthoring
    {
        [MenuItem("MetaHackthone/Design Mode/Group Current Level for Editing")]
        public static void GroupCurrent()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before grouping a level.");
            var scene=SceneManager.GetActiveScene();
            var owner=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<BuildingSceneLayout>(true)).Single();
            Migrate(owner); EditorSceneManager.MarkSceneDirty(scene);
        }
        public static void Migrate(BuildingSceneLayout owner)
        {
            if(owner.LevelLayouts!=null && owner.LevelLayouts.Length>0) { SandboxTrackHierarchyAuthoring.Apply(owner.GetComponent<SandboxFactoryLevel>()); return; }
            Undo.IncrementCurrentGroup(); int undo=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Group sandbox level");
            Undo.RegisterFullObjectHierarchyUndo(owner.gameObject,"Group sandbox level");
            var factory=owner.GetComponent<SandboxFactoryLevel>();
            var authored=owner.GetComponent<AuthoredSandboxLayout>();
            var id=factory?(factory.UseStarterPalette?SandboxCourseId.BeautifulCorner:SandboxCourseId.FactoryLevel4):SandboxCourseId.GravityGarage;
            var first=Group(owner,id);
            if(factory) {
                factory.Layout=first;
                if(factory.WorkbenchVisuals) Move(factory.WorkbenchVisuals,first.BuildingRoot);
                foreach(var scenery in factory.Scenery.Where(t=>t))
                    if(!scenery.IsChildOf(first.SandboxRoot) && scenery!=factory.WorkbenchVisuals) Move(scenery,first.Course.SandboxFrame);
                owner.LevelLayouts=new[]{first};
            } else if(authored) {
                // Clone together so every control, marker, collider and preview reference remaps.
                var second=Object.Instantiate(first,first.transform.parent);
                Undo.RegisterCreatedObjectUndo(second.gameObject,"Create independent Level 3 layout");
                second.CourseId=SandboxCourseId.TeleportPipes;
                second.name="Level 3 - Waterfall Valley";
                var valley=second.GetComponentInChildren<GravityGarage3DCourse>(true);
                var garage=second.GetComponentsInChildren<GravityGarageCourse>(true).Single(c=>c.Markers);
                Undo.DestroyObjectImmediate(garage.gameObject);
                Undo.DestroyObjectImmediate(authored.Level2.gameObject);
                authored.Level2=valley;
                owner.LevelLayouts=new[]{first,second};
                authored.Show(authored.EditorLevel);
            } else owner.LevelLayouts=new[]{first};
            SandboxTrackHierarchyAuthoring.Apply(factory);
            EditorUtility.SetDirty(owner);
            Undo.CollapseUndoOperations(undo);
        }
        static SandboxLevelLayout Group(BuildingSceneLayout owner,SandboxCourseId id)
        {
            var course=owner.Downhill;
            var presentation=course.GetComponent<DownhillPresentation>();
            var binding=Undo.AddComponent<SandboxLevelLayout>(course.gameObject);
            binding.CourseId=id; binding.Course=course;
            binding.Spawns=owner.Spawns; binding.Menu=owner.Menu; binding.Surface=owner.Surface;
            binding.BuildStand=owner.BuildStand; binding.ModeLabel=owner.ModeLabel; binding.StatusLabel=owner.StatusLabel;
            binding.CommandLabel=owner.CommandLabel; binding.ScoreLabel=owner.ScoreLabel;
            course.name="Level "+SandboxCourseCatalog.Number(id)+" - "+SandboxCourseCatalog.Title(id);
            var children=course.transform.Cast<Transform>().ToArray();
            binding.BuildingRoot=Root("Building Area",course.transform,course.WorkbenchFrame);
            binding.SandboxRoot=Root("Sandbox Level",course.transform,course.SandboxFrame);
            foreach(var child in children) Move(child,child==course.SandboxFrame?binding.SandboxRoot:binding.BuildingRoot);
            // Keep the authored frame and geometry unchanged inside the editable root.
            course.SandboxFrame.name="Launch animation frame";
            if(owner.Menu && !owner.Menu.IsChildOf(binding.BuildingRoot)) Move(owner.Menu,binding.BuildingRoot);
            if(owner.Surface && !owner.Surface.transform.IsChildOf(binding.BuildingRoot)) Move(owner.Surface.transform,binding.BuildingRoot);
            if(owner.BuildStand && !owner.BuildStand.transform.IsChildOf(binding.BuildingRoot)) Move(owner.BuildStand.transform,binding.BuildingRoot);
            foreach(var spawn in owner.Spawns.Where(s=>s)) if(!spawn.transform.IsChildOf(binding.BuildingRoot)) Move(spawn.transform,binding.BuildingRoot);
            return binding;
        }
        static Transform Root(string name,Transform parent,Transform pivot)
        {
            var root=new GameObject(name).transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject,"Create "+name);
            root.SetParent(parent,false); root.SetPositionAndRotation(pivot.position,Quaternion.Euler(0,pivot.eulerAngles.y,0));
            return root;
        }
        static void Move(Transform child,Transform parent) => Undo.SetTransformParent(child,parent,"Group sandbox content");
    }
}
