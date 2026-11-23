using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class SandboxTrackBaker
    {
        public struct Sample { public Vector3 Position, Forward, Right, Up; public float Width, Distance; }
        public static bool IsGround(Collider c) => c && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger &&
            !c.GetComponentInParent<SandboxTrackGenerator>() && !c.GetComponentInParent<AssemblyPart>() &&
            (c is TerrainCollider || c.GetComponentInParent<SandboxTrackGround>());

        public static string Signature(SandboxTrackGenerator t)
        {
            var b = new StringBuilder(JsonUtility.ToJson(t.Curve));
            b.Append("|CreekDeck-v2|").Append(t.DeckStyle).Append("|ShowVisuals:").Append(t.ShowGeneratedVisuals);
            if(t.DeckModule) b.Append(AssetDatabase.GetAssetPath(t.DeckModule)).Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(t.DeckModule)));
            b.Append("|FactoryPuzzleBake-v3-smooth-surfaces|");
            b.Append(FormattableString.Invariant($"|{t.DefaultWidth}|{t.DeckThickness}|{t.Guardrails}|{t.RailHeight}|{t.RailThickness}|{t.Supports}|{t.SupportSpacing}|{t.SupportThickness}|{t.SupportInset}|{t.MaximumReach}|{t.GroundMask.value}|{t.SampleSpacing}"));
            foreach(int side in new[]{-1,1}) b.Append(FormattableString.Invariant($"|SeparateRails-v1|{side}|{t.RailEnabled(side)}|{t.RailHeightForSide(side)}|{t.RailThicknessForSide(side)}"));
            b.Append(FormattableString.Invariant($"|RailSections-v1|{t.RailSectionLength}"));
            foreach(var section in t.LeftRailSections) b.Append("|L").Append(JsonUtility.ToJson(section));
            foreach(var section in t.RightRailSections) b.Append("|R").Append(JsonUtility.ToJson(section));
            b.Append(t.transform.localToWorldMatrix.ToString("R"));
            foreach(var puzzle in SandboxPuzzleEditing.Puzzles(t).OrderBy(p=>p.GetInstanceID())) b.Append(puzzle.Signature);
            foreach (var o in new Object[] { t.RoadMaterial, t.TimberMaterial, t.RoadPhysics, t.RailPhysics }) b.Append(o ? o.GetInstanceID() : 0).Append('|');
            if (t.Supports) foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.InstanceID).Where(c => c.gameObject.scene == t.gameObject.scene && IsGround(c))) {
                b.Append(c.GetInstanceID()).Append(c.gameObject.layer).Append(JsonUtility.ToJson(c)).Append(c.transform.localToWorldMatrix.ToString("R"));
                if (c is TerrainCollider terrain && terrain.terrainData) b.Append(EditorUtility.GetDirtyCount(terrain.terrainData));
                if (c is MeshCollider mesh && mesh.sharedMesh) b.Append(EditorUtility.GetDirtyCount(mesh.sharedMesh));
            }
            return Hash128.Compute(b.ToString()).ToString();
        }

        public static bool RebuildFromInspector(SandboxTrackGenerator track)
        {
            if(track.AuthoredObjects) return Rebuild(track);
            if(track.AutoSmoothOnRebuild) {
                Undo.RecordObject(track.Curve,"Smooth track tangents");
                SmoothTangents(track);
                EditorUtility.SetDirty(track.Curve);
                PrefabUtility.RecordPrefabInstancePropertyModifications(track.Curve);
                EditorSceneManager.MarkSceneDirty(track.gameObject.scene);
            }
            return Rebuild(track,true);
        }

        public static void SmoothTangents(SandboxTrackGenerator track)
        {
            var spline=track.Curve.Spline;
            if(track.Curve.Splines.Count!=1 || spline.Closed || spline.Count<2) return;
            // Angle-bisector handles provide a shared tangent direction at each knot.
            // One-third chord handles also avoid Unity AutoSmooth's very short end handles.
            // Recompute from positions only: repeated rebuilds never shrink/move the path.
            for(int i=0;i<spline.Count;i++) {
                Vector3 point=spline[i].Position;
                Vector3 incoming=point-(Vector3)spline[Mathf.Max(0,i-1)].Position;
                Vector3 outgoing=(Vector3)spline[Mathf.Min(spline.Count-1,i+1)].Position-point;
                Vector3 direction=(incoming.normalized+outgoing.normalized).normalized;
                if(direction.sqrMagnitude<.000001f || Vector3.Cross(Vector3.up,direction).sqrMagnitude<.000001f) continue;
                float a=incoming.magnitude,b=outgoing.magnitude;
                if(i==0) a=b;
                if(i==spline.Count-1) b=a;
                spline.SetTangentMode(i,TangentMode.Continuous);
                var knot=spline[i];
                knot.Rotation=Quaternion.LookRotation(direction,Vector3.up);
                knot.TangentIn=new Unity.Mathematics.float3(0,0,-a/3);
                knot.TangentOut=new Unity.Mathematics.float3(0,0,b/3);
                spline[i]=knot;
            }
        }

        public static List<Sample> SamplePath(SandboxTrackGenerator track)
        {
            var spline = track.Curve.Spline;
            if (track.Curve.Splines.Count != 1 || spline.Closed || spline.Count < 2 || spline.Count > 512)
                throw new InvalidOperationException("Use one open spline with 2–512 control points.");
            foreach (var v in new[] { track.DefaultWidth, track.DeckThickness, track.SampleSpacing, track.SupportSpacing, track.SupportThickness, track.MaximumReach })
                if (!float.IsFinite(v) || v <= 0) throw new InvalidOperationException("Track dimensions, sampling, and support spacing must be positive finite values.");
            foreach(int side in new[]{-1,1}) if(track.RailEnabled(side))
                foreach(float value in new[]{track.RailHeightForSide(side),track.RailThicknessForSide(side)})
                    if(!float.IsFinite(value) || value <= 0) throw new InvalidOperationException($"{(side < 0 ? "Left" : "Right")} rail height and thickness must be positive finite values.");
            if(!float.IsFinite(track.RailSectionLength) || track.RailSectionLength < 0 || (track.RailSectionLength > 0 && track.RailSectionLength < .01f))
                throw new InvalidOperationException("Rail section length must be zero (continuous) or at least 1 cm.");
            if(track.RailSectionLength>0) foreach(int side in new[]{-1,1}) {
                var sections=side<0?track.LeftRailSections:track.RightRailSections;
                if(sections.Any(s=>s.Index<0) || sections.Select(s=>s.Index).Distinct().Count()!=sections.Count)
                    throw new InvalidOperationException("Rail section numbers must be unique and nonnegative.");
                foreach(var section in sections) if(track.RailSectionEnabled(side,section.Index))
                    foreach(float value in new[]{track.RailSectionHeight(side,section.Index),track.RailSectionThickness(side,section.Index)})
                        if(!float.IsFinite(value) || value<=0) throw new InvalidOperationException($"{(side<0?"Left":"Right")} rail section {section.Index+1}: height and thickness must be positive finite values.");
            }
            if (!float.IsFinite(track.SupportInset) || track.SupportInset < 0) throw new InvalidOperationException("Support inset must be nonnegative.");
            var scale = track.transform.lossyScale;
            if (scale.x <= 0 || Mathf.Abs(scale.x-scale.y) > .0001f || Mathf.Abs(scale.x-scale.z) > .0001f || Vector3.Dot(track.transform.up, Vector3.up) < .999f)
                throw new InvalidOperationException("Keep the track upright and use a positive uniform scale. Change slope using spline points.");
            for (int i = 0; i < spline.Count; i++)
                if (!float.IsFinite(track.KnotWidth(i)) || track.KnotWidth(i) < .05f)
                    throw new InvalidOperationException($"Point {i + 1}: width must be at least 0.05 course units.");
            var result = new List<Sample>();
            float distance = 0;
            void Add(int segment, float parameter) {
                var curve = spline.GetCurve(segment);
                Vector3 p = CurveUtility.EvaluatePosition(curve, parameter);
                Vector3 tangent = CurveUtility.EvaluateTangent(curve, parameter);
                if (tangent.sqrMagnitude < 1e-10f) {
                    tangent = (Vector3)CurveUtility.EvaluatePosition(curve, Mathf.Min(1,parameter+.001f)) - (Vector3)CurveUtility.EvaluatePosition(curve, Mathf.Max(0,parameter-.001f));
                }
                if (!float.IsFinite(p.sqrMagnitude) || tangent.sqrMagnitude < 1e-10f || !float.IsFinite(tangent.sqrMagnitude)) throw new InvalidOperationException("A curve contains coincident points or a zero-length tangent.");
                tangent.Normalize();
                var right = Vector3.Cross(Vector3.up, tangent);
                if (right.sqrMagnitude < .0001f) throw new InvalidOperationException("Vertical track segments have no upright width direction.");
                if (result.Count > 0) {
                    float step = Vector3.Distance(result[result.Count-1].Position, p);
                    if (step < .00001f) throw new InvalidOperationException("The track contains a degenerate segment.");
                    distance += step;
                }
                right.Normalize();
                result.Add(new Sample { Position=p, Forward=tangent, Right=right, Up=Vector3.Cross(tangent,right).normalized, Width=track.WidthAt(segment+parameter), Distance=distance });
                if (result.Count > 32768) throw new InvalidOperationException("Track exceeds 32,768 samples. Increase sample spacing or split it into multiple tracks.");
            }
            for (int segment = 0; segment < spline.Count-1; segment++) {
                var curve = spline.GetCurve(segment);
                if (segment == 0) Add(segment,0);
                void Divide(float a, float b, int depth) {
                    Vector3 p = CurveUtility.EvaluatePosition(curve,a), q = CurveUtility.EvaluatePosition(curve,b);
                    Vector3 m = CurveUtility.EvaluatePosition(curve,(a+b)*.5f);
                    Vector3 q1 = CurveUtility.EvaluatePosition(curve,Mathf.Lerp(a,b,.25f)), q3 = CurveUtility.EvaluatePosition(curve,Mathf.Lerp(a,b,.75f));
                    bool split = Vector3.Distance(p,q) > track.SampleSpacing || Vector3.Distance(m,(p+q)*.5f) > .005f || Vector3.Distance(q1,Vector3.Lerp(p,q,.25f)) > .005f || Vector3.Distance(q3,Vector3.Lerp(p,q,.75f)) > .005f;
                    Vector3 ta=CurveUtility.EvaluateTangent(curve,a), tm=CurveUtility.EvaluateTangent(curve,(a+b)*.5f), tb=CurveUtility.EvaluateTangent(curve,b);
                    split |= Vector3.Angle(ta,tm)>1 || Vector3.Angle(tm,tb)>1 || Vector3.Angle(ta,tb)>2;
                    if (split && depth < 16) { Divide(a,(a+b)*.5f,depth+1); Divide((a+b)*.5f,b,depth+1); }
                    else Add(segment,b);
                }
                Divide(0,1,0);
            }
            if (distance < .1f) throw new InvalidOperationException("Track must be at least 0.1 course units long.");
            return result;
        }

        sealed class MeshBuilder
        {
            readonly List<Vector3> vertices = new(); readonly List<Vector2> uv = new(); readonly List<int> triangles = new();
            readonly List<int> smoothingGroups = new();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0=0, float u1=1, float v0=0, float v1=1,int smoothingGroup=0) {
                int n=vertices.Count; vertices.AddRange(new[]{a,b,c,d}); uv.AddRange(new[]{new Vector2(u0,v0),new Vector2(u1,v0),new Vector2(u1,v1),new Vector2(u0,v1)});
                for(int i=0;i<4;i++) smoothingGroups.Add(smoothingGroup);
                triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
            public void Box(Vector3[] a, Vector3[] b, float d0=0, float d1=1) {
                for(int j=0;j<4;j++) { int k=(j+1)%4; Quad(a[k],a[j],b[j],b[k],0,Vector3.Distance(a[j],a[k]),d0,d1,j+1); }
            }
            public void Caps(Vector3[] a, Vector3[] b) { Quad(a[0],a[1],a[2],a[3]); Quad(b[3],b[2],b[1],b[0]); }
            public void Cap(Vector3[] ring,bool end) { if(end) Quad(ring[3],ring[2],ring[1],ring[0]); else Quad(ring[0],ring[1],ring[2],ring[3]); }
            public Mesh Mesh(string name) {
                var m=new Mesh{name=name,indexFormat=IndexFormat.UInt32}; m.SetVertices(vertices); m.SetUVs(0,uv); m.SetTriangles(triangles,0); m.RecalculateNormals();
                var normals=m.normals;
                var sums=new Dictionary<(Vector3,int),Vector3>();
                // Join shading along each surface only. Side/top corners, end caps,
                // and puzzle cutouts retain their deliberate hard edges and geometry.
                for(int i=0;i<vertices.Count;i++) if(smoothingGroups[i]!=0) {
                    var key=(vertices[i],smoothingGroups[i]); sums.TryGetValue(key,out var sum); sums[key]=sum+normals[i];
                }
                for(int i=0;i<vertices.Count;i++) if(smoothingGroups[i]!=0) normals[i]=sums[(vertices[i],smoothingGroups[i])].normalized;
                m.normals=normals; m.RecalculateBounds(); m.RecalculateTangents(); return m;
            }
        }
        static Vector3[] Ring(Sample s, float left, float right, float bottom, float top) => new[] {
            s.Position+s.Right*left+s.Up*top, s.Position+s.Right*right+s.Up*top,
            s.Position+s.Right*right+s.Up*bottom, s.Position+s.Right*left+s.Up*bottom };

        public static bool Rebuild(SandboxTrackGenerator track, bool force=false)
        {
            // Authored children are the source of truth, including intentional deletions.
            // Even forced callers must not silently replace them.
            if(track.AuthoredObjects) return track.Generated && track.Samples.Length>=2;
            var meshes=new List<Mesh>(); GameObject root=null;
            try {
                SandboxPuzzleEditing.UpdateTrack(track);
                var signature=Signature(track);
                if (!force && track.Generated && signature==track.BakedSignature && string.IsNullOrEmpty(track.ValidationError)) return true;
                var path=SamplePath(track);
                var puzzles=SandboxPuzzleEditing.Puzzles(track);
                foreach(var p in puzzles) if(!string.IsNullOrEmpty(p.ValidationError)) throw new InvalidOperationException(p.name+": "+p.ValidationError);
                var cuts=puzzles.Where(p=>p.ReplacesRoad).OrderBy(p=>p.Distance).ToArray();
                for(int i=1;i<cuts.Length;i++) if(cuts[i-1].Distance+cuts[i-1].Span*.5f>cuts[i].Distance-cuts[i].Span*.5f+.00001f)
                    throw new InvalidOperationException("Road replacement puzzles overlap. Separate their intervals.");
                // Exact cut boundaries prevent even a single triangle/collider crossing an opening.
                foreach(var p in cuts) foreach(float boundary in new[]{p.Distance-p.Span*.5f,p.Distance+p.Span*.5f})
                    if(!path.Any(s=>Mathf.Abs(s.Distance-boundary)<.00001f)) { var sample=SandboxPuzzleEditing.At(path,boundary); path.Add(sample); path.Sort((a,b)=>a.Distance.CompareTo(b.Distance)); }
                SandboxPuzzle Cut(float d)=>cuts.FirstOrDefault(p=>d>p.Distance-p.Span*.5f && d<p.Distance+p.Span*.5f);
                Vector2[] Bands(Sample s,SandboxPuzzle cut)=>cut?cut.RoadBands(s.Width):new[]{new Vector2(-s.Width*.5f,s.Width*.5f)};
                if (!track.RoadMaterial || !track.TimberMaterial) throw new InvalidOperationException("Assign the road and timber materials.");
                var road=new MeshBuilder(); var pillars=new MeshBuilder();
                Vector3[] previous=null,first=null;
                for(int i=1;i<path.Count;i++) {
                    var a=path[i-1]; var s=path[i]; var cut=Cut((a.Distance+s.Distance)*.5f);
                    var left=Bands(a,cut); var right=Bands(s,cut);
                    for(int lane=0;lane<left.Length;lane++) {
                        var start=Ring(a,left[lane].x,left[lane].y,-track.DeckThickness,0);
                        var end=Ring(s,right[lane].x,right[lane].y,-track.DeckThickness,0);
                        road.Box(start,end,a.Distance,s.Distance);
                        if(i==1 || Cut((path[i-2].Distance+a.Distance)*.5f)!=cut) road.Cap(start,false);
                        if(i==path.Count-1 || Cut((s.Distance+path[i+1].Distance)*.5f)!=cut) road.Cap(end,true);
                    }
                }
                meshes.Add(road.Mesh("Road"));
                float railLength=path[path.Count-1].Distance;
                float sectionLength=track.RailSectionLength>0?track.RailSectionLength/track.transform.lossyScale.x:railLength;
                int sectionCount=Mathf.Max(1,Mathf.CeilToInt(railLength/sectionLength-.00001f));
                if(sectionCount>4096) throw new InvalidOperationException("Too many rail sections. Increase section length.");
                foreach(int side in new[]{-1,1}) {
                    if(!track.RailEnabled(side)) continue;
                    for(int section=0;section<sectionCount;section++) {
                        bool segmented=track.RailSectionLength>0;
                        if(segmented && !track.RailSectionEnabled(side,section)) continue;
                        var rails=new MeshBuilder();
                        float height=segmented?track.RailSectionHeight(side,section):track.RailHeightForSide(side);
                        float thickness=segmented?track.RailSectionThickness(side,section):track.RailThicknessForSide(side);
                        float start=section*sectionLength,end=Mathf.Min(railLength,(section+1)*sectionLength);
                        var piece=path;
                        if(segmented) {
                            piece=path.Where(s=>s.Distance>start && s.Distance<end).ToList();
                            piece.Insert(0,SandboxPuzzleEditing.At(path,start));piece.Add(SandboxPuzzleEditing.At(path,end));
                        }
                        bool hasGeometry=false;
                        for(int i=1;i<piece.Count;i++) {
                            var a=piece[i-1]; var b=piece[i]; var cut=Cut((a.Distance+b.Distance)*.5f);
                            var bands=Bands(a,cut); if(bands.Length==0) continue;
                            Vector3[] Rail(Sample s) { var bs=Bands(s,cut); float edge=side<0?bs[0].x:bs[bs.Length-1].y; return Ring(s,side<0?edge-thickness:edge,side<0?edge:edge+thickness,0,height); }
                            first=Rail(a); previous=Rail(b); rails.Box(first,previous,a.Distance,b.Distance);
                            if(i==1 || Cut((piece[i-2].Distance+a.Distance)*.5f)!=cut) rails.Cap(first,false);
                            if(i==piece.Count-1 || Cut((b.Distance+piece[i+1].Distance)*.5f)!=cut) rails.Cap(previous,true);
                            hasGeometry=true;
                        }
                        if(hasGeometry) meshes.Add(rails.Mesh((side<0?"Left Rail":"Right Rail")+(segmented?$" - Section {section+1:000}":"")));
                    }
                }
                Physics.SyncTransforms(); int missing=0, supports=0;
                float length=path[path.Count-1].Distance;
                int supportCount=Mathf.CeilToInt(length/track.SupportSpacing);
                if(track.Supports && supportCount>4096) throw new InvalidOperationException("Too many supports. Increase support spacing.");
                if(track.Supports) for(int n=0;n<=supportCount;n++) {
                    float distance=Mathf.Min(length,n*track.SupportSpacing);
                    var supportCut=Cut(distance);
                    if(supportCut && supportCut.RoadBands(1).Length==0) continue;
                    int i=path.FindIndex(s=>s.Distance>=distance); i=Mathf.Clamp(i,1,path.Count-1);
                    var a=path[i-1]; var b=path[i]; float f=Mathf.InverseLerp(a.Distance,b.Distance,distance);
                    var s=new Sample { Position=Vector3.Lerp(a.Position,b.Position,f), Right=Vector3.Slerp(a.Right,b.Right,f), Up=Vector3.Slerp(a.Up,b.Up,f), Width=Mathf.Lerp(a.Width,b.Width,f) };
                    foreach(int side in new[]{-1,1}) {
                        var bands=Bands(s,supportCut); var band=side<0?bands[0]:bands[bands.Length-1];
                        float inset=Mathf.Clamp(track.SupportInset,track.SupportThickness*.5f,(band.y-band.x)*.5f);
                        float lateral=side<0?band.x+inset:band.y-inset;
                        var top=track.transform.TransformPoint(s.Position+s.Right*lateral-s.Up*track.DeckThickness);
                        float reach=track.MaximumReach*track.transform.lossyScale.x;
                        var hits=Physics.RaycastAll(top+Vector3.up*.001f,Vector3.down,reach,track.GroundMask,QueryTriggerInteraction.Ignore)
                            .Where(h=>h.collider.gameObject.scene==track.gameObject.scene && IsGround(h.collider)).OrderBy(h=>h.distance).ToArray();
                        if(hits.Length==0 || top.y-hits[0].point.y<=.001f) { missing++; continue; }
                        var bottom=track.transform.InverseTransformPoint(new Vector3(top.x,hits[0].point.y,top.z));
                        var upper=track.transform.InverseTransformPoint(top); float r=track.SupportThickness*.5f;
                        Vector3[] Corners(Vector3 p)=>new[]{p+new Vector3(-r,0,-r),p+new Vector3(r,0,-r),p+new Vector3(r,0,r),p+new Vector3(-r,0,r)};
                        var lo=Corners(bottom); var hi=Corners(upper); pillars.Box(lo,hi); pillars.Caps(lo,hi); supports++;
                    }
                }
                if(supports>0) meshes.Add(pillars.Mesh("Supports"));
                root=new GameObject("Generated track (edit the spline)"); root.SetActive(false); root.transform.SetParent(track.transform,false);
                foreach(var mesh in meshes) {
                    var parent=root.transform;string label=mesh.name;
                    int split=label.IndexOf(" - Section ",StringComparison.Ordinal);
                    if(split>=0) {
                        string side=label.Substring(0,split);
                        parent=root.transform.Find(side);
                        if(!parent) {parent=new GameObject(side).transform;parent.SetParent(root.transform,false);}
                        label=label.Substring(split+3);
                    }
                    var go=new GameObject(label); go.transform.SetParent(parent,false); go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial=mesh.name=="Road"?track.RoadMaterial:track.TimberMaterial;
                    var collider=go.AddComponent<MeshCollider>(); collider.sharedMesh=mesh; collider.sharedMaterial=mesh.name=="Road"?track.RoadPhysics:track.RailPhysics;
                    collider.contactOffset=.001f;
                }
                var old=track.Generated;
                if(track.DeckStyle==SandboxDeckStyle.CreekDeck) {
                    CreekDeckBaker.Build(track,path,root.transform,meshes);
                }
                if(track.DeckStyle==SandboxDeckStyle.CreekDeck)
                    root.transform.Find("Road").GetComponent<MeshRenderer>().enabled=false;
                if(!track.ShowGeneratedVisuals) foreach(var renderer in root.GetComponentsInChildren<Renderer>()) renderer.enabled=false;
                if(PrefabUtility.IsPartOfPrefabInstance(track.gameObject))
                    PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(track.gameObject),PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                track.Generated=root; root.SetActive(true); root=null;
                if(old) DestroyGenerated(old);
                foreach(Transform child in track.transform)
                    if(child.gameObject!=track.Generated && child.name=="Generated track (edit the spline)" && !child.GetComponent<SandboxPuzzle>())
                        child.gameObject.SetActive(false);
                track.Samples=path.Select(s=>s.Position).ToArray(); track.BakedSignature=signature; track.ValidationError="";
                track.SupportWarning=missing>0?$"{missing} support positions have no eligible ground below them. Add SandboxTrackGround to ground colliders or adjust maximum reach.":"";
                EditorUtility.SetDirty(track); EditorSceneManager.MarkSceneDirty(track.gameObject.scene);
                PrefabUtility.RecordPrefabInstancePropertyModifications(track);
                return true;
            } catch(Exception e) {
                if(root) Object.DestroyImmediate(root);
                foreach(var mesh in meshes) if(mesh && !AssetDatabase.Contains(mesh)) Object.DestroyImmediate(mesh);
                track.ValidationError=e.Message; EditorUtility.SetDirty(track); return false;
            }
        }
        static void DestroyGenerated(GameObject root)
        {
            foreach(var mesh in root.GetComponentsInChildren<MeshFilter>(true).Select(f=>f.sharedMesh).Distinct())
                if(mesh && !AssetDatabase.Contains(mesh)) Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(root);
        }
        public static void EnsureFolder(string path)
        {
            path=path.Replace('\\','/'); if(AssetDatabase.IsValidFolder(path)) return;
            var parent=Path.GetDirectoryName(path).Replace('\\','/'); EnsureFolder(parent); AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        public static void Persist(SandboxTrackGenerator track, string folder=null, string owner=null)
        {
            if(track.AuthoredObjects) {
                AuthoredCampaignSaving.PersistResources(track.gameObject);
                return;
            }
            if(!Rebuild(track)) throw new InvalidOperationException(track.ValidationError);
            if(folder==null) {
                string scene=track.gameObject.scene.path;
                if(string.IsNullOrEmpty(scene)) throw new InvalidOperationException("Save the level scene before baking track assets.");
                folder=Path.ChangeExtension(scene,null)+"_Factory/Tracks";
            }
            EnsureFolder(folder);
            owner??=GlobalObjectId.GetGlobalObjectIdSlow(track).ToString()+"|"+track.gameObject.scene.path;
            if(track.BakeOwner!=owner || string.IsNullOrEmpty(track.MeshAssetPath) || !track.MeshAssetPath.StartsWith(folder+"/",StringComparison.Ordinal)) {
                track.BakeOwner=owner; track.MeshAssetPath=folder+"/Track_"+Guid.NewGuid().ToString("N")+".asset";
            }
            var existing=AssetDatabase.LoadAllAssetsAtPath(track.MeshAssetPath).OfType<Mesh>().ToDictionary(m=>m.name);
            // Unity may rename the main Road asset to the file stem on import.
            // Reuse it instead of adding a second Road subasset on the next save.
            if(!existing.ContainsKey("Road") && AssetDatabase.LoadMainAssetAtPath(track.MeshAssetPath) is Mesh mainRoad)
                existing.Add("Road",mainRoad);
            foreach(var filter in track.Generated.GetComponentsInChildren<MeshFilter>()) {
                var mesh=filter.sharedMesh;
                if(!existing.TryGetValue(mesh.name,out var saved)) {
                    saved=Object.Instantiate(mesh); saved.name=mesh.name;
                    if(!AssetDatabase.LoadMainAssetAtPath(track.MeshAssetPath)) AssetDatabase.CreateAsset(saved,track.MeshAssetPath);
                    else AssetDatabase.AddObjectToAsset(saved,track.MeshAssetPath);
                } else if(saved!=mesh) {
                    // CopySerialized changes CPU geometry without invalidating the GPU buffers.
                    // Keep the asset identity, but use Mesh setters so rendering and collision
                    // both receive the new road after saving or entering Play Mode.
                    saved.Clear();
                    saved.indexFormat=mesh.indexFormat;
                    saved.vertices=mesh.vertices;
                    saved.normals=mesh.normals;
                    saved.tangents=mesh.tangents;
                    saved.uv=mesh.uv;
                    saved.triangles=mesh.triangles;
                    saved.bounds=mesh.bounds;
                    saved.UploadMeshData(false);
                }
                filter.sharedMesh=saved;
                var collider=filter.GetComponent<MeshCollider>(); if(collider) collider.sharedMesh=saved;
                EditorUtility.SetDirty(saved);
                if(mesh!=saved && !AssetDatabase.Contains(mesh)) Object.DestroyImmediate(mesh);
            }
            // All meshes share one asset file. Save once after updating the pieces;
            // saving each rail section repeatedly imports the entire mesh collection.
            var meshAsset=AssetDatabase.LoadMainAssetAtPath(track.MeshAssetPath);
            EditorUtility.SetDirty(meshAsset); AssetDatabase.SaveAssetIfDirty(meshAsset);
            EditorUtility.SetDirty(track); PrefabUtility.RecordPrefabInstancePropertyModifications(track);
        }
    }
}
