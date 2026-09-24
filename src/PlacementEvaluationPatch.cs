using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Simulation;
using HarmonyLib;

namespace DistrictPlanner
{
    // EvaluateDistrictPlacement fills DistrictPlacementEvaluation.ValidTiles on the sandbox thread;
    // the placement cursor's pins and hex highlights only read ValidTile.FimsTier (< 3 = recommended).
    [HarmonyPatch(typeof(SimulationEvaluator), nameof(SimulationEvaluator.EvaluateDistrictPlacement))]
    internal static class PlacementEvaluationPatch
    {
        private const int NotRecommended = 3;

        // For LogEvaluations timings.
        private static long VanillaStart;

        private static void Prefix() => VanillaStart = System.Diagnostics.Stopwatch.GetTimestamp();

        private static void Postfix(SimulationEvaluator __instance, DistrictDefinition extension, SimulationEntityGUID settlementGuid, DistrictPlacementEvaluation districtPlacementEvaluation)
        {
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var eval = districtPlacementEvaluation;
                if (eval?.ValidTiles == null || eval.ValidTileCount == 0)
                {
                    return;
                }

                // Pretend queued districts are built, for synergy with them (QueuedConstructions).
                Amplitude.Mercury.Sandbox.Sandbox.SimulationEntityRepository.TryGetSimulationEntity(settlementGuid, out Settlement settlement);
                long vanillaTiles = eval.ValidTileCount;
                QueuedConstructions.Gather(settlement);
                for (int i = 0; settlement != null && i < eval.ValidTileCount; i++)
                {
                    QueuedConstructions.Apply(__instance, ref eval.ValidTiles[i], extension, settlement);
                }

                FimsInfo ponderation = __instance.scorePonderation;
                int[] vanillaTiers = new int[eval.ValidTileCount];
                for (int i = 0; i < vanillaTiers.Length; i++)
                {
                    vanillaTiers[i] = eval.ValidTiles[i].FimsTier;
                }

                var scorer = new FitScore.Scorer(eval, ponderation);

                // Only for the placement cursor: buyable tiles become clickable candidates (see PlacementClickPatch).
                var offers = DumpCommand.CurrentDumpId == null
                    ? BuyablePlacement.Append(eval, extension, settlementGuid, scorer)
                    : new Dictionary<int, BuyablePlacement.Offer>();

                long afterBuyable = watch.ElapsedMilliseconds, detailsMs = 0;
                if (DumpCommand.CurrentDumpId == null)
                {
                    var triggers = new Dictionary<int, List<SynergyTriggers.Trigger>>();
                    var neighbourEffects = new Dictionary<int, List<NeighbourEffects.Effect>>();
                    for (int i = 0; settlement != null && i < eval.ValidTileCount; i++)
                    {
                        triggers[eval.ValidTiles[i].TileIndex] = SynergyTriggers.Of(__instance, in eval.ValidTiles[i], extension, settlement);
                        neighbourEffects[eval.ValidTiles[i].TileIndex] = NeighbourEffects.Of(__instance, in eval.ValidTiles[i], extension, settlement);
                    }
                    detailsMs = watch.ElapsedMilliseconds - afterBuyable;
                    PlacementDetails.Publish(eval, scorer, offers, triggers, neighbourEffects, extension.Name);
                }

                if (Plugin.FixRecommendationTiers.Value)
                {
                    Retier(eval, extension, scorer, offers);
                }

                // Only the buyable tiles picked as clearly better than the typical tile are recommended (and so show their
                // pin and price); the rest are placeable but only show on hover.
                for (int i = 0; i < eval.ValidTileCount; i++)
                {
                    if (offers.TryGetValue(eval.ValidTiles[i].TileIndex, out var offer))
                    {
                        eval.ValidTiles[i].FimsTier = offer.Recommended
                            ? System.Math.Min(eval.ValidTiles[i].FimsTier, NotRecommended - 1)
                            : NotRecommended;
                    }
                }

                if (Plugin.DumpEvaluations.Value || DumpCommand.CurrentDumpId != null)
                {
                    EvaluationDump.Write(eval, extension, settlementGuid, vanillaTiers, t => scorer.Of(in t));
                }

                if (Plugin.LogEvaluations.Value && DumpCommand.CurrentDumpId == null)
                {
                    long vanilla = (System.Diagnostics.Stopwatch.GetTimestamp() - VanillaStart) * 1000 / System.Diagnostics.Stopwatch.Frequency - watch.ElapsedMilliseconds;
                    Plugin.Log.LogInfo($"{extension.Name}: {vanillaTiles} tiles + {eval.ValidTileCount - vanillaTiles} buyable; game {vanilla} ms, planner {watch.ElapsedMilliseconds} ms (buyable {afterBuyable}, details {detailsMs})");
                    LogEvaluation(eval, extension, scorer);
                }
            }
            catch (Exception e)
            {
                Guard.Fail(typeof(PlacementEvaluationPatch), e);
            }
        }

