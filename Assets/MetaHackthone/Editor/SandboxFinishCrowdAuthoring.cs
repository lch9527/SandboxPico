// Menu items here are editor-only; nothing in this file runs in builds.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAssembly.Editor
{
    // Finish crowd marker: lives under a level's Track, next to the goal, and marks the ground the
    // mini people stand on. Saving the level scene snaps everyone to the visible ground and bakes the
    // marker into the game copy with everything else.
    [InitializeOnLoad]
    public static class SandboxFinishCrowdAuthoring
    {
        // Scenery people must not stand on top of.
        static readonly string[] NotGround={"Pine","Tree","Canopy","Fern","Flower","Ivy","Grass","Bush","Shrub","Leaf","Lantern","Pennant","Windmill","Rope","Lupin","Cloud","Water","Banner","Flag","GOAL"};

        static SandboxFinishCrowdAuthoring()
        {
            // The scenery has no colliders, so the heights are baked whenever a level is saved.
            EditorSceneManager.sceneSaving+=(scene,path)=>{
                if(Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
                foreach(var crowd in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SandboxFinishCrowd>(false))) SnapToGround(crowd);
            };
        }

        [MenuItem("MetaHackthone/Design Mode/Select Finish Crowd")]
        public static void Select()
        {
            var factory=SandboxFactoryAuthoring.Find(SceneManager.GetActiveScene());
            if(!factory) { Debug.LogWarning("No factory level in the open scene."); return; }
            var crowd=Ensure(factory,out bool created);
            if(!crowd) { Debug.LogWarning("The level needs a goal before a finish crowd can be added."); return; }
            if(created) {
                EditorSceneManager.MarkSceneDirty(crowd.gameObject.scene);
                Debug.Log("Finish crowd added just past the goal. Move it onto clear ground, then save the scene.");
            }
            Selection.activeTransform=crowd.transform; DesignModeEditor.Frame(crowd.transform);
        }
        [MenuItem("MetaHackthone/Design Mode/Snap Finish Crowd to Ground")]
        public static void SnapOpenScene()
        {
            var crowds=Object.FindObjectsByType<SandboxFinishCrowd>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
            foreach(var crowd in crowds) {
                int snapped=SnapToGround(crowd);
                Debug.Log($"Finish crowd: {snapped} of {crowd.Total} people found ground under them. The rest stand on the marker's own plane. Select the crowd to see where people can walk (green).",crowd);
                EditorSceneManager.MarkSceneDirty(crowd.gameObject.scene);
            }
            if(crowds.Length==0) Debug.LogWarning("No finish crowd in the open scene.");
        }
        // An existing crowd is left where the designer put it.
        public static SandboxFinishCrowd Ensure(SandboxFactoryLevel factory,out bool created)
        {
            created=false;
            var crowd=factory.GetComponentInChildren<SandboxFinishCrowd>(true);
            if(crowd || !factory.Finish) return crowd;
            var finish=factory.Finish;
            var go=new GameObject(SandboxFinishCrowd.MarkerName);
            Undo.RegisterCreatedObjectUndo(go,"Add finish crowd");
            go.transform.SetParent(factory.TrackRoot?factory.TrackRoot:finish.transform.parent,false);
            // Just past the goal at road height, front row facing back down the track.
            float unit=finish.transform.lossyScale.x;
            var centre=finish.transform.TransformPoint(finish.center);
            var position=centre+finish.transform.forward*(unit*2.5f);
            position.y=Physics.Raycast(centre,Vector3.down,out var hit,unit*6,~0,QueryTriggerInteraction.Ignore)?hit.point.y:finish.bounds.min.y;
            var back=-Vector3.ProjectOnPlane(finish.transform.forward,Vector3.up);
            go.transform.SetPositionAndRotation(position,Quaternion.LookRotation(back.sqrMagnitude>1e-6f?back.normalized:Vector3.forward,Vector3.up));
            created=true;
            return Undo.AddComponent<SandboxFinishCrowd>(go);
        }

        // Finds the visible ground under each person and stores how far it is from the marker's plane.
        // Looks from a little above the plane down to two person-heights below it. Also bakes where people
        // may wander while you build: a grid of ground heights over the area, with cells blocked where
        // something stands between knee and head height (trees, posts, benches, rails, cliff walls) or
        // where the ground drops away. Returns how many people found ground under their own place.
        public static int SnapToGround(SandboxFinishCrowd crowd)
        {
            var factory=crowd.GetComponentInParent<SandboxFactoryLevel>(true);
            var scope=factory?factory.transform:crowd.transform.root;
            var surfaces=new List<(Bounds bounds,Vector3[] vertices,int[] triangles)>();
            var solids=new List<(Bounds bounds,Matrix4x4 matrix,Mesh mesh)>();
            foreach(var renderer in scope.GetComponentsInChildren<MeshRenderer>(false)) {
                if(!renderer.enabled || renderer.GetComponentInParent<SandboxFinishCrowd>()) continue;
                var filter=renderer.GetComponent<MeshFilter>();
                if(!filter || !filter.sharedMesh || !filter.sharedMesh.isReadable) continue;
                solids.Add((renderer.bounds,renderer.transform.localToWorldMatrix,filter.sharedMesh));
                if(!IsGround(renderer.transform,scope)) continue;
                var vertices=filter.sharedMesh.vertices; var matrix=renderer.transform.localToWorldMatrix;
                for(int i=0;i<vertices.Length;i++) vertices[i]=matrix.MultiplyPoint3x4(vertices[i]);
                surfaces.Add((renderer.bounds,vertices,filter.sharedMesh.triangles));
            }
            float h=crowd.PersonHeight, unit=Mathf.Max(1e-5f,crowd.transform.lossyScale.x), above=h*.6f, reach=(above+h*2)*unit;
            var down=-crowd.transform.up;
            // Height of the visible ground under a point on the marker's plane, NaN when there is none.
            float GroundAt(Vector3 local)
            {
                local.y=above;
                var from=crowd.transform.TransformPoint(local); float nearest=float.MaxValue;
                foreach(var surface in surfaces) {
                    if(!surface.bounds.IntersectRay(new Ray(from,down),out float enter) || enter>reach) continue;
                    for(int t=0;t<surface.triangles.Length;t+=3)
                        if(Hit(from,down,surface.vertices[surface.triangles[t]],surface.vertices[surface.triangles[t+1]],surface.vertices[surface.triangles[t+2]],out float distance) && distance<nearest) nearest=distance;
                }
                return nearest<=reach?above-nearest/unit:float.NaN;
            }
            var serialized=new SerializedObject(crowd);
            var before=serialized.FindProperty("groundHeights");
            var beforeGrid=serialized.FindProperty("gridHeights"); var beforeWalkable=serialized.FindProperty("gridWalkable");
            crowd.SetGroundHeights(null);
            int count=crowd.Total, snapped=0; var heights=new float[count];
            float lowest=float.MaxValue, highest=float.MinValue;
            for(int i=0;i<count;i++) {
                float ground=GroundAt(crowd.StandingSpot(i));
                if(float.IsNaN(ground)) continue;
                heights[i]=ground; snapped++; lowest=Mathf.Min(lowest,ground); highest=Mathf.Max(highest,ground);
            }
            if(snapped==0) lowest=highest=0;

            crowd.GroundGridSize(out int columns,out int rows);
            var grid=new float[columns*rows]; var walkable=new bool[grid.Length];
            for(int r=0;r<rows;r++) for(int c=0;c<columns;c++) {
                int i=r*columns+c; float ground=GroundAt(crowd.GridCellCentre(c,r,columns,rows));
                grid[i]=ground;
                // Ground far below or above everyone's place is a drop or a ledge, not somewhere to stroll.
                walkable[i]=!float.IsNaN(ground) && ground>=lowest-.6f*h && ground<=highest+.6f*h;
            }
            float cellX=crowd.Area.x/columns, cellZ=crowd.Area.y/rows;
            var toLocal=crowd.transform.worldToLocalMatrix;
            var reachable=new Bounds(new Vector3(0,(lowest+highest)*.5f+.5f*h,0),new Vector3(crowd.Area.x+cellX*2,highest-lowest+3*h,crowd.Area.y+cellZ*2));
            var half=new Vector3(cellX*.4f,.4f*h,cellZ*.4f);
            foreach(var solid in solids) {
                if(!LocalBounds(solid.bounds,toLocal).Intersects(reachable)) continue;
                var matrix=toLocal*solid.matrix; var vertices=solid.mesh.vertices; var triangles=solid.mesh.triangles;
                for(int i=0;i<vertices.Length;i++) vertices[i]=matrix.MultiplyPoint3x4(vertices[i]);
                for(int t=0;t<triangles.Length;t+=3) {
                    Vector3 v0=vertices[triangles[t]], v1=vertices[triangles[t+1]], v2=vertices[triangles[t+2]];
                    var min=Vector3.Min(v0,Vector3.Min(v1,v2)); var max=Vector3.Max(v0,Vector3.Max(v1,v2));
                    int c0=Mathf.Max(0,Mathf.FloorToInt((min.x/crowd.Area.x+.5f)*columns)), c1=Mathf.Min(columns-1,Mathf.FloorToInt((max.x/crowd.Area.x+.5f)*columns));
                    int r0=Mathf.Max(0,Mathf.FloorToInt((min.z/crowd.Area.y+.5f)*rows)), r1=Mathf.Min(rows-1,Mathf.FloorToInt((max.z/crowd.Area.y+.5f)*rows));
                    for(int r=r0;r<=r1;r++) for(int c=c0;c<=c1;c++) {
                        int i=r*columns+c; if(!walkable[i]) continue;
                        float ground=grid[i];
                        if(max.y<ground+.3f*h || min.y>ground+1.1f*h) continue;
                        var centre=crowd.GridCellCentre(c,r,columns,rows); centre.y=ground+.7f*h;
                        if(TriangleTouchesBox(centre,half,v0,v1,v2)) walkable[i]=false;
                    }
                }
            }
            bool changed=before.arraySize!=count || beforeGrid.arraySize!=grid.Length || beforeWalkable.arraySize!=walkable.Length;
            for(int i=0;i<count && !changed;i++) changed=Mathf.Abs(before.GetArrayElementAtIndex(i).floatValue-heights[i])>1e-4f;
            for(int i=0;i<grid.Length && !changed;i++) {
                float old=beforeGrid.GetArrayElementAtIndex(i).floatValue;
                changed=float.IsNaN(old)!=float.IsNaN(grid[i]) || !float.IsNaN(old) && Mathf.Abs(old-grid[i])>1e-4f ||
                    beforeWalkable.GetArrayElementAtIndex(i).boolValue!=walkable[i];
            }
            crowd.SetGroundHeights(heights);
            crowd.SetGroundGrid(columns,rows,grid,walkable);
            if(changed) EditorUtility.SetDirty(crowd);
            return snapped;
        }
        static Bounds LocalBounds(Bounds world,Matrix4x4 toLocal)
        {
            var result=new Bounds(toLocal.MultiplyPoint3x4(world.min),Vector3.zero);
            for(int i=0;i<8;i++)
                result.Encapsulate(toLocal.MultiplyPoint3x4(world.center+Vector3.Scale(world.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
            return result;
        }
        // Separating-axis test between a triangle and an axis-aligned box.
        static bool TriangleTouchesBox(Vector3 centre,Vector3 half,Vector3 a,Vector3 b,Vector3 c)
        {
            Vector3 v0=a-centre, v1=b-centre, v2=c-centre;
            for(int axis=0;axis<3;axis++)
                if(Mathf.Max(v0[axis],Mathf.Max(v1[axis],v2[axis]))<-half[axis] || Mathf.Min(v0[axis],Mathf.Min(v1[axis],v2[axis]))>half[axis]) return false;
            Vector3 e0=v1-v0, e1=v2-v1, e2=v0-v2;
            foreach(var edge in new[]{e0,e1,e2})
                foreach(var box in new[]{Vector3.right,Vector3.up,Vector3.forward})
                    if(!Overlap(Vector3.Cross(box,edge),v0,v1,v2,half)) return false;
            return Overlap(Vector3.Cross(e0,e1),v0,v1,v2,half);
        }
        static bool Overlap(Vector3 axis,Vector3 v0,Vector3 v1,Vector3 v2,Vector3 half)
        {
            if(axis.sqrMagnitude<1e-12f) return true;
            float p0=Vector3.Dot(axis,v0), p1=Vector3.Dot(axis,v1), p2=Vector3.Dot(axis,v2);
            float r=half.x*Mathf.Abs(axis.x)+half.y*Mathf.Abs(axis.y)+half.z*Mathf.Abs(axis.z);
            return !(Mathf.Min(p0,Mathf.Min(p1,p2))>r || Mathf.Max(p0,Mathf.Max(p1,p2))<-r);
        }
        static bool IsGround(Transform item,Transform scope)
        {
            for(var t=item;t && t!=scope;t=t.parent) {
                if(t.GetComponent<SandboxFinishCrowd>()) return false;
                foreach(string word in NotGround) if(t.name.IndexOf(word,System.StringComparison.OrdinalIgnoreCase)>=0) return false;
            }
            return true;
        }
        // Ray against one triangle; only faces a person could stand on (facing up) count.
        static bool Hit(Vector3 origin,Vector3 direction,Vector3 a,Vector3 b,Vector3 c,out float distance)
        {
            distance=0;
            var ab=b-a; var ac=c-a; var normal=Vector3.Cross(ab,ac);
            if(Mathf.Abs(Vector3.Dot(normal.normalized,direction))<.6f) return false;
            var p=Vector3.Cross(direction,ac); float det=Vector3.Dot(ab,p);
            if(Mathf.Abs(det)<1e-12f) return false;
            float inverse=1/det; var s=origin-a; float u=Vector3.Dot(s,p)*inverse;
            if(u<0 || u>1) return false;
            var q=Vector3.Cross(s,ab); float v=Vector3.Dot(direction,q)*inverse;
            if(v<0 || u+v>1) return false;
            distance=Vector3.Dot(ac,q)*inverse;
            return distance>0;
        }
    }
}
