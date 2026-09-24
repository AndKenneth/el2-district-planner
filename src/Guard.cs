using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DistrictPlanner
{
    // Every prefix and postfix catches its own exceptions and hands them here, so a game update that breaks the mod
    // quietly (a renamed UI child, a changed data shape) turns that feature off instead of throwing inside the game,
    // which ends the game with "Unexpected error". A prefix returns true after a failure, so the vanilla method runs.
    // The failed patch class is removed on the main thread (Plugin.Update), leaving that feature fully vanilla.
    internal static class Guard
    {
        private static readonly object Lock = new object();
        private static readonly List<Type> ToUnpatch = new List<Type>();

        // Patches run on both the main and the sandbox thread.
        public static void Fail(Type patch, Exception e)
        {
            lock (Lock)
            {
                if (!Plugin.FailedPatches.Add(patch))
                {
                    return;
                }
                ToUnpatch.Add(patch);
            }
            Plugin.Log.LogError($"{patch.Name} failed and is now off (game update?): {e}");
        }

        // Main thread only. True when it removed something.
        public static bool UnpatchFailed(Harmony harmony)
        {
            Type[] failed;
            lock (Lock)
            {
                if (ToUnpatch.Count == 0)
                {
                    return false;
                }
                failed = ToUnpatch.ToArray();
                ToUnpatch.Clear();
            }
            foreach (var type in failed)
            {
                try
                {
                    Unpatch(harmony, type);
                    if (type == typeof(PlacementPinPatch))
                    {
                        PlacementPinPatch.RestoreAll();
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Could not unpatch {type.Name}: {e}");
                }
            }
            return true;
        }

        private static void Unpatch(Harmony harmony, Type type)
        {
            foreach (var method in new List<System.Reflection.MethodBase>(harmony.GetPatchedMethods()))
            {
                var info = Harmony.GetPatchInfo(method);
                foreach (var patch in info.Prefixes)
                {
                    Remove(harmony, method, patch, type);
                }
                foreach (var patch in info.Postfixes)
                {
                    Remove(harmony, method, patch, type);
                }
            }
        }

        private static void Remove(Harmony harmony, System.Reflection.MethodBase method, Patch patch, Type type)
        {
            if (patch.owner == harmony.Id && patch.PatchMethod.DeclaringType == type)
            {
                harmony.Unpatch(method, patch.PatchMethod);
            }
        }
    }
}
