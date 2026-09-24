using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Simulation;
using Amplitude.Mercury.Terrain;

namespace DistrictPlanner
{
    // The evaluator only sees built districts (settlement.GetDistrictAt / DistrictMap), so a district queued next to a
    // candidate tile counts as empty ground. Add the synergy between the placed district and each queued neighbour, as
    // if the queue were finished, using the evaluator's own Evaluate with overriding definitions:
    // - received: the placed district's synergies, with queued neighbours overriding their tiles, into NewSynergies[d];
    // - given: each queued neighbour's synergies, with the placed district overriding its tile, into NewSynergy.
    // Everything downstream (pins, neighbour labels, FitScore, tile previews) reads those fields.
    // Not simulated: level-ups that queued districts would trigger or count towards.
    // Sandbox thread only: Evaluate uses the evaluator's working buffers.
    internal static class QueuedConstructions
    {
        // Queued districts of the local empire by tile, for the latest evaluation; read by SourceNames.Describe.
        public static Dictionary<int, DistrictDefinition> Current { get; private set; } = new Dictionary<int, DistrictDefinition>();

        public static void Gather(Settlement settlement)
        {
            var queued = new Dictionary<int, DistrictDefinition>();
            if (Plugin.CountQueuedConstructions.Value && settlement?.Empire.Entity is MajorEmpire empire)
            {
                for (int s = 0; s < empire.Settlements.Count; s++)
                {
                    var constructions = empire.Settlements[s].ConstructionQueue.Entity?.Constructions;
                    for (int i = 0; constructions != null && i < constructions.Length; i++)
                    {
                        ref var construction = ref constructions.Data[i];
                        // Queued Foundations give and receive no synergy; a district queued after one on the same tile wins.
                        if (construction.ConstructibleDefinition is DistrictDefinition district && !district.IsFoundationDistrict
                            && WorldPosition.IsWorldPositionValid(construction.WorldPosition))
                        {
                            queued[construction.WorldPosition] = district;
                        }
                    }
                }
            }
            Current = queued;
        }

        // Any construction (including a Foundation) queued on tileIndex by this settlement.
        public static bool IsQueuedBy(Settlement settlement, int tileIndex)
        {
            var constructions = settlement.ConstructionQueue.Entity?.Constructions;
            for (int i = 0; constructions != null && i < constructions.Length; i++)
            {
                if (constructions.Data[i].WorldPosition == tileIndex && constructions.Data[i].ConstructibleDefinition is DistrictDefinition)
                {
                    return true;
                }
            }
            return false;
        }

        public static void Apply(SimulationEvaluator evaluator, ref DistrictPlacementEvaluation.ValidTile tile, DistrictDefinition district, Settlement settlement)
        {
            if (Current.Count == 0 || tile.NeighbourTiles == null || tile.NewSynergies == null || Current.ContainsKey(tile.TileIndex))
            {
                return;
            }
            var around = new DistrictDefinition[6];
            bool any = false;
            for (int d = 0; d < 6; d++)
            {
                int neighbour = tile.NeighbourTiles[d].TileIndex;
                if (neighbour >= 0 && Current.TryGetValue(neighbour, out var queued))
                {
                    around[d] = queued;
                    any = true;
                }
            }
            if (!any)
            {
                return;
            }

            // What the placed district becomes on this tile, and all its synergies (as the evaluator does).
            int levels = ConstructibleHelper.CountLevelsGainedAt(tile.TileIndex, settlement, district);
            DistrictDefinition placed = ConstructibleHelper.GetFinalLevelUpDefinition(district, levels);
            SimulationEvaluator.RetrieveAllSynergiesAfterLevelUp(district, levels);
            var synergies = new List<StaticString>();
            for (int i = 0; i < SimulationEvaluator.WorkingListOfDatatableElementReferences.Length; i++)
            {
                synergies.Add(SimulationEvaluator.WorkingListOfDatatableElementReferences.Data[i].ElementName);
            }

            // Received, attributed per direction by adding queued neighbours one at a time (stack caps make it non-additive).
            var overrides = new DistrictDefinition[6];
            FimsInfo previous = Sum(evaluator, synergies, tile.TileIndex, placed, overrides);
            for (int d = 0; d < 6; d++)
            {
                if (around[d] == null)
                {
                    continue;
                }
                overrides[d] = around[d];
                FimsInfo next = Sum(evaluator, synergies, tile.TileIndex, placed, overrides);
                tile.NewSynergies[d] += next - previous;
                previous = next;
            }

            // Given: each queued neighbour's own synergies with and without the placed district next to it.
            for (int d = 0; d < 6; d++)
            {
                if (around[d] == null)
                {
                    continue;
                }
                ref var neighbour = ref tile.NeighbourTiles[d];
                var queuedSynergies = new List<StaticString>();
                foreach (var reference in around[d].OwnSynergyReferences ?? new Amplitude.Framework.DatatableElementReference[0])
                {
                    queuedSynergies.Add(reference.ElementName);
                }
                var neighbourOverrides = new DistrictDefinition[6];
                for (int e = 0; e < 6; e++)
                {
                    int next = WorldPosition.GetTileIndexNeighbour(neighbour.TileIndex, (Hexagon.Direction)e);
                    if (next >= 0 && next != tile.TileIndex && Current.TryGetValue(next, out var other))
                    {
                        neighbourOverrides[e] = other;
                    }
                }
                FimsInfo without = Sum(evaluator, queuedSynergies, neighbour.TileIndex, around[d], neighbourOverrides);
                neighbourOverrides[(d + 3) % 6] = placed;
                FimsInfo with = Sum(evaluator, queuedSynergies, neighbour.TileIndex, around[d], neighbourOverrides);
                neighbour.NewSynergy += with - without;
            }
        }

        private static FimsInfo Sum(SimulationEvaluator evaluator, List<StaticString> synergies, int tileIndex, DistrictDefinition onTile, DistrictDefinition[] neighbours)
        {
            FimsInfo total = default;
            foreach (var synergy in synergies)
            {
                evaluator.Evaluate(synergy, ref total, tileIndex, onTile, neighbours);
            }
            return total;
        }
    }
}
