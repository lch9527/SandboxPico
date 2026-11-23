using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    // An isolated, disposable physics scene: never simulates or saves authoring objects.
    [InitializeOnLoad]
    public static class SandboxTrackCarPreview
    {
        static Scene scene;
        static SandboxTrackGenerator track;
        static Rigidbody chassis;
        static readonly List<Rigidbody> wheels = new();
        static PhysicsMaterial tireMaterial;
        static Vector3 bodySize;
        static float radius, elapsed, accumulator;
        static double lastUpdate;
        static string signature;
        const float Step = 1f / 120f;

        static SandboxTrackCarPreview()
        {
            EditorApplication.update += Update;
            SceneView.duringSceneGui += Draw;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.playModeStateChanged += _ => Stop();
            PrefabStage.prefabStageClosing += _ => Stop();
            EditorSceneManager.sceneClosing += (s, removing) => { if(track && track.gameObject.scene==s) Stop(); };
            Undo.undoRedoPerformed += Stop;
        }

        public static bool IsTesting(SandboxTrackGenerator target) => track == target && chassis;

        public static void Start(SandboxTrackGenerator target, float startFraction, float speed)
        {
            Stop();
            if(!target || Application.isPlaying) return;
            try
            {
                if(!target.Generated || !string.IsNullOrEmpty(target.ValidationError) ||
                    target.BakedSignature!=SandboxTrackBaker.Signature(target))
                    throw new InvalidOperationException("Rebuild Road and Supports first, then click Test Car. Testing does not rebuild or replace your track.");
                var path=SandboxTrackBaker.SamplePath(target);
                float length=path[path.Count-1].Distance;
                float width=path[0].Width;
                float desired=Mathf.Clamp(length*Mathf.Clamp01(startFraction),width*.55f,Mathf.Max(width*.55f,length-width*.55f));
                if(length<width*1.2f) throw new InvalidOperationException("Track is too short for the preview car.");
                int index=path.FindIndex(s=>s.Distance>=desired);
                var sample=path[Mathf.Max(0,index)];
                track=target;
                signature=SandboxTrackBaker.Signature(track);
                scene=EditorSceneManager.NewPreviewScene();
                if(scene.GetPhysicsScene().Equals(Physics.defaultPhysicsScene))
                    throw new InvalidOperationException("Car preview requires an isolated physics scene.");
                foreach(var source in track.Generated.GetComponentsInChildren<MeshCollider>())
                {
                    if(!source.enabled || !source.gameObject.activeInHierarchy || !source.sharedMesh) continue;
                    var go=Create("Preview road collider");
                    go.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
                    go.transform.localScale=source.transform.lossyScale;
                    var collider=go.AddComponent<MeshCollider>();
                    // Own the mesh copy so a subsequent road rebuild cannot invalidate this simulation.
                    collider.sharedMesh=Object.Instantiate(source.sharedMesh);
                    collider.sharedMesh.hideFlags=HideFlags.HideAndDontSave;
                    collider.sharedMaterial=source.sharedMaterial;
                    collider.contactOffset=.0001f;
                }
                float scale=track.transform.lossyScale.x;
                float carWidth=Mathf.Min(width,sample.Width)*.65f*scale;
                radius=carWidth*.16f;
                bodySize=new Vector3(carWidth*.7f,carWidth*.22f,carWidth*1.35f);
                var rotation=track.transform.rotation*Quaternion.LookRotation(sample.Forward,sample.Up);
                var surface=track.transform.TransformPoint(sample.Position);
                var position=surface+rotation*Vector3.up*(radius*1.25f+bodySize.y*.5f);
                chassis=Body("Preview car",position,rotation,3);
                var box=chassis.gameObject.AddComponent<BoxCollider>(); box.size=bodySize; box.contactOffset=.0001f;
                chassis.centerOfMass=new Vector3(0,-bodySize.y*.3f,0);
                tireMaterial=new PhysicsMaterial("Preview tires") { hideFlags=HideFlags.HideAndDontSave, staticFriction=.8f,dynamicFriction=.7f,bounciness=0 };
                foreach(int side in new[]{-1,1}) foreach(int end in new[]{-1,1})
                {
                    var offset=new Vector3(side*(carWidth*.5f-radius*.5f),-bodySize.y*.5f,end*bodySize.z*.34f);
                    var wheel=Body("Preview wheel",position+rotation*offset,rotation,.25f);
                    var tire=wheel.gameObject.AddComponent<SphereCollider>(); tire.radius=radius; tire.sharedMaterial=tireMaterial; tire.contactOffset=.0001f;
                    var joint=wheel.gameObject.AddComponent<HingeJoint>();
                    joint.connectedBody=chassis; joint.axis=Vector3.right; joint.autoConfigureConnectedAnchor=false;
                    joint.anchor=Vector3.zero; joint.connectedAnchor=offset;
                    joint.enableCollision=false;
                    wheel.linearVelocity=rotation*Vector3.forward*(Mathf.Max(0,speed)*scale);
                    wheel.maxAngularVelocity=200;
                    wheel.angularVelocity=rotation*Vector3.right*(Mathf.Max(0,speed)*scale/radius);
                    wheels.Add(wheel);
                }
                chassis.linearVelocity=rotation*Vector3.forward*(Mathf.Max(0,speed)*scale);
                Physics.SyncTransforms();
                lastUpdate=EditorApplication.timeSinceStartup;
                var view=SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
                view.Frame(new Bounds(position,Vector3.one*carWidth*7),false);
                view.Focus();
                SceneView.RepaintAll();
            }
            catch(Exception e) { Stop(); Debug.LogError("Track car preview: "+e.Message,target); }
        }

        static GameObject Create(string name)
        {
            var go=new GameObject(name) { hideFlags=HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(go,scene);
            return go;
        }
        static Rigidbody Body(string name,Vector3 position,Quaternion rotation,float mass)
        {
            var go=Create(name); go.transform.SetPositionAndRotation(position,rotation);
            var body=go.AddComponent<Rigidbody>(); body.mass=mass;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations=12; body.solverVelocityIterations=12;
            body.linearDamping=0; body.angularDamping=0;
            return body;
        }
        static void Update()
        {
            if(!chassis) return;
            if(!track || Application.isPlaying || !track.gameObject.scene.isLoaded ||
                signature!=SandboxTrackBaker.Signature(track)) { Stop(); return; }
            double now=EditorApplication.timeSinceStartup;
            accumulator+=Mathf.Min((float)(now-lastUpdate),.05f); lastUpdate=now;
            while(accumulator>=Step) { scene.GetPhysicsScene().Simulate(Step); accumulator-=Step; elapsed+=Step; }
            if(elapsed>60) { Stop(); return; }
            SceneView.RepaintAll();
        }
        static void Draw(SceneView view)
        {
            if(!chassis || Event.current.type!=EventType.Repaint) return;
            using(new Handles.DrawingScope(new Color(1,.55f,.08f),Matrix4x4.TRS(chassis.position,chassis.rotation,bodySize)))
                Handles.CubeHandleCap(0,Vector3.zero,Quaternion.identity,1,EventType.Repaint);
            using(new Handles.DrawingScope(new Color(.08f,.09f,.11f)))
                foreach(var wheel in wheels) if(wheel) Handles.SphereHandleCap(0,wheel.position,wheel.rotation,radius*2,EventType.Repaint);
            Handles.Label(chassis.position+Vector3.up*bodySize.z,"Track test car — gravity preview");
        }
        public static void Stop()
        {
            track=null; chassis=null; wheels.Clear(); elapsed=accumulator=0;
            if(scene.IsValid())
            {
                foreach(var root in scene.GetRootGameObjects()) foreach(var collider in root.GetComponentsInChildren<MeshCollider>())
                    if(collider.sharedMesh) Object.DestroyImmediate(collider.sharedMesh);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            scene=default;
            if(tireMaterial) Object.DestroyImmediate(tireMaterial);
            tireMaterial=null;
            SceneView.RepaintAll();
        }
    }
}
