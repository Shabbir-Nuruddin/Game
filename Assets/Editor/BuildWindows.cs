using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TrustIssues.EditorTools
{
    /// <summary>
    /// A throwaway Windows player, built for ONE reason: to look at the game.
    ///
    /// Every screen in Trust Issues is built in code, so there is no scene view to
    /// judge and no way to check "does the castle actually look like the artwork?"
    /// without running it. This target is Mono (not IL2CPP), so it builds in a
    /// couple of minutes instead of twenty, and pairs with ShotBot: launch the
    /// player with -shot &lt;folder&gt; and it screenshots the real running game and
    /// quits. Never shipped — the store builds are BuildAndroid / BuildWebGL.
    ///
    ///   Unity.exe -batchmode -nographics -quit -projectPath . ^
    ///     -executeMethod TrustIssues.EditorTools.BuildWindows.Build -logFile Logs\win.log
    /// </summary>
    public static class BuildWindows
    {
        [MenuItem("Trust Issues/Build Windows (dev)")]
        public static void Build()
        {
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1568;    // the gameplay artwork's own size,
            PlayerSettings.defaultScreenHeight = 1003;   // so a shot lines up with the reference
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;       // an unfocused window must keep rendering

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/scene.unity" },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = "Builds/Win/TrustIssues.exe",
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"Windows build: {report.summary.result}, " +
                      $"{report.summary.totalSize / (1024 * 1024)} MB -> {options.locationPathName}");
            if (report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