        private static bool Excluded(in DistrictPlacementEvaluation.ValidTile tile, DistrictDefinition extension) =>
            !StaticString.IsNullOrEmpty(tile.Resource) && !extension.ExtractResource;

        // Vanilla StoreOverrallGain never breaks after inserting a new best score, so it fills every slot with that
        // value and only tiles tied for first are ever recommended. Rebuild the tiers from the top 3 distinct fit
        // scores, so synergy with neighbours outranks raw stat and level-up gains, and only among tiles that clearly
        // beat the typical (median) tile, so a spread of near-identical tiles recommends nothing. Then make sure each
        // territory of the city's region (the city's own and each attached one) recommends at least its best tile.
        // Buyable tiles (offers) are left out: BuyablePlacement decides which of them to recommend.
        private static void Retier(DistrictPlacementEvaluation eval, DistrictDefinition extension, FitScore.Scorer scorer, Dictionary<int, BuyablePlacement.Offer> offers)
        {
            var keys = new float[eval.ValidTileCount];
            var eligible = new List<float>();
            for (int i = 0; i < eval.ValidTileCount; i++)
            {
                ref var tile = ref eval.ValidTiles[i];
                keys[i] = scorer.Of(in tile).TierKey;
                if (!Excluded(in tile, extension) && !offers.ContainsKey(tile.TileIndex))
                {
                    eligible.Add(keys[i]);
                }
            }

            float threshold = 0f;
            if (eligible.Count > 0)
            {
                var sorted = eligible.OrderBy(k => k).ToList();
                float median = sorted[sorted.Count / 2];
                threshold = median + System.Math.Max(Plugin.RecommendMinAdvantage.Value, System.Math.Abs(median) * Plugin.RecommendRelativeAdvantage.Value);
            }

            float[] tiers = eligible.Where(k => k > 0f && k >= threshold).Distinct().OrderByDescending(k => k).Take(NotRecommended).ToArray();

            for (int i = 0; i < eval.ValidTileCount; i++)
            {
                ref var tile = ref eval.ValidTiles[i];
                int tier = Excluded(in tile, extension) ? -1 : Array.IndexOf(tiers, keys[i]);
                tile.FimsTier = tier >= 0 ? tier : NotRecommended;
            }

            if (Plugin.RecommendBestPerTerritory.Value)
            {
                RecommendBestPerTerritory(eval, extension, scorer, keys, offers);
            }
        }

        private static void RecommendBestPerTerritory(DistrictPlacementEvaluation eval, DistrictDefinition extension, FitScore.Scorer scorer, float[] keys, Dictionary<int, BuyablePlacement.Offer> offers)
        {
            var bestByTerritory = new Dictionary<int, int>();
            var recommended = new HashSet<int>();
            var tileInfo = Amplitude.Mercury.Sandbox.Sandbox.World.TileInfo.Data;
            for (int i = 0; i < eval.ValidTileCount; i++)
            {
                ref var tile = ref eval.ValidTiles[i];
                int territory = tileInfo[tile.TileIndex].TerritoryIndex;
                if (tile.FimsTier < NotRecommended)
                {
                    recommended.Add(territory);
                }
                // A tile with a negative synergy is still eligible when it would gain something without its penalty, so
                // a territory where every tile has one still gets its least bad tile (clean tiles always rank higher).
                float gain = keys[i] + (scorer.Of(in tile).HasNegativeSynergy ? Plugin.NegativeSynergyPenalty.Value : 0f);
                if (Excluded(in tile, extension) || gain <= 0f || offers.ContainsKey(tile.TileIndex))
                {
                    continue;
                }
                if (!bestByTerritory.TryGetValue(territory, out int best) || keys[i] > keys[best])
                {
                    bestByTerritory[territory] = i;
                }
            }
            foreach (var pair in bestByTerritory)
            {
                if (!recommended.Contains(pair.Key))
                {
                    eval.ValidTiles[pair.Value].FimsTier = NotRecommended - 1;
                }
            }
        }

        private static void LogEvaluation(DistrictPlacementEvaluation eval, DistrictDefinition extension, FitScore.Scorer scorer)
        {
            var lines = Enumerable.Range(0, eval.ValidTileCount)
                .Select(i => eval.ValidTiles[i])
                .OrderBy(t => t.FimsTier)
                .Take(8)
                .Select(t => $"  tile {t.TileIndex}: tier {t.FimsTier} {scorer.Of(in t)} gains [{t.DeltaTotalWithNeighboursGains}] levelUp {t.WillLevelUp}");
            Plugin.Log.LogInfo($"{extension.Name}: {eval.ValidTileCount} valid tiles\n{string.Join("\n", lines)}");
        }
    }
}
