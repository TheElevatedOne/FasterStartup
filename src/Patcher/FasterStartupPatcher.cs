using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using Mono.Cecil;

namespace FasterStartup.Patcher
{
    // Preloader patcher. It doesn't patch anything: it is loaded before the chainloader so it can
    // timestamp every log line from the very start, including each plugin's load time.
    // The plugin reads the collected timeline and writes the startup report.
    public static class FasterStartupPatcher
    {
        public static IEnumerable<string> TargetDLLs { get; } = Array.Empty<string>();

        public static void Initialize()
        {
            StartupTimeline.Start();
            Logger.Listeners.Add(new TimelineLogListener());
            StartupTimeline.Mark("Preloader patchers initializing");
        }

        public static void Patch(AssemblyDefinition assembly)
        {
        }

        public static void Finish()
        {
            StartupTimeline.Mark("Preloader patching finished");
        }
    }

    public struct TimelineEntry
    {
        public double timeMs;
        public string source;
        public LogLevel level;
        public string message;
    }

    public static class StartupTimeline
    {
        // Process uptime when the stopwatch started, so times are measured from game launch.
        private static double offsetMs;
        private static readonly Stopwatch stopwatch = new Stopwatch();
        private static readonly object entriesLock = new object();
        private static List<TimelineEntry> entries = new List<TimelineEntry>(8192);

        public static bool IsRunning => stopwatch.IsRunning;

        // Milliseconds since the game process started.
        public static double NowMs => offsetMs + stopwatch.Elapsed.TotalMilliseconds;

        public static void Start()
        {
            if (stopwatch.IsRunning)
            {
                return;
            }
            try
            {
                offsetMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
                if (offsetMs < 0 || offsetMs > 10 * 60 * 1000)
                {
                    offsetMs = 0;
                }
            }
            catch
            {
                offsetMs = 0;
            }
            stopwatch.Start();
        }

        public static void Mark(string message)
        {
            Add("FasterStartup", LogLevel.Message, "== " + message);
        }

        internal static void Add(string source, LogLevel level, string message)
        {
            if (!stopwatch.IsRunning)
            {
                return;
            }
            var entry = new TimelineEntry { timeMs = NowMs, source = source, level = level, message = message };
            lock (entriesLock)
            {
                entries?.Add(entry);
            }
        }

        // Hands the collected entries over and stops collecting. Called once startup is done.
        public static List<TimelineEntry> Stop()
        {
            lock (entriesLock)
            {
                var result = entries ?? new List<TimelineEntry>();
                entries = null;
                return result;
            }
        }
    }

    internal sealed class TimelineLogListener : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            StartupTimeline.Add(eventArgs.Source?.SourceName ?? "?", eventArgs.Level, eventArgs.Data?.ToString() ?? "");
        }

        public void Dispose()
        {
        }
    }
}
