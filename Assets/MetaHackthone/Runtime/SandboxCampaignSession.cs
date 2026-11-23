using System.Collections.Generic;
using UnityEngine;
namespace PhysicalAssembly
{
    // Serialized IDs stay stable. Display numbers come from SandboxCourseCatalog.
    public enum SandboxCourseId { GravityGarage=0, TeleportPipes=1, BeautifulCorner=2, FactoryLevel4=3, DrillDropBridge=4 }
    public static class SandboxCourseCatalog
    {
        public static readonly IReadOnlyList<SandboxCourseId> Courses=System.Array.AsReadOnly(new[]{
            SandboxCourseId.BeautifulCorner,SandboxCourseId.TeleportPipes,SandboxCourseId.GravityGarage,SandboxCourseId.DrillDropBridge,SandboxCourseId.FactoryLevel4});
        // Parts unlock with the course that teaches them, wherever that course sits in the order.
        // Finishing Level 1 gives the long blocks (used from Crab Bridge, Level 2); finishing Level 2 gives the
        // big wheels (used from Wheel Size Challenge, Level 3).
        public const SandboxCourseId LongBlockCourse=SandboxCourseId.TeleportPipes, LargeWheelCourse=SandboxCourseId.GravityGarage, DrillCourse=SandboxCourseId.DrillDropBridge;
        public static bool Reached(SandboxCourseId course,SandboxCourseId unlock) => Number(course)>=Number(unlock);
        public static int Number(SandboxCourseId course) { for(int i=0;i<Courses.Count;i++) if(Courses[i]==course) return i+1; throw new System.ArgumentOutOfRangeException(nameof(course)); }
        public static string Title(SandboxCourseId course) => course switch {
            SandboxCourseId.BeautifulCorner=>"Beautiful Corner",SandboxCourseId.GravityGarage=>"Wheel Size Challenge",
            SandboxCourseId.TeleportPipes=>"Crab Bridge",SandboxCourseId.DrillDropBridge=>"Drill Drop Bridge",SandboxCourseId.FactoryLevel4=>"Factory Final Run",
            _=>throw new System.ArgumentOutOfRangeException(nameof(course)) };
        public static bool TryNext(SandboxCourseId course,out SandboxCourseId next) {
            int index=Number(course); next=index<Courses.Count?Courses[index]:course; return index<Courses.Count;
        }
        public static bool PartAvailable(SandboxCourseId course,PartKind kind) =>
            course==SandboxCourseId.FactoryLevel4 || (kind!=PartKind.DownhillCube &&
            (kind!=PartKind.DownhillLargeWheel || Reached(course,LargeWheelCourse)) &&
            (kind!=PartKind.DownhillDrill || course==SandboxCourseId.DrillDropBridge));
    }
    public static class SandboxCampaignSession
    {
        static readonly HashSet<SandboxCourseId> completed=new();
        public static SandboxCourseId Current { get; private set; }=SandboxCourseId.BeautifulCorner;
        // Legacy API names refer to course identity, never the displayed number.
        public static bool FirstComplete => IsComplete(SandboxCourseId.GravityGarage);
        public static bool SecondComplete => IsComplete(SandboxCourseId.TeleportPipes);
        public static bool DrillClaimed { get; private set; }
        public static bool AllComplete => IsComplete(SandboxCourseId.FactoryLevel4);
        public static bool IsComplete(SandboxCourseId course) => completed.Contains(course);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() { Current=SandboxCourseId.BeautifulCorner; completed.Clear(); DrillClaimed=false; }
        public static void Complete(SandboxCourseId course,bool hasLevel4=false) { if(course==Current) completed.Add(course); }
        public static void Advance(bool hasLevel4=false) {
            if(!IsComplete(Current) || !SandboxCourseCatalog.TryNext(Current,out var next)) return;
            if(next==SandboxCourseId.FactoryLevel4 && !hasLevel4) return;
            if(next==SandboxCourseCatalog.DrillCourse) DrillClaimed=true;
            Current=next;
        }
    }
}
