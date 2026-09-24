using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Simulation;
using Amplitude.Mercury.Terrain;

namespace DistrictPlanner
{
    // What the placement changes on each neighbouring district (built or queued): which of its own synergies start or
    // stop firing, how often, and whether it levels up. The district tooltip lists these per neighbour, with the
    // neighbour's own Details wording. Sandbox thread only: Evaluate and GetValidTiles use the evaluator's buffers.
    internal static class NeighbourEffects
    {
        internal struct SynergyChange
        {
            public string Synergy;
            public int Count;       // applications after the placement
            public bool Negative;   // the change lowers the neighbour's yields
        }

        internal sealed class Effect
        {
            public string Definition;         // SourceNames.Describe of the neighbour tile
            public string DistrictDefinition; // the neighbour's district definition, for its per-level Details lines
            public int NextLevel;             // > 0 when the placement levels it up
            public FimsInfo Yields;           // its total change
            public readonly List<SynergyChange> Synergies = new List<SynergyChange>();
        }

        public static List<Effect> Of(SimulationEvaluator evaluator, in DistrictPlacementEvaluation.ValidTile tile, DistrictDefinition district, Settlement settlement)
        {
            var result = new List<Effect>();
            if (tile.NeighbourTiles == null)
            {
                return result;
            }
            var world = Amplitude.Mercury.Sandbox.Sandbox.World;
            int levels = ConstructibleHelper.CountLevelsGainedAt(tile.TileIndex, settlement, district);
            DistrictDefinition placed = ConstructibleHelper.GetFinalLevelUpDefinition(district, levels);

            for (int d = 0; d < 6; d++)
            {
                var neighbour = tile.NeighbourTiles[d];
                if (neighbour.TileIndex < 0)
                {
                    continue;
                }
                DistrictDefinition current = null;
                if (!QueuedConstructions.Current.TryGetValue(neighbour.TileIndex, out current))
                {
                    int districtIndex = world.DistrictInfoMap[neighbour.TileIndex];
                    if (districtIndex >= 0 && evaluator.constructibleDatabase.TryGetValue(world.DistrictInfo[districtIndex].DistrictDefinitionName, out var definition))
                    {
                        current = definition as DistrictDefinition;
                    }
                }
                if (current == null)
                {
                    continue;
                }
                DistrictDefinition after = neighbour.WillLevelUp ? ConstructibleHelper.GetFinalLevelUpDefinition(current) : current;

                // The neighbour's surroundings: queued districts count as built; the placed district only "after".
                var around = new DistrictDefinition[6];
                for (int e = 0; e < 6; e++)
                {
                    int next = WorldPosition.GetTileIndexNeighbour(neighbour.TileIndex, (Hexagon.Direction)e);
                    if (next >= 0 && next != tile.TileIndex && QueuedConstructions.Current.TryGetValue(next, out var queued))
                    {
                        around[e] = queued;
                    }
                }
                var withPlaced = (DistrictDefinition[])around.Clone();
                withPlaced[(d + 3) % 6] = placed;

                var effect = new Effect
                {
                    Definition = SourceNames.Describe(neighbour.TileIndex),
                    DistrictDefinition = after.Name.ToString(),
                    NextLevel = neighbour.WillLevelUp ? neighbour.NextDistrictLevel : 0,
                    Yields = neighbour.DeltaTotal,
                };
                SimulationEvaluator.RetrieveAllSynergiesAfterLevelUp(after, 0);
                var names = new List<StaticString>();
                for (int i = 0; i < SimulationEvaluator.WorkingListOfDatatableElementReferences.Length; i++)
                {
                    names.Add(SimulationEvaluator.WorkingListOfDatatableElementReferences.Data[i].ElementName);
                }
                foreach (var name in names)
                {
                    if (effect.Synergies.Exists(s => s.Synergy == name.ToString()))
                    {
                        continue;
                    }
                    FimsInfo before = default, now = default;
                    evaluator.Evaluate(name, ref before, neighbour.TileIndex, current, around);
                    evaluator.Evaluate(name, ref now, neighbour.TileIndex, after, withPlaced);
                    FimsInfo delta = now - before;
                    if (delta.IsEmpty)
                    {
                        continue;
                    }
                    effect.Synergies.Add(new SynergyChange
                    {
                        Synergy = name.ToString(),
                        Count = evaluator.GetValidTiles(evaluator.synergyDatabase.GetValue(name), neighbour.TileIndex, after, withPlaced),
                        Negative = (float)delta.GetTotalFims() < 0f,
                    });
                }
                if (effect.NextLevel > 0 || effect.Synergies.Count > 0 || YieldText.Format(effect.Yields).Length > 0)
                {
                    result.Add(effect);
                }
            }
            return result;
        }
    }
}
