using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace FriendSlop.EditorTools
{
    public static class GameplayGoalBuild
    {
        public static void Build()
        {
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/_Project/Scenes/Game.unity" },
                "Builds/GoalValidation/FriendSlop.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Goal QA build failed: " + report.summary.result);
        }

        public static void BuildForItch()
        {
            string output = "Builds/CacapaRussa_" + PlayerSettings.bundleVersion + "/CacapaRussa.exe";
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/_Project/Scenes/Game.unity" },
                output, BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("Windows release build failed: " + report.summary.result);
            UnityEngine.Debug.Log("[Release] Caçapa Russa " + PlayerSettings.bundleVersion + " Windows x64: " + output);
        }
    }
}
