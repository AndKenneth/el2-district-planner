using System;
using System.Collections.Generic;
using System.Linq;
using Amplitude;
using Amplitude.Mercury;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Simulation;
using Amplitude.Mercury.Terrain;

namespace DistrictPlanner
{
    // Offers the tiles that need a Foundation bought with Influence first as extra placement candidates: they are
    // appended to the cursor's DistrictPlacementEvaluation.ValidTiles so they get pins, hover feedback and tooltips,
    // and PlacementClickPatch buys the Foundation before placing the district there. The best few that beat the
    // median valid tile are marked Recommended.
    // Sandbox thread only.
    internal static class BuyablePlacement
    {
        internal struct Offer
        {
            public FixedPoint InfluenceCost;
            public bool Affordable;
            public bool Recommended;
        }

        // Appends every buyable tile, registers their corrected scores with the scorer and marks up to
        // Plugin.BuyableMaxRecommended whose fit beats the median valid tile. Returns them by tile index.
        public static Dictionary<int, Offer> Append(DistrictPlacementEvaluation eval, DistrictDefinition district, SimulationEntityGUID settlementGuid, FitScore.Scorer scorer)
        {
            var offers = new Dictionary<int, Offer>();
            int max = Plugin.BuyableMaxRecommended.Value;
            if (!Plugin.OfferBuyableTiles.Value || eval.ValidTileCount == 0
                || !Amplitude.Mercury.Sandbox.Sandbox.SimulationEntityRepository.TryGetSimulationEntity(settlementGuid, out Settlement settlement))
            {
                return offers;
            }

            var fits = new List<float>();
            for (int i = 0; i < eval.ValidTileCount; i++)
            {
                fits.Add(scorer.Of(in eval.ValidTiles[i]).Fit);
            }
            fits.Sort();
            float median = fits[fits.Count / 2];
            float threshold = median + Math.Max(Plugin.RecommendMinAdvantage.Value, Math.Abs(median) * Plugin.RecommendRelativeAdvantage.Value);

            var candidates = BuyableTiles.Find(district, settlement, eval)
                .Where(c => c.CanBuildFoundation && (StaticString.IsNullOrEmpty(c.Tile.Resource) || district.ExtractResource))
                .ToList();
            if (candidates.Count == 0)
            {
                return offers;
            }
            // A Foundation can be bought next to any district in the region, including another city's, which makes
            // tiles far from this city buyable at a steep price. Only those detached tiles are charged, and only for
            // their premium over the cheapest Foundation on offer now (prices rise as the city grows).
            FixedPoint cheapest = candidates.Min(c => c.InfluenceCost);

            var references = FoundationTilesByLevel(eval);
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                AsIfFoundation(ref candidate.Tile, references, district, settlement);
                candidates[i] = candidate;
            }

            var picked = new List<(BuyableTiles.Candidate candidate, FitScore score)>();
            foreach (var candidate in candidates)
            {
                FitScore score = scorer.Of(in candidate.Tile)
                    .WithBuyCost(IsDetached(candidate.Tile.TileIndex, settlement) ? scorer.BuyCostScore(candidate.InfluenceCost - cheapest) : 0f);
                picked.Add((candidate, score));
            }

            var recommended = new HashSet<int>(picked
                .Where(p => p.score.Fit >= threshold && p.score.Fit > 0f)
                .OrderByDescending(p => p.score.Fit)
                .Take(max)
                .Select(p => p.candidate.Tile.TileIndex));

            foreach (var (candidate, score) in picked)
            {
                int index = eval.ValidTileCount;
                if (eval.ValidTiles.Length <= index)
                {
                    Array.Resize(ref eval.ValidTiles, index + 1);
                }
                if (eval.ConditionalFlagsTileIndexes.Length <= index)
                {
                    Array.Resize(ref eval.ConditionalFlagsTileIndexes, index + 1);
                }
                eval.ValidTiles[index] = candidate.Tile;
                eval.ConditionalFlagsTileIndexes[index] = DistrictPlacementConditionFlags.None;
                eval.ValidTileCount++;

                scorer.Override(candidate.Tile.TileIndex, score);
                offers[candidate.Tile.TileIndex] = new Offer
                {
                    InfluenceCost = candidate.InfluenceCost,
                    Affordable = candidate.FailureFlags == ConstructionFailureFlags.None,
                    Recommended = recommended.Contains(candidate.Tile.TileIndex),
                };
            }
            return offers;
        }

        // The already-valid tile with a Foundation (the evaluator's full path) and the lowest district gains, per level the
        // district reaches there: its gains are the level's flat yields without on-tile bonuses.
        private static Dictionary<int, DistrictPlacementEvaluation.ValidTile> FoundationTilesByLevel(DistrictPlacementEvaluation eval)
        {
            var byLevel = new Dictionary<int, DistrictPlacementEvaluation.ValidTile>();
            for (int i = 0; i < eval.ValidTileCount; i++)
            {
                var tile = eval.ValidTiles[i];
                if (tile.HasFoundations && (!byLevel.TryGetValue(tile.NextDistrictLevel, out var best)
                    || (float)tile.NewDistrictGains.GetTotalFims() < (float)best.NewDistrictGains.GetTotalFims()))
                {
                    byLevel[tile.NextDistrictLevel] = tile;
                }
            }
            return byLevel;
        }

        // On an empty tile the evaluator places the district at level 1 whatever its neighbours, drops its flat empire and
        // settlement gains, and counts the tile's natural yields as the district's gain. Evaluate it instead as if the
        // Foundation were already bought, like the tiles that have one: level from its neighbours, that level's district
        // gains and flat gains from a comparable Foundation tile, natural yields already worked.
        private static void AsIfFoundation(ref DistrictPlacementEvaluation.ValidTile tile, Dictionary<int, DistrictPlacementEvaluation.ValidTile> references,
            DistrictDefinition district, Settlement settlement)
        {
            int levels = ConstructibleHelper.CountLevelsGainedAt(tile.TileIndex, settlement, district);
            int level = 1 + levels;
            tile.HasFoundations = true;
            tile.WillLevelUp = levels > 0;
            tile.NextDistrictLevel = level;
            if (references.TryGetValue(level, out var reference) || references.TryGetValue(1, out reference))
            {
                if (reference.NextDistrictLevel == level)
                {
                    tile.NewDistrictGains = reference.NewDistrictGains;
                }
                tile.NewEmpireGains += reference.NewEmpireGains - reference.OldEmpireGains;
                tile.NewSettlementGains += reference.NewSettlementGains - reference.OldSettlementGains;
            }
        }

        // No neighbour holds one of this settlement's districts, built or queued.
        private static bool IsDetached(int tileIndex, Settlement settlement)
        {
            for (int d = 0; d < 6; d++)
            {
                int neighbour = WorldPosition.GetTileIndexNeighbour(tileIndex, (Hexagon.Direction)d);
                if (neighbour >= 0 && (settlement.GetDistrictAt(neighbour) != null || QueuedConstructions.IsQueuedBy(settlement, neighbour)))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
