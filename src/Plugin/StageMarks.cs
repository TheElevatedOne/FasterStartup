using System;
using RoR2;

namespace FasterStartup
{
    // Extra stage marks for the startup report, at the steps RoR2Application.InitializeGameRoutine runs
    // on the loading screen before content loading starts.
    internal static class StageMarks
    {
        internal static void Install()
        {
            On.RoR2.RoR2Application.EnableBehaviours += RoR2Application_EnableBehaviours;
            On.RoR2.WwiseIntegrationManager.Init += WwiseIntegrationManager_Init;
            On.RoR2.PlatformSystems.Init += PlatformSystems_Init;
            On.RoR2.RewiredIntegrationManager.Init += RewiredIntegrationManager_Init;
            On.RoR2.Console.Awake += Console_Awake;
            On.RoR2.MusicController.Init += MusicController_Init;
        }

        internal static void Uninstall()
        {
            On.RoR2.RoR2Application.EnableBehaviours -= RoR2Application_EnableBehaviours;
            On.RoR2.WwiseIntegrationManager.Init -= WwiseIntegrationManager_Init;
            On.RoR2.PlatformSystems.Init -= PlatformSystems_Init;
            On.RoR2.RewiredIntegrationManager.Init -= RewiredIntegrationManager_Init;
            On.RoR2.Console.Awake -= Console_Awake;
            On.RoR2.MusicController.Init -= MusicController_Init;
        }

        private static void Mark(string step, Action orig)
        {
            FasterStartupPlugin.Stage("starting " + step);
            orig();
        }

        // Same as vanilla (enable each behaviour in order), timing each one.
        private static void RoR2Application_EnableBehaviours(On.RoR2.RoR2Application.orig_EnableBehaviours orig, RoR2Application self)
        {
            FasterStartupPlugin.Stage("starting startup behaviours");
            var behaviours = self.BehavioursToEnableDuringStartup;
            if (behaviours == null)
            {
                orig(self);
                return;
            }
            var stopwatch = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < behaviours.Length; i++)
            {
                stopwatch.Restart();
                behaviours[i].enabled = true;
                double ms = stopwatch.Elapsed.TotalMilliseconds;
                if (ms >= 20)
                {
                    FasterStartupPlugin.Log.LogInfo($"Enabling {behaviours[i].GetType().FullName} on '{behaviours[i].name}' took {ms:0} ms");
                }
            }
        }

        private static void WwiseIntegrationManager_Init(On.RoR2.WwiseIntegrationManager.orig_Init orig)
            => Mark("Wwise (audio)", () => orig());

        private static void PlatformSystems_Init(On.RoR2.PlatformSystems.orig_Init orig)
            => Mark("platform systems (Addressables ready)", () => orig());

        private static void RewiredIntegrationManager_Init(On.RoR2.RewiredIntegrationManager.orig_Init orig)
            => Mark("Rewired (input), after Steamworks", () => orig());

        private static void Console_Awake(On.RoR2.Console.orig_Awake orig, RoR2.Console self)
            => Mark("console (config, convars)", () => orig(self));

        private static void MusicController_Init(On.RoR2.MusicController.orig_Init orig)
            => Mark("music, shaders, splash (language loaded)", () => orig());
    }
}
