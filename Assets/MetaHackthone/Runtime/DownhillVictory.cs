using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicalAssembly
{
    public sealed partial class DownhillController
    {
        public float VictoryDecomposeSeconds=.25f, VictoryTravelSeconds=.9f, VictoryReconstructSeconds=.35f;
        public float VictoryPauseSeconds=1.5f;
        public bool VictoryInProgress { get; private set; }
        public SandboxRewardCoordinator Reward { get; private set; }
        public IReadOnlyDictionary<AssemblyPart,AssemblyPart> SimulationOriginals => simulationOriginals;
        readonly Dictionary<AssemblyPart,AssemblyPart> simulationOriginals=new();
        readonly List<SandboxReturnEffect> victoryEffects=new();
        readonly List<AssemblyPart> winningParts=new();
        bool rewardsSuppressed;
        bool preserveVictoryConstruction;
        readonly HashSet<SandboxReturnEffect> victoryDone=new();
        SandboxFinishCrowd victoryCrowd;
        // The mini people at this course's finish, when the level has them.
        public SandboxFinishCrowd FinishCrowd { get {
            var root=ActiveLayout?ActiveLayout.transform:FactoryLevel?FactoryLevel.transform:Course?Course.transform:null;
            var crowd=root?root.GetComponentInChildren<SandboxFinishCrowd>():null;
            return crowd && crowd.isActiveAndEnabled && crowd.Count>0?crowd:null;
        } }
        // The reward for a course is whatever the next course unlocks.
        SandboxCourseId RewardCourse => SandboxCourseCatalog.TryNext(CurrentCourse,out var next)?next:CurrentCourse;
        public bool HasReward => RewardCourse!=CurrentCourse && (RewardCourse==SandboxCourseCatalog.LargeWheelCourse || RewardCourse==SandboxCourseCatalog.LongBlockCourse || RewardCourse==SandboxCourseCatalog.DrillCourse);
        // Every part the next course unlocks, shown together in the reward reveal (side by side).
        public IReadOnlyList<PartKind> RewardKinds { get {
            var kinds=new List<PartKind>();
            if(RewardCourse==SandboxCourseCatalog.LargeWheelCourse) kinds.Add(PartKind.DownhillLargeWheel);
            if(RewardCourse==SandboxCourseCatalog.LongBlockCourse) kinds.Add(PartKind.DownhillLongBlock);
            if(kinds.Count==0) kinds.Add(PartKind.DownhillDrill);
            return kinds;
        } }
        public PartKind RewardKind => RewardCourse==SandboxCourseCatalog.LargeWheelCourse?PartKind.DownhillLargeWheel:RewardCourse==SandboxCourseCatalog.LongBlockCourse?PartKind.DownhillLongBlock:PartKind.DownhillDrill;
        public string RewardLabel => RewardCourse==SandboxCourseCatalog.DrillCourse?"Drill":
            (RewardCourse==SandboxCourseCatalog.LargeWheelCourse?"Big wheels x2":"")+(RewardCourse==SandboxCourseCatalog.LargeWheelCourse && RewardCourse==SandboxCourseCatalog.LongBlockCourse?" and ":"")+(RewardCourse==SandboxCourseCatalog.LongBlockCourse?"long blocks (3 total)":"");
        string VictoryMessage => $"LEVEL {SandboxCourseCatalog.Number(CurrentCourse)} COMPLETE! "+(CurrentCourse==SandboxCourseId.FactoryLevel4?"ALL LEVELS COMPLETE!":HasReward?RewardLabel+" unlocked. Select Next Level on the left panel.":"Select Next Level on the left panel.");

        void BeginVictoryReturn()
        {
            preserveVictoryConstruction=Settings.Campaign && CurrentCourse!=SandboxCourseId.FactoryLevel4;
            VictoryInProgress=true; State=DownhillState.Transition;
            RestoreClock(); FreezeSimulation(); CompleteBoundaryReturns();
            Workshop.World.EditingEnabled=false;
            winningParts.Clear(); winningParts.AddRange(simulationOriginals.Values);
            // The crowd swarms the stopped car: one helper per part, the rest gather round the core.
            victoryCrowd=SimulationCore && SimulationCore.MainBody?FinishCrowd:null;
            if(victoryCrowd) {
                float half=0;
                foreach(var renderer in SimulationCore.VisualRenderers) if(renderer) half=Mathf.Max(half,renderer.bounds.extents.y);
                victoryCrowd.BeginSwarm(simulationOriginals.Keys.Where(p=>p && p!=SimulationCore && p.MainBody).Select(p=>p.MainBody.transform).ToList(),
                    SimulationCore.MainBody.transform,half);
            }
            transition=StartCoroutine(VictoryReturn());
        }
        // later: a part whose return effect is created afterwards (the core, while the crowd lifts it).
        void CreateVictoryEffects(AssemblyPart later=null)
        {
            if(preserveVictoryConstruction) Restore(saved);
            else {
            var ids=new HashSet<string>(winningParts.Select(p=>p.InstanceId));
            foreach(var edge in Workshop.World.Connections.Where(e=>ids.Contains(e.a.partId)||ids.Contains(e.b.partId)).ToArray())
                Workshop.World.RemoveConnection(edge.id);
            // Original bodies are still inactive; place each body, including articulated parts,
            // before taking destination visual poses. Unused palette parts retain saved poses.
            foreach(var part in winningParts) Restore(spawns[part]);
            saved.RemoveAll(p=>winningParts.Contains(p.Body.Part));
            saved.AddRange(winningParts.SelectMany(p=>spawns[p]));
            }
            foreach(var pair in simulationOriginals) if(pair.Key!=later) CreateVictoryEffect(pair.Key,pair.Value);
        }
        void CreateVictoryEffect(AssemblyPart simulated,AssemblyPart original)
        {
            var effect=new GameObject("Victory return "+original.name).AddComponent<SandboxReturnEffect>();
            effect.transform.SetParent(transform,false);
            var pose=preserveVictoryConstruction?saved.First(p=>p.Body==original.MainBody):spawns[original].First(p=>p.Body==original.MainBody);
            effect.Initialize(simulated,original,Course.WorkbenchFrame,pose.Position,pose.Rotation,
                VictoryDecomposeSeconds,VictoryTravelSeconds,VictoryReconstructSeconds,
                Mathf.Max(1,SandboxFragments.Budget/Mathf.Max(1,winningParts.Count)));
            victoryEffects.Add(effect);
        }
        // True once every return effect has finished; finished effects are left alone.
        bool AdvanceVictoryEffects(float dt)
        {
            bool complete=true;
            foreach(var effect in victoryEffects) {
                if(!effect || victoryDone.Contains(effect)) continue;
                if(effect.Advance(dt)) victoryDone.Add(effect); else complete=false;
            }
            return complete;
        }
        IEnumerator VictoryReturn()
        {
            // With a crowd the car rests long enough for everyone to reach it.
            float pause=0, rest=victoryCrowd?Mathf.Max(VictoryPauseSeconds,victoryCrowd.SwarmSeconds):VictoryPauseSeconds;
            while(pause<rest){yield return null;pause+=Time.unscaledDeltaTime;}
            var carried=victoryCrowd?SimulationCore:null;
            if(carried) {
                // The held parts dissolve first, taking their helpers along; the core follows once it has been lifted.
                CreateVictoryEffects(carried);
                victoryCrowd.ReleaseParts(VictoryDecomposeSeconds);
                float lift=0;
                while(lift<victoryCrowd.LiftSeconds) { yield return null; lift+=Time.unscaledDeltaTime; AdvanceVictoryEffects(Time.unscaledDeltaTime); }
                victoryCrowd.ReleaseCore();
            }
            if(Feedback && SimulationCore && Core) Feedback.BeginTransfer(SimulationCore.MainBody.transform,Core.MainBody.transform,VictoryDecomposeSeconds,VictoryTravelSeconds);
            if(carried) CreateVictoryEffect(carried,simulationOriginals[carried]); else CreateVictoryEffects();
            var courseStart=Course.SandboxFrame.localPosition;
            float age=0, total=VictoryDecomposeSeconds+VictoryTravelSeconds+VictoryReconstructSeconds;
            bool complete=false;
            while(!complete) {
                yield return null;
                age+=Time.unscaledDeltaTime; complete=AdvanceVictoryEffects(Time.unscaledDeltaTime);
                Presentation.SetCoursePosition(Vector3.Lerp(courseStart,Presentation.BuildPosition,Mathf.SmoothStep(0,1,age/Mathf.Max(.01f,total))));
            }
            transition=null;
            FinishVictoryReturn(true);
        }
        void FinishVictoryReturn(bool completed)
        {
            if(!VictoryInProgress) return;
            VictoryInProgress=false;
            // A cancelled win sends everybody back to their places.
            if(victoryCrowd && !completed) victoryCrowd.ResetCrowd();
            victoryCrowd=null; victoryDone.Clear();
            foreach(var effect in victoryEffects) if(effect) { effect.Restore(); Destroy(effect.gameObject); }
            victoryEffects.Clear();
            if(preserveVictoryConstruction) Restore(saved);
            else foreach(var part in winningParts) if(part) Restore(spawns[part]);
            winningParts.Clear();
            if(!completed) return;
            RestoreBuildImmediately();
            if(Feedback && Core) Feedback.Emit(SandboxSoundCue.Arrive,Core.MainBody.transform.position,Core);
            Workshop.Session.Notify(VictoryMessage);
            EnsureReward();
        }
        void EnsureReward()
        {
            if(rewardsSuppressed || !Course.SandboxPolish || !Settings.Campaign || DesignModeSession.Enabled || !IsBuilding ||
                !HasReward || !SandboxCampaignSession.IsComplete(CurrentCourse)) return;
            if(!Reward) { Reward=gameObject.AddComponent<SandboxRewardCoordinator>(); Reward.Initialize(this); }
            Reward.Reveal();
        }
        public Transform RewardGoal => FactoryLevel?FactoryLevel.Finish.transform:GarageCourse && GarageCourse.Markers && GarageCourse.Markers.Finish?
            GarageCourse.Markers.Finish:Course.FinishArea?Course.FinishArea.transform:Course.SandboxFrame;
    }
}
