using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TrustIssues.EditorTools
{
    /// <summary>
    /// One-shot WebGL build, runnable from the menu or headless:
    ///   Unity.exe -batchmode -nographics -quit -projectPath . ^
    ///     -executeMethod TrustIssues.EditorTools.BuildWebGL.Build -logFile Logs\build.log
    /// Output lands in Builds/WebGL (index.html at the root — serve that folder).
    /// </summary>
    public static class BuildWebGL
    {
        [MenuItem("Trust Issues/Build WebGL")]
        public static void Build()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/scene.unity" },
                target = BuildTarget.WebGL,
                locationPathName = "Builds/WebGL",
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"WebGL build: {report.summary.result}, " +
                      $"{report.summary.totalSize / (1024 * 1024)} MB -> {options.locationPathName}");
            if (report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);   // headless callers see the failure
        }
    }
}
