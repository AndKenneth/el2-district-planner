using Amplitude;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Simulation;

namespace DistrictPlanner
{
    // Vanilla bug: for a neighbour that levels up, the evaluator takes the Old yields from the live district but rebuilds
    // the New yields from what it can model (the district's descriptors, its own improvements, empire traits, synergy).
    // Bonuses that reach the district from elsewhere are live but not modelled, so the preview shows them as lost:
    // - an improvement built in another district (GetSettlementImprovementFims only counts improvements the district
    //   could hold itself), e.g. the Aspects' City Centre upgrade giving +2 Dust to Food districts;
    // - councillors, e.g. "Alchemical Genius" giving +1 Science to every non-Foundation district.
    // Measure what the evaluator's model misses at the district's current level and carry it over to the New yields:
    // those bonuses don't depend on the district's level. Measured rather than modelled, so if a game update starts
    // modelling a source, the gap for it drops to zero and nothing is counted twice. Sandbox thread only.
    internal static class LevelUpCarryOver
    {
        public static void Apply(SimulationEvaluator evaluator, ref DistrictPlacementEvaluation.ValidTile tile, Settlement settlement)
        {
            for (int i = 0; tile.NeighbourTiles != null && i < tile.NeighbourTiles.Length; i++)
            {
                ref var neighbour = ref tile.NeighbourTiles[i];
                District district = neighbour.WillLevelUp && neighbour.TileIndex >= 0 ? settlement.GetDistrictAt(neighbour.TileIndex) : null;
                if (district != null && Unmodelled(evaluator, district, in neighbour, out FimsInfo unmodelled))
                {
                    neighbour.NewDistrictGains += unmodelled;
                }
            }
        }

        // The live yields (Old, as the evaluator split them) minus what the evaluator's model gives the district as it
        // is now. False when a percent bonus applies, since it can't be told apart from the flat part.
        private static bool Unmodelled(SimulationEvaluator evaluator, District district, in DistrictPlacementEvaluation.NeighbourTile neighbour, out FimsInfo unmodelled)
        {
            FimsInfo districtGains = default, settlementGains = default, empireGains = default, percent = default;
            var resources = new FixedPoint[32];
            evaluator.GetLevelUpFims(district, district.DistrictDefinition, ref districtGains, ref settlementGains, ref empireGains, ref percent, ref resources, 0);
            FimsInfo modelled = districtGains + settlementGains + empireGains;
            FimsInfo live = neighbour.OldDistrictGains + neighbour.OldSettlementGains;
            unmodelled = default;
            if (!percent.IsEmpty)
            {
                return false;
            }
            // Only the yields the evaluator reads from the live district (SetFimsOnTile).
            unmodelled.Food = live.Food - modelled.Food;
            unmodelled.Industry = live.Industry - modelled.Industry;
            unmodelled.Money = live.Money - modelled.Money;
            unmodelled.Science = live.Science - modelled.Science;
            unmodelled.Influence = live.Influence - modelled.Influence;
            unmodelled.Approval = live.Approval - modelled.Approval;
            unmodelled.Fortification = live.Fortification - modelled.Fortification;
            return !unmodelled.IsEmpty;
        }
    }
}
