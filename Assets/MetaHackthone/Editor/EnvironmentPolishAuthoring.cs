using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PhysicalAssembly.Editor
{
    public static class EnvironmentPolishAuthoring
    {
        const string Art = "Assets/CreekDiorama";
        public const string Output = Art + "/Meshes/EnvironmentPolish";
        const string Crown = "Tree_Round_Canopy";
        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/" + name + ".mat");
        internal static Transform Group(Transform parent, string name)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }
        static void Dirty(Object o)
        { EditorUtility.SetDirty(o); if (PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o); }
        internal static Mesh Save(Mesh m, string key)
        {
            string path = Output + "/" + key + ".asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved) {
                EditorUtility.CopySerialized(m, saved);
                // CopySerialized updates CPU data but can leave an already-rendered
                // mesh's GPU buffers stale until reimport. Explicitly invalidate them.
                saved.vertices=m.vertices; saved.normals=m.normals;
                for(int s=0;s<m.subMeshCount;s++)saved.SetIndices(m.GetIndices(s),m.GetTopology(s),s,false);
                saved.bounds=m.bounds;
                Object.DestroyImmediate(m); Dirty(saved); return saved;
            }
            AssetDatabase.CreateAsset(m, path); return m;
        }
        sealed class Geometry
        {
            public readonly List<Vector3> V = new(); readonly List<Vector2> uv = new(); readonly List<int> indices = new();
            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            { int n = V.Count; V.AddRange(new[] { a, b, c }); uv.AddRange(new[] { new Vector2(a.x, a.y), new Vector2(b.x, b.y), new Vector2(c.x, c.y) }); indices.AddRange(new[] { n, n + 1, n + 2 }); }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
            public void Branch(Vector3 a, Vector3 b, float bottom, float top)
            {
                var q = Quaternion.FromToRotation(Vector3.up, b - a);
                for (int i = 0; i < 8; i++)
                { float x = i * Mathf.PI / 4, y = (i + 1) * Mathf.PI / 4;
                    var p = q * new Vector3(Mathf.Cos(x), 0, Mathf.Sin(x)); var r = q * new Vector3(Mathf.Cos(y), 0, Mathf.Sin(y));
                    Quad(a + p * bottom, b + p * top, b + r * top, a + r * bottom);
                    Tri(b, b + r * top, b + p * top); Tri(a, a + p * bottom, a + r * bottom);
                }
            }
            public void Lobe(Vector3 center, Vector3 size, float seed)
            {
                const int sides = 9, rings = 5;
                Vector3 Point(int ring, int side)
                {
                    float phi = ring * Mathf.PI / rings, theta = side * Mathf.PI * 2 / sides;
                    float r = 1 + .13f * Mathf.Sin(theta * 3 + seed) * Mathf.Sin(phi) + .07f * Mathf.Cos(theta * 5 - phi * 3 + seed);
                    var v = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                    v.x += .13f * v.y; return center + Vector3.Scale(v * r, size);
                }
                for (int j = 0; j < rings; j++) for (int i = 0; i < sides; i++)
                { var a = Point(j, i); var b = Point(j + 1, i); var c = Point(j + 1, i + 1); var d = Point(j, i + 1); Quad(a, d, c, b); }
            }
            public Mesh Mesh(string name)
            { var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 }; m.SetVertices(V); m.SetUVs(0, uv); m.SetTriangles(indices, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m; }
        }
        static Material Leaf(int index)
        {
            string[] names = { "shadow", "green", "sun" };
            string path = Art + "/Materials/Botanical leaf " + names[index] + " sculpted.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Mat("Botanical leaf " + names[index])); AssetDatabase.CreateAsset(m, path); }
            Color[] colors = { new(.23f, .37f, .16f), new(.38f, .53f, .23f), new(.57f, .66f, .31f) };
            m.SetColor("_BaseColor", colors[index]); m.SetFloat("_Smoothness", .12f); m.enableInstancing = true; Dirty(m); return m;
        }
        internal static void RenderMesh(Transform root, string name, Mesh mesh, Material material)
        { var t = Group(root, name); t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh; t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material; }
        public static void BuildTree()
        {
            Directory.CreateDirectory(Output); AssetDatabase.Refresh();
            var g = new[] { new Geometry(), new Geometry(), new Geometry(), new Geometry() };
            var trunk = g[3]; trunk.Branch(Vector3.zero, new Vector3(.013f, .29f, .006f), .031f, .022f);
            trunk.Branch(new Vector3(.013f, .28f, .006f), new Vector3(-.012f, .56f, .02f), .022f, .005f);
            for (int i = 0; i < 5; i++)
            { float a = i * Mathf.PI * .4f; trunk.Branch(new Vector3(Mathf.Cos(a) * .055f, .002f, Mathf.Sin(a) * .055f), new Vector3(.005f, .11f, 0), .012f, .016f); }
            for (int i = 0; i < 11; i++)
            {
                float angle = i * 2.399963f, tier = i / 10f;
                float radius = Mathf.Lerp(.145f, .045f, tier);
                var end = new Vector3(Mathf.Cos(angle) * radius, .39f + tier * .21f, Mathf.Sin(angle) * radius);
                trunk.Branch(new Vector3(.01f, .24f + tier * .20f, .005f), end, .010f * (1 - tier * .35f), .003f);
                g[i % 3 == 0 ? 0 : 1].Lobe(end, new Vector3(.100f, .055f, .087f), i * 1.3f);
                for (int j = 0; j < 3; j++)
                {
                    float a = angle + j * 2.1f;
                    var p = end + new Vector3(Mathf.Cos(a) * .048f, .022f + j * .009f, Mathf.Sin(a) * .044f);
                    g[j == 1 ? 2 : 1].Lobe(p, new Vector3(.057f, .039f, .051f), i * 3 + j);
                }
            }
            var go = new GameObject(Crown);
            try
            {
                for (int i = 0; i < 4; i++) RenderMesh(go.transform, i == 3 ? "Sculpted branching trunk" : "Sculpted leaf tier " + i,
                    Save(g[i].Mesh("Sculpted crown " + i), "Crown-" + i), i == 3 ? Mat("Botanical bark") : Leaf(i));
                Group(go.transform, "Anchor_Ground");
                PrefabUtility.SaveAsPrefabAsset(go, Art + "/Prefabs/Botanical/" + Crown + ".prefab");
            }
            finally { Object.DestroyImmediate(go); }
            AssetDatabase.SaveAssets();
        }
        static Mesh WindMesh(Mesh source, Transform plant, Transform target, string key)
        {
            var m = Object.Instantiate(source); float phase = SceneryMotionAuthoring.Phase(plant.name + plant.localPosition);
            var wind = new Vector4(target.InverseTransformPoint(plant.position).y, Mathf.Tan(1.4f * Mathf.Deg2Rad), .9f, phase);
            m.SetUVs(3, Enumerable.Repeat(wind, m.vertexCount).ToList()); m.SetUVs(4, Enumerable.Repeat(Vector4.zero, m.vertexCount).ToList());
            SceneryMotionAuthoring.UpdateMotionBounds(m); return Save(m, key);
        }
        static Material WindMaterial(Material m)
        {
            if (m.shader.name == SceneryWindClock.ShaderName) return m;
            string path = Art + "/Materials/" + m.name + " breeze.mat";
            var saved = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!saved) { saved = new Material(m) { shader = Shader.Find(SceneryWindClock.ShaderName) }; AssetDatabase.CreateAsset(saved, path); }
            return saved;
        }
        static void Trees(GameObject root, string key)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Prefabs/Botanical/" + Crown + ".prefab");
            int id = 0;
            foreach (var tree in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == Crown || t.name == Crown + "_Animated").ToArray())
            {
                // Replace only mesh geometry, retaining every authored placement and other components.
                foreach (var f in tree.GetComponentsInChildren<MeshFilter>(true)) { var r = f.GetComponent<Renderer>(); if (r) r.enabled = false; }
                var old = tree.Find("Sculpted crown"); if (old) Object.DestroyImmediate(old.gameObject);
                var crown = Group(tree, "Sculpted crown");
                foreach (var f in template.GetComponentsInChildren<MeshFilter>())
                {
                    var t = Group(crown, f.name); var m = f.sharedMesh;
                    var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = WindMaterial(f.GetComponent<Renderer>().sharedMaterial);
                    t.gameObject.AddComponent<MeshFilter>().sharedMesh = WindMesh(m, tree, t, key + "-Tree" + id + "-" + f.name);
                }
                id++;
            }
            // Refresh only the tree batch; preserve all other manual environment edits.
            foreach (var dressing in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "10 Botanical dressing").ToArray())
            {
                var editable = dressing.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Editable placement"));
                var batch = dressing.Find("Render batches/Layered canopy"); var source = editable ? editable.Find("Layered canopy") : null;
                if (!batch || !source) continue;
                Combine(source, batch, key + "-Canopy");
                var props = dressing.Find("Render batches/Story props");
                if (props && !props.Find("Botanical oak"))
                {
                    // cs274 Level2 lost this renderer; use the original batch in precisely the sibling space.
                    RenderMesh(props, "Botanical oak", AssetDatabase.LoadAssetAtPath<Mesh>(Art + "/Meshes/Botanical/Dressing_3_1.asset"), Mat("Botanical oak"));
                }
            }
        }
        internal static void Combine(Transform source, Transform destination, string key)
        {
            foreach (Transform t in destination.Cast<Transform>().ToArray()) Object.DestroyImmediate(t.gameObject);
            var filters = source.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && f.GetComponent<Renderer>() && f.GetComponent<Renderer>().enabled).ToArray();
            int i = 0;
            foreach (var group in filters.GroupBy(f => f.GetComponent<Renderer>().sharedMaterial))
            {
                var combined = new Mesh { name = key + " " + group.Key.name, indexFormat = IndexFormat.UInt32 };
                combined.CombineMeshes(group.Select(f => new CombineInstance { mesh = f.sharedMesh, transform = destination.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray(), true, true);
                // Transform wind pivots into the batch's space; preserve per-tree phases.
                var data = new List<Vector4>();
                foreach (var f in group)
                {
                    var tree = f.transform; while (tree.parent && tree.parent != source) tree = tree.parent;
                    float phase = SceneryMotionAuthoring.Phase(tree.name + tree.localPosition);
                    var wind = new Vector4(destination.InverseTransformPoint(tree.position).y, Mathf.Tan(1.4f * Mathf.Deg2Rad), .9f, phase);
                    data.AddRange(Enumerable.Repeat(wind, f.sharedMesh.vertexCount));
                }
                combined.SetUVs(3, data); combined.SetUVs(4, Enumerable.Repeat(Vector4.zero, combined.vertexCount).ToList()); SceneryMotionAuthoring.UpdateMotionBounds(combined);
                RenderMesh(destination, group.Key.name, Save(combined, key + "-" + i++), WindMaterial(group.Key));
            }
        }
        internal static Transform Box(Transform root, string name, Vector3 center, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root, false); go.transform.localPosition = center; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material; return go.transform;
        }
        internal static Transform Beam(Transform root, string name, Vector3 a, Vector3 b, float width, Material material)
        { var t = Box(root, name, (a + b) * .5f, new Vector3(width, (b - a).magnitude, width), material); t.localRotation = Quaternion.FromToRotation(Vector3.up, b - a); return t; }
        static Transform Wheel(Transform root, string name, Vector3 p, float radius, float width, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = name; Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root, false); go.transform.localPosition = p; go.transform.localRotation = Quaternion.Euler(0, 0, 90);
            go.transform.localScale = new Vector3(radius * 2, width * .5f, radius * 2); go.GetComponent<Renderer>().sharedMaterial = material; return go.transform;
        }
        internal static Transform Rock(Transform root, string name, Vector3 center, Vector3 size)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Prefabs/Botanical/Rock_Stratum_A.prefab");
            var t = ((GameObject)PrefabUtility.InstantiatePrefab(asset, root)).transform; t.name = name;
            var mesh = t.GetComponentInChildren<MeshFilter>(); var b = mesh.sharedMesh.bounds;
            t.localScale = new Vector3(size.x / b.size.x, size.y / b.size.y, size.z / b.size.z);
            t.localPosition = center - Vector3.Scale(b.center, t.localScale); return t;
        }
        static void Bridge(SandboxDropBridge bridge)
        {
            if (!bridge.Deck || !bridge.Generated) throw new InvalidOperationException("Bridge missing generated physics.");
            var root = bridge.Generated.transform;
            var old = root.Find("Timber lifting rig"); if (old) Object.DestroyImmediate(old.gameObject);
            var deckOld = bridge.Deck.transform.Find("Articulated timber deck"); if (deckOld) Object.DestroyImmediate(deckOld.gameObject);
            var rig = bridge.GetComponent<SandboxBridgeRig>(); if (!rig) rig = bridge.gameObject.AddComponent<SandboxBridgeRig>();
            rig.Bridge = bridge;
            var art = Group(root, "Timber lifting rig");
            var wood = Mat("Botanical oak"); var metal = Mat("Botanical bronze");
            foreach (string n in new[] { "Rear support post", "Overhead support", "Target suspension" })
            { var t = root.Find(n); if (t) foreach (var r in t.GetComponentsInChildren<Renderer>()) r.enabled = false; }
            if (bridge.Chain) bridge.Chain.gameObject.SetActive(false);
            var slab = bridge.Deck.transform.Find("Deck"); if (slab) slab.GetComponent<Renderer>().enabled = false;
            var deck = Group(bridge.Deck.transform, "Articulated timber deck");
            float scale = bridge.transform.lossyScale.x;
            deck.localScale = Vector3.one * scale;
            var plankAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Prefabs/BridgePlank.prefab");
            for (int i = 0; i < 14; i++)
            {
                var plank = Object.Instantiate(plankAsset, deck).transform; plank.name = "Oak tread " + (i + 1);
                var rr = plank.GetComponentsInChildren<Renderer>(); var b = rr[0].bounds; foreach (var r in rr) b.Encapsulate(r.bounds);
                // Native plank local bounds, independent of current hinge rotation.
                var bounds = new Bounds(); bool first = true;
                foreach (var f in plank.GetComponentsInChildren<MeshFilter>()) { var q = CarSceneryClearance.TransformBounds(f.sharedMesh.bounds, plank.worldToLocalMatrix * f.transform.localToWorldMatrix); if (first) { bounds = q; first = false; } else bounds.Encapsulate(q); }
                var size = new Vector3(bridge.Width, bridge.Thickness, bridge.Length / 14 * .97f);
                plank.localScale = new Vector3(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
                plank.localPosition = new Vector3(0, -bridge.Thickness * .5f, (i + .5f) * bridge.Length / 14) - Vector3.Scale(bounds.center, plank.localScale);
                foreach (var c in plank.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Box(deck, "Underside oak stringer", new Vector3(side * bridge.Width * .35f, -bridge.Thickness - .065f, bridge.Length * .5f), new Vector3(.16f, .15f, bridge.Length), wood);
                Box(deck, "Iron binding strap", new Vector3(side * bridge.Width * .38f, -.005f, bridge.Length * .5f), new Vector3(.055f, .018f, bridge.Length * .94f), metal);
            }
            rig.DeckEyes = new Transform[2]; rig.Pulleys = new Transform[2]; rig.TailGuides = new Transform[2]; rig.Cables = new LineRenderer[2];
            var target = root.InverseTransformPoint(bridge.TargetBody.position);
            var hinge = root.InverseTransformPoint(bridge.Pivot.position);
            float top = Mathf.Max(target.y + 1.4f, hinge.y + bridge.Length * Mathf.Sin(bridge.RaisedAngle * Mathf.Deg2Rad) + .6f);
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -1 : 1) * (bridge.Width * .5f + .27f);
                var a = new Vector3(x, hinge.y - .65f, hinge.z); var b = new Vector3(x, top, hinge.z);
                Beam(art, "Hinge tower", a, b, .19f, wood);
                Rock(art, "Hinge stone footing", a - Vector3.up * .08f, new Vector3(.55f, .45f, .62f));
                Beam(art, "Tower knee brace", a + Vector3.up * 1.1f, b + Vector3.back * .9f - Vector3.up * .1f, .12f, wood);
                Beam(art, "Side cable gantry", b, new Vector3(x, top, target.z), .16f, wood);
                Beam(art, "Latch tower", new Vector3(x, target.y - 1.3f, target.z), new Vector3(x, top, target.z), .18f, wood);
                Box(art, "Foot shoe", new Vector3(x, target.y - 1.2f, target.z), new Vector3(.28f, .26f, .28f), metal);
                Beam(art, "Diagonal gantry brace", b - Vector3.up * .8f, b + Vector3.back * .65f, .11f, wood);
                var pulley = Wheel(art, "Bronze pulley", b, .22f, .10f, metal); rig.Pulleys[i] = Group(art, "Cable pulley anchor"); rig.Pulleys[i].localPosition = b + Vector3.up * .19f;
                rig.TailGuides[i] = Group(art, "Released cable guide"); rig.TailGuides[i].localPosition = new Vector3(x, top + .19f, target.z);
                Wheel(art, "Guide pulley", rig.TailGuides[i].localPosition - Vector3.up * .19f, .22f, .1f, metal);
                rig.DeckEyes[i] = Group(deck, "Deck lifting eye"); rig.DeckEyes[i].localPosition = new Vector3((i == 0 ? -1 : 1) * bridge.Width * .46f, .05f, bridge.Length * .88f);
                Wheel(deck, "Lifting eye bolt", rig.DeckEyes[i].localPosition, .075f, .065f, metal);
                var cable = Group(art, "Lifting rope " + i).gameObject.AddComponent<LineRenderer>(); cable.useWorldSpace = true; cable.widthMultiplier = .035f * scale; cable.numCornerVertices = 2;
                cable.sharedMaterial = wood; rig.Cables[i] = cable;
            }
            Beam(art, "Hinge axle", new Vector3(-bridge.Width * .57f, hinge.y, hinge.z), new Vector3(bridge.Width * .57f, hinge.y, hinge.z), .14f, metal);
            Box(art, "Latch overhead crossbeam", new Vector3(0, top - .1f, target.z), new Vector3(bridge.Width + .8f, .18f, .21f), wood);
            rig.Drum = Group(art, "Release shaft"); rig.Drum.localPosition = new Vector3(0, top - .3f, target.z);
            Wheel(rig.Drum, "Cable drum", Vector3.zero, .15f, bridge.Width + .65f, wood);
            for (int i = -1; i <= 1; i += 2) Wheel(rig.Drum, "Drum flange", Vector3.right * i * (bridge.Width * .5f + .2f), .23f, .065f, metal);
            rig.ReleaseLever = Group(art, "Stone locked release pawl"); rig.ReleaseLever.localPosition = target + new Vector3(-.48f, .03f, .08f);
            Beam(rig.ReleaseLever, "Locking arm", Vector3.zero, new Vector3(.48f, .03f, 0), .08f, metal);
            Beam(art, "Release linkage", target + new Vector3(-.48f, .03f, .08f), new Vector3(-.48f, top - .3f, target.z), .055f, metal);
            // A fork around the stone reads as a sacrificial pin; the forward drill face remains clear.
            foreach (int side in new[] { -1, 1 }) Box(art, "Stone cradle cheek", target + new Vector3(side * bridge.TargetSize * .62f, -.08f, .12f), new Vector3(.08f, bridge.TargetSize * .9f, .12f), metal);
            rig.ResetVisuals(); Dirty(rig); Dirty(bridge);
        }
        static void Falls(GameObject root, string key)
        {
            GroundedSceneryRepair.AnchorWaterfalls(root,key);
            DressWaterfallMouths(root);
        }
        public static void DressWaterfallMouths(GameObject root)
        {
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name.StartsWith("Flowing curtain")).ToArray())
            {
                var t = f.transform; var old = t.Find("Anchored waterfall source"); if (old) Object.DestroyImmediate(old.gameObject);
                var vertices = f.sharedMesh.vertices; float top = vertices.Max(v => v.y), bottom = vertices.Min(v => v.y);
                var lip = vertices.Where(v => v.y > top - .006f).Aggregate(Vector3.zero, (a, b) => a + b) / vertices.Count(v => v.y > top - .006f);
                var foot = vertices.Where(v => v.y < bottom + .006f).Aggregate(Vector3.zero, (a, b) => a + b) / vertices.Count(v => v.y < bottom + .006f);
                var forward = foot - lip; forward.y = 0; if (forward.sqrMagnitude < .0001f) forward = Vector3.forward; forward.Normalize();
                var right = Vector3.Cross(Vector3.up, forward);
                var source = Group(t, "Anchored waterfall source"); source.localPosition = lip; source.localRotation = Quaternion.LookRotation(forward);
                var crest=vertices.Where(v=>v.y>top-.006f).Select(v=>Vector3.Dot(v-lip,right)).ToArray();
                float width = Mathf.Max(.06f,crest.Max()-crest.Min());
                Rock(source, "Spring bedrock", new Vector3(0, -.04f, -.12f), new Vector3(width * 2, .08f, .24f));
                Rock(source, "Spring left bank", new Vector3(-width * .64f, .012f, -.065f), new Vector3(width * .34f, .065f, .20f));
                Rock(source, "Spring right bank", new Vector3(width * .64f, .007f, -.06f), new Vector3(width * .34f, .06f, .19f));
                // Hide the water's top edge inside a thin rock fissure. There is no
                // horizontal pool or second water material meeting the falling sheet.
                Rock(source, "Spring fissure overhang", new Vector3(0, .018f, -.10f), new Vector3(width * 1.4f, .06f, .24f));
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Waterfall spring"))
            { var run = t.Find("Spring run"); if (run) run.gameObject.SetActive(false); }
        }
        static void Roads(SandboxFactoryLevel factory, string key)
        {
            var wood = Mat("Botanical oak"); var metal = Mat("Botanical bronze"); int id = 0;
            var lanes = CarSceneryClearance.Lanes(factory);
            var glow = Mat("Quiet levitation runes");
            if (!glow) { glow = new Material(Shader.Find("Creek Diorama/Foundation Glow")); AssetDatabase.CreateAsset(glow, Art + "/Materials/Quiet levitation runes.mat"); }
            foreach (var track in factory.Tracks)
            {
                var old = track.transform.Find("Timber trestles and floating foundations"); if (old) Object.DestroyImmediate(old.gameObject);
                var art = Group(track.transform, "Timber trestles and floating foundations");
                var samples = SandboxTrackBaker.SamplePath(track); float last = -100;
                foreach (var s in samples)
                {
                    if (s.Distance < .5f || s.Distance - last < 4.2f || s.Distance > samples.Last().Distance - .6f) continue;
                    if (factory.Puzzles.Any(p => p && p.Track == track && p.ReplacesRoad && Mathf.Abs(s.Distance - p.Distance) < p.Span * .5f + 1.3f)) continue;
                    last = s.Distance;
                    var trestle = Group(art, "Trestle " + id++); trestle.localPosition = s.Position; trestle.localRotation = Quaternion.LookRotation(s.Forward, s.Up);
                    float width = s.Width;
                    Box(trestle, "Deck cross bearer", new Vector3(0, -.23f, 0), new Vector3(width + .3f, .24f, .28f), wood);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = side * width * .36f;
                        Box(trestle, "Underdeck stringer", new Vector3(x, -.14f, 0), new Vector3(.14f, .17f, 1.9f), wood);
                        Beam(trestle, "Trestle post", new Vector3(x, -.3f, 0), new Vector3(x, -1.45f, 0), .18f, wood);
                        Beam(trestle, "Splayed knee brace", new Vector3(x, -.9f, 0), new Vector3(side * width * .48f, -.33f, .52f), .12f, wood);
                        Box(trestle, "Iron post collar", new Vector3(x, -.50f, 0), new Vector3(.21f, .14f, .21f), metal);
                    }
                    Beam(trestle, "Trestle cross brace", new Vector3(-width * .36f, -.4f, 0), new Vector3(width * .36f, -1.3f, 0), .11f, wood);
                    Rock(trestle, "Suspended limestone foundation", new Vector3(0, -1.65f, 0), new Vector3(width * 1.05f, .85f, 1.05f));
                    Rock(trestle, "Floating lower shard", new Vector3(.12f, -2.18f, .04f), new Vector3(width * .48f, .5f, .56f));
                    for (int side = -1; side <= 1; side += 2) Box(trestle, "Quiet levitation inlay", new Vector3(side * width * .33f, -1.68f, -.54f), new Vector3(.06f, .24f, .025f), glow);
                    var halo = new Geometry();
                    for (int segment = 0; segment < 12; segment++)
                    {
                        float a = segment * Mathf.PI / 6, b = a + Mathf.PI / 9;
                        Vector3 Ring(float angle, float r) => new Vector3(Mathf.Cos(angle) * width * .44f * r, -2.04f, Mathf.Sin(angle) * .59f * r);
                        halo.Quad(Ring(a, 1), Ring(b, 1), Ring(b, .965f), Ring(a, .965f));
                    }
                    RenderMesh(trestle, "Broken levitation halo", Save(halo.Mesh("Levitation sigil"), key + "-Halo" + id), glow);
                    // Multilevel roads can pass beneath a higher section: omit any pier that would cut through a lower lane.
                    if (trestle.GetComponentsInChildren<MeshFilter>().Any(f => CarSceneryClearance.MeshOverlaps(f, lanes))) Object.DestroyImmediate(trestle.gameObject);
                }
                // All added load-bearing artwork stays below the existing drive surface.
                foreach (var c in art.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                // Consolidate static materials to avoid hundreds of additional draws on Quest.
                var baked = Group(track.transform, "Trestle render batches");
                var previous = track.transform.Cast<Transform>().Where(t => t.name == "Trestle render batches" && t != baked).ToArray(); foreach (var t in previous) Object.DestroyImmediate(t.gameObject);
                BakeStatic(art, baked, key + "-Trestles"); art.gameObject.SetActive(false);
            }
        }
        internal static void BakeStatic(Transform source, Transform destination, string key)
        {
            int i = 0;
            foreach (var group in source.GetComponentsInChildren<MeshFilter>().GroupBy(f => f.GetComponent<Renderer>().sharedMaterial))
            {
                var mesh = new Mesh { name = key, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(group.Select(f => new CombineInstance { mesh = f.sharedMesh, transform = destination.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray(), true, true);
                RenderMesh(destination, group.Key.name, Save(mesh, key + "-" + i++), group.Key);
            }
        }
        public static void SlowClouds()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Art }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (!m || !(m.shader.name == "Creek Diorama/Cloud Puff" || m.shader.name == "Creek Diorama/Cloud Shell")) continue;
                m.SetFloat("_RollSpeed", .012f); m.SetFloat("_WispSpeed", .016f); m.SetFloat("_Billow", .045f); m.SetFloat("_Drift", .06f); m.SetFloat("_Swell", .45f); Dirty(m);
            }
        }
        static void Dress(GameObject root, string key)
        {
            var layout=root.GetComponent<SandboxFactoryLevel>();
            if(layout && (key=="Scene5" || key=="Level5"))ContactPolishAuthoring.FinalCourseComposition(layout,key);
            if(layout){GroundedSceneryRepair.Prepare(layout,key);TrayWaterRepair.Fit(layout,key);}
            Trees(root, key); Falls(root, key); GroundedSceneryRepair.ClearWaterfallDressing(root,key); ContactPolishAuthoring.Terrain(root, key);
            var factory = root.GetComponent<SandboxFactoryLevel>();
            if (factory) {
                TrayWaterRepair.GroundProps(factory,key);
                ContactPolishAuthoring.Roads(factory, key);
                foreach (var b in factory.GetComponentsInChildren<SandboxDropBridge>(true)) { Bridge(b); ContactPolishAuthoring.GroundBridge(b); }
                ContactPolishAuthoring.Foam(factory, key);
            }
        }
        public static string ApplyLevel(int level)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play first.");
            Directory.CreateDirectory(Output); AssetDatabase.Refresh();
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene(); bool suspended = SandboxTrackEditing.Suspended; SandboxTrackEditing.Suspended = true;
            try
            {
                string path = $"Assets/MetaHackthone/Scenes/Level{level}.unity";
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path); bool opened = !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try { var f = SandboxFactoryAuthoring.Find(scene); Dress(f.gameObject, "Scene" + level); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
                path = $"Assets/MetaHackthone/Generated/Campaign/Level{level}.prefab"; var root = PrefabUtility.LoadPrefabContents(path);
                try { Dress(root, "Level" + level); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                AssetDatabase.SaveAssets(); return "Level " + level + " scene and runtime prefab updated";
            }
            finally { SandboxTrackEditing.Suspended = suspended; if (active.IsValid()) UnityEngine.SceneManagement.SceneManager.SetActiveScene(active); }
        }
    }
}
