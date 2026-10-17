using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using RoR2;
using RoR2.ConVar;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FasterStartup
{
    // Startup is a chain of coroutines and async loads that each advance once per frame, so the frame
    // rate is the speed limit of the loading screen. The game caps it at 60 FPS by default (fps_max) and
    // may add vsync on top. This lifts both until the main menu is up, then puts the player's settings back.
    internal static class FramePacing
    {
        // After loading finishes, keep the limit lifted for at most this long while the next scene opens.
        private const float RestoreTimeoutSeconds = 20f;

        private static bool active;
        private static int loadingFrameRate;
        private static int savedFrameRate;
        private static int savedVSyncCount;
        private static float loadFinishedTime = -1f;

        private static readonly List<Hook> convarHooks = new List<Hook>();

        internal static bool IsActive => active;

        internal static void Begin(int frameRateDuringLoad)
        {
            loadingFrameRate = frameRateDuringLoad <= 0 ? -1 : frameRateDuringLoad;
            savedFrameRate = Application.targetFrameRate;
            savedVSyncCount = QualitySettings.vSyncCount;
            active = true;
            HookConVar("FpsMaxConVar", nameof(FpsMaxSetString));
            HookConVar("VSyncCountConVar", nameof(VSyncCountSetString));
            Enforce();
        }

        // Record the player's own fps_max / vsync_count when the game applies them from the config,
        // even when the value happens to match what is currently set.
        private static void HookConVar(string nestedTypeName, string handlerName)
        {
            var setString = typeof(SettingsConVars)
                .GetNestedType(nestedTypeName, BindingFlags.NonPublic | BindingFlags.Public)
                ?.GetMethod("SetString", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            if (setString == null)
            {
                FasterStartupPlugin.Log.LogWarning($"SettingsConVars.{nestedTypeName} not found; relying on frame checks to remember the player's setting.");
                return;
            }
            convarHooks.Add(new Hook(setString, typeof(FramePacing).GetMethod(handlerName, BindingFlags.Static | BindingFlags.NonPublic)));
        }

        private static void FpsMaxSetString(Action<BaseConVar, string> orig, BaseConVar self, string newValue)
        {
            orig(self, newValue);
            if (active)
            {
                savedFrameRate = Application.targetFrameRate;
                Application.targetFrameRate = loadingFrameRate;
            }
        }

        private static void VSyncCountSetString(Action<BaseConVar, string> orig, BaseConVar self, string newValue)
        {
            orig(self, newValue);
            if (active)
            {
                savedVSyncCount = QualitySettings.vSyncCount;
                QualitySettings.vSyncCount = 0;
            }
        }

        private static void UnhookConVars()
        {
            foreach (var hook in convarHooks)
            {
                hook.Dispose();
            }
            convarHooks.Clear();
        }

        // Called every Update and LateUpdate while loading.
        internal static void Tick()
        {
            if (!active)
            {
                return;
            }
            if (ShouldRestore())
            {
                Restore();
                return;
            }
            Enforce();
        }

        private static void Enforce()
        {
            // The game applies the player's fps_max / vsync_count from their config part way through
            // loading. Remember those as the values to restore, and keep the limit lifted.
            if (Application.targetFrameRate != loadingFrameRate)
            {
                savedFrameRate = Application.targetFrameRate;
                Application.targetFrameRate = loadingFrameRate;
            }
            if (QualitySettings.vSyncCount != 0)
            {
                savedVSyncCount = QualitySettings.vSyncCount;
                QualitySettings.vSyncCount = 0;
            }
        }

        private static bool ShouldRestore()
        {
            if (RoR2Application.IsMainMenuInitialized)
            {
                return true;
            }
            if (!RoR2Application.loadFinished)
            {
                return false;
            }
            if (loadFinishedTime < 0f)
            {
                loadFinishedTime = Time.unscaledTime;
            }
            // The intro cutscene (if it isn't skipped) should play at the normal frame rate.
            string scene = SceneManager.GetActiveScene().name;
            if (scene == "intro" || scene == "lobby" || scene == "logbook")
            {
                return true;
            }
            return Time.unscaledTime - loadFinishedTime > RestoreTimeoutSeconds;
        }

        internal static void Restore()
        {
            if (!active)
            {
                return;
            }
            active = false;
            UnhookConVars();
            Application.targetFrameRate = savedFrameRate;
            QualitySettings.vSyncCount = savedVSyncCount;
            FasterStartupPlugin.Log.LogInfo($"Restored frame rate limit (fps_max {savedFrameRate}, vsync {savedVSyncCount}).");
        }
    }
}
