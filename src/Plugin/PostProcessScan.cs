using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using UnityEngine.Rendering.PostProcessing;

namespace FasterStartup
{
    // When the game's post-processing volume is first enabled on the loading screen, Unity's PostProcessManager
    // looks for every post-processing effect type by calling GetTypes() on every assembly in the game. With mods
    // that includes MMHOOK_RoR2 (one generated type per hookable method in the game) and every mod, and mods that
    // optionally support another mod make each lookup of that missing mod fail slowly. Measured: 13 s of the
    // loading screen on a 50-mod profile.
    //
    // An effect type derives from PostProcessEffectSettings, so it can only be declared in the post-processing
    // assembly or in an assembly that references it. HookGen's MMHOOK_* assemblies reference it too, but only
    // contain generated hook classes and delegates (and MMHOOK_RoR2 alone takes ~8 s to list). Scanning the rest
    // finds exactly the same types (checked against the game's own scan on a 50-mod profile: 17 of 17).
    internal static class PostProcessScan
    {
        private static ILHook hook;

        internal static int AssembliesScanned { get; private set; }
        internal static int AssembliesSkipped { get; private set; }
        internal static double ScanMs { get; private set; }

        internal static void Install()
        {
            var reloadBaseTypes = typeof(PostProcessManager).GetMethod("ReloadBaseTypes", BindingFlags.Instance | BindingFlags.NonPublic);
            if (reloadBaseTypes == null)
            {
                FasterStartupPlugin.Log.LogWarning("PostProcessManager.ReloadBaseTypes not found; post-processing type scan unchanged.");
                return;
            }
            hook = new ILHook(reloadBaseTypes, il =>
            {
                var cursor = new ILCursor(il);
                if (cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(out var method)
                    && method.Name == nameof(RuntimeUtilities.GetAllTypesDerivedFrom)
                    && method.DeclaringType.FullName == typeof(RuntimeUtilities).FullName))
                {
                    cursor.Remove();
                    cursor.EmitDelegate<Func<IEnumerable<Type>>>(FindEffectSettingsTypes);
                }
                else
                {
                    FasterStartupPlugin.Log.LogWarning("Post-processing type scan not found in PostProcessManager.ReloadBaseTypes; left unchanged.");
                }
            });
        }

        internal static void Uninstall()
        {
            hook?.Dispose();
            hook = null;
        }

        // Same result as RuntimeUtilities.GetAllTypesDerivedFrom<PostProcessEffectSettings>().
        private static IEnumerable<Type> FindEffectSettingsTypes()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var baseType = typeof(PostProcessEffectSettings);
            var postProcessingAssembly = baseType.Assembly;
            string postProcessingName = postProcessingAssembly.GetName().Name;
            var result = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly != postProcessingAssembly && (IsHookGenAssembly(assembly) || !References(assembly, postProcessingName)))
                {
                    AssembliesSkipped++;
                    continue;
                }
                AssembliesScanned++;
                double before = stopwatch.Elapsed.TotalMilliseconds;
                var types = GetTypes(assembly);
                double took = stopwatch.Elapsed.TotalMilliseconds - before;
                if (took >= 50)
                {
                    FasterStartupPlugin.Log.LogDebug($"Post-processing scan: {assembly.GetName().Name} took {took:0} ms ({types.Length} types).");
                }
                foreach (var type in types)
                {
                    if (type != null && type.IsSubclassOf(baseType))
                    {
                        result.Add(type);
                    }
                }
            }

            ScanMs = stopwatch.Elapsed.TotalMilliseconds;
            FasterStartupPlugin.Log.LogDebug($"Post-processing effects: found {result.Count} types in {AssembliesScanned} assemblies "
                + $"(skipped {AssembliesSkipped} that can't contain any) in {ScanMs:0} ms.");
            return result;
        }

        private static bool IsHookGenAssembly(Assembly assembly)
        {
            return assembly.GetName().Name.StartsWith("MMHOOK_", StringComparison.Ordinal);
        }

        private static bool References(Assembly assembly, string assemblyName)
        {
            try
            {
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    if (reference.Name == assemblyName)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception)
            {
                // Can't tell (e.g. a dynamic assembly): scan it, like the game would.
                return true;
            }
        }

        // Like the game's own scan: an assembly whose types can't be loaded contributes the ones that can.
        private static Type[] GetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types ?? Array.Empty<Type>();
            }
            catch (Exception)
            {
                return Array.Empty<Type>();
            }
        }
    }
}
