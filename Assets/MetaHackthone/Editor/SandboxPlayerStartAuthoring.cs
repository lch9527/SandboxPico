using System.Linq;
// Gizmos and placement checks here are editor-only; nothing in this file runs in builds.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAssembly.Editor
{
    // Player start marker: a child of each level's Building Area, so moving or turning the
    // Building Area moves where the player stands. The camera mesh is EditorOnly (stripped from builds).
    public static class SandboxPlayerStartAuthoring
    {
        const string CameraMesh="Assets/ShadowWorkshop/Art/Torch2/Meshes/Travel camera.asset";
        const string MarkerName="Player view (editor only)";
        public const float CameraSize=.35f;

        [MenuItem("MetaHackthone/Design Mode/Add Player Start Markers to Open Scenes")]
        public static void AddToOpenScenes()
        {
            int added=0;
            for(int i=0;i<SceneManager.sceneCount;i++) {
                var scene=SceneManager.GetSceneAt(i); if(!scene.isLoaded) continue;
                foreach(var layout in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<SandboxLevelLayout>(true)))
                    if(Ensure(layout)) { added++; EditorSceneManager.MarkSceneDirty(scene); }
            }
            Debug.Log($"Player start markers added: {added}. Save the scene, then Save Level to Game to bake the prefab.");
        }
        [MenuItem("MetaHackthone/Design Mode/Select Player Start")]
        public static void Select()
        {
            var layout=Selection.activeTransform?Selection.activeTransform.GetComponentInParent<SandboxLevelLayout>(true):null;
            if(!layout) layout=Object.FindObjectsByType<SandboxLevelLayout>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).FirstOrDefault();
            if(!layout) { Debug.LogWarning("No sandbox level layout in the open scene."); return; }
            Ensure(layout); DesignModeEditor.Frame(layout.PlayerStart);
        }
        // Returns true when something was created. Existing markers are left where the designer put them.
        public static bool Ensure(SandboxLevelLayout layout)
        {
            if(!layout || !layout.BuildingRoot) return false;
            bool changed=false;
            if(!layout.PlayerStart) {
                var existing=layout.BuildingRoot.Find(SandboxLevelLayout.PlayerStartName);
                if(!existing) {
                    existing=new GameObject(SandboxLevelLayout.PlayerStartName).transform;
                    Undo.RegisterCreatedObjectUndo(existing.gameObject,"Add player start");
                    existing.SetParent(layout.BuildingRoot,false);
                    // Matches the runtime default: eyes of a standing player in front of the Building Area, facing it.
                    existing.localPosition=DefaultLocalPosition(layout);
                    existing.localRotation=Quaternion.identity;
                }
                Undo.RecordObject(layout,"Assign player start"); layout.PlayerStart=existing;
                EditorUtility.SetDirty(layout); changed=true;
            }
            if(!layout.PlayerStart.GetComponent<SandboxPlayerStart>()) {
                Undo.AddComponent<SandboxPlayerStart>(layout.PlayerStart.gameObject); changed=true;
            }
            var existingMarker=layout.PlayerStart.Find(MarkerName);
            if(existingMarker && existingMarker.localPosition.sqrMagnitude>1e-8f) {
                // Older markers put the Player Start at the feet and the camera 1.6 m above it. The Player
                // Start is now the camera itself: move it up to the camera so nothing moves in the world.
                Undo.RecordObject(layout.PlayerStart,"Move player start to camera"); Undo.RecordObject(existingMarker,"Move player start to camera");
                var camera=existingMarker.position;
                layout.PlayerStart.position=camera; existingMarker.position=camera; existingMarker.localPosition=Vector3.zero;
                EditorUtility.SetDirty(layout.PlayerStart); EditorUtility.SetDirty(existingMarker); changed=true;
            }
            if(!existingMarker) {
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(CameraMesh);
                var marker=new GameObject(MarkerName){tag="EditorOnly"};
                Undo.RegisterCreatedObjectUndo(marker,"Add player view marker");
                marker.transform.SetParent(layout.PlayerStart,false);
                float scale=Mathf.Max(.0001f,layout.PlayerStart.lossyScale.x);
                marker.transform.localPosition=Vector3.zero;
                if(mesh) {
                    // The travel camera's lens faces -Z in its source mesh; turn it to look along the start's forward.
                    float size=Mathf.Max(mesh.bounds.size.x,mesh.bounds.size.y,mesh.bounds.size.z);
                    marker.transform.localRotation=Quaternion.Euler(0,180,0);
                    marker.transform.localScale=Vector3.one*CameraSize/Mathf.Max(.0001f,size)/scale;
                    marker.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=marker.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial=MarkerMaterial();
                    renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                changed=true;
            }
            else if(existingMarker.TryGetComponent<MeshRenderer>(out var old) && NeedsMarkerMaterial(old.sharedMaterial)) {
                // Markers made with Unity's built-in Default-Material draw solid magenta under URP.
                Undo.RecordObject(old,"Fix player view marker material"); old.sharedMaterial=MarkerMaterial();
                EditorUtility.SetDirty(old); changed=true;
            }
            return changed;
        }
        const string MarkerMaterialPath="Assets/MetaHackthone/Art/SceneLayouts/PlayerViewMarker.mat";
        static bool NeedsMarkerMaterial(Material material)
            => !material || !material.shader || !material.shader.name.StartsWith("Universal Render Pipeline/");
        // A URP material for the editor-only camera preview: a calm slate blue that reads in the Scene view.
        static Material MarkerMaterial()
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(MarkerMaterialPath);
            if(material) return material;
            material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="PlayerViewMarker"};
            material.SetColor("_BaseColor",new Color(.36f,.52f,.62f)); material.SetFloat("_Smoothness",.3f);
            AssetDatabase.CreateAsset(material,MarkerMaterialPath);
            return material;
        }
        // Debug reach of a human arm from the headset camera, in real-world metres.
        public const float ArmReach=.8f;
        public struct Placement
        {
            public bool HasBoundary, HasPlatform;
            public bool CameraOutsideBoundary, PlatformInsideBoundary, PlatformInReach, BoundaryInReach;
            public float FarthestPlatform, NearestBoundary, FarthestBoundary;
            public bool Good => HasBoundary && CameraOutsideBoundary && (!HasPlatform || PlatformInsideBoundary && PlatformInReach) && NearestBoundary<ArmReach;
        }
        static readonly Vector3[] Unit=new Vector3[8];
        static Vector3[] Corners(Bounds b)
        {
            for(int i=0;i<8;i++) Unit[i]=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            return Unit;
        }
        public static Renderer Platform(SandboxLevelLayout layout) =>
            layout.Course && layout.Course.WorkbenchFrame ? layout.Course.WorkbenchFrame.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r=>r.name=="Centered grid pad") : null;
        // The goal: the camera stands outside the Sandbox building boundary, while the building platform
        // sits inside the boundary and within arm's reach, and the arm can reach into the boundary.
        public static Placement Check(SandboxLevelLayout layout)
        {
            var result=new Placement();
            if(!layout || !layout.BuildingRoot) return result;
            var eye=layout.PlayerStartPose(SandboxLevelLayout.DefaultPlayerDistance).position;
            var area=layout.Course?layout.Course.BuildArea:null;
            result.HasBoundary=area;
            if(area) {
                result.CameraOutsideBoundary=!area.Contains(eye);
                // Distances in world metres to the boundary box (nearest surface point and farthest corner).
                var local=area.transform.InverseTransformPoint(eye);
                var nearest=area.transform.TransformPoint(area.LocalBounds.ClosestPoint(local));
                result.NearestBoundary=result.CameraOutsideBoundary?Vector3.Distance(eye,nearest):0;
                result.FarthestBoundary=Corners(area.LocalBounds).Max(c=>Vector3.Distance(eye,area.transform.TransformPoint(c)));
                result.BoundaryInReach=result.FarthestBoundary<=ArmReach;
            }
            var platform=Platform(layout);
            if(platform && platform.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh) {
                result.HasPlatform=true;
                var corners=Corners(filter.sharedMesh.bounds).Select(c=>platform.transform.TransformPoint(c)).ToArray();
                result.FarthestPlatform=corners.Max(c=>Vector3.Distance(eye,c));
                result.PlatformInReach=result.FarthestPlatform<=ArmReach;
                result.PlatformInsideBoundary=area && corners.All(area.Contains);
            }
            return result;
        }
        public static Vector3 DefaultLocalPosition(SandboxLevelLayout layout)
        {
            float scale=Mathf.Max(.0001f,layout.BuildingRoot.lossyScale.x);
            return (Vector3.back*SandboxLevelLayout.DefaultPlayerDistance+Vector3.up*SandboxLevelLayout.DefaultEyeHeight)/scale;
        }
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        static void DrawPlayerStart(SandboxLevelLayout layout,GizmoType type)
        {
            if(!layout || !layout.BuildingRoot) return;
            // The Player Start is the headset camera: draw the view from that exact point.
            var start=layout.PlayerStartPose(SandboxLevelLayout.DefaultPlayerDistance);
            var previous=Gizmos.matrix; var color=Gizmos.color;
            // Arm reach (editor only): green when the placement goal is met, orange when it is not.
            var reach=Check(layout).Good?new Color(.25f,1,.45f):new Color(1,.45f,.15f);
            Gizmos.color=new Color(reach.r,reach.g,reach.b,.06f); Gizmos.DrawSphere(start.position,ArmReach);
            Gizmos.color=new Color(reach.r,reach.g,reach.b,.8f); Gizmos.DrawWireSphere(start.position,ArmReach);
            Gizmos.color=new Color(1,.85f,.2f);
            Gizmos.DrawWireSphere(start.position,.12f);
            Gizmos.matrix=Matrix4x4.TRS(start.position,start.rotation,Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero,70,1.2f,.05f,1.3f);
            Gizmos.DrawLine(Vector3.zero,Vector3.forward*.6f);
            Gizmos.DrawLine(Vector3.forward*.6f,new Vector3(.12f,0,.45f));
            Gizmos.DrawLine(Vector3.forward*.6f,new Vector3(-.12f,0,.45f));
            Gizmos.matrix=previous; Gizmos.color=color;
        }
    }
}
