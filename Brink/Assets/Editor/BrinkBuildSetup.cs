using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Brink.EditorTools
{
    /// <summary>
    /// Android build configuration, applied through the PlayerSettings API rather
    /// than by hand-editing ProjectSettings.asset — so it is reviewable, rerunnable
    /// and survives a settings reset.
    ///
    /// Menu: Brink → Configure Android, then Brink → Build Android APK.
    /// Headless:
    ///   Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///     -executeMethod Brink.EditorTools.BrinkBuildSetup.ConfigureAndroid
    ///   Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///     -executeMethod Brink.EditorTools.BrinkBuildSetup.BuildAndroidApk
    /// </summary>
    public static class BrinkBuildSetup
    {
        public const string ApplicationId = "com.southsidegames.brink";

        /// <summary>Where a local test build lands. Outside Assets/ so Unity does not import it.</summary>
        public const string OutputDirectory = "Builds/Android";

        [MenuItem("Brink/Configure Android")]
        public static void ConfigureAndroid()
        {
            // --- identity ---
            PlayerSettings.companyName = "Southside Games";
            PlayerSettings.productName = "Brink";
            PlayerSettings.SetApplicationIdentifier(
                UnityEditor.Build.NamedBuildTarget.Android, ApplicationId);

            // --- orientation: landscape only, both ways (GDD §2) ---
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.useAnimatedAutorotation = true;

            // --- Android platform ---
            // IL2CPP + ARM64 is required for Google Play and is what a modern
            // device runs anyway.
            PlayerSettings.SetScriptingBackend(
                UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;

            // Cast rather than naming the enum member: the installed SDK set
            // changes faster than Unity's enum, and a missing member is a
            // compile error rather than a warning.
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)35;

            // The terminal is a flat 2D UI. None of this is needed and all of it
            // costs battery or startup time on a phone.
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = true; // shell pads for cutouts itself
            PlayerSettings.MTRendering = true;
            PlayerSettings.gpuSkinning = false;
            PlayerSettings.accelerometerFrequency = 0; // never read
            PlayerSettings.muteOtherAudioSources = false;

            // No splash: this is a premium single-player title and the Unity logo
            // splash is only removable on paid tiers — leave whatever the licence
            // permits rather than forcing a value that may be rejected.
            PlayerSettings.SplashScreen.showUnityLogo = PlayerSettings.SplashScreen.showUnityLogo;

            // --- scripting ---
            PlayerSettings.SetApiCompatibilityLevel(
                UnityEditor.Build.NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(
                UnityEditor.Build.NamedBuildTarget.Android, ManagedStrippingLevel.Low);

            // --- versioning ---
            // bundleVersionCode must rise on every upload; the display version is
            // the human-facing one.
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = Math.Max(1, PlayerSettings.Android.bundleVersionCode);

            EnsureBuildScene();

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[BRINK] Android configured. id={ApplicationId} " +
                $"backend=IL2CPP arch=ARM64 minSdk={PlayerSettings.Android.minSdkVersion} " +
                $"targetSdk={(int)PlayerSettings.Android.targetSdkVersion} " +
                $"version={PlayerSettings.bundleVersion}+{PlayerSettings.Android.bundleVersionCode}");
        }

        /// <summary>
        /// The game builds its own UI at runtime (`GameBootstrap`), so any scene
        /// boots it — but the build needs at least one enabled scene or the player
        /// starts with nothing.
        /// </summary>
        static void EnsureBuildScene()
        {
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled) return;

            const string fallback = "Assets/Scenes/SampleScene.unity";
            if (!File.Exists(fallback))
            {
                Debug.LogError($"[BRINK] No enabled build scene and {fallback} is missing.");
                return;
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(fallback, true) };
            Debug.Log($"[BRINK] Build scene list was empty; enabled {fallback}.");
        }

        [MenuItem("Brink/Build Android APK")]
        public static void BuildAndroidApk() => BuildAndroid(false);

        [MenuItem("Brink/Build Android App Bundle (AAB)")]
        public static void BuildAndroidAppBundle() => BuildAndroid(true);

        static void BuildAndroid(bool appBundle)
        {
            ConfigureAndroid();

            EditorUserBuildSettings.buildAppBundle = appBundle;
            Directory.CreateDirectory(OutputDirectory);

            string extension = appBundle ? "aab" : "apk";
            string output = Path.Combine(OutputDirectory, $"Brink-{PlayerSettings.bundleVersion}.{extension}");

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled) scenes.Add(scene.path);

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BRINK] Build succeeded: {output} " +
                          $"({summary.totalSize / 1024f / 1024f:F1} MB, {summary.totalTime.TotalMinutes:F1} min)");
            }
            else
            {
                Debug.LogError($"[BRINK] Build {summary.result}: {summary.totalErrors} error(s).");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Reports anything that would stop a build or produce a broken player,
        /// without building. Cheap to run and safe in batch mode.
        /// </summary>
        [MenuItem("Brink/Check Android Readiness")]
        public static void CheckAndroidReadiness()
        {
            int problems = 0;

            void Problem(string message)
            {
                problems++;
                Debug.LogError($"[BRINK CHECK] {message}");
            }

            void Note(string message) => Debug.Log($"[BRINK CHECK] {message}");

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                Problem("The Android build module is not installed for this editor version.");

            string id = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
            if (string.IsNullOrEmpty(id) || id.Contains("DefaultCompany"))
                Problem($"Application identifier is unset or still the Unity default ('{id}').");
            else
                Note($"Application id: {id}");

            bool anyScene = false;
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled) anyScene = true;
            if (!anyScene) Problem("No enabled scene in the build list — the player would start empty.");

            if (Resources.Load("UI/TerminalShell") == null)
                Problem("Resources/UI/TerminalShell is missing; GameBootstrap would abort at runtime.");
            if (Resources.Load("UI/BrinkTheme") == null)
                Problem("Resources/UI/BrinkTheme is missing; GameBootstrap would abort at runtime.");

            if (PlayerSettings.allowedAutorotateToPortrait ||
                PlayerSettings.allowedAutorotateToPortraitUpsideDown)
                Problem("Portrait orientation is allowed; the terminal is landscape-only (GDD §2).");

            var backend = PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android);
            if (backend != ScriptingImplementation.IL2CPP)
                Problem($"Scripting backend is {backend}; Google Play requires IL2CPP/ARM64.");

            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) == 0)
                Problem("ARM64 is not among the target architectures; Google Play requires it.");

            if ((int)PlayerSettings.Android.targetSdkVersion != 0 &&
                (int)PlayerSettings.Android.targetSdkVersion < 34)
                Problem($"Target SDK {(int)PlayerSettings.Android.targetSdkVersion} is below the Play minimum (34).");

            Note(string.IsNullOrEmpty(PlayerSettings.Android.keystoreName)
                ? "No keystore set — Unity will sign with the debug key. Fine for sideloading, "
                  + "not for Play upload."
                : $"Keystore: {PlayerSettings.Android.keystoreName}");

            if (problems == 0) Debug.Log("[BRINK CHECK] Ready to build for Android.");
            else Debug.LogError($"[BRINK CHECK] {problems} problem(s) must be fixed before building.");

            if (Application.isBatchMode && problems > 0) EditorApplication.Exit(1);
        }
    }
}
