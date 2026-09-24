using System;
using Amplitude.Mercury.Data.Presentation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Presentation;
using HarmonyLib;

namespace DistrictPlanner
{
    // The vanilla hover feedback only marks synergy with small arrows on the hovered tile's edges. Also highlight the
    // neighbour tiles themselves: those the placed district receives synergy from (PlacementDetails.ReceivedFrom:
    // districts, anomalies, deposits, rivers, ...) and those that gain synergy from it. The feedback goes into the
    // same slots vanilla uses for a Keep's raised foundations, so vanilla stops it on the next update.
    [HarmonyPatch(typeof(DistrictPlacementTileFeedback), nameof(DistrictPlacementTileFeedback.UpdateWith))]
    internal static class SynergyHighlightPatch
    {
        // Runs after TileOutlinePatch's prefix, so these are its filtered arguments.
        private static void Postfix(DistrictPlacementTileFeedback __instance, DistrictPlacementEvaluation.ValidTile[] validTiles, int selectedValidTileIndex)
        {
            try
            {
                Apply(__instance, validTiles, selectedValidTileIndex);
            }
            catch (System.Exception e)
            {
                Guard.Fail(typeof(SynergyHighlightPatch), e);
            }
        }

        private static void Apply(DistrictPlacementTileFeedback __instance, DistrictPlacementEvaluation.ValidTile[] validTiles, int selectedValidTileIndex)
        {
            if (validTiles == null || selectedValidTileIndex < 0 || selectedValidTileIndex >= validTiles.Length
                || !Enum.TryParse(Plugin.SynergyTileHighlight.Value, out PresentationEntityLevelBuildEnums.TileFeedback feedback))
            {
                return;
            }
            ref DistrictPlacementEvaluation.ValidTile tile = ref validTiles[selectedValidTileIndex];
            if (tile.NeighbourTiles == null || !PlacementDetails.TryGet(tile.TileIndex, out var details))
            {
                return;
            }

            var slots = __instance.neighboursTileFeedbacks;
            foreach (var neighbour in tile.NeighbourTiles)
            {
                if (neighbour.TileIndex < 0 || __instance.neighboursTileFeedbackCount >= slots.Length
                    || !(details.ReceivedFrom.ContainsKey(neighbour.TileIndex) || neighbour.GainsSynergy)
                    || neighbour.WillRaiseFoundation)
                {
                    continue;
                }
                ref var slot = ref slots[__instance.neighboursTileFeedbackCount++];
                slot.FeedbackId = Presentation.PresentationTileFeedbackController.StartFeedback(neighbour.TileIndex, 0f, ref __instance.feedbackGroupName, feedback);
            }
        }
    }
}
