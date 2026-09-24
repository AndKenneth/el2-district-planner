using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Simulation;

namespace DistrictPlanner
{
    // Which of the placed district's own synergies fire on a tile, and how many times, counting queued neighbours as
    // built. The district tooltip's "Details" lists those synergies; ConstructibleEffectsPatch highlights the ones that
    // fire. Sandbox thread only: Evaluate and GetValidTiles use the evaluator's working buffers.
    internal static class SynergyTriggers
    {
        internal struct Trigger
        {
            public string Synergy;
            public int Count;
            public bool Negative;
        }

        public static List<Trigger> Of(SimulationEvaluator evaluator, in DistrictPlacementEvaluation.ValidTile tile, DistrictDefinition district, Settlement settlement)
        {
            var result = new List<Trigger>();
            int levels = ConstructibleHelper.CountLevelsGainedAt(tile.TileIndex, settlement, district);
            DistrictDefinition placed = ConstructibleHelper.GetFinalLevelUpDefinition(district, levels);
            SimulationEvaluator.RetrieveAllSynergiesAfterLevelUp(district, levels);
            var names = new List<StaticString>();
            for (int i = 0; i < SimulationEvaluator.WorkingListOfDatatableElementReferences.Length; i++)
            {
                names.Add(SimulationEvaluator.WorkingListOfDatatableElementReferences.Data[i].ElementName);
            }

            var queued = new DistrictDefinition[6];
            for (int d = 0; tile.NeighbourTiles != null && d < 6; d++)
            {
                int neighbour = tile.NeighbourTiles[d].TileIndex;
                if (neighbour >= 0 && QueuedConstructions.Current.TryGetValue(neighbour, out var definition))
                {
                    queued[d] = definition;
                }
            }

            foreach (var name in names)
            {
                FimsInfo gain = default;
                evaluator.Evaluate(name, ref gain, tile.TileIndex, placed, queued);
                if (gain.IsEmpty || result.Exists(t => t.Synergy == name.ToString()))
                {
                    continue;
                }
                var definition = evaluator.synergyDatabase.GetValue(name);
                result.Add(new Trigger
                {
                    Synergy = name.ToString(),
                    Count = evaluator.GetValidTiles(definition, tile.TileIndex, placed, queued),
                    Negative = (float)gain.GetTotalFims() < 0f,
                });
            }
            return result;
        }
    }
}
