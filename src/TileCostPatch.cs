using System.Collections.Generic;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Presentation;
using HarmonyLib;

namespace DistrictPlanner
{
    // In the city's Foundation mode the per-tile yield markers show each tile's Foundation cost (InfluenceBuyCost).
    // Do the same during district placement for buyable tiles (BuyablePlacement), except the hovered one and the
    // neighbours whose markers the game blanks to draw the placement preview ("0 > 2") instead.
    [HarmonyPatch(typeof(PresentationFimsController), nameof(PresentationFimsController.CopyToEncodedFims))]
    internal static class TileCostPatch
    {
        private static void Postfix(PresentationFimsController __instance)
        {
            try
            {
                Apply(__instance);
            }
            catch (System.Exception e)
            {
                Guard.Fail(typeof(TileCostPatch), e);
            }
        }

        private static void Apply(PresentationFimsController __instance)
        {
            if (!Plugin.ShowBuyCostOnTiles.Value || __instance.fimsRenderer == null
                || (__instance.fimsStatus & PresentationFimsController.FimsRendererStatus.DistrictPlacement) == PresentationFimsController.FimsRendererStatus.None
                || !(Presentation.PresentationCursorController.CurrentCursor is DistrictPlacementCursor))
            {
                return;
            }

            var previewed = new HashSet<int> { Presentation.PresentationCursorController.CurrentHighlightedPosition };
            DistrictPlacementEvaluation eval = Snapshots.DistrictPlacementCursorSnapshot.PresentationData?.PlacementEvaluation;
            if (eval != null && eval.CurrentPositionIndex >= 0 && eval.CurrentPositionIndex < eval.ValidTileCount)
            {
                ref DistrictPlacementEvaluation.ValidTile current = ref eval.ValidTiles[eval.CurrentPositionIndex];
                previewed.Add(current.TileIndex);
                // Same condition as vanilla CopyToEncodedFims uses to blank a neighbour's markers.
                foreach (var neighbour in current.NeighbourTiles)
                {
                    if (neighbour.GainsSynergy || neighbour.WillRaiseFoundation || !neighbour.DeltaTotal.IsEmpty)
                    {
                        previewed.Add(neighbour.TileIndex);
                    }
                }
            }

            bool changed = false;
            foreach (var pair in PlacementDetails.Offers())
            {
                int tileIndex = pair.Key;
                if (previewed.Contains(tileIndex) || tileIndex < 0 || tileIndex >= __instance.fimsPerTile.Length)
                {
                    continue;
                }
                FimsInfo fims = __instance.fimsPerTile[tileIndex];
                fims.InfluenceBuyCost += pair.Value.InfluenceCost;
                __instance.SetEncodedFims(tileIndex, ref fims, FIMSFlags.None);
                changed = true;
            }
            if (changed)
            {
                __instance.fimsRenderer.SetTileEncodedContent(__instance.encodedRenderFims);
            }
        }
    }
}
