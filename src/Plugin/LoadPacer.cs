using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;

namespace FasterStartup
{
    // The game loads content packs and runs system initializers from coroutines that keep stepping the
    // loading code until a per-frame time budget is used up (1/60 s for content, 22 ms for catalogs).
    //
    // Most of that loading code waits on Addressables with `while (!handle.IsDone) yield return null;`, and
    // those handles only advance between frames, when Unity integrates loaded assets on the main thread.
    // So a big budget mostly means the loader spins on handles that can't finish yet while holding back the
    // asset integration they are waiting for. Measured on a large modpack: a 50 ms budget made startup ~25%
    // slower than vanilla, a few milliseconds made it faster. This sets both budgets to WorkBudgetMs.
    internal static class LoadPacer
    {
        private static readonly List<ILHook> hooks = new List<ILHook>();
        private static double budgetMs;
        private static int initializerSitesPatched;

        internal static void Install(double workBudgetMs)
        {
            // The initializer loop compares whole milliseconds and never steps at all with a budget of 0.
            budgetMs = Math.Max(1.0, workBudgetMs);

            var loadGameContent = FindMoveNext(typeof(RoR2Application), "<LoadGameContent>");
            if (loadGameContent != null)
            {
                hooks.Add(new ILHook(loadGameContent, PatchContentBudget));
            }
            else
            {
                FasterStartupPlugin.Log.LogWarning("RoR2Application.LoadGameContent not found; content loading budget unchanged.");
            }

            // RoR2BepInExPack replaces SystemInitializerAttribute.ExecuteCoroutine with its own copy, so patch
            // both; whichever one runs reads the new budget.
            var targets = new List<MethodBase>();
            var vanilla = FindMoveNext(typeof(SystemInitializerAttribute), "<ExecuteCoroutine>");
            if (vanilla != null)
            {
                targets.Add(vanilla);
            }
            targets.AddRange(FindBepInExPackSystemInitializer());
            foreach (var target in targets)
            {
                hooks.Add(new ILHook(target, PatchInitializerBudget));
            }
            if (initializerSitesPatched == 0)
            {
                FasterStartupPlugin.Log.LogWarning("SystemInitializerAttribute frame budget not found; catalog initialization budget unchanged.");
            }
        }

        internal static void Uninstall()
        {
            foreach (var hook in hooks)
            {
                hook.Dispose();
            }
            hooks.Clear();
        }

        internal static MethodBase FindMoveNext(Type declaringType, string iteratorNamePrefix)
        {
            var stateMachine = declaringType
                .GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(t => t.Name.StartsWith(iteratorNamePrefix, StringComparison.Ordinal));
            return stateMachine?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        // The replacement coroutine's name differs between versions, so return every iterator in that class;
        // the patch only changes the ones that read the budget field.
        private static IEnumerable<MethodBase> FindBepInExPackSystemInitializer()
        {
            var fixType = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "RoR2BepInExPack")
                ?.GetType("RoR2BepInExPack.VanillaFixes.FixSystemInitializer");
            if (fixType == null)
            {
                yield break;
            }
            foreach (var nested in fixType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (typeof(IEnumerator).IsAssignableFrom(nested))
                {
                    var moveNext = nested.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (moveNext != null)
                    {
                        yield return moveNext;
                    }
                }
            }
        }

        // `while (coroutineStack.Count > 0 && thisFrameLoadStopwatch.Elapsed < maxLoadTimePerFrame)`
        private static void PatchContentBudget(ILContext il)
        {
            var cursor = new ILCursor(il);
            var opLessThan = typeof(TimeSpan).GetMethod("op_LessThan", new[] { typeof(TimeSpan), typeof(TimeSpan) });
            int patched = 0;
            while (cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(opLessThan)))
            {
                cursor.Remove();
                cursor.EmitDelegate<Func<TimeSpan, TimeSpan, bool>>((elapsed, vanillaBudget) => elapsed.TotalMilliseconds < budgetMs);
                patched++;
            }
            LogPatch("content loading", il, patched);
        }

        // Every read of SystemInitializerAttribute.FRAME_TIME_THRESHOLD_MS (22 ms).
        private static void PatchInitializerBudget(ILContext il)
        {
            var cursor = new ILCursor(il);
            var threshold = typeof(SystemInitializerAttribute).GetField("FRAME_TIME_THRESHOLD_MS", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            int patched = 0;
            while (cursor.TryGotoNext(MoveType.Before, i => i.MatchLdsfld(threshold)))
            {
                cursor.Remove();
                cursor.EmitDelegate<Func<long>>(() => (long)budgetMs);
                patched++;
            }
            initializerSitesPatched += patched;
            if (patched > 0)
            {
                LogPatch("system initializers", il, patched);
            }
        }

        private static void LogPatch(string what, ILContext il, int patched)
        {
            if (patched == 0)
            {
                FasterStartupPlugin.Log.LogWarning($"Could not find the frame budget check in {il.Method.FullName}; {what} budget unchanged.");
            }
            else
            {
                FasterStartupPlugin.Log.LogDebug($"Set {what} frame budget to {budgetMs} ms: {il.Method.FullName} ({patched} site(s)).");
            }
        }
    }
}
