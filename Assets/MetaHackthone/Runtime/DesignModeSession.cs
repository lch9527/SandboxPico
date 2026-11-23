using UnityEngine;

namespace PhysicalAssembly
{
    // IDs are serialized in authored scenes; display order is independent.
    public enum DesignLevelId { GravityGarage=0, WaterfallValley=1, BeautifulCorner=2, FactoryFinalRun=3, DrillDropBridge=4 }
    public static class DesignLevelCatalog
    {
        public const string Level1Title="Level 1 - Beautiful Corner";
        public const string Level2Title="Level 2 - Crab Bridge";
        public const string Level3Title="Level 3 - Wheel Size Challenge";
        public const string Level4Title="Level 5 - Factory Final Run";
        public static readonly System.Collections.Generic.IReadOnlyList<DesignLevelId> Levels=
            System.Array.AsReadOnly(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(SandboxCourseCatalog.Courses,c=>(DesignLevelId)c)));
        public static string Title(DesignLevelId level) => "Level "+Number(level)+" - "+SandboxCourseCatalog.Title((SandboxCourseId)level);
        public static int Number(DesignLevelId level) => SandboxCourseCatalog.Number((SandboxCourseId)level);
        public static bool UsesFactory(DesignLevelId level) => IsValid(level);
        public static bool IsValid(DesignLevelId level) => System.Linq.Enumerable.Contains(Levels,level);
        public static bool PartAvailable(DesignLevelId level,PartKind kind) => SandboxCourseCatalog.PartAvailable((SandboxCourseId)level,kind);
    }
    // Play-session state only. Player builds always use the normal game flow.
    public static class DesignModeSession
    {
        public static bool Enabled { get; private set; }
        public static DesignLevelId SelectedLevel { get; private set; }
        public static void SelectLevel(DesignLevelId level) { if(!DesignLevelCatalog.IsValid(level)) throw new System.ArgumentOutOfRangeException(nameof(level)); SelectedLevel=Enabled?level:DesignLevelId.GravityGarage; }
#if UNITY_EDITOR
        const string LaunchKey="MetaHackthone.DesignMode.Launch";
        const string LevelKey="MetaHackthone.DesignMode.SelectedLevel";
        const string TrackEditKey="MetaHackthone.DesignMode.EditLevel2Track";
        public static bool HasQueuedTrackEdit => UnityEditor.SessionState.GetBool(TrackEditKey,false);
        public static bool CanEditTrack(DownhillController level) => Application.isPlaying && Enabled && level &&
            level.Course3D && level.Course3D.BezierTrack && level.Course3D.gameObject.activeInHierarchy;
        public static bool RequestTrackEdit(DownhillController level)
        {
            if(!CanEditTrack(level)) return false;
            // Persist only the navigation request across domain reload, never a Play-mode curve copy.
            UnityEditor.SessionState.SetBool(TrackEditKey,true);
            UnityEditor.EditorApplication.isPlaying=false;
            return true;
        }
        public static void ClearTrackEditRequest() => UnityEditor.SessionState.EraseBool(TrackEditKey);
        public static bool HasQueuedEditorLaunch => UnityEditor.SessionState.GetBool(LaunchKey,false);
        public static void QueueEditorLaunch(DesignLevelId level=DesignLevelId.BeautifulCorner)
        {
            UnityEditor.SessionState.SetBool(LaunchKey,true);
            UnityEditor.SessionState.SetInt(LevelKey,(int)level);
        }
        public static void CancelEditorLaunch() { UnityEditor.SessionState.EraseBool(LaunchKey); UnityEditor.SessionState.EraseInt(LevelKey); }
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Enabled=false;
            SelectedLevel=DesignLevelId.GravityGarage;
#if UNITY_EDITOR
            Enabled=UnityEditor.SessionState.GetBool(LaunchKey,false);
            SelectLevel((DesignLevelId)UnityEditor.SessionState.GetInt(LevelKey,0));
            CancelEditorLaunch();
#endif
        }
        public static void SetEnabled(bool enabled)
        {
            SelectedLevel=DesignLevelId.GravityGarage;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Enabled=enabled;
            if(enabled) SelectedLevel=DesignLevelId.BeautifulCorner;
#else
            Enabled=false;
#endif
        }
    }
}
