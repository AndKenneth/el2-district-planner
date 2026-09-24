using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace DistrictPlanner
{
    // The placement tooltip's "Placement Details" asks the simulation for a breakdown of the hovered tile, which first
    // checks that the district can be placed there as things stand. A buyable tile (BuyablePlacement) fails that check
    // until its Foundation is bought, so the breakdown came back empty. Skip the check for those tiles; the breakdown
    // then reads the tile's evaluation from the placement cursor snapshot, where BuyablePlacement added it.
    [HarmonyPatch(typeof(SimulationEvaluator), "ValidateDistrictBreakdown")]
    internal static class PlacementBreakdownPatch
    {
        private static void Prefix(RequestDistrictPlacementBreakdown request)
        {
            if (request != null && request.ExtensionDefinition.ToString() == PlacementDetails.DistrictName
                && PlacementDetails.TryGet(request.TileIndex, out var details) && details.Offer.HasValue)
            {
                request.Options |= RequestDistrictPlacementBreakdown.RequestDistrictPlacementBreakdownOptions.IgnorePlacementValidity;
            }
        }
    }
}
