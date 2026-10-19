using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2;
using RoR2.ContentManagement;
using RoR2.UI.MainMenu;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FasterStartup
{
    [BepInDependency(RiskOfOptionsCompat.PluginGUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class FasterStartupPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.elevated.FasterStartup";
        public const string PluginName = "FasterStartup";
        public const string PluginVersion = "1.0.1";

        internal static ManualLogSource Log;

        private static ConfigEntry<bool> unlockFrameRate;
        private static ConfigEntry<int> loadingFrameRateLimit;
        private static ConfigEntry<bool> changeLoadBudget;
        private static ConfigEntry<float> loadBudgetMs;
        private static ConfigEntry<bool> fastPostProcessingScan;
        private static ConfigEntry<bool> writeStartupReport;

        private bool timeline;
        private bool startupDone;
        private bool initializersDone;
        private int loadingFrames;

        public void Awake()
        {
            Log = Logger;

            unlockFrameRate = Config.Bind("Loading", "UnlockFrameRate", true,
                "Raise the frame rate limit to LoadingFrameRateCap and turn off vsync while the game loads. Loading advances "
                + "once per frame, and the game caps the loading screen at 60 FPS by default. Your own settings are restored at the main menu.");
            // Replaces LoadingFrameRateLimit (1.0.0), whose default 0 meant unlimited. A new key so existing configs get the cap.
            loadingFrameRateLimit = Config.Bind("Loading", "LoadingFrameRateCap", FramePacing.MaxLoadingFrameRate,
                new ConfigDescription($"Frame rate limit while loading when UnlockFrameRate is on. At most {FramePacing.MaxLoadingFrameRate}: "
                    + "the loading screen is cheap to draw, and an unlimited frame rate can make powerful GPUs spike in power draw.",
                    new AcceptableValueRange<int>(30, FramePacing.MaxLoadingFrameRate)));
            changeLoadBudget = Config.Bind("Loading", "ChangeLoadBudget", true,
                "Replace the game's per-frame loading budget (16 ms for content packs, 22 ms for catalogs) with LoadBudgetMs. "
                + "The loader mostly waits on assets that only arrive between frames, so a short budget gets them in sooner.");
            loadBudgetMs = Config.Bind("Loading", "LoadBudgetMs", 2f,
                new ConfigDescription("How long the loader may run per frame, in milliseconds. Lower lets assets stream in "
                    + "faster; very low values slow down the CPU-heavy parts of loading.", new AcceptableValueRange<float>(1f, 50f)));
            fastPostProcessingScan = Config.Bind("Mod loading", "FastPostProcessingScan", true,
                "When the loading screen starts, the game looks for post-processing effects in every loaded assembly, "
                + "including MMHOOK and every mod. With many mods that takes several seconds. Only scan the assemblies "
                + "that can contain such effects; finds the same effects.");
            writeStartupReport = Config.Bind("Diagnostics", "WriteStartupReport", true,
                "Write BepInEx/FasterStartup_Report.txt with how long each plugin and loading stage took. Needs the "
                + "FasterStartup patcher (installed with this mod).");

            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(RiskOfOptionsCompat.PluginGUID))
            {
                RiskOfOptionsCompat.Init(System.IO.Path.GetDirectoryName(Info.Location),
                    unlockFrameRate, loadingFrameRateLimit, changeLoadBudget, loadBudgetMs, fastPostProcessingScan, writeStartupReport);
            }

            if (unlockFrameRate.Value)
            {
                FramePacing.Begin(loadingFrameRateLimit.Value);
            }
            if (changeLoadBudget.Value)
            {
                LoadPacer.Install(loadBudgetMs.Value);
            }
            if (fastPostProcessingScan.Value)
            {
                PostProcessScan.Install();
            }

            timeline = writeStartupReport.Value && StartupReport.TimelineAvailable;
            if (writeStartupReport.Value && !timeline)
            {
                Log.LogWarning("The FasterStartup patcher isn't loaded, so no startup report will be written.");
            }
            if (timeline)
            {
                ContentPackTimer.Install();
                StageMarks.Install();
            }

            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            ContentManager.onContentPacksAssigned += OnContentPacksAssigned;
            RoR2Application.onLoadFinished += OnLoadFinished;
            RoR2Application.onLoad += OnLoad;
            MainMenuController.OnMainMenuInitialised += OnMainMenuInitialised;

            Log.LogInfo($"Settings: {SettingsSummary()}");
        }

        public void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            ContentManager.onContentPacksAssigned -= OnContentPacksAssigned;
            RoR2Application.onLoadFinished -= OnLoadFinished;
            RoR2Application.onLoad -= OnLoad;
            MainMenuController.OnMainMenuInitialised -= OnMainMenuInitialised;
            FramePacing.Restore();
            LoadPacer.Uninstall();
            ContentPackTimer.Uninstall();
            PostProcessScan.Uninstall();
            StageMarks.Uninstall();
        }

        public void Update()
        {
            FramePacing.Tick();
            if (startupDone)
            {
                return;
            }
            loadingFrames++;
            if (!initializersDone && SystemInitializerAttribute.hasExecuted)
            {
                initializersDone = true;
                Stage("system initializers (catalogs) finished");
            }
        }

        public void LateUpdate()
        {
            FramePacing.Tick();
        }

        private static string SettingsSummary()
        {
            return $"UnlockFrameRate={unlockFrameRate.Value} (limit {loadingFrameRateLimit.Value}), "
                + $"ChangeLoadBudget={changeLoadBudget.Value} ({loadBudgetMs.Value} ms), FastPostProcessingScan={fastPostProcessingScan.Value}";
        }

        internal static void Stage(string name)
        {
            Log.LogInfo(StartupReport.StagePrefix + name);
        }

        private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            if (!startupDone)
            {
                Stage($"scene '{newScene.name}' active");
            }
        }

        private static void OnContentPacksAssigned(HG.ReadOnlyArray<ReadOnlyContentPack> contentPacks)
        {
            Stage($"content packs loaded ({contentPacks.Length} packs)");
        }

        private static void OnLoadFinished()
        {
            Stage("game content, sound banks and assets loaded");
        }

        private static void OnLoad()
        {
            Stage("startup finished (achievements, logbook, entity states, user profiles)");
        }

        private void OnMainMenuInitialised()
        {
            if (startupDone)
            {
                return;
            }
            startupDone = true;
            Stage("main menu ready");
            FramePacing.Restore();

            if (timeline)
            {
                try
                {
                    StartupReport.Write(loadingFrames, SettingsSummary());
                }
                catch (Exception e)
                {
                    Log.LogError($"Failed to write the startup report: {e}");
                }
            }
        }
    }
}
