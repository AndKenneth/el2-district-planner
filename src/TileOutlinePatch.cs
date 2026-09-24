using System.Collections.Generic;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Presentation;
using HarmonyLib;

namespace DistrictPlanner
{
    // Buyable tiles (BuyablePlacement) are in the placement evaluation so they can be hovered and clicked, but should
    // not get the placeable-tile hex outline: hand the tile feedback a list without them, except the hovered one so its
    // hover outline, synergy arrows and neighbour feedback still show.
    [HarmonyPatch(typeof(DistrictPlacementTileFeedback), nameof(DistrictPlacementTileFeedback.UpdateWith))]
    internal static class TileOutlinePatch
    {
        private static readonly List<DistrictPlacementEvaluation.ValidTile> Filtered = new List<DistrictPlacementEvaluation.ValidTile>();

        private static void Prefix(ref DistrictPlacementEvaluation.ValidTile[] validTiles, ref int validTilesCount, ref int selectedValidTileIndex)
        {
            if (validTiles == null)
            {
                return;
            }
            Filtered.Clear();
            int selected = -1;
            for (int i = 0; i < validTilesCount; i++)
            {
                bool buyable = PlacementDetails.TryGet(validTiles[i].TileIndex, out var details) && details.Offer.HasValue;
                if (buyable && i != selectedValidTileIndex)
                {
                    continue;
                }
                if (i == selectedValidTileIndex)
                {
                    selected = Filtered.Count;
                }
                Filtered.Add(validTiles[i]);
            }
            if (Filtered.Count == validTilesCount)
            {
                return;
            }
            validTiles = Filtered.ToArray();
            validTilesCount = validTiles.Length;
            selectedValidTileIndex = selected;
        }
    }
}
