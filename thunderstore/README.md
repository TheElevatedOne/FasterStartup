# Faster Startup

![Static Badge](https://img.shields.io/badge/CLAUDE-VIBECODED?style=for-the-badge&logo=claude&label=VIBECODED&labelColor=%23333333&color=%23de7356)

![Icon](https://raw.githubusercontent.com/TheElevatedOne/FasterStartup/refs/heads/main/icon.png)

Gets you from launching the game to the main menu faster, with or without mods, and tells you which mods make your startup slow.

Measured on a 50-mod profile (R2API, ItemQualities, ZetAspects, WolfoQualityOfLife, LookingGlass, ...) with all four DLCs:

| | Game start → main menu | Loading screen |
| --- | --- | --- |
| Without Faster Startup | 53.8 s | 42.4 s |
| With Faster Startup | **33.1 s** | **21.7 s** |

The gain is on the loading screen, which takes half as long. How much you save depends on your PC and your mods;
the more mods you have, the more the post-processing fix below saves.

## How it works

**Post-processing scan.** When the loading screen starts, Unity's post-processing system looks for effect types by listing every
type in every loaded assembly. In a modded game that includes HookGen's `MMHOOK_RoR2` (about 90,000 generated types) and every mod,
which took 13 seconds on the profile above. Faster Startup only scans the assemblies that can contain post-processing effects
(the ones that reference Unity's post-processing library, minus HookGen's generated ones). It finds exactly the same effects in 16 ms.

**Loading pacing.** The game loads its content (items, survivors, every DLC and every mod's content packs) and builds its catalogs in coroutines.
Each one advances a little every frame, so two things decide how fast the loading screen finishes:

- **The frame rate limit.** The loading screen runs at your normal FPS cap and vsync. Faster Startup turns off vsync and raises
  the cap to 500 FPS while the game loads (not unlimited, so fast GPUs don't spike in power draw), and puts your own settings
  back when the main menu opens.
- **The per-frame loading budget.** Each frame the game spends up to 16 ms (content) or 22 ms (catalogs) running loading code
  before it lets the frame finish. Most of that loading code is waiting for assets, and those only arrive between frames. So the game
  spends most of each frame checking on assets that can't be ready yet, and holds back the work that would make them ready.
  Faster Startup shortens the budget to 2 ms so the frames keep coming and the assets stream in sooner.

Nothing is skipped or loaded differently. The same code runs in the same order, just paced differently.

To skip the developer logos and the intro cutscene too, use [Remove Intro Cinematic](https://thunderstore.io/package/ELEVATED/RemoveIntroCinematic/) alongside this mod.

## Startup report

After every launch, `BepInEx/FasterStartup_Report.txt` lists:

- the total time from game start to the main menu, split into engine start, preloader, plugins and game loading,
- the game's loading stages (content packs, catalogs, achievements and profiles, main menu),
- when each content pack (the base game, each DLC, each mod that adds content) finished loading,
- how long every plugin took to load, slowest first,
- the full BepInEx log with timestamps.

The one-line summary is also in the BepInEx log, e.g.
`Startup took 33.08s: engine 1.02s, preloader 1.65s, plugins 8.73s, game loading 21.67s. Slowest plugins: WolfoQualityOfLife 5.2.0 2.52s, ...`

## Configuration

`BepInEx/config/com.elevated.FasterStartup.cfg`

| Setting | Default | Description |
| --- | --- | --- |
| `UnlockFrameRate` | `true` | Raise the frame rate limit to `LoadingFrameRateCap` and turn off vsync while the game loads. Restored at the main menu. |
| `LoadingFrameRateCap` | `500` | Frame rate limit while loading, when `UnlockFrameRate` is on. 30 to 500; capped at 500 so fast GPUs don't spike in power draw. |
| `ChangeLoadBudget` | `true` | Replace the game's per-frame loading budget (16 ms content, 22 ms catalogs) with `LoadBudgetMs`. |
| `LoadBudgetMs` | `2` | Loading budget per frame, in milliseconds. 1 to 4 ms all measured about the same; 50 ms was much slower than vanilla. |
| `FastPostProcessingScan` | `true` | Only scan assemblies that can contain post-processing effects when the loading screen starts. |
| `WriteStartupReport` | `true` | Write `BepInEx/FasterStartup_Report.txt` after each launch. |

### In-game settings

If [Risk Of Options](https://thunderstore.io/package/Rune580/Risk_Of_Options/) is installed (it's a dependency), the same settings are under
**Settings → Mod Options → Faster Startup**. They only affect startup, so changes apply the next time you launch the game.

## Compatibility

- Works with the base game and all four DLCs (Survivors of the Void, Seekers of the Storm, Alloyed Collective and Hallowed Concepts).
  It changes how fast loading runs, not what is loaded.
- Client-side only; doesn't affect multiplayer.
- Works alongside RoR2BepInExPack's fixes for the catalog initializer. Both its version and the vanilla one get the new budget.
- Install it with a mod manager: the package has a small preloader patcher (it only timestamps the log for the report) and the plugin.
