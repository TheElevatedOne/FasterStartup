using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using RoR2.ContentManagement;

namespace FasterStartup
{
    // Measures each content pack provider (the base game, each DLC, and each mod that adds content) for the
    // startup report. Providers are wrapped as they are collected; the wrapper forwards everything unchanged.
    internal static class ContentPackTimer
    {
        internal sealed class Timing
        {
            public string identifier;
            public double loadDoneMs = -1;
            public double generateDoneMs = -1;
            public double finalizeDoneMs = -1;
            public double mainThreadMs;
        }

        internal static readonly List<Timing> timings = new List<Timing>();
        private static readonly Stopwatch sinceStart = new Stopwatch();
        private static FieldInfo collectField;

        private static ILHook hook;

        // ContentManager.LoadContentPacks is a tiny iterator stub that Mono inlines into its caller, so an On hook
        // on it never runs. Instead, wrap the providers right before RoR2Application.LoadGameContent calls it.
        internal static void Install()
        {
            collectField = typeof(ContentManager).GetField("collectContentPackProviders", BindingFlags.Static | BindingFlags.NonPublic);
            var loadGameContent = LoadPacer.FindMoveNext(typeof(RoR2Application), "<LoadGameContent>");
            if (collectField == null || loadGameContent == null)
            {
                FasterStartupPlugin.Log.LogDebug("Content loading code not found; content packs won't be timed.");
                return;
            }
            hook = new ILHook(loadGameContent, il =>
            {
                var cursor = new ILCursor(il);
                var loadContentPacks = typeof(ContentManager).GetMethod(nameof(ContentManager.LoadContentPacks));
                if (cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(loadContentPacks)))
                {
                    cursor.EmitDelegate<Action>(WrapProviders);
                }
                else
                {
                    FasterStartupPlugin.Log.LogDebug("ContentManager.LoadContentPacks call not found; content packs won't be timed.");
                }
            });
        }

        internal static void Uninstall()
        {
            hook?.Dispose();
            hook = null;
        }

        private static void WrapProviders()
        {
            try
            {
                var collect = collectField.GetValue(null) as ContentManager.CollectContentPackProvidersDelegate;
                if (collect != null)
                {
                    ContentManager.CollectContentPackProvidersDelegate wrapped = addContentPackProvider =>
                        collect(provider => addContentPackProvider(provider == null ? null : new TimedProvider(provider)));
                    collectField.SetValue(null, wrapped);
                }
                sinceStart.Restart();
                FasterStartupPlugin.Stage("starting content packs");
            }
            catch (Exception e)
            {
                FasterStartupPlugin.Log.LogDebug($"Could not time content packs: {e}");
            }
        }

        private sealed class TimedProvider : IContentPackProvider
        {
            private readonly IContentPackProvider inner;
            private readonly Timing timing;

            public TimedProvider(IContentPackProvider inner)
            {
                this.inner = inner;
                timing = new Timing { identifier = inner.identifier };
                timings.Add(timing);
            }

            public string identifier => inner.identifier;

            public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
                => new TimedCoroutine(inner.LoadStaticContentAsync(args), timing, ms => timing.loadDoneMs = ms);

            public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args)
                => new TimedCoroutine(inner.GenerateContentPackAsync(args), timing, ms => timing.generateDoneMs = ms);

            public IEnumerator FinalizeAsync(FinalizeAsyncArgs args)
                => new TimedCoroutine(inner.FinalizeAsync(args), timing, ms => timing.finalizeDoneMs = ms);
        }

        private sealed class TimedCoroutine : IEnumerator
        {
            private readonly IEnumerator inner;
            private readonly Timing timing;
            private readonly Action<double> onDone;

            public TimedCoroutine(IEnumerator inner, Timing timing, Action<double> onDone)
            {
                this.inner = inner;
                this.timing = timing;
                this.onDone = onDone;
            }

            public object Current => inner?.Current;

            public bool MoveNext()
            {
                if (inner == null)
                {
                    return false;
                }
                double start = sinceStart.Elapsed.TotalMilliseconds;
                bool more = inner.MoveNext();
                double end = sinceStart.Elapsed.TotalMilliseconds;
                timing.mainThreadMs += end - start;
                if (!more)
                {
                    onDone(end);
                }
                return more;
            }

            public void Reset() => inner?.Reset();
        }
    }
}
