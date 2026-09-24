using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Presentation;
using Amplitude.Mercury.Sandbox;
using Amplitude.Mercury.UI;
using Amplitude.UI;
using HarmonyLib;

namespace DistrictPlanner
{
    // Clicking a buyable tile (BuyablePlacement) asks to buy its Foundation, then sends the Foundation order followed by
    // the cursor's usual construction order. Orders run in sequence, so the Foundation exists when the district order
    // is validated.
    [HarmonyPatch(typeof(DistrictPlacementCursor), nameof(DistrictPlacementCursor.OnClick))]
    internal static class PlacementClickPatch
    {
        private static bool Prefix(DistrictPlacementCursor __instance, MouseButton mouseButton)
        {
            try
            {
                return Apply(__instance, mouseButton);
            }
            catch (System.Exception e)
            {
                Guard.Fail(typeof(PlacementClickPatch), e);
                return true;
            }
        }

        private static bool Apply(DistrictPlacementCursor __instance, MouseButton mouseButton)
        {
            if (mouseButton != MouseButton.Left || GodMode.Enabled || !__instance.isCurrentPositionValid
                || !PlacementDetails.TryGet(__instance.currentPosition, out var details) || !(details.Offer is BuyablePlacement.Offer offer))
            {
                return true;
            }

            string cost = YieldText.Cost(Amplitude.Mercury.Data.World.UIResourceType.Influence, offer.InfluenceCost);
            if (!offer.Affordable)
            {
                MessageModalWindow.ShowMessage(new MessageModalWindow.Message
                {
                    Title = Words.BuyTitle,
                    Description = Words.CannotAfford(cost),
                    Buttons = new[] { new MessageBoxButton.Data(MessageBox.Choice.Ok, null, isDismiss: true) },
                });
                return false;
            }

            int tileIndex = __instance.currentPosition;
            var settlementGuid = __instance.SettlementGUID;
            MessageModalWindow.ShowMessage(new MessageModalWindow.Message
            {
                Title = Words.BuyTitle,
                Description = Words.BuyQuestion(cost),
                Buttons = new[]
                {
                    new MessageBoxButton.Data(MessageBox.Choice.No, null, isDismiss: true),
                    new MessageBoxButton.Data(MessageBox.Choice.Yes, () =>
                    {
                        SandboxManager.PostOrder(new OrderBuildFoundationAt(settlementGuid, tileIndex));
                        return __instance.SendConstructionOrder();
                    }),
                },
            });
            return false;
        }
    }
}
