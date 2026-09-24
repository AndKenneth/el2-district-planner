using Amplitude.Mercury.Interop;
using Amplitude.Mercury.UI;
using Amplitude.UI;
using Amplitude.UI.Renderers;
using HarmonyLib;

namespace DistrictPlanner
{
    // While hovering a placement tile, the game shows a DistrictLevelUpPreviewPin ("Lv. N") on each neighbour that
    // levels up. Show it on every neighbour that gives or receives yields instead, so the bonuses collated on the
    // hovered pin are also shown where they come from and where they are realised:
    // - synergy the placed district receives from that neighbour (district, anomaly, deposit, river, mountain, ...),
    //   from PlacementDetails;
    // - then the neighbour's own yield change, after "Lv. N" or "Foundation" when it levels up or gets one.
    internal static class NeighbourPins
    {
        // Synergy the hovered tile's new district receives from the neighbour on neighbourTileIndex.
        public static string ReceivedText(int neighbourTileIndex)
        {
            int hovered = Amplitude.Mercury.Presentation.Presentation.PresentationCursorController.CurrentHighlightedPosition;
            return PlacementDetails.TryGet(hovered, out var details) && details.ReceivedFrom.TryGetValue(neighbourTileIndex, out var text)
                ? text
                : string.Empty;
        }

        public static string OwnChangeText(in DistrictPlacementEvaluation.NeighbourTile neighbour, string levelUpText)
        {
            string yields = YieldText.Format(neighbour.DeltaTotal);
            if (neighbour.WillLevelUp)
            {
                return yields.Length > 0 ? levelUpText + " " + yields : levelUpText;
            }
            if (yields.Length == 0)
            {
                return string.Empty;
            }
            return neighbour.WillRaiseFoundation ? Plugin.TextFoundation.Value + " " + yields : yields;
        }
    }

    [HarmonyPatch(typeof(DistrictLevelUpPreviewPinsSubset), "IsDataRelevant")]
    internal static class NeighbourPinRelevancePatch
    {
        private static void Postfix(DistrictLevelUpPreviewPinsSubset __instance, int dataIndex, ref bool __result)
        {
            if (__result || !Plugin.ShowNeighbourGains.Value)
            {
                return;
            }
            ref DistrictPlacementEvaluation.NeighbourTile data = ref __instance.GetData(dataIndex);
            __result = data.TileIndex >= 0
                && (YieldText.Format(data.DeltaTotal).Length > 0 || NeighbourPins.ReceivedText(data.TileIndex).Length > 0);
        }
    }

    [HarmonyPatch(typeof(DistrictLevelUpPreviewPin), "Refresh")]
    internal static class NeighbourPinTextPatch
    {
        private static void Postfix(int ___dataIndex, DistrictLevelUpPreviewPinsSubset ___districtLevelUpPreviewPinsSubset, UILabel ___levelUpLabel)
        {
            if (!Plugin.ShowNeighbourGains.Value || ___dataIndex < 0 || ___districtLevelUpPreviewPinsSubset == null)
            {
                return;
            }
            ref DistrictPlacementEvaluation.NeighbourTile data = ref ___districtLevelUpPreviewPinsSubset.GetData(___dataIndex);
            string source = NeighbourPins.ReceivedText(data.TileIndex);
            string own = NeighbourPins.OwnChangeText(in data, ___levelUpLabel.Text);
            ___levelUpLabel.Text = source.Length > 0 && own.Length > 0 ? source + "  " + own : source + own;
            // The level-up arrow only makes sense on a neighbour that levels up.
            var picto = ___levelUpLabel.transform.parent?.Find("LevelUpPicto")?.GetComponent<UITransform>();
            if (picto != null)
            {
                picto.VisibleSelf = data.WillLevelUp;
            }
        }
    }
}
