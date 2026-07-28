using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TrustIssues.EditorTools
{
    /// <summary>
    /// The PLAY STORE build: a signed Android App Bundle (.aab), not the .apk that
    /// BuildAndroid produces. Play will not accept an apk for a new app, and the
    /// two builds differ in more than the extension — this one refuses to run at
    /// all unless a real keystore is configured, because a bundle signed with
    /// Unity's debug key is worse than useless: it uploads, then can never be
    /// updated.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . ^
    ///     -executeMethod TrustIssues.EditorTools.BuildRelease.Build -logFile release.log
    ///
    /// Output: Builds/TrustIssues.aab
    /// See docs/PLAY_STORE.md for the whole publishing run-through.
    /// </summary>
    public static class BuildRelease
    {
        [MenuItem("Trust Issues/Build Play Store Bundle (.aab)")]
        public static void Build()
        {
            var android = UnityEditor.Build.NamedBuildTarget.Android;

            // Identity. Must never change once the app is live — the package name IS
            // the app as far as Play is concerned.
            PlayerSettings.SetApplicationIdentifier(android, "com.shabbir.trustissues");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = true;

            // Every upload needs a version code strictly higher than the last one, and
            // forgetting to bump it is the classic wasted-hour mistake — so it's
            // automatic, and printed so it can go in the release notes.
            PlayerSettings.Android.bundleVersionCode += 1;
            Debug.Log($"RELEASE version {PlayerSettings.bundleVersion} " +
                      $"(code {PlayerSettings.Android.bundleVersionCode})");

            // Refuse to build unsigned. Unity silently falls back to its DEBUG key,
            // which produces a bundle that looks fine, uploads fine, and then locks
            // you out of ever shipping an update — the failure only shows up months
            // later. Better to stop here with an explanation.
            if (!PlayerSettings.Android.useCustomKeystore ||
                string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
            {
                Debug.LogError(
                    "RELEASE ABORTED — no upload keystore is set.\n" +
                    "This build would be signed with Unity's debug key, which Play " +
                    "accepts once and then refuses every update to.\n" +
                    "Create a keystore and point Unity at it: see docs/PLAY_STORE.md, Part 2.");
                EditorApplication.Exit(2);
                return;
            }

            Directory.CreateDirectory("Builds");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/scene.unity" },
                target = BuildTarget.Android,
                locationPathName = "Builds/TrustIssues.aab",
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"Play Store bundle: {report.summary.result}, " +
                      $"{report.summary.totalSize / (1024 * 1024)} MB -> {options.locationPathName}");

            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
                return;
            }

            AssetDatabase.SaveAssets();   // persist the bumped version code
            Debug.Log("RELEASE_DONE  next: upload to Internal testing (PLAY_STORE.md, Part 9)");
        }
    }
}
