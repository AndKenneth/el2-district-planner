using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Simulation;

namespace DistrictPlanner
{
    // Tiles the cursor never offers because they have no Foundation yet, but where the player could buy one with
    // Influence (OrderBuildFoundationAt, instant) and then build the district. Mirrors the AI's approach in
    // SimulationEvaluator.TryGatherConstructiblePositions: valid positions ignoring the Foundation requirement,
    // intersected with the tiles where the empire's Foundation district could be placed right now.
    // Sandbox thread only: FillTileWithDistrictGains uses shared static working buffers.
    internal static class BuyableTiles
    {
        internal struct Candidate
        {
            public DistrictPlacementEvaluation.ValidTile Tile;
            public FixedPoint InfluenceCost;
            public ConstructibleTileState State;
            public ConstructionFailureFlags FailureFlags;
            public bool CanBuildFoundation;
        }

        public static List<Candidate> Find(DistrictDefinition district, Settlement settlement, DistrictPlacementEvaluation alreadyValid)
        {
            var result = new List<Candidate>();
            if (StaticString.IsNullOrEmpty(district.FoundationDistrict.ElementName) || !(settlement.Empire.Entity is MajorEmpire empire))
            {
                return result;
            }
            DistrictDefinition foundation = empire.DepartmentOfTheInterior.FoundationDefinition;
            if (foundation == null)
            {
                return result;
            }

            var foundationTiles = new CustomTileIndexArea();
            ConstructibleHelper.FillValidPositionFor(foundation, settlement, foundationTiles,
                ConstructibleHelper.ValidPositionOptions.IgnoreQueued | ConstructibleHelper.ValidPositionOptions.ForbidReplacement);

            var looseTiles = new CustomTileIndexArea();
            ConstructibleHelper.FillValidPositionFor(district, settlement, looseTiles,
                ConstructibleHelper.ValidPositionOptions.IgnoreFoundation | ConstructibleHelper.ValidPositionOptions.ForbidReplacement);

            var valid = new HashSet<int>();
            for (int i = 0; i < alreadyValid.ValidTileCount; i++)
            {
                valid.Add(alreadyValid.ValidTiles[i].TileIndex);
            }

            var evaluator = Amplitude.Mercury.Sandbox.Sandbox.SimulationEvaluator;
            for (int i = 0; i < looseTiles.TileIndexesCount; i++)
            {
                int tileIndex = looseTiles.TileIndexes[i];
                if (valid.Contains(tileIndex) || !foundationTiles.Contains(tileIndex))
                {
                    continue;
                }

                var candidate = new Candidate
                {
                    CanBuildFoundation = ConstructibleHelper.CanBuildFoundationAt(settlement, tileIndex),
                };
                ConstructibleHelper.GetFoundationStateAndFailuresAt(settlement, tileIndex, out candidate.State, out candidate.FailureFlags, out candidate.InfluenceCost);
                // On an empty tile the evaluator gives the new district no level-up weight, so neighbours it would level up
                // are missed; LevelUpWeightPatch treats this tile as if the district stood there.
                LevelUpWeightPatch.SimulatedDistrictAt = tileIndex;
                try
                {
                    evaluator.FillTileWithDistrictGains(ref candidate.Tile, district, settlement, tileIndex);
                }
                finally
                {
                    LevelUpWeightPatch.SimulatedDistrictAt = -1;
                }
                LevelUpCarryOver.Apply(evaluator, ref candidate.Tile, settlement);
                QueuedConstructions.Apply(evaluator, ref candidate.Tile, district, settlement);
                result.Add(candidate);
            }
            return result;
        }
    }
}
