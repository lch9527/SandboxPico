using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    // PICO APK of the sandbox-only project. PicoBuild.Prepare switches on the PICO OpenXR features and generates the
    // portable player; PicoBuild.OnProcessScene swaps it into each scene. The Quest configuration is restored afterwards,
    // even when the build fails. Scenes come from Build Settings (MainMenu, DownhillSandbox).
    // Command line, editor closed:
    //   Unity.exe -batchmode -quit -projectPath . -buildTarget Android -executeMethod PhysicalAssembly.Editor.SandboxPicoBuild.BuildFromCommandLine
    public static class SandboxPicoBuild
    {
        public const string Apk="Builds/SnapCarSandbox-Pico.apk"; // must end in -Pico.apk for PicoBuild's checks

        [MenuItem("MetaHackthone/Pico/Build sandbox PICO APK",false,200)]
        public static void BuildMenu()
        {
            var report=Build();
            if(report.summary.result==BuildResult.Succeeded && File.Exists(Apk)) EditorUtility.RevealInFinder(Path.GetFullPath(Apk));
        }

        public static void BuildFromCommandLine()
        {
            var report=Build();
            if(report.summary.result!=BuildResult.Succeeded) throw new BuildFailedException("Sandbox PICO build failed: "+report.summary.result);
        }

        public static BuildReport Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) throw new InvalidOperationException("Stop Play mode and wait for any current build first.");
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android) throw new InvalidOperationException("Switch the active build target to Android first.");
            var scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray();
            if(scenes.Length==0 || scenes[0]!=MainMenuBuilder.ScenePath) throw new InvalidOperationException("MainMenu must be the first enabled build scene.");
            // PicoBuild generates its player in a scratch scene and refuses while an XR Origin or untitled scene is open.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            PicoBuild.Prepare();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Apk)); if(File.Exists(Apk)) File.Delete(Apk);
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=scenes,target=BuildTarget.Android,
                    targetGroup=BuildTargetGroup.Android,locationPathName=Apk,options=BuildOptions.None});
                var apk=new FileInfo(Apk);
                Debug.Log($"SANDBOX_PICO_BUILD result={report.summary.result} errors={report.summary.totalErrors} warnings={report.summary.totalWarnings} "+
                    $"seconds={report.summary.totalTime.TotalSeconds:F0} id={PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)} "+
                    (apk.Exists?$"apk={apk.FullName} bytes={apk.Length}":"apk=missing"));
                return report;
            }
            finally { PicoBuild.Restore(); AssetDatabase.SaveAssets(); }
        }
    }
}
