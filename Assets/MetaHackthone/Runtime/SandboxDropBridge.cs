using UnityEngine;

namespace PhysicalAssembly
{
    public sealed class SandboxDropBridge : SandboxPuzzle
    {
        [Min(.1f)] public float Length = 3, Width = 2.4f;
        [Range(10, 80)] public float RaisedAngle = 55;
        [Min(.01f)] public float Thickness = .12f, Mass = 2, TargetSize = .5f;
        [Min(.1f)] public float TargetHeight = 2.2f, TargetApproach = 3;
        [Tooltip("Independent distance from the track start. Negative uses the legacy approach distance until migrated.")]
        public float TargetDistance = -1;
        [Min(1)] public int TargetLayer = 3;
        public bool UseBuildingLayers = true;
        public enum ReferenceWheels { Large, Small }
        [Tooltip("Standard wheel axle is aligned with the first building layer. Choose the wheels the challenge is designed for.")]
        public ReferenceWheels HeightReference = ReferenceWheels.Large;
        public float TargetTrackDistance => TargetDistance>=0 ? TargetDistance : Distance-Length*.5f-TargetApproach;
        public float LayerHeight => BuildingGeometry.Unit/GravityGarage25DCourse.UnitScale *
            ((HeightReference==ReferenceWheels.Large?.44f:.30f)/.38f+Mathf.Max(0,TargetLayer-1));
        public float SupportSide = -2, PivotHeight;
        [Tooltip("Moves the hinge and landing stop relative to the cutout; keep the deck covering the gap.")]
        public Vector3 PivotOffset;
        [Tooltip("Number of physics frames of drill-tip contact needed to break the target.")]
        [Min(1)] public int RequiredHits = 1;
        [Min(0)] public float AngularDamping = .15f;
        [Range(-170,0)] public float MinimumAngle = -80;
        [Range(0,170)] public float MaximumAngle = 80;
        public bool CollideWithSupport;
        [Tooltip("Release the hanging support with the broken target so raised tools can continue underneath.")]
        public bool ReleaseSuspensionWithTarget;
        [Tooltip("Normally off: the hinge seam can overlap the road lip. The deck still collides with vehicles and its landing stop.")]
        public bool CollideWithTrack;
        public PhysicsMaterial DeckPhysics;
        public Material ChainMaterial;
        [Tooltip("Material for the destructible target only. Empty uses the puzzle material.")]
        public Material TargetMaterial;
        [HideInInspector] public Vector3 TargetLocalPosition;
        [HideInInspector] public Rigidbody Deck, TargetBody;
        [HideInInspector] public Transform Pivot, Chain;
        [HideInInspector] public Collider TargetCollider;
        [HideInInspector] public ConfigurableJoint Restraint;
        [HideInInspector] public HingeJoint Hinge;
        int hits; bool active, touching;
        public bool Released { get; private set; }
        public override float Span => Length;
        public override bool ReplacesRoad => true;
        public override Collider DrillCollider => isActiveAndEnabled && active && !Released ? TargetCollider : null;
        public override string ValidatePuzzle()
        {
            if (!float.IsFinite(Length + Width + Thickness + TargetHeight + TargetApproach + TargetSize + SupportSide + PivotHeight + PivotOffset.sqrMagnitude + Mass + AngularDamping + RaisedAngle + MinimumAngle + MaximumAngle)) return "Bridge values must be finite.";
            if (Length <= 0 || Width <= 0 || Thickness <= 0 || TargetSize <= 0 || Mass <= 0 || TargetApproach <= 0 || RequiredHits < 1) return "Bridge dimensions, mass and hit count must be positive.";
            if (Mathf.Abs(SupportSide) < Width * .5f + .2f) return "Move the support outside the driving corridor.";
            if (TargetHeight <= TargetSize * .5f + .02f) return "Raise the target to leave clearance beneath it.";
            return null;
        }
        public override void BuildGeometry(Transform root)
        {
            Pivot = new GameObject("Bridge hinge").transform; Pivot.SetParent(root, false);
            Pivot.localPosition = new Vector3(0, PivotHeight, -Length * .5f)+PivotOffset;
            var anchor=Pivot.gameObject.AddComponent<Rigidbody>(); anchor.isKinematic=true; anchor.useGravity=false;
            var deck = new GameObject("Dynamic bridge deck"); deck.transform.SetParent(Pivot, false);
            float scale=transform.lossyScale.x;
            // Match the vehicle system: bake dimensions into geometry; do not scale dynamic Rigidbody roots.
            deck.transform.localScale=Vector3.one/scale;
            deck.transform.localRotation = Quaternion.Euler(-RaisedAngle, 0, 0);
            Deck = deck.AddComponent<Rigidbody>(); Deck.mass = Mass; Deck.isKinematic = true; Deck.useGravity = false;
            Deck.angularDamping = AngularDamping; Deck.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Deck.solverIterations = 24; Deck.solverVelocityIterations = 12;
            var slab = Box(deck.transform, "Deck", new Vector3(0, -Thickness * .5f, Length * .5f)*scale, new Vector3(Width, Thickness, Length)*scale);
            slab.GetComponent<Collider>().sharedMaterial = DeckPhysics ? DeckPhysics : Track ? Track.RoadPhysics : null;
            Hinge = deck.AddComponent<HingeJoint>(); Hinge.axis = Vector3.right; Hinge.autoConfigureConnectedAnchor = false;
            Hinge.connectedBody=anchor; Hinge.anchor = Vector3.zero; Hinge.connectedAnchor = Vector3.zero;
            Hinge.useLimits = true; Hinge.limits = new JointLimits { min = MinimumAngle, max = MaximumAngle };
            var targetPos = TargetLocalPosition == Vector3.zero ? new Vector3(0, TargetHeight, -Length * .5f - TargetApproach) : TargetLocalPosition;
            var target = Box(root, "Destructible drill target", targetPos, Vector3.one * TargetSize);
            if (TargetMaterial) target.GetComponent<Renderer>().sharedMaterial = TargetMaterial;
            EnsureStoneTarget(target.transform);
            TargetCollider = target.GetComponent<Collider>(); TargetBody = target.AddComponent<Rigidbody>(); TargetBody.isKinematic = true;
            Box(root, "Rear support post", new Vector3(targetPos.x + SupportSide, (targetPos.y + 1) * .5f, targetPos.z), new Vector3(.18f, targetPos.y + 1, .18f));
            Box(root, "Overhead support", new Vector3(targetPos.x + SupportSide * .5f, targetPos.y + 1, targetPos.z), new Vector3(Mathf.Abs(SupportSide) + .2f, .18f, .18f));
            Box(root, "Target suspension", targetPos + Vector3.up * (.5f + TargetSize * .25f), new Vector3(.045f, 1 - TargetSize * .5f, .045f));
            Box(root, "Bridge landing stop", new Vector3(0, PivotHeight - Thickness - .1f, Length * .5f + .1f)+PivotOffset, new Vector3(Width, .2f, .4f));
            Chain = new GameObject("Holding chain").transform; Chain.SetParent(root,false);
            int links=Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(targetPos,Pivot.localPosition+Quaternion.Euler(-RaisedAngle,0,0)*Vector3.forward*Length*.85f)/.15f),4,64);
            for(int i=0;i<links;i++) {
                var link=new GameObject("Chain link").AddComponent<LineRenderer>(); link.transform.SetParent(Chain,false);
                link.useWorldSpace=false; link.loop=true; link.positionCount=12;
                link.widthMultiplier=.012f*scale; link.numCornerVertices=2;
                link.sharedMaterial=ChainMaterial?ChainMaterial:Track.TimberMaterial;
                for(int j=0;j<12;j++) {float a=j*Mathf.PI*2/12;float y=-1+2*(i+.5f)/links+Mathf.Sin(a)*1.25f/links;
                    link.SetPosition(j,i%2==0?new Vector3(Mathf.Cos(a)*.5f,y,0):new Vector3(0,y,Mathf.Cos(a)*.5f));}
            }
            ResetPuzzle(false);
        }
        public const string StoneVisualName = "Stone target";
        public SandboxStoneShatter Debris { get; private set; }
        bool authoredPoseCaptured, authoredTargetActive, authoredChainActive, authoredSuspensionActive;
        Vector3 authoredDeckPosition;
        Quaternion authoredDeckRotation;
        bool AuthoredObjects => Track && Track.AuthoredObjects;
        // The cube keeps the drill collider and authored size; a stone from the
        // Beautiful Corner kit is what the player sees and what shatters.
        static Transform EnsureStoneTarget(Transform target)
        {
            var stone = target.Find(StoneVisualName);
            // A missing authored visual is an intentional deletion, not an old bake.
            var level=target.GetComponentInParent<SandboxFactoryLevel>(true);
            if(level && level.AuthoredObjects) return stone;
            if (!stone) stone = SandboxStoneLibrary.AddStoneVisual(target, StoneVisualName);
            if (stone && target.TryGetComponent<Renderer>(out var cube)) cube.enabled = false;
            return stone;
        }
        // Levels baked before the stone target existed are upgraded when they load.
        void Start() { if (Application.isPlaying && TargetBody) EnsureStoneTarget(TargetBody.transform); }
        void ClearDebris() { if (Debris) { if (Application.isPlaying) Destroy(Debris.gameObject); else DestroyImmediate(Debris.gameObject); } Debris = null; }
        void OnDisable() { ClearDebris(); }
        public override void ResetPuzzle(bool running)
        {
            active = running; Released = false; hits = 0; touching = false;
            ClearDebris();
            if (!Deck || !TargetBody || !Pivot) return;
            if(AuthoredObjects && !authoredPoseCaptured) {
                authoredDeckPosition=Pivot.InverseTransformPoint(Deck.transform.position);
                authoredDeckRotation=Quaternion.Inverse(Pivot.rotation)*Deck.transform.rotation;
                authoredTargetActive=TargetBody.gameObject.activeSelf;
                authoredChainActive=Chain && Chain.gameObject.activeSelf;
                var suspension=Generated?Generated.transform.Find("Target suspension"):null;
                authoredSuspensionActive=suspension && suspension.gameObject.activeSelf;
                authoredPoseCaptured=true;
            }
            if (Application.isPlaying) EnsureStoneTarget(TargetBody.transform);
            if (!Deck.isKinematic) { Deck.linearVelocity = Vector3.zero; Deck.angularVelocity = Vector3.zero; }
            Deck.isKinematic = true; Deck.useGravity = false;
            Deck.transform.SetPositionAndRotation(AuthoredObjects?Pivot.TransformPoint(authoredDeckPosition):Pivot.position,
                Pivot.rotation * (AuthoredObjects?authoredDeckRotation:Quaternion.Euler(-RaisedAngle, 0, 0)));
            Deck.position = Deck.transform.position; Deck.rotation = Deck.transform.rotation;
            TargetBody.gameObject.SetActive(!AuthoredObjects || authoredTargetActive);
            if(Chain) Chain.gameObject.SetActive(!AuthoredObjects || authoredChainActive);
            SetSuspension(true);
            TargetBody.position = TargetBody.transform.position; TargetBody.rotation = TargetBody.transform.rotation;
            var anchor=Pivot.GetComponent<Rigidbody>(); anchor.position=Pivot.position; anchor.rotation=Pivot.rotation;
            Hinge.connectedAnchor = Vector3.zero;
            if (!Restraint) Restraint = Deck.gameObject.AddComponent<ConfigurableJoint>();
            Restraint.connectedBody = TargetBody; Restraint.autoConfigureConnectedAnchor = false;
            Restraint.anchor = new Vector3(0, 0, Length * .85f * transform.lossyScale.x); Restraint.connectedAnchor = Vector3.zero;
            Restraint.xMotion = Restraint.yMotion = Restraint.zMotion = ConfigurableJointMotion.Limited;
            Restraint.angularXMotion = Restraint.angularYMotion = Restraint.angularZMotion = ConfigurableJointMotion.Free;
            Restraint.linearLimit = new SoftJointLimit { limit = Vector3.Distance(Deck.transform.TransformPoint(Restraint.anchor), TargetBody.position) / Mathf.Max(.0001f,Deck.transform.lossyScale.x) };
            Restraint.enableCollision = CollideWithSupport;
            foreach (var a in Deck.GetComponentsInChildren<Collider>()) foreach (var b in Generated.GetComponentsInChildren<Collider>())
                if (!b.GetComponentInParent<Rigidbody>() && b.name != "Bridge landing stop") Physics.IgnoreCollision(a, b, !CollideWithSupport);
            if(Track && Track.Generated) foreach(var a in Deck.GetComponentsInChildren<Collider>()) foreach(var b in Track.Generated.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(a,b,!CollideWithTrack);
            // A distance-limited tether can swing/sag before a hit. The intact
            // target is a latch: keep the exact raised pose until DrillHit.
            Deck.isKinematic = true; Deck.useGravity = false;
            UpdateChain();
            var rig = GetComponent<SandboxBridgeRig>();
            if (rig) rig.ResetVisuals();
        }
        public override void DrillHit(Vector3 position)
        {
            if (!active || Released || touching) return;
            touching = true;
            if (++hits < RequiredHits) return;
            Released = true;
            // Release the physical tether; the hinge and gravity remain active.
            Restraint.xMotion = Restraint.yMotion = Restraint.zMotion = ConfigurableJointMotion.Free;
            var stone = TargetBody.transform.Find(StoneVisualName);
            if (stone && stone.TryGetComponent<MeshFilter>(out var filter) && stone.TryGetComponent<Renderer>(out var stoneRenderer))
                Debris = SandboxStoneShatter.Spawn(stone, filter.sharedMesh, stoneRenderer.sharedMaterial, position, Vector3.zero);
            TargetBody.gameObject.SetActive(false); if(Chain) Chain.gameObject.SetActive(false);
            SetSuspension(false);
            Deck.isKinematic = false; Deck.useGravity = true;
            Deck.linearVelocity = Vector3.zero; Deck.angularVelocity = Vector3.zero;
            Deck.WakeUp();
            var rig = GetComponent<SandboxBridgeRig>();
            if (rig) rig.Release();
        }
        void SetSuspension(bool visible) {
            if(!ReleaseSuspensionWithTarget || !Generated) return;
            var suspension=Generated.transform.Find("Target suspension");
            if(suspension) suspension.gameObject.SetActive(visible && (!AuthoredObjects || authoredSuspensionActive));
        }
        void FixedUpdate() { touching = false; }
        void LateUpdate() { if (!Released) UpdateChain(); }
        void UpdateChain()
        {
            if(AuthoredObjects) return;
            if (!Chain || !Deck || !TargetBody) return;
            var a = TargetBody.position; var b = Deck.transform.TransformPoint(new Vector3(0, 0, Length * .85f * transform.lossyScale.x));
            Chain.position = (a + b) * .5f; Chain.rotation = Quaternion.FromToRotation(Vector3.up, b - a);
            float scale = Mathf.Max(.0001f, transform.lossyScale.x);
            Chain.localScale = new Vector3(.11f, Vector3.Distance(a, b) / (2 * scale), .11f);
        }
    }
}
