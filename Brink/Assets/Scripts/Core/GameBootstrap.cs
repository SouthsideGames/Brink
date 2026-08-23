using Brink.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Brink.Core
{
    /// <summary>
    /// Self-contained startup: builds the UI Toolkit panel and terminal shell at
    /// runtime from Resources, so the game runs from any scene with zero scene
    /// wiring. Keeps Phase 0 free of hand-edited scene/asset serialization.
    /// </summary>
    public static class GameBootstrap
    {
        static GameObject rootObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (rootObject != null || Application.isBatchMode)
                return;

            Application.targetFrameRate = 60;

            // Before any state is attached, so the very first session of a run is
            // recorded rather than the second.
            TelemetryFile.Initialize();

            var theme = Resources.Load<ThemeStyleSheet>("UI/BrinkTheme");
            var layout = Resources.Load<VisualTreeAsset>("UI/TerminalShell");

            if (theme == null || layout == null)
            {
                Debug.LogError("[BOOT] Terminal UI resources missing; bootstrap aborted.");
                return;
            }

            // Scale is computed from pixel width, not physical size.
            //
            // ConstantPhysicalSize needs the platform to report an honest screen
            // DPI. When it does not — the Device Simulator, and some real
            // handsets — it falls back to 96, the scale collapses to 1, and the
            // whole terminal renders at 13 actual pixels on a 2300-pixel screen.
            // Deriving the scale ourselves removes that dependency entirely while
            // keeping the GDD §4 promise that a larger screen shows *more*
            // columns rather than bigger text.
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "BrinkPanelSettings";
            panelSettings.themeStyleSheet = theme;
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.scale = TerminalScale.ScaleFor(
                Screen.width, Screen.height, UI.DisplaySettings.ScaleMultiplier);

            rootObject = new GameObject("BrinkRuntime");
            Object.DontDestroyOnLoad(rootObject);

            var document = rootObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = layout;

            rootObject.AddComponent<TerminalShellController>();

            // Logged so a layout problem on a real device can be diagnosed from
            // logcat instead of guessed at from a screenshot.
            float scale = panelSettings.scale;
            Debug.Log($"[BOOT] screen={Screen.width}x{Screen.height} dpi={Screen.dpi:F0} " +
                      $"scale={scale:F2} font={TerminalScale.BaseFontPx * scale:F0}px " +
                      $"targetCols={TerminalScale.TargetColumns(Screen.width, Screen.height)}");
            GameLog.Info("BOOT", "Runtime bootstrap complete.");
        }
    }
}
