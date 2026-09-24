using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace DistrictPlanner
{
    // ConstructibleHelper.GetLevelUpWeight(tile, settlement, definition) is how much a district on `tile` counts towards
    // its neighbours' level-up; it is 0 when no district stands there yet. While BuyableTiles evaluates a tile that
    // needs a Foundation, answer as if the placed district were already there. Sandbox thread only.
    [HarmonyPatch(typeof(ConstructibleHelper), nameof(ConstructibleHelper.GetLevelUpWeight), typeof(int), typeof(Settlement), typeof(DistrictDefinition))]
    internal static class LevelUpWeightPatch
    {
        public static int SimulatedDistrictAt = -1;

        private static bool Prefix(int tileIndex, Settlement settlement, DistrictDefinition definitionOverride, ref int __result)
        {
            if (tileIndex != SimulatedDistrictAt || definitionOverride == null)
            {
                return true;
            }
            __result = ConstructibleHelper.GetLevelUpWeight(definitionOverride, settlement?.Empire.Entity as MajorEmpire);
            return false;
        }
    }
}
