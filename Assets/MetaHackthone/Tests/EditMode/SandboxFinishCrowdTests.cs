using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace PhysicalAssembly.Tests
{
    public sealed class SandboxFinishCrowdTests
    {
        readonly List<GameObject> made=new();
        [TearDown] public void Clean() { foreach(var go in made) if(go) Object.DestroyImmediate(go); made.Clear(); }
        Transform Make(string name,Vector3 position)
        {
            var go=new GameObject(name); made.Add(go); go.transform.position=position; return go.transform;
        }
        SandboxFinishCrowd Crowd(int count=10)
        {
            var crowd=Make("Finish Crowd",Vector3.zero).gameObject.AddComponent<SandboxFinishCrowd>(); crowd.Count=count; return crowd;
        }
        List<Transform> Parts(int count,float z)
        {
            var parts=new List<Transform>();
            for(int i=0;i<count;i++) parts.Add(Make("Part "+i,new Vector3(i*.4f-count*.2f,.5f,z)));
            return parts;
        }
        static void Run(SandboxFinishCrowd crowd,float seconds) { for(float t=0;t<seconds;t+=.05f) crowd.Advance(.05f); }
        // Flat ground over the whole area, with a wall people cannot walk through where |x| <= wall.
        static void FlatGround(SandboxFinishCrowd crowd,float wall)
        {
            crowd.GroundGridSize(out int columns,out int rows);
            var heights=new float[columns*rows]; var walkable=new bool[heights.Length];
            for(int r=0;r<rows;r++) for(int c=0;c<columns;c++) walkable[r*columns+c]=Mathf.Abs(crowd.GridCellCentre(c,r,columns,rows).x)>wall;
            crowd.SetGroundGrid(columns,rows,heights,walkable);
        }
        static float Farthest(SandboxFinishCrowd crowd) => Enumerable.Range(0,crowd.Total).Max(i=>Vector3.Distance(crowd.PersonPosition(i),crowd.StandingSpot(i)));

        [Test] public void StandaloneWinCheersWithoutARewardTransfer()
        {
            var crowd=Crowd();crowd.Celebrate();Run(crowd,.5f);
            Assert.AreEqual(SandboxFinishCrowd.CrowdPhase.Cheer,crowd.Phase);
            Assert.AreEqual(10,crowd.Remaining);
            Run(crowd,crowd.CheerSeconds+crowd.ReturnSeconds+.2f);
            Assert.AreEqual(SandboxFinishCrowd.CrowdPhase.Waiting,crowd.Phase);
            crowd.Celebrate();crowd.ResetCrowd();Assert.AreEqual(SandboxFinishCrowd.CrowdPhase.Waiting,crowd.Phase);
        }
        [Test] public void OneHelperLeavesPerPartButTheStayersNeverDo()
        {
            Assert.AreEqual(3,SandboxFinishCrowd.HelperCount(3,10,2));
            Assert.AreEqual(8,SandboxFinishCrowd.HelperCount(12,10,2),"Two people always stay");
            Assert.AreEqual(0,SandboxFinishCrowd.HelperCount(0,10,2));
            Assert.AreEqual(0,SandboxFinishCrowd.HelperCount(5,1,2),"A crowd smaller than the stayers keeps everyone");
        }
        [Test] public void FewerPartsLeaveMorePeopleWhoLiftTheCore()
        {
            var crowd=Crowd();
            var core=Make("Core",new Vector3(0,.5f,4));
            Assert.AreEqual(10,crowd.Remaining);
            crowd.BeginSwarm(Parts(3,4),core,.5f);
            Assert.AreEqual(SandboxFinishCrowd.CrowdPhase.Rush,crowd.Phase);
            Run(crowd,crowd.SwarmSeconds);
            Assert.AreEqual(SandboxFinishCrowd.CrowdPhase.Hold,crowd.Phase,"Everyone waits at the car until its parts dissolve");
            Assert.AreEqual(10,crowd.Remaining);
            crowd.ReleaseParts(.25f);
            Run(crowd,crowd.LiftSeconds);
            Assert.AreEqual(7,crowd.Remaining,"One helper left with each of the three parts");
            Assert.Greater(core.position.y,1.4f,"The people left lift the core above their heads");
            crowd.ReleaseCore();
            Assert.AreEqual(SandboxFinishCrowd.CrowdPhase.Cheer,crowd.Phase);
            Run(crowd,crowd.CheerSeconds+crowd.ReturnSeconds+.2f);
            Assert.AreEqual(SandboxFinishCrowd.CrowdPhase.Waiting,crowd.Phase);
            Assert.AreEqual(7,crowd.Remaining,"Helpers stay away until the next run");
            crowd.ResetCrowd();
            Assert.AreEqual(10,crowd.Remaining);
        }
        [Test] public void MorePartsThanPeopleStillLeavesTwoToCarry()
        {
            var crowd=Crowd(6);
            crowd.BeginSwarm(Parts(15,3),Make("Core",new Vector3(0,.5f,3)));
            Run(crowd,crowd.SwarmSeconds); crowd.ReleaseParts(.25f); Run(crowd,.5f);
            Assert.AreEqual(2,crowd.Remaining);
        }
        [Test] public void ACancelledWinBringsEveryoneBack()
        {
            var crowd=Crowd();
            crowd.BeginSwarm(Parts(5,3),Make("Core",new Vector3(0,.5f,3)));
            Run(crowd,crowd.SwarmSeconds); crowd.ReleaseParts(.25f); Run(crowd,.5f);
            Assert.AreEqual(5,crowd.Remaining);
            crowd.ResetCrowd();
            Assert.AreEqual(10,crowd.Remaining);
            Assert.AreEqual(SandboxFinishCrowd.CrowdPhase.Waiting,crowd.Phase);
        }
        [Test] public void OnlyThePeopleLeftAreDrawn()
        {
            var crowd=Crowd(8);
            var camera=new Vector3(0,3,-6);
            var drawn=crowd.Draw(camera);
            int full=drawn.vertexCount;
            Assert.Greater(full,0);
            Assert.That(drawn.bounds.size.y,Is.GreaterThan(.8f).And.LessThan(2f),"People stand about one unit tall on the marker's ground");
            crowd.BeginSwarm(Parts(4,3),Make("Core",new Vector3(0,.5f,3)));
            Run(crowd,crowd.SwarmSeconds); crowd.ReleaseParts(.25f); Run(crowd,crowd.LiftSeconds); crowd.ReleaseCore();
            Run(crowd,crowd.CheerSeconds+crowd.ReturnSeconds+.2f);   // confetti gone, everyone left is back in place
            Assert.AreEqual(4,crowd.Remaining);
            Assert.Less(crowd.Draw(camera).vertexCount,full,"People who left are no longer drawn");
        }

        [Test] public void IdlePeopleKeepMovingButNeverCrossBlockedGround()
        {
            // Four people spread along a long area with a wall down the middle.
            var crowd=Crowd(4); crowd.Area=new Vector2(8,2); crowd.Liveliness=1;
            FlatGround(crowd,.3f);
            Assert.IsTrue(crowd.HasWalkableGround);
            var homes=Enumerable.Range(0,4).Select(crowd.StandingSpot).ToArray();
            var farthest=new float[4];
            for(int frame=0;frame<60*30;frame++) {     // a minute at 30 frames a second
                crowd.Advance(1/30f);
                for(int i=0;i<4;i++) {
                    var at=crowd.PersonPosition(i);
                    Assert.LessOrEqual(Mathf.Abs(at.x),4f,"Person "+i+" stays in the area");
                    Assert.LessOrEqual(Mathf.Abs(at.z),1f,"Person "+i+" stays in the area");
                    Assert.Greater(Mathf.Abs(at.x),.35f,"Person "+i+" never walks into the wall");
                    Assert.AreEqual(Mathf.Sign(homes[i].x),Mathf.Sign(at.x),"Person "+i+" never crosses the wall");
                    farthest[i]=Mathf.Max(farthest[i],Vector3.Distance(at,homes[i]));
                }
            }
            Assert.GreaterOrEqual(farthest.Count(d=>d>.5f),3,"People keep moving around while you build: "+string.Join(", ",farthest.Select(d=>d.ToString("F2"))));
        }
        [Test] public void WithoutBakedGroundEveryoneStaysPutButStillMoves()
        {
            var crowd=Crowd(6);
            Assert.IsFalse(crowd.HasWalkableGround);
            var camera=new Vector3(0,3,-6);
            Run(crowd,10); var early=crowd.Draw(camera).vertices;
            Run(crowd,3); var later=crowd.Draw(camera).vertices;
            Assert.AreEqual(0,Farthest(crowd),1e-4f,"Without walkable ground nobody leaves their place");
            CollectionAssert.AreNotEqual(early,later,"Poses keep changing");
        }
        [Test] public void StartingARunCallsEveryoneBackAndAFailureEndsInIdleTime()
        {
            var crowd=Crowd(6); crowd.Area=new Vector2(6,2.4f); crowd.Liveliness=1;
            FlatGround(crowd,-1);
            Run(crowd,20);
            Assert.Greater(Farthest(crowd),.3f,"Someone wandered off while you built");
            crowd.SetMood(SandboxFinishCrowd.CrowdMood.Ready);
            Run(crowd,6);
            Assert.Less(Farthest(crowd),.01f,"Everyone is back at their place to watch the run");
            crowd.SetMood(SandboxFinishCrowd.CrowdMood.Watch); Run(crowd,2);
            Assert.Less(Farthest(crowd),.01f,"Nobody wanders off during the run");
            crowd.SetMood(SandboxFinishCrowd.CrowdMood.Groan); Run(crowd,3.5f);
            Assert.AreEqual(SandboxFinishCrowd.CrowdMood.Idle,crowd.Mood,"The groan passes");
            Run(crowd,20);
            Assert.Greater(Farthest(crowd),.3f,"Idle time starts again");
        }
        [Test] public void TheSwarmStartsFromWhereverEveryoneIs()
        {
            var crowd=Crowd(); crowd.Area=new Vector2(6,2.4f); crowd.Liveliness=1;
            FlatGround(crowd,-1);
            Run(crowd,15);
            var before=Enumerable.Range(0,crowd.Total).Select(crowd.PersonPosition).ToArray();
            crowd.BeginSwarm(Parts(3,4),Make("Core",new Vector3(0,.5f,4)),.5f);
            for(int i=0;i<before.Length;i++) Assert.Less(Vector3.Distance(before[i],crowd.PersonPosition(i)),1e-4f,"Person "+i+" does not jump back to their place");
        }
    }
}
