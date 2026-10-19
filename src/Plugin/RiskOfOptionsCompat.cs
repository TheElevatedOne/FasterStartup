using System.IO;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using UnityEngine;

namespace FasterStartup
{
    // Only touched when RiskOfOptions is installed, so its types are never loaded otherwise.
    internal static class RiskOfOptionsCompat
    {
        internal const string PluginGUID = "com.rune580.riskofoptions";

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        internal static void Init(string pluginDirectory,
            ConfigEntry<bool> unlockFrameRate, ConfigEntry<int> loadingFrameRateLimit,
            ConfigEntry<bool> changeLoadBudget, ConfigEntry<float> loadBudgetMs, ConfigEntry<bool> fastPostProcessingScan,
            ConfigEntry<bool> writeStartupReport)
        {
            const string guid = FasterStartupPlugin.PluginGUID;
            const string name = "Faster Startup";

            ModSettingsManager.SetModDescription(
                "Makes the game (and your mods) load faster at startup. All settings only affect startup, "
                + "so changes take effect the next time the game starts.",
                guid, name);

            var icon = LoadIcon(Path.Combine(pluginDirectory, "icon.png"));
            if (icon)
            {
                ModSettingsManager.SetModIcon(icon, guid, name);
            }

            ModSettingsManager.AddOption(new CheckBoxOption(unlockFrameRate, new CheckBoxConfig { restartRequired = true }), guid, name);
            ModSettingsManager.AddOption(new IntSliderOption(loadingFrameRateLimit, new IntSliderConfig
            {
                min = 30,
                max = FramePacing.MaxLoadingFrameRate,
                restartRequired = true,
                checkIfDisabled = () => !unlockFrameRate.Value
            }), guid, name);
            ModSettingsManager.AddOption(new CheckBoxOption(changeLoadBudget, new CheckBoxConfig { restartRequired = true }), guid, name);
            ModSettingsManager.AddOption(new StepSliderOption(loadBudgetMs, new StepSliderConfig
            {
                min = 1f,
                max = 50f,
                increment = 1f,
                FormatString = "{0:0} ms",
                restartRequired = true,
                checkIfDisabled = () => !changeLoadBudget.Value
            }), guid, name);
            ModSettingsManager.AddOption(new CheckBoxOption(fastPostProcessingScan, new CheckBoxConfig { restartRequired = true }), guid, name);
            ModSettingsManager.AddOption(new CheckBoxOption(writeStartupReport, new CheckBoxConfig { restartRequired = true }), guid, name);
        }

        private static Sprite LoadIcon(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                return null;
            }
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
    }
}
