using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace PhysicalAssembly.Editor {
    public static class SandboxTrackHierarchyAuthoring {
        public static void Apply(SandboxFactoryLevel factory) {
            if(!factory || !factory.Layout || !factory.TrackRoot) return;
            var frame=factory.Layout.Course.SandboxFrame;
            Undo.RecordObject(factory.TrackRoot.gameObject,"Name track group");
            factory.TrackRoot.name="Track";
            if(factory.TrackRoot.parent!=frame) Undo.SetTransformParent(factory.TrackRoot,frame,"Group track");
            var environment=frame.Find("Environment");
            if(!environment) {
                environment=new GameObject("Environment").transform;
                Undo.RegisterCreatedObjectUndo(environment.gameObject,"Group environment");
                environment.SetParent(frame,false);
            }
            foreach(var scenery in factory.Scenery.Where(t=>t && t!=factory.WorkbenchVisuals).ToArray()) {
                // Beautiful Corner's visible bridge and stations must follow its hidden collision spline.
                foreach(var name in new[]{"04 Timber bridge","Station_Start","Station_Goal","Anchor_Start","Anchor_Goal","09 Trail signs"}) {
                    var visual=scenery.Find(name);
                    if(visual) Undo.SetTransformParent(visual,factory.TrackRoot,"Group track artwork");
                }
                if(scenery!=environment && !scenery.IsChildOf(environment)) Undo.SetTransformParent(scenery,environment,"Separate environment from track");
            }
            foreach(var marker in new[]{factory.Start,factory.Layout.Course.StartPoint,factory.Finish?factory.Finish.transform:null,factory.TestBounds?factory.TestBounds.transform:null})
                if(marker && !marker.IsChildOf(factory.TrackRoot)) Undo.SetTransformParent(marker,factory.TrackRoot,"Group track marker");
            // Include every puzzle bound to this course even if it was dragged out of Track.
            foreach(var puzzle in factory.GetComponentsInChildren<SandboxPuzzle>(true)) {
                if(!puzzle.Track || !factory.Tracks.Contains(puzzle.Track)) continue;
                if(!puzzle.transform.IsChildOf(factory.TrackRoot)) Undo.SetTransformParent(puzzle.transform,factory.TrackRoot,"Group track puzzle");
                if(puzzle.Generated && !puzzle.Generated.transform.IsChildOf(puzzle.transform))
                    Undo.SetTransformParent(puzzle.Generated.transform,puzzle.transform,"Group puzzle geometry");
            }
            EditorUtility.SetDirty(factory);
        }
    }
}
