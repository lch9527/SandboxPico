using System.Linq;
using System.Collections.Generic;
using UnityEngine;
namespace PhysicalAssembly
{
    public sealed partial class DownhillController
    {
        GravityGarageLevelAsset pipeCourse;
        GravityGarage3DLevelAsset valleyCourse;
        Texture2D firstCoursePreview;
        float authoredWorkbenchApproach;
        BuildingPartSpawn[] starterSpawns;
        readonly Dictionary<Renderer,bool> workbenchRenderers=new();
        Transform factoryWorkbench;
        public SandboxCourseId CurrentCourse { get; private set; }
        bool HasNextCourse => SandboxCampaignSession.IsComplete(CurrentCourse) &&
            SandboxCourseCatalog.TryNext(CurrentCourse,out var next) && CourseAvailable(next);
        bool CourseAvailable(SandboxCourseId course) => Settings.FactoryCourse(course);
        public bool CanAdvanceLevel => Settings.Campaign && !DesignModeSession.Enabled && HasNextCourse &&
            (IsBuilding || State==DownhillState.Success) && CanSwitchMode && !Rotating;
        void InitializeCampaign()
        {
            pipeCourse=Settings.Level25D; valleyCourse=Settings.Level3D;
            authoredWorkbenchApproach=Settings.WorkbenchApproach;
            starterSpawns=Workshop.Layout.Spawns;
            CurrentCourse=Settings.Campaign?SandboxCampaignSession.Current:DesignModeSession.Enabled?
                (SandboxCourseId)DesignModeSession.SelectedLevel:((pipeCourse || valleyCourse)?SandboxCourseId.TeleportPipes:SandboxCourseId.GravityGarage);
            if(Workshop.Layout.GetComponent<SandboxFactoryLevel>() is SandboxFactoryLevel direct && direct.Layout) CurrentCourse=direct.Layout.CourseId;
            if(CurrentCourse!=SandboxCourseId.TeleportPipes) { Settings.Level25D=null; Settings.Level3D=null; }
        }
        void RefreshCampaignCard()
        {
            if((!Settings.Campaign && !DesignModeSession.Enabled) || !Presentation.Interface) return;
            if(!Settings.Campaign && FactoryLevel && !DesignLevelCatalog.UsesFactory(DesignModeSession.SelectedLevel)) return;
            var ui=Presentation.Interface;
            if(!firstCoursePreview) firstCoursePreview=ui.CoursePreview;
            ui.CourseNumber=SandboxCourseCatalog.Number(CurrentCourse); ui.CourseCount=SandboxCourseCatalog.Courses.Count;
            ui.CourseTitle=SandboxCourseCatalog.Title(CurrentCourse);
            ui.CoursePreview=FactoryLevel?null:CurrentCourse==SandboxCourseId.GravityGarage?firstCoursePreview:valleyCourse?valleyCourse.Thumbnail:pipeCourse?pipeCourse.thumbnail:null;
            ui.RefreshCourseCard();
        }
        public bool TryAdvanceLevel()
        {
            if(!CanAdvanceLevel || !SandboxCourseCatalog.TryNext(CurrentCourse,out var next)) return false;
            if(Settings.FactoryCourse(next)) return LoadFactoryCourse(next,true);
            var binding=FindLayout(next);
            if(binding && !binding.Validate(out var layoutError)) { Workshop.Session.Notify(layoutError); return false; }
            CompleteBoundaryReturns();  RestoreBuildImmediately();
            var origin=Presentation.AssemblyAnchor.position;
            RemoveFactoryCourse();
            Settings.WorkbenchApproach=authoredWorkbenchApproach;
            Settings.Level25D=next==SandboxCourseId.TeleportPipes?pipeCourse:null;
            Settings.Level3D=next==SandboxCourseId.TeleportPipes?valleyCourse:null;
            CurrentCourse=next; SandboxCampaignSession.Advance(Settings.Level4);
            if(binding) ActivateLayout(binding,true);
            RebuildGarageCourse(); Presentation.Configure();
            var shift=Presentation.AssemblyAnchor.position-origin;
            if(!binding) TranslateDesignerKit(shift);
            if(!binding && Course.BuildArea) Course.BuildArea.transform.position+=shift;
            if(!binding) foreach(var records in spawns.Values) for(int i=0;i<records.Length;i++)
                records[i].Position+=Course.WorkbenchFrame.InverseTransformVector(shift);
            RefreshDesignerKitAvailability();
            if(Reward) Reward.Claim(); // advancing always takes the pending unlock, as the factory path does
            TopologyChanged(); SpawnPreview.Refresh(); UpdateControls();
            Workshop.Session.Notify("LEVEL "+SandboxCourseCatalog.Number(next)+": "+SandboxCourseCatalog.Title(next)+"\nAdjust your construction, then press START.");
            var desktop=Workshop.GetComponent<DesktopInputAdapter>(); if(desktop && desktop.IsDesktop) desktop.FramePlatform();
            return true;
        }
        public bool TryLoadInitialFactory()
        {
            if(FactoryLevel) { ApplyFactoryWorkbench(); return true; }
            if(!Settings.FactoryCourse(CurrentCourse)) return false;
            return LoadFactoryCourse(CurrentCourse,false);
        }
        public bool TryLoadDesignFactory() => DesignModeSession.Enabled && !Settings.Campaign && TryLoadInitialFactory();
        bool LoadFactoryCourse(SandboxCourseId course,bool advanceCampaign)
        {
            if(!IsBuilding) return false;
            var prefab=Settings.FactoryCourse(course);
            if(!prefab) { Workshop.Session.Notify("Save this course to Game from its authoring scene first."); return false; }
            if(!prefab.Validate(out var error) || prefab.CampaignSpawns.Count(s=>s && s.Kind==PartKind.DownhillCore)!=1) {
                Workshop.Session.Notify("This course needs to be saved from the factory again. "+error); return false;
            }
            CompleteBoundaryReturns();  RestoreBuildImmediately();
            // Palette selection controls a fresh entry, not whether an advancing player's car survives.
            bool preserveConstruction=kit.Count>0 && (advanceCampaign || prefab.UseStarterPalette);
            var previousFactory=campaignFactory;
            if(Reward) { Reward.Claim(); Destroy(Reward); Reward=null; }
            campaignFactory=Instantiate(prefab,Workshop.Layout.transform,false);
            campaignFactory.name="Level "+SandboxCourseCatalog.Number(course)+" - "+SandboxCourseCatalog.Title(course);
            var authored=Workshop.Layout.GetComponent<AuthoredSandboxLayout>();
            if(authored) { authored.Level1.gameObject.SetActive(false); authored.Level2.gameObject.SetActive(false); }
            else {
                if(GarageCourse) GarageCourse.gameObject.SetActive(false);
                if(Course25D) Course25D.gameObject.SetActive(false);
                if(Course3D) Course3D.gameObject.SetActive(false);
            }
            Settings.Level25D=null; Settings.Level3D=null; Settings.WorkbenchApproach=0;
            if(advanceCampaign) SandboxCampaignSession.Advance(true);
            CurrentCourse=course;
            if(campaignFactory.Layout) {
                ActivateLayout(campaignFactory.Layout,preserveConstruction);
            } else if(!campaignFactory.UseStarterPalette) {
                foreach(var spawn in Workshop.Layout.Spawns) if(spawn) spawn.gameObject.SetActive(false);
                Workshop.Layout.Spawns=campaignFactory.CampaignSpawns;
            } else {
                foreach(var spawn in campaignFactory.CampaignSpawns) if(spawn) spawn.gameObject.SetActive(false);
            }
            if(previousFactory) { previousFactory.gameObject.SetActive(false); Destroy(previousFactory.gameObject); }
            Workshop.Layout.HidePreviews(); Presentation.Configure(); ApplyFactoryWorkbench();
            if(!preserveConstruction) { ClearGarageKit(); SpawnDesignerKit(); }
            else {
                EnsureCampaignParts(); RefreshDesignerKitAvailability();
                // Newly unlocked parts must enter the same floating build state as the retained kit.
                TopologyChanged(); RebuildGarageCourse();
            }
            Workshop.RealignBuildingArea();
            BuildStarterCar();
            Workshop.Session.Notify(campaignFactory.Title+"\nBuild your car, then press START.");
            var desktop=Workshop.GetComponent<DesktopInputAdapter>(); if(desktop && desktop.IsDesktop) desktop.FramePlatform();
            return true;
        }
        void ApplyFactoryWorkbench()
        {
            if(factoryWorkbench || !FactoryLevel || !FactoryLevel.WorkbenchVisuals) return;
            if(FactoryLevel.Layout) {
                foreach(var renderer in Presentation.WorkbenchVisuals.GetComponentsInChildren<Renderer>(true)) {
                    if(renderer.GetComponentInParent<WorkshopButton>() || renderer.transform.IsChildOf(Course.RotationWheel.transform)) continue;
                    renderer.enabled=false;
                }
                return;
            }
            factoryWorkbench=FactoryLevel.WorkbenchVisuals;
            // Workbench never follows SandboxFrame's launch animation or build/test visibility.
            factoryWorkbench.SetParent(Course.WorkbenchFrame,true);
            foreach(var renderer in Presentation.WorkbenchVisuals.GetComponentsInChildren<Renderer>(true)) {
                if(renderer.GetComponentInParent<WorkshopButton>() || renderer.transform.IsChildOf(Course.RotationWheel.transform)) continue;
                workbenchRenderers[renderer]=renderer.enabled; renderer.enabled=false;
            }
        }
        void RemoveFactoryCourse()
        {
            foreach(var pair in workbenchRenderers) if(pair.Key) pair.Key.enabled=pair.Value;
            workbenchRenderers.Clear();
            if(factoryWorkbench) { factoryWorkbench.gameObject.SetActive(false); Destroy(factoryWorkbench.gameObject); factoryWorkbench=null; }
            if(campaignFactory) { campaignFactory.gameObject.SetActive(false); Destroy(campaignFactory.gameObject); campaignFactory=null; }
            Workshop.Layout.Spawns=starterSpawns;
            foreach(var spawn in starterSpawns) if(spawn) spawn.gameObject.SetActive(true);
        }
    }
}
