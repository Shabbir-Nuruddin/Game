using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TrustIssues.EditorTools
{
    /// <summary>
    /// One-shot Android APK build, runnable from the menu or headless:
    ///   Unity.exe -batchmode -nographics -quit -projectPath . ^
    ///     -executeMethod TrustIssues.EditorTools.BuildAndroid.Build -logFile Logs\apk.log
    /// Output: Builds/TrustIssues.apk — sideload it straight onto a phone.
    ///
    /// Signed with Unity's DEBUG keystore, which is all a test install needs. A Play
    /// Store release needs a real keystore (see the store checklist) — deliberately
    /// not wired here so a test build can never be mistaken for a shippable one.
    /// </summary>
    public static class BuildAndroid
    {
        [MenuItem("Trust Issues/Build Android APK")]
        public static void Build()
        {
            // A stable package name. Without this Unity falls back to
            // com.DefaultCompany.*, and the package name is the app's permanent
            // identity on a device (changing it later = a separate install).
            PlayerSettings.SetApplicationIdentifier(
                UnityEditor.Build.NamedBuildTarget.Android, "com.shabbir.trustissues");

            // ARM64 is required by modern devices and the Play Store, and ARM64 on
            // Android only builds under IL2CPP — so the two settings go together.
            PlayerSettings.SetScriptingBackend(
                UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // One self-contained .apk to sideload (not an .aab app bundle, which
            // phones can't install directly).
            EditorUserBuildSettings.buildAppBundle = false;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/scene.unity" },
                target = BuildTarget.Android,
                locationPathName = "Builds/TrustIssues.apk",
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"Android build: {report.summary.result}, " +
                      $"{report.summary.totalSize / (1024 * 1024)} MB -> {options.locationPathName}");
            if (report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);   // headless callers see the failure
        }
    }
}
