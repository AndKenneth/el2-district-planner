using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Data.World;
using Amplitude.Mercury.EffectMapper;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Presentation;
using Amplitude.Mercury.Simulation;
using Amplitude.Mercury.UI;
using Amplitude.Mercury.UI.Tooltips;
using HarmonyLib;

namespace DistrictPlanner
{
    // While placing a district, its tooltip lists every yield effect it can get under "Details". Colour each line by
    // whether it applies on the hovered tile:
    // - adjacency synergies: when they fire there (SynergyTriggers), green, red for a penalty, " x2" when stacked. Their
    //   lines are found through the game's own translation (SimulationEvaluatorHelper.TryGetSynergyLocalization);
    // - on-tile effects (Effect_District_*_OnTerrainType_*): green when the tile qualifies;
    // - every other yield line (flat per level, empire effects on the district type) always applies: green.
    // Then, per neighbour the placement changes, a heading (name, level-up, total change) and the lines of that
    // neighbour's own Details that change.
    [HarmonyPatch(typeof(ConstructibleEffectsTooltipBrick), "BindRequestedData")]
    internal static class ConstructibleEffectsPatch
    {
        private sealed class DistrictLines
        {
            // Every line of the district's synergies, fired or not: they are conditional.
            public readonly HashSet<string> Synergy = new HashSet<string>();

            // Lines of its on-tile effects, by descriptor name.
            public readonly Dictionary<string, string> OnTileDescriptorByLine = new Dictionary<string, string>();
        }

        private static readonly Dictionary<string, DistrictLines> LinesByDistrict = new Dictionary<string, DistrictLines>();

        private static void Postfix(ConstructibleEffectsTooltipBrick __instance, ConstructibleEffectAsyncOperation asyncOperation, bool __result)
        {
            try
            {
                if (!__result || !Plugin.ShowSynergyOnPins.Value
                    || !(Presentation.PresentationCursorController.CurrentCursor is DistrictPlacementCursor)
                    || __instance.constructibleEvaluation == null
                    || __instance.constructibleEvaluation.Name.ToString() != PlacementDetails.DistrictName)
                {
                    return;
                }
                int hovered = Presentation.PresentationCursorController.CurrentHighlightedPosition;
                if (!PlacementDetails.TryGet(hovered, out var details))
                {
                    return;
                }

                var lines = LinesOf(PlacementDetails.DistrictName);
                var fired = new Dictionary<string, SynergyTriggers.Trigger>();
                foreach (var trigger in details.Triggers)
                {
                    foreach (string line in SynergyLines(trigger.Synergy))
                    {
                        fired[line] = trigger;
                    }
                }

                var output = new List<string>();
                foreach (string raw in __instance.description.Text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (fired.TryGetValue(line, out var trigger))
                    {
                        output.Add(Mark(line, trigger.Negative, trigger.Count));
                    }
                    else if (lines.Synergy.Contains(line))
                    {
                        output.Add(raw);
                    }
                    else if (lines.OnTileDescriptorByLine.TryGetValue(line, out string descriptor))
                    {
                        output.Add(AppliesOnTile(descriptor, in details.NaturalYields) ? Mark(line, false, 1) : raw);
                    }
                    else
                    {
                        output.Add(IsYieldLine(line) ? Mark(line, false, 1) : raw);
                    }
                }
                string text = string.Join("\n", output);

                // Not in the Details list: what the placement changes on each neighbour, under the neighbour's name, with
                // the lines of that neighbour's own Details that change (synergies starting or stopping, per-level
                // yields when it levels up).
                foreach (var effect in details.NeighbourEffects)
                {
                    var section = new List<string>();
                    foreach (var change in effect.Synergies)
                    {
                        foreach (string line in SynergyLines(change.Synergy))
                        {
                            section.Add(Mark(line, change.Negative, change.Count));
                        }
                    }
                    if (effect.NextLevel > 0)
                    {
                        foreach (string line in LevelScaledLines(effect.DistrictDefinition))
                        {
                            section.Add(Mark(line, false, 1));
                        }
                    }
                    string heading = Utils.TextUtils.BoldText(Utils.TextUtils.ColorizeText(SourceNames.Localize(effect.Definition), Utils.ColorUtils.LabelGold));
                    if (effect.NextLevel > 0)
                    {
                        heading += " " + Utils.TextUtils.StartLocalize("%DistrictLevelUpPreviewPin_Level").AddParam(effect.NextLevel).Translate("Lv. {0}");
                    }
                    string yields = YieldText.Format(effect.Yields);
                    if (yields.Length > 0)
                    {
                        heading += "  " + yields;
                    }
                    text += "\n\n" + heading + (section.Count > 0 ? "\n" + string.Join("\n", section) : "");
                }

                // A buyable tile's Foundation price, in the game's own words ("Foundation Cost: 50[Influence]"). Not on the
                // cursor marker: the game unbinds this tooltip whenever the marker box shows.
                if (details.Offer is BuyablePlacement.Offer offer)
                {
                    string cost = Utils.FormatUtils.Cost.Format(offer.InfluenceCost, UIResourceType.Influence, offer.Affordable);
                    text = Utils.TextUtils.StartLocalize("%ConstructionMenu_HoverConstructibleFoundation").AddParam(cost).Translate("Foundation Cost: {0}")
                        + "\n\n" + text;
                }
                __instance.description.Text = text;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Tooltip highlight failed: " + e);
            }
        }

