# FasterStartup

![Static Badge](https://img.shields.io/badge/CLAUDE-VIBECODED?style=for-the-badge&logo=claude&label=VIBECODED&labelColor=%23333333&color=%23de7356)

![ICON](https://raw.githubusercontent.com/TheElevatedOne/FasterStartup/refs/heads/main/icon.png)

A Risk of Rain 2 mod that makes startup faster and reports where startup time goes. The player-facing description
(packaged as the Thunderstore README, without building instructions) is [thunderstore/README.md](thunderstore/README.md).

## Preview
- Both tests were done on a r2modman profile I used for playing with all 4 dlc's
- r2modman code `01a14ad1-dfac-7dae-a349-240ed52e3a91`

<details>
     <summary>Test Without Mod Enabled</summary>
     <video src="https://github.com/user-attachments/assets/8e508d44-9e76-45bf-b53f-6f65f1980d69">
</details>

<details>
     <summary>Test With Mod Enabled</summary>
     <video src="https://github.com/user-attachments/assets/505f50dc-d94b-4869-bc1f-0e287b2527fd">
</details>

## Layout

| Path | What it is |
| --- | --- |
| `src/Patcher` | `FasterStartup.Patcher.dll`, a BepInEx preloader patcher. Patches nothing; it registers a log listener that timestamps every log line from the start of the preloader, so the report can time each plugin's load. |
| `src/Plugin` | `FasterStartup.dll`, the plugin. |
| `src/Plugin/FramePacing.cs` | Raises `Application.targetFrameRate` to the loading cap (at most 500) and turns off vsync while loading. Hooks the `fps_max` / `vsync_count` convar setters to remember the player's values, restores them at the main menu. |
| `src/Plugin/LoadPacer.cs` | IL hooks that replace the per-frame budget in `RoR2Application.LoadGameContent` (the `TimeSpan` comparison) and `SystemInitializerAttribute.FRAME_TIME_THRESHOLD_MS` reads, in both the vanilla coroutine and RoR2BepInExPack's replacement of it. |
| `src/Plugin/PostProcessScan.cs` | IL hook on `PostProcessManager.ReloadBaseTypes` replacing `RuntimeUtilities.GetAllTypesDerivedFrom<PostProcessEffectSettings>()` (which calls `GetTypes()` on every assembly) with a scan of assemblies that reference `Unity.Postprocessing.Runtime`, minus HookGen's `MMHOOK_*`. |
| `src/Plugin/StageMarks.cs` | Stage marks for the report at the steps of `RoR2Application.InitializeGameRoutine` before content loading, and per-behaviour timing in `EnableBehaviours`. |
| `src/Plugin/ContentPackTimer.cs` | Wraps each `IContentPackProvider` (injected right before `ContentManager.LoadContentPacks` is called) to time each content pack. |
| `src/Plugin/StartupReport.cs` | Writes `BepInEx/FasterStartup_Report.txt`. |
| `src/Plugin/RiskOfOptionsCompat.cs` | In-game settings. |
| `thunderstore.conf` | Package metadata for Thunderstore: namespace, name, version, description, dependencies. |
| `thunderstore/README.md` | The README shown on Thunderstore. |
| `publish.sh` | Builds the Thunderstore zip (called by `make publish`). |

## Building

Needs the .NET SDK, the game, and an r2modman profile with RoR2BepInExPack, HookGenPatcher (for `MMHOOK_RoR2.dll`) and
Risk Of Options installed. The project references their DLLs from the game and profile folders.

```sh
make build PROFILE_DIR="$HOME/.config/r2modmanPlus-local/RiskOfRain2/profiles/<profile>"
make install PROFILE_DIR=...   # build and copy the plugin and patcher into the profile
make uninstall PROFILE_DIR=... # remove them again
make publish PROFILE_DIR=...   # build dist/ELEVATED-FasterStartup-<version>.zip for Thunderstore
make clean
```

`RORDIR` overrides the game folder (default: `~/.local/share/Steam/steamapps/common/Risk of Rain 2`).

## Publishing

1. Bump `VERSION` in `thunderstore.conf` and `PluginVersion` in `src/Plugin/FasterStartupPlugin.cs`.
2. Add a `## <version>` entry at the top of `CHANGELOG.md`.
3. Run `make publish`. `publish.sh` checks that the three versions match, that the icon is a 256x256 PNG and that the
   description fits Thunderstore's 250 characters, then writes the zip to `dist/`.
4. Upload the zip at <https://thunderstore.io/package/create/> (community: Risk of Rain 2).

The zip contains `manifest.json`, `icon.png`, `README.md` (from `thunderstore/README.md`), `CHANGELOG.md`,
`plugins/FasterStartup.dll` + `plugins/icon.png` and `patchers/FasterStartup.Patcher.dll`; mod managers install
`plugins/` and `patchers/` into the matching BepInEx folders.

## Measurements

Profile "Hallowed Ready": 50 plugins, all four DLCs, Proton, Ryzen 7 5700X, RX 480, player settings fps_max 144 and vsync on.
Times are game start to main menu from the startup report, one launch each.

| Configuration | Total | Loading screen | Content packs |
| --- | --- | --- | --- |
| Vanilla (all options off) | 54.2 s / 54.1 s | 42.7 s / 42.8 s | 28.9 s |
| Frame rate unlock only | 51.1 s | 39.8 s | |
| Unlock + 50 ms budget | 67.3 s | 55.9 s | |
| Unlock + 8 ms budget | 48.2 s | 36.9 s | 25.5 s |
| Unlock + 4 ms budget | 46.7 s | 35.2 s | |
| Unlock + 2 ms budget (default) | 46.1 s / 46.3 s / 46.2 s | 34.7–34.9 s | 23.2 s |
| Unlock + 1 ms budget | 46.4 s | 34.8 s | 23.3 s |
| 240 FPS cap + 4 ms budget | 47.3 s | 36.0 s | |
| Unlock + 2 ms + post-processing scan, MMHOOK still scanned | 42.9 s | 31.6 s | |
| **All defaults** (unlock, 2 ms, post-processing scan) | **33.1 s / 33.3 s** | **21.7 s / 21.8 s** | 7.4 s after it starts |
| Vanilla, measured again in the same batch | 53.8 s | 42.4 s | |

"Content packs" up to the 2 ms rows is measured from the loading scene, so it includes the 13 s post-processing scan that
runs before content loading starts. The stage marks added later showed that.

The post-processing scan was checked against the game's own scan in the same launch: both found the same 17 effect types.
Of 217 assemblies, 6 are scanned (16 ms); `MMHOOK_RoR2` alone took 8.5 s and `MMHOOK_Assembly-CSharp` 0.4 s.

Things that were tried and dropped because they measured no gain:

- Ending the frame early when the loader only polls unfinished asset handles (with a 50 ms budget): slower than vanilla (60.6 s).
- Running `InitDuringStartupPhaseAttribute` phases without a yield every 5 tasks: no difference (51.2 s vs 51.1 s).
- Caching BepInEx's assembly lookups: only 22 lookups happen per launch, no difference (53.9 s vs 54.2 s).

What's left: catalog initialization (~6.3 s), the content packs themselves (~7.4 s) and plugin loading (~8.7 s) are work in the
game's and the mods' own code.
Roughly 490 MonoMod hooks are created while plugins load, at ~2.7 ms each.

## Measuring

Start the game, wait for the main menu, and read `BepInEx/FasterStartup_Report.txt`. To compare against vanilla pacing,
turn off `UnlockFrameRate`, `ChangeLoadBudget` and `FastPostProcessingScan` (keep `WriteStartupReport` on).
