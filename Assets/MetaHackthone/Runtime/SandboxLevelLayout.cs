using System;
using UnityEngine;

namespace PhysicalAssembly
{
    // Serialized authoring ownership; the controller binds one layout at a time.
    [DisallowMultipleComponent]
    public sealed class SandboxLevelLayout : MonoBehaviour
    {
        public SandboxCourseId CourseId;
        public Transform BuildingRoot, SandboxRoot;
        public DownhillCourse Course;
        public BuildingPartSpawn[] Spawns = Array.Empty<BuildingPartSpawn>();
        public Transform Menu;
        public Collider Surface;
        public GameObject BuildStand;
        public TextMesh ModeLabel, StatusLabel, CommandLabel, ScoreLabel;
        public DownhillPresentation Presentation => Course.GetComponent<DownhillPresentation>();
        [Tooltip("Optional. Where the player stands and looks when the level starts. Keep it under Building Area so it follows; empty uses a spot in front of the Building Area.")]
        public Transform PlayerStart;
        public const string PlayerStartName="Player Start";
        public const float DefaultPlayerDistance=.65f; // matches DownhillSettings.PlayerDistance
        public static Quaternion Yaw(Quaternion rotation) { var forward=Vector3.ProjectOnPlane(rotation*Vector3.forward,Vector3.up); return forward.sqrMagnitude>.0001f?Quaternion.LookRotation(forward):Quaternion.identity; }
        // World-space floor point and facing of the player's start. Always follows the Building Area.
        public Pose PlayerStartPose(float distance)
        {
            if(PlayerStart) return new Pose(PlayerStart.position,Yaw(PlayerStart.rotation));
            var facing=Yaw(BuildingRoot.rotation);
            return new Pose(BuildingRoot.position-facing*Vector3.forward*distance+Vector3.up*DefaultEyeHeight,facing);
        }
        // Default camera height above the Building Area origin for a new Player Start (standing eye level).
        public const float DefaultEyeHeight=1.6f;
        // Metres the player's eyes (headset camera) sit above (+) or below (-) the Building Area's origin.
        // The Player Start lands on the headset camera, so raising it lowers the level around the player.
        public float PlayerStartHeight => PlayerStart && BuildingRoot?PlayerStart.position.y-BuildingRoot.position.y:0;
        [Tooltip("Optional. Where the centre of a newly unlocked part rises to. Keep it under Building Area so it follows; empty uses a spot above the assembly spot.")]
        public Transform RewardRevealPoint;
        public const string RewardRevealPointName="Reward Reveal Point";
        [Tooltip("Metres above the assembly spot that a newly unlocked part rises to when no Reward Reveal Point is assigned.")]
        public float RewardRevealHeight=.4f;
        // World-space centre and facing of a newly unlocked part once it has risen.
        public Pose RewardRevealPose()
        {
            if(RewardRevealPoint) return new Pose(RewardRevealPoint.position,Yaw(RewardRevealPoint.rotation));
            var presentation=Course?Presentation:null;
            var anchor=presentation && presentation.AssemblyAnchor?presentation.AssemblyAnchor:BuildingRoot;
            return new Pose(anchor.position+Vector3.up*RewardRevealHeight,Yaw(anchor.rotation));
        }
        public float BuildingScale => BuildingRoot.localScale.x;
        public float SandboxScale => SandboxRoot.localScale.x;

        public void Bind(BuildingSceneLayout owner)
        {
            owner.Downhill=Course; owner.Spawns=Spawns; owner.Menu=Menu; owner.Surface=Surface;
            owner.BuildStand=BuildStand; owner.ModeLabel=ModeLabel; owner.StatusLabel=StatusLabel;
            owner.CommandLabel=CommandLabel; owner.ScoreLabel=ScoreLabel;
        }
        public bool Validate(out string error)
        {
            error="";
            if(!Course || !BuildingRoot || !SandboxRoot) { error="Assign both layout groups and their course."; return false; }
            foreach(var root in new[]{BuildingRoot,SandboxRoot}) {
                var scale=root.lossyScale;
                if(!float.IsFinite(scale.x) || !float.IsFinite(scale.y) || !float.IsFinite(scale.z) ||
                    scale.x<=0 || scale.y<=0 || scale.z<=0 || Mathf.Abs(scale.x-scale.y)>.0001f || Mathf.Abs(scale.x-scale.z)>.0001f ||
                    root.localScale.x<=0 || root.localScale.y<=0 || root.localScale.z<=0) {
                    error=root.name+": use a positive uniform scale."; return false;
                }
                if(Vector3.Dot(root.up,Vector3.up)<.9999f) { error=root.name+": rotate around the vertical (Y) axis only."; return false; }
            }
            return true;
        }
    }
}