        private static string Mark(string line, bool negative, int count) =>
            Utils.TextUtils.BoldText(Utils.TextUtils.ColorizeText(line, negative ? Utils.ColorUtils.Negative : Utils.ColorUtils.Positive))
            + (count > 1 ? " ×" + count : "");

        // A yield effect line starts with its signed amount, e.g. "+6 [Dust] per District Level".
        private static bool IsYieldLine(string line) => line.Length > 1 && (line[0] == '+' || line[0] == '-') && char.IsDigit(line[1]);

        private static IEnumerable<string> SynergyLines(string synergy) =>
            SimulationEvaluatorHelper.TryGetSynergyLocalization(new Amplitude.StaticString(synergy), out string localized)
                ? localized.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())
                : Enumerable.Empty<string>();

        // The common districts' on-tile conditions (from the game data), read from the tile's own yields: Food on a
        // tile yielding Food, Industry/Money on an Industry/Money tile, Science on a tile with two or more yield types.
        // Other conditions (sand, near water) are left uncoloured.
        private static bool AppliesOnTile(string descriptor, in FimsInfo natural)
        {
            if (descriptor.Contains("MixedFIDSI"))
            {
                int types = new[] { natural.Food, natural.Industry, natural.Money, natural.Science, natural.Influence, natural.Approval }
                    .Count(v => (float)v > 0f);
                return types >= 2;
            }
            if (descriptor.Contains("OnTerrainType_Food"))
            {
                return (float)natural.Food > 0f;
            }
            if (descriptor.Contains("OnTerrainType_Industry"))
            {
                return (float)natural.Industry > 0f;
            }
            if (descriptor.Contains("OnTerrainType_Money"))
            {
                return (float)natural.Money > 0f;
            }
            return false;
        }

        private static readonly Dictionary<string, List<string>> LevelScaledLinesByDistrict = new Dictionary<string, List<string>>();

        // Tooltip lines of a district's flat, level-scaled descriptors (Effect_District_<Family>_FlatNN; the
        // tile-conditional OnTerrainType ones are not level-scaled), translated by the game's own effect pipeline.
        private static List<string> LevelScaledLines(string districtName)
        {
            if (LevelScaledLinesByDistrict.TryGetValue(districtName, out var lines))
            {
                return lines;
            }
            lines = new List<string>();
            // A levelled-up district keeps the lower levels' effects (the per-level yield is on Tier1), so walk the chain.
            TryGetDistrict(districtName, out var district);
            var seen = new HashSet<string>();
            for (var level = district; level != null; level = level.LeveledDownDistrict)
            {
                foreach (var descriptor in level.AllDescriptors)
                {
                    string name = descriptor.ElementName.ToString();
                    if (seen.Add(name) && name.StartsWith("Effect_District_") && name.Contains("_Flat") && !name.Contains("OnTerrainType"))
                    {
                        lines.AddRange(DescriptorLines(descriptor.ElementName));
                    }
                }
            }
            LevelScaledLinesByDistrict[districtName] = lines;
            return lines;
        }

        private static bool TryGetDistrict(string districtName, out DistrictDefinition district)
        {
            district = null;
            var database = Amplitude.Framework.Databases.GetDatabase<ConstructibleDefinition>();
            return database != null && database.TryGetValue(new Amplitude.StaticString(districtName), out var definition)
                && (district = definition as DistrictDefinition) != null;
        }

        // A descriptor's effect lines, as the game's tooltips word them.
        private static IEnumerable<string> DescriptorLines(Amplitude.StaticString descriptor)
        {
            int empireIndex = Snapshots.GameSnapshot.PresentationData.LocalEmpireInfo.EmpireIndex;
            var working = WorkingEffectEvaluation.GetWorkingEffectEvaluation(empireIndex);
            working.Add(descriptor);
            var evaluation = default(EffectEvaluation);
            working.FillOutputEvaluation(ref evaluation);
            var lines = new List<string>();
            for (int i = 0; i < evaluation.SimpleEffectsCount; i++)
            {
                lines.Add(evaluation.SimpleEffectLines[i].Trim());
            }
            for (int i = 0; i < evaluation.EffectCount; i++)
            {
                lines.Add(evaluation.EffectLines[i].Trim());
            }
            return lines;
        }

        private static DistrictLines LinesOf(string districtName)
        {
            if (LinesByDistrict.TryGetValue(districtName, out var lines))
            {
                return lines;
            }
            lines = new DistrictLines();
            if (TryGetDistrict(districtName, out var district))
            {
                // Synergies accumulate along the level chain.
                for (var level = district; level != null; level = level.LeveledDownDistrict)
                {
                    foreach (var synergy in level.OwnSynergyReferences ?? new Amplitude.Framework.DatatableElementReference[0])
                    {
                        lines.Synergy.UnionWith(SynergyLines(synergy.ElementName.ToString()));
                    }
                }

                foreach (var descriptor in district.AllDescriptors)
                {
                    string name = descriptor.ElementName.ToString();
                    if (name.Contains("OnTerrainType"))
                    {
                        foreach (string line in DescriptorLines(descriptor.ElementName))
                        {
                            lines.OnTileDescriptorByLine[line] = name;
                        }
                    }
                }
            }
            LinesByDistrict[districtName] = lines;
            return lines;
        }
    }
}
