using System.Collections.Generic;
using System.Linq;
using Amplitude;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.UI;
using Amplitude.UI;
using HarmonyLib;
using UnityEngine;

namespace DistrictPlanner
{
    // On the hovered pin only, the level-up tag also shows what the neighbour pins cannot: on-tile bonuses, e.g.
    // "Lv. 2  +1[Food] on tile". The tag's label and table resize to their text, whereas the yield panel's lines and
    // background are a fixed width. While a candidate is hovered, the other candidates near it are hidden entirely (yields,
    // recommended badge and pin line), by hiding the pin's children rather than the pin, which the pin pool manages.
    [HarmonyPatch(typeof(PlacementPin), nameof(PlacementPin.Refresh))]
    internal static class PlacementPinPatch
    {
        private static void Postfix(PlacementPin __instance)
        {
            if (!Plugin.ShowSynergyOnPins.Value || __instance.dataIndex < 0 || !(__instance.PinsSubset is DistrictPlacementPinsSubset))
            {
                return;
            }

            ref DistrictPlacementEvaluation.ValidTile data = ref __instance.PinsSubset.GetData(__instance.dataIndex);
            int hovered = Amplitude.Mercury.Presentation.Presentation.PresentationCursorController.CurrentHighlightedPosition;
            PlacementDetails.TryGet(data.TileIndex, out var details);

            bool collapse = hovered != data.TileIndex && Plugin.CollapseOtherPins.Value && PlacementDetails.TryGet(hovered, out _)
                && Amplitude.Mercury.WorldPosition.GetTileIndexDistance(hovered, data.TileIndex) <= Plugin.CollapseRadius.Value;
            SetChildrenHidden(__instance, collapse);
            if (collapse)
            {
                return;
            }

            if (hovered != data.TileIndex || details == null)
            {
                return;
            }

            // Split mode: the hovered pin shows only what the new district itself yields (synergy it receives included);
            // what it does to each neighbour is on that neighbour's own label. Unhovered pins keep the combined total.
            if (Plugin.SplitHoverEffects.Value)
            {
                FimsInfo own = data.DeltaTotal;
                FimsInfo gain = own.Filtered();
                FimsInfo loss = (own * (FixedPoint)(-1)).Filtered();
                bool any = gain.CountFimsType() > 0 || loss.CountFimsType() > 0;
                __instance.topGroup.UITransform.VisibleSelf = any;
                __instance.bottomGroup.UITransform.VisibleSelf = any;
                if (any)
                {
                    PlacementPin_FimsGroup.Refresh(__instance.topGroup, __instance.bottomGroup, ref gain, ref loss);
                }
            }

            // Synergy with neighbours is shown on the neighbour pins (Gives / Gains); here only on-tile bonuses.
            string breakdown = string.Join("  ", details.Entries
                .Where(e => e.Direction == PlacementDetails.Direction.OnTile)
                .Select(e => YieldText.Format(e.Type, e.Amount) + " " + e.Text));
            if (breakdown.Length > 0)
            {
                // Vanilla shows "Lv. N" in the tag when the hovered tile levels up; keep it in front.
                __instance.levelUpLabel.Text = __instance.levelUpContainer.VisibleSelf ? __instance.levelUpLabel.Text + "  " + breakdown : breakdown;
                __instance.levelUpContainer.VisibleSelf = true;
            }

            // Draw the expanded pin above its neighbours' pins.
            __instance.transform.SetAsLastSibling();
        }

        // Children this patch hid, per pin, so they are restored exactly (vanilla Refresh sets the yield groups' and
        // level-up tag's visibility itself, before this postfix).
        // Kept in AppDomain data, not a static field: pins are pooled and outlive a hot reload, so a reloaded plugin must
        // still know what the previous one hid (only game and mscorlib types, so it survives the assembly swap).
        private static Dictionary<PlacementPin, List<UITransform>> HiddenChildren
        {
            get
            {
                const string key = "DistrictPlanner.HiddenPinChildren";
                if (!(System.AppDomain.CurrentDomain.GetData(key) is Dictionary<PlacementPin, List<UITransform>> hidden))
                {
                    hidden = new Dictionary<PlacementPin, List<UITransform>>();
                    System.AppDomain.CurrentDomain.SetData(key, hidden);
                }
                return hidden;
            }
        }

        // Shows everything this patch hid, e.g. when the plugin unloads.
        public static void RestoreAll()
        {
            foreach (var pin in new List<PlacementPin>(HiddenChildren.Keys))
            {
                if (pin != null)
                {
                    SetChildrenHidden(pin, false);
                }
            }
            HiddenChildren.Clear();
        }

        private static void SetChildrenHidden(PlacementPin pin, bool hidden)
        {
            if (HiddenChildren.TryGetValue(pin, out var children))
            {
                if (hidden)
                {
                    return;
                }
                HiddenChildren.Remove(pin);
                foreach (var child in children)
                {
                    if (child != null && child != pin.topGroup.UITransform && child != pin.bottomGroup.UITransform && child != pin.levelUpContainer)
                    {
                        child.VisibleSelf = true;
                    }
                }
                return;
            }
            if (!hidden)
            {
                return;
            }
            children = new List<UITransform>();
            foreach (Transform child in pin.transform)
            {
                var ui = child.GetComponent<UITransform>();
                if (ui != null && ui.VisibleSelf)
                {
                    ui.VisibleSelf = false;
                    children.Add(ui);
                }
            }
            HiddenChildren[pin] = children;
        }
    }

    // Vanilla only restyles pins when the hovered tile changes; refresh them so the breakdown follows the cursor and
    // the other candidates collapse or expand.
    [HarmonyPatch(typeof(PlacementPin), nameof(PlacementPin.PresentationCursorController_OnPositionChanged))]
    internal static class PlacementPinHoverPatch
    {
        private static void Postfix(PlacementPin __instance)
        {
            if (!Plugin.ShowSynergyOnPins.Value || __instance.dataIndex < 0 || !(__instance.PinsSubset is DistrictPlacementPinsSubset))
            {
                return;
            }
            __instance.Refresh();
        }
    }
}
