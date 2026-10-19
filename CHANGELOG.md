# Changelog

## 1.0.1

- The loading screen frame rate is now capped at 500 FPS (it was unlimited), to avoid power spikes on fast GPUs.
  The setting is now `LoadingFrameRateCap` (30 to 500, default 500); the old `LoadingFrameRateLimit` is no longer used.

## 1.0.0

- Initial release.
- Lifts the frame rate limit and vsync on the loading screen, and restores your settings at the main menu.
- Shortens the game's per-frame loading budget (16 ms content, 22 ms catalogs) to 2 ms, so assets stream in sooner.
- Speeds up the post-processing effect scan at the start of the loading screen, which went through every type in MMHOOK and
  every mod (13 s on a 50-mod profile, now under 20 ms). Finds the same effects.
- Startup report (`BepInEx/FasterStartup_Report.txt`) with the load time of every plugin, loading stage and content pack.
- All settings available in-game through Risk Of Options.
