using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using FasterStartup.Patcher;

namespace FasterStartup
{
    // Turns the timeline collected by the preloader patcher into a report of where startup time went:
    // engine start, preloader, each plugin's load, and the game's own loading stages.
    internal static class StartupReport
    {
        internal static string ReportPath => Path.Combine(Paths.BepInExRootPath, "FasterStartup_Report.txt");

        private struct Span
        {
            public string name;
            public double startMs;
            public double durationMs;
        }

        // The patcher is a separate assembly; only touch its types if it's installed.
        internal static bool TimelineAvailable
        {
            get
            {
                try
                {
                    return IsTimelineRunning();
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsTimelineRunning() => StartupTimeline.IsRunning;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static double NowMs() => StartupTimeline.NowMs;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Write(int loadingFrames, string settingsSummary)
        {
            var entries = StartupTimeline.Stop();
            if (entries.Count == 0)
            {
                return;
            }
            double endMs = entries[entries.Count - 1].timeMs;

            double preloaderStart = entries[0].timeMs;
            double chainloaderStart = FindTime(entries, "BepInEx", "Chainloader started") ?? preloaderStart;
            double chainloaderEnd = FindTime(entries, "BepInEx", "Chainloader startup complete") ?? chainloaderStart;
            var plugins = PluginSpans(entries, chainloaderEnd);
            var stages = StageSpans(entries, chainloaderEnd, endMs);

            var sb = new StringBuilder();
            sb.AppendLine($"FasterStartup startup report ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");
            sb.AppendLine(settingsSummary);
            sb.AppendLine();
            sb.AppendLine($"Game start -> main menu:     {Seconds(endMs)}");
            sb.AppendLine($"  Engine start:              {Seconds(preloaderStart)}");
            sb.AppendLine($"  Preloader (patchers):      {Seconds(chainloaderStart - preloaderStart)}");
            sb.AppendLine($"  Plugins (chainloader):     {Seconds(chainloaderEnd - chainloaderStart)}");
            sb.AppendLine($"  Game loading:              {Seconds(endMs - chainloaderEnd)}");
            if (loadingFrames > 0)
            {
                sb.AppendLine($"  Frames during game loading: {loadingFrames} (avg {(endMs - chainloaderEnd) / loadingFrames:0.0} ms)");
            }
            sb.AppendLine();

            sb.AppendLine("Game loading stages:");
            foreach (var stage in stages)
            {
                sb.AppendLine($"  {Seconds(stage.startMs),9} {Seconds(stage.durationMs),9}  {stage.name}");
            }
            sb.AppendLine();

            if (ContentPackTimer.timings.Count > 0)
            {
                sb.AppendLine("Content packs (when each step finished, counted from the start of content loading; and the main-thread");
                sb.AppendLine("time spent in the pack's own code, not counting the assets it waits for). Slowest load first:");
                sb.AppendLine($"  {"loaded",9} {"generated",9} {"finalized",9} {"own time",9}  content pack");
                foreach (var pack in ContentPackTimer.timings.OrderByDescending(t => t.loadDoneMs))
                {
                    sb.AppendLine($"  {Seconds(pack.loadDoneMs),9} {Seconds(pack.generateDoneMs),9} {Seconds(pack.finalizeDoneMs),9} {Seconds(pack.mainThreadMs),9}  {pack.identifier}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("Plugin load times (Awake), slowest first:");
            foreach (var plugin in plugins.OrderByDescending(p => p.durationMs))
            {
                sb.AppendLine($"  {Seconds(plugin.durationMs),9}  {plugin.name}");
            }
            sb.AppendLine();

            sb.AppendLine("Timeline (time since game start, time since previous line):");
            double previous = 0;
            foreach (var entry in entries)
            {
                string message = entry.message.Replace("\r", "").Replace("\n", "\n                         ");
                sb.AppendLine($"{entry.timeMs / 1000.0,9:0.000}s {entry.timeMs - previous,8:+0.0}ms [{entry.level,-7}:{entry.source,15}] {message}");
                previous = entry.timeMs;
            }

            File.WriteAllText(ReportPath, sb.ToString());

            var slowest = plugins.OrderByDescending(p => p.durationMs).Take(3).Select(p => $"{p.name} {Seconds(p.durationMs)}");
            FasterStartupPlugin.Log.LogMessage(
                $"Startup took {Seconds(endMs)}: engine {Seconds(preloaderStart)}, preloader {Seconds(chainloaderStart - preloaderStart)}, "
                + $"plugins {Seconds(chainloaderEnd - chainloaderStart)}, game loading {Seconds(endMs - chainloaderEnd)}. "
                + $"Slowest plugins: {string.Join(", ", slowest)}. Full report: {ReportPath}");
        }

        private static string Seconds(double ms) => $"{ms / 1000.0:0.00}s";

        private static double? FindTime(List<TimelineEntry> entries, string source, string messageStart)
        {
            foreach (var entry in entries)
            {
                if (entry.source == source && entry.message.StartsWith(messageStart, StringComparison.Ordinal))
                {
                    return entry.timeMs;
                }
            }
            return null;
        }

        // The chainloader logs "Loading [Name Version]" before each plugin's Awake; the plugin's load time
        // runs until the next chainloader line.
        private static List<Span> PluginSpans(List<TimelineEntry> entries, double chainloaderEnd)
        {
            var spans = new List<Span>();
            Span? current = null;
            foreach (var entry in entries)
            {
                if (entry.timeMs > chainloaderEnd)
                {
                    break;
                }
                if (entry.source != "BepInEx")
                {
                    continue;
                }
                bool loading = entry.message.StartsWith("Loading [", StringComparison.Ordinal);
                if (current.HasValue && (loading || entry.message.StartsWith("TS Manifest", StringComparison.Ordinal)
                    || entry.message.StartsWith("Chainloader startup complete", StringComparison.Ordinal)))
                {
                    var span = current.Value;
                    span.durationMs = entry.timeMs - span.startMs;
                    spans.Add(span);
                    current = null;
                }
                if (loading)
                {
                    string name = entry.message.Substring("Loading [".Length).TrimEnd(']');
                    current = new Span { name = name, startMs = entry.timeMs };
                }
            }
            if (current.HasValue)
            {
                var span = current.Value;
                span.durationMs = chainloaderEnd - span.startMs;
                spans.Add(span);
            }
            return spans;
        }

        // Stages are marked by this plugin's own "Stage:" log lines.
        private static List<Span> StageSpans(List<TimelineEntry> entries, double chainloaderEnd, double endMs)
        {
            var spans = new List<Span>();
            double previous = chainloaderEnd;
            foreach (var entry in entries)
            {
                if (entry.source == FasterStartupPlugin.PluginName && entry.message.StartsWith(StagePrefix, StringComparison.Ordinal))
                {
                    spans.Add(new Span
                    {
                        name = "until " + entry.message.Substring(StagePrefix.Length),
                        startMs = previous,
                        durationMs = entry.timeMs - previous
                    });
                    previous = entry.timeMs;
                }
            }
            return spans;
        }

        internal const string StagePrefix = "Stage: ";
    }
}
