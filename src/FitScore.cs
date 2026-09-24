using System;
using System.Collections.Generic;
using Amplitude.Mercury.Interop;

namespace DistrictPlanner
{
    // Placement score that favours putting a district where it pairs with its surroundings, over where it merely
    // bumps stats. Raw is the game's own score (StoreOverrallGain). From it:
    // - Adjacency: synergy gains the district receives (terrain, coast, river, mountain, anomaly, deposit and
    //   district neighbours all go through SynergyDefinition) plus those it gives neighbours that are not levelling up.
    // - OnTile: descriptor bonuses for what the district sits on (terrain/yield type, anomaly, near coast). The
    //   evaluator folds them into NewDistrictGains, so they are the excess over the lowest NewDistrictGains among the
    //   evaluation's tiles at the same district level. Only on tiles with a district already there (Path A); the
    //   evaluator drops them on empty tiles.
    // - NeighbourLevelUp: total gain of neighbours that level up. Mostly a property of the tile, since most districts
    //   placed there count towards the neighbour's level-up requirement.
    // - NeighbourFoundation: natural yields of neighbours the district raises a Foundation on (e.g. a Keep). Real
    //   income, but those tiles could also be bought with Influence, so they weigh less than synergy.
    // - BuyCost: for a buyable tile detached from the city (no neighbouring district of this settlement), its
    //   Foundation's Influence price above the cheapest Foundation on offer, spread over Plugin.BuyCostPaybackTurns
    //   and weighted like Influence income, subtracted from the fit.
    // - HasNegativeSynergy: the district takes or gives a negative synergy (e.g. Population next to Industry,
    //   -5 Approval). Such tiles lose Plugin.NegativeSynergyPenalty, so they only win when every tile has one.
    // Not simulated: village descriptors (Last Lord estates only). Percent/multiply synergies score 0, but their base is
    // 0 in the data too, so they are inert in the game as well.
    internal readonly struct FitScore
    {
        public readonly float Raw;
        public readonly float Adjacency;
        public readonly float OnTile;
        public readonly float NeighbourLevelUp;
        public readonly float NeighbourFoundation;
        public readonly float BuyCost;
        public readonly bool HasNegativeSynergy;

        // OnTile per yield type, unweighted, for display on the placement pin.
        public readonly FimsInfo OnTileYields;

        private FitScore(float raw, float adjacency, float onTile, float neighbourLevelUp, float neighbourFoundation, FimsInfo onTileYields, bool hasNegativeSynergy, float buyCost = 0f)
        {
            Raw = raw;
            Adjacency = adjacency;
            OnTile = onTile;
            NeighbourLevelUp = neighbourLevelUp;
            NeighbourFoundation = neighbourFoundation;
            BuyCost = buyCost;
            HasNegativeSynergy = hasNegativeSynergy;
            OnTileYields = onTileYields;
        }

        public float Synergy => Adjacency + OnTile;

        public float Fit => Raw
            + (Plugin.SynergyWeight.Value - 1f) * Synergy
            + (Plugin.NeighbourLevelUpWeight.Value - 1f) * NeighbourLevelUp
            + (Plugin.NeighbourFoundationWeight.Value - 1f) * NeighbourFoundation
            - BuyCost
            - (HasNegativeSynergy ? Plugin.NegativeSynergyPenalty.Value : 0f);

        // Rounded so that float noise does not split tiers that the game would consider equal.
        public float TierKey => (float)Math.Round(Fit, 2);

        public FitScore WithBuyCost(float buyCost) => new FitScore(Raw, Adjacency, OnTile, NeighbourLevelUp, NeighbourFoundation, OnTileYields, HasNegativeSynergy, buyCost);

        public override string ToString() =>
            $"fit {Fit:0.##} (raw {Raw:0.##}, adjacency {Adjacency:0.##}, on-tile {OnTile:0.##}, neighbour level-up {NeighbourLevelUp:0.##}, neighbour foundation {NeighbourFoundation:0.##}, buy cost {BuyCost:0.##}{(HasNegativeSynergy ? ", negative synergy" : "")})";

        // Scores the tiles of one evaluation; OnTile needs the per-level baseline across all of its tiles.
        internal sealed class Scorer
        {
            private readonly FimsInfo ponderation;
            private readonly Dictionary<int, FimsInfo> baselineByLevel = new Dictionary<int, FimsInfo>();
            private readonly Dictionary<int, FitScore> overrides = new Dictionary<int, FitScore>();

            public void Override(int tileIndex, FitScore score) => overrides[tileIndex] = score;

            // A one-off Influence cost as a per-turn score, comparable with yields.
            public float BuyCostScore(Amplitude.FixedPoint influence) =>
                (float)influence * (float)ponderation.Influence / System.Math.Max(1f, Plugin.BuyCostPaybackTurns.Value);

            public Scorer(DistrictPlacementEvaluation eval, FimsInfo ponderation)
            {
                this.ponderation = ponderation;
                for (int i = 0; i < eval.ValidTileCount; i++)
                {
                    ref var tile = ref eval.ValidTiles[i];
                    if (!tile.HasFoundations)
                    {
                        continue;
                    }
                    baselineByLevel[tile.NextDistrictLevel] = baselineByLevel.TryGetValue(tile.NextDistrictLevel, out var min)
                        ? Min(min, tile.NewDistrictGains)
                        : tile.NewDistrictGains;
                }
            }

            public FitScore Of(in DistrictPlacementEvaluation.ValidTile tile)
            {
                if (overrides.TryGetValue(tile.TileIndex, out var overridden))
                {
                    return overridden;
                }
                FimsInfo multiplier = tile.NewBonusMultiplier;
                FimsInfo adjacencyYields = tile.NewSynergySum * multiplier - tile.OldSynergySum;
                bool negative = false;
                for (int d = 0; tile.NewSynergies != null && d < tile.NewSynergies.Length; d++)
                {
                    negative |= HasNegative(tile.NewSynergies[d] * multiplier - (tile.OldSynergies != null ? tile.OldSynergies[d] : default));
                }
                float neighbourLevelUp = 0f;
                float neighbourFoundation = 0f;
                if (tile.NeighbourTiles != null)
                {
                    foreach (var neighbour in tile.NeighbourTiles)
                    {
                        if (neighbour.TileIndex < 0)
                        {
                            continue;
                        }
                        if (neighbour.WillLevelUp)
                        {
                            neighbourLevelUp += Weighted(neighbour.DeltaTotal);
                        }
                        else
                        {
                            adjacencyYields.Add(neighbour.DeltaSynergy);
                            negative |= HasNegative(neighbour.DeltaSynergy);
                            if (neighbour.WillRaiseFoundation)
                            {
                                neighbourFoundation += Weighted(neighbour.DeltaTotal - neighbour.DeltaSynergy);
                            }
                        }
                    }
                }

                FimsInfo onTileYields = default;
                if (tile.HasFoundations && baselineByLevel.TryGetValue(tile.NextDistrictLevel, out var baseline))
                {
                    onTileYields = (tile.NewDistrictGains - baseline) * multiplier;
                }

                return new FitScore(Weighted(tile.DeltaTotalWithNeighbours), Weighted(adjacencyYields), Weighted(onTileYields),
                    neighbourLevelUp, neighbourFoundation, onTileYields, negative);
            }

            // Any yield below -0.5 (float noise and rounding aside).
            private static bool HasNegative(in FimsInfo fims) =>
                (float)fims.Food < -0.5f || (float)fims.Industry < -0.5f || (float)fims.Money < -0.5f || (float)fims.Science < -0.5f
                || (float)fims.Influence < -0.5f || (float)fims.Approval < -0.5f;

            private float Weighted(FimsInfo fims)
            {
                fims.Multiply(in ponderation);
                return (float)fims.GetTotalFims();
            }

            private static FimsInfo Min(FimsInfo a, in FimsInfo b)
            {
                a.Food = Amplitude.FixedPoint.Min(a.Food, b.Food);
                a.Industry = Amplitude.FixedPoint.Min(a.Industry, b.Industry);
                a.Money = Amplitude.FixedPoint.Min(a.Money, b.Money);
                a.Science = Amplitude.FixedPoint.Min(a.Science, b.Science);
                a.Influence = Amplitude.FixedPoint.Min(a.Influence, b.Influence);
                a.Approval = Amplitude.FixedPoint.Min(a.Approval, b.Approval);
                a.InfluenceBuyCost = Amplitude.FixedPoint.Min(a.InfluenceBuyCost, b.InfluenceBuyCost);
                a.Cadaver = Amplitude.FixedPoint.Min(a.Cadaver, b.Cadaver);
                a.Fortification = Amplitude.FixedPoint.Min(a.Fortification, b.Fortification);
                return a;
            }
        }
    }
}
