using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAssembly
{
    // Blender-authored looks for the designer car kit and the road speed bump
    // (Resources/CarPartArt, built by MetaHackthone > Car Parts Art > Import Blender kit).
    // Only renderers change: colliders, rigid bodies, joints and sockets stay where the
    // part factory put them. Without the asset, or with Enabled off, the procedural look returns.
    public sealed class CarPartArtLibrary : ScriptableObject
    {
        [Serializable] public sealed class Visual
        {
            public PartKind Kind;
            public string Body;                 // AssemblyBody.Id that carries this visual
            public string Parent;               // optional child of the body, e.g. the drill's spinning bit
            public Mesh[] Lods;                 // metres at GeometryScale 1, highest detail first
            public Material[] Materials;        // identical slots on every LOD
        }

        public bool Enabled=true;
        public Visual[] Visuals=Array.Empty<Visual>();
        // Angle one building unit subtends at the eye. Workbench parts stay above Lod0Degrees;
        // a car on the track drops to LOD1 a few metres out and LOD2 on far islands.
        [Min(.01f)] public float Lod0Degrees=1.6f;
        [Min(.01f)] public float Lod1Degrees=.5f;

        [Header("Speed bump (replaces the roller's gravel)")]
        public Mesh[] BumpMid=Array.Empty<Mesh>();
        public Mesh[] BumpEnd=Array.Empty<Mesh>();
        public Material BumpPaint, BumpBase;
        // Angle the bump's height subtends at the eye.
        [Min(.01f)] public float BumpLod0Degrees=2f;
        [Min(.01f)] public float BumpLod1Degrees=.7f;

        [Header("Build turntable handwheel")]
        public Mesh[] Handwheel=Array.Empty<Mesh>();
        public Material[] HandwheelMaterials=Array.Empty<Material>();

        public const string VisualName="Art visual";
        public const string HandwheelName="Handwheel";
        // Quest 3 vertical field of view; converts the angular rules above into LODGroup heights.
        public const float ReferenceFieldOfView=96f;
        const float MidLength=.8f, EndLength=.3f;   // module lengths for a bump of diameter 1

        static CarPartArtLibrary cached;
        static HashSet<UnityEngine.Object> shared;
        public static CarPartArtLibrary Load()
        {
            if(!cached) { cached=Resources.Load<CarPartArtLibrary>("CarPartArt"); shared=null; }
            return cached;
        }
        void OnValidate() { shared=null; }

        // Library materials are assets shared by every part; per-part cleanup must leave them alone.
        public static bool IsShared(UnityEngine.Object item)
        {
            var library=Load(); if(!library || !item) return false;
            if(shared==null) {
                shared=new HashSet<UnityEngine.Object>();
                foreach(var visual in library.Visuals) if(visual.Materials!=null) foreach(var material in visual.Materials) if(material) shared.Add(material);
                if(library.BumpPaint) shared.Add(library.BumpPaint);
                if(library.BumpBase) shared.Add(library.BumpBase);
            }
            return shared.Contains(item);
        }

        public static float ScreenHeight(float degrees,float groupSize,float unitSize)
            => Mathf.Clamp(degrees*Mathf.Deg2Rad*groupSize/Mathf.Max(1e-6f,unitSize)/(2*Mathf.Tan(ReferenceFieldOfView*.5f*Mathf.Deg2Rad)),.0002f,.9f);

        // Call after the part's bodies, colliders and sockets exist and before AssemblyPart.Initialize,
        // so the art renderers are the ones every effect sees as the part's visuals.
        public static bool Apply(AssemblyPart part,float scale)
        {
            var library=Load(); if(!library || !library.Enabled || !part) return false;
            var kind=part.BuildingCore?PartKind.DownhillCore:part.Kind;
            var bodies=part.GetComponentsInChildren<AssemblyBody>(true);
            var resolved=new List<(Visual visual,Transform parent)>(); var strip=new List<AssemblyBody>();
            foreach(var visual in library.Visuals) {
                if(visual.Kind!=kind || visual.Lods==null || visual.Lods.Length==0 || !visual.Lods[0] || visual.Materials==null) continue;
                var body=Array.Find(bodies,b=>b.Id==visual.Body); if(!body) continue;
                var parent=string.IsNullOrEmpty(visual.Parent)?body.transform:body.transform.Find(visual.Parent); if(!parent) continue;
                resolved.Add((visual,parent)); if(!strip.Contains(body)) strip.Add(body);
            }
            if(resolved.Count==0) return false;
            foreach(var body in strip) StripProceduralLook(body);
            foreach(var (visual,parent) in resolved) {
                var go=new GameObject(VisualName); go.transform.SetParent(parent,false); go.transform.localScale=Vector3.one*scale;
                go.AddComponent<MeshFilter>().sharedMesh=visual.Lods[0];
                go.AddComponent<MeshRenderer>().sharedMaterials=visual.Materials;
                if(visual.Lods.Length>1) {
                    var lod=go.AddComponent<CarPartLod>(); lod.Meshes=visual.Lods; lod.UnitSize=BuildingGeometry.Unit;
                    lod.Lod0Degrees=library.Lod0Degrees; lod.Lod1Degrees=library.Lod1Degrees;
                }
            }
            return true;
        }

        // The factory's renderers go; a MeshCollider keeps using the mesh it was given.
        static void StripProceduralLook(AssemblyBody body)
        {
            foreach(var renderer in body.GetComponentsInChildren<MeshRenderer>(true)) {
                if(renderer.GetComponentInParent<AssemblySocket>(true)) continue;
                var filter=renderer.GetComponent<MeshFilter>(); var mesh=filter?filter.sharedMesh:null; var material=renderer.sharedMaterial;
                var collider=renderer.GetComponent<MeshCollider>();
                UnityEngine.Object.DestroyImmediate(renderer); if(filter) UnityEngine.Object.DestroyImmediate(filter);
                if(material && !IsAsset(material)) UnityEngine.Object.DestroyImmediate(material);
                // Only meshes the factory built for looks alone; primitives and collider meshes stay.
                if(mesh && mesh.name=="Sandbox drill mesh" && !(collider && collider.sharedMesh==mesh) && !IsAsset(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        static bool IsAsset(UnityEngine.Object item)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.IsPersistent(item);
#else
            return false;
#endif
        }

        // The turntable's grab ring: a solid handwheel on the circle the hand closes on. Visual only.
        public static Transform AddHandwheel(Transform wheel)
        {
            var library=Load();
            if(!library || !library.Enabled || library.Handwheel==null || library.Handwheel.Length==0 || !library.Handwheel[0] || library.HandwheelMaterials==null || library.HandwheelMaterials.Length==0) return null;
            var existing=wheel.Find(HandwheelName); if(existing) return existing;
            var go=new GameObject(HandwheelName); go.transform.SetParent(wheel,false);
            go.AddComponent<MeshFilter>().sharedMesh=library.Handwheel[0];
            var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterials=library.HandwheelMaterials;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            if(library.Handwheel.Length>1) {
                var lod=go.AddComponent<CarPartLod>(); lod.Meshes=library.Handwheel; lod.UnitSize=BuildingGeometry.Unit;
                lod.Lod0Degrees=library.Lod0Degrees; lod.Lod1Degrees=library.Lod1Degrees;
            }
            return go.transform;
        }

        // A striped rubber speed bump over a roller of `diameter` lying along local X and resting at `height`.
        // Visual only: the roller's own cylinder keeps deciding how the car is slowed.
        public static Transform AddSpeedBump(Transform root,string label,float width,float diameter,float height)
        {
            var library=Load();
            if(!library || !library.Enabled || library.BumpMid==null || library.BumpEnd==null || library.BumpMid.Length==0 || library.BumpEnd.Length==0
                || !library.BumpMid[0] || !library.BumpEnd[0] || !library.BumpPaint || !library.BumpBase) return null;
            var bump=new GameObject(label).transform; bump.SetParent(root,false); bump.localPosition=new Vector3(0,height,0);
            float end=Mathf.Min(EndLength*diameter,width*.25f), inner=width-end*2, modules=inner/(MidLength*diameter);
            // An odd count keeps the stripes symmetric: yellow ends, dark next to them.
            int count=Mathf.Max(1,2*Mathf.FloorToInt(modules*.5f)+1);
            float step=inner/count;
            int levels=Mathf.Min(library.BumpMid.Length,library.BumpEnd.Length);
            var lods=new LOD[levels];
            var striped=new[]{library.BumpPaint,library.BumpBase}; var dark=new[]{library.BumpBase,library.BumpBase};
            for(int level=0;level<levels;level++) {
                var renderers=new List<Renderer>();
                for(int i=0;i<count;i++)
                    renderers.Add(Module(bump,$"Mid {i} LOD{level}",library.BumpMid[level],i%2==0?dark:striped,new Vector3(-inner*.5f+(i+.5f)*step,0,0),Quaternion.identity,
                        new Vector3(step/MidLength,diameter,diameter)));
                for(int side=-1;side<=1;side+=2)
                    renderers.Add(Module(bump,$"End {(side<0?"left":"right")} LOD{level}",library.BumpEnd[level],striped,new Vector3(side*inner*.5f,0,0),
                        side<0?Quaternion.Euler(0,180,0):Quaternion.identity,new Vector3(end/EndLength,diameter,diameter)));
                lods[level]=new LOD(1f/(level+2),renderers.ToArray());   // provisional; real heights follow once the bounds are known
            }
            var group=bump.gameObject.AddComponent<LODGroup>(); group.fadeMode=LODFadeMode.None; group.animateCrossFading=false;
            group.SetLODs(lods); group.RecalculateBounds();
            float first=ScreenHeight(library.BumpLod0Degrees,group.size,diameter), second=ScreenHeight(library.BumpLod1Degrees,group.size,diameter);
            for(int level=0;level<levels;level++) lods[level].screenRelativeTransitionHeight=level==levels-1?.0002f:level==0?first:Mathf.Min(second,first*.6f);
            group.SetLODs(lods);
            return bump;
        }
        static Renderer Module(Transform parent,string label,Mesh mesh,Material[] materials,Vector3 position,Quaternion rotation,Vector3 scale)
        {
            var go=new GameObject(label); go.transform.SetParent(parent,false);
            go.transform.SetLocalPositionAndRotation(position,rotation); go.transform.localScale=scale;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterials=materials;
            return renderer;
        }
    }

    // Distance LOD for one part visual. It swaps the mesh on a single renderer rather than using a
    // LODGroup: outlines, ghosts, dissolves and pinch tests all walk AssemblyPart.VisualRenderers and
    // must keep meeting exactly one renderer and one mesh per visual.
    [DisallowMultipleComponent]
    public sealed class CarPartLod : MonoBehaviour
    {
        public Mesh[] Meshes;
        public float UnitSize=1;            // local length of one building unit before this transform's scale
        public float Lod0Degrees=1.6f, Lod1Degrees=.5f;
        public int Level { get; private set; }
        const float Band=.12f;              // hysteresis, so a part resting on a threshold does not flicker
        MeshFilter filter; Renderer view; int phase;
        static int spawned; static Camera eye;

        void Awake() { phase=spawned++&3; }
        void LateUpdate()
        {
            // Four interleaved groups: each visual is re-evaluated every fourth frame.
            if((Time.frameCount&3)!=phase || Meshes==null || Meshes.Length<2) return;
            if(!view) view=GetComponent<Renderer>();
            if(!view || !view.enabled) return;
            if(!eye || !eye.isActiveAndEnabled) eye=Camera.main;
            if(eye) Show(Evaluate(eye.transform.position));
        }
        public float Degrees(Vector3 eyePosition)
            => UnitSize*Mathf.Abs(transform.lossyScale.x)/Mathf.Max(.01f,Vector3.Distance(eyePosition,transform.position))*Mathf.Rad2Deg;
        public int Evaluate(Vector3 eyePosition)
        {
            float degrees=Degrees(eyePosition); int level=Level;
            if(level==0) { if(degrees<Lod0Degrees*(1-Band)) level=degrees<Lod1Degrees*(1-Band)?2:1; }
            else if(level==1) { if(degrees>Lod0Degrees*(1+Band)) level=0; else if(degrees<Lod1Degrees*(1-Band)) level=2; }
            else if(degrees>Lod1Degrees*(1+Band)) level=degrees>Lod0Degrees*(1+Band)?0:1;
            return Mathf.Clamp(level,0,Meshes.Length-1);
        }
        public void Show(int level)
        {
            level=Mathf.Clamp(level,0,Meshes.Length-1);
            if(!filter) filter=GetComponent<MeshFilter>();
            if(!filter || !Meshes[level]) return;
            Level=level; if(filter.sharedMesh!=Meshes[level]) filter.sharedMesh=Meshes[level];
        }
    }
}
