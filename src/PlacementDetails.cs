using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Data.World;
using Amplitude.Mercury.Interop;

namespace DistrictPlanner
{
    // What drives each tile's fit, named by where it comes from, for the placement pins. Built on the sandbox thread
    // from the latest placement cursor evaluation and read on the Unity thread; the dictionary is replaced, never mutated.
    internal sealed class PlacementDetails
    {
        public enum Direction { From, To, OnTile }

        // One yield of one source, e.g. Money +10 from an anomaly, Money +4 to a Money district, Food +1 on the tile.
        // Definition is the source's definition name, localized on the Unity thread by SourceNames.Localize.
        public struct Entry
        {
            public UIResourceType Type;
            public FixedPoint Amount;
            public Direction Direction;
            public string Definition;

            // Neighbour tiles merged into this entry, e.g. 3 for "+6 from River x3".
            public int Count;

            // What the yield is for: "(Tile)" for an on-tile bonus, else the source's name.
            public string Text => Direction == Direction.OnTile
                ? "(" + Words.Tile + ")"
                : SourceNames.Localize(Definition) + (Count > 1 ? " \u00d7" + Count : "");
        }

        public FitScore Score;

        // Set when the tile needs a Foundation bought with Influence first (BuyablePlacement).
        public BuyablePlacement.Offer? Offer;

        public readonly List<Entry> Entries = new List<Entry>();

        // The placed district's own synergies that fire on this tile (SynergyTriggers), for the tooltip's Details.
        public List<SynergyTriggers.Trigger> Triggers = new List<SynergyTriggers.Trigger>();

        // What the placement changes on each neighbouring district, for the tooltip.
        public List<NeighbourEffects.Effect> NeighbourEffects = new List<NeighbourEffects.Effect>();

        // The tile's own yields, for on-tile effect conditions in the tooltip.
        public FimsInfo NaturalYields;

        // Synergy the placed district receives from each neighbour tile, formatted, by neighbour tile index.
        public readonly Dictionary<int, string> ReceivedFrom = new Dictionary<int, string>();

        // The district of the latest evaluation, so tooltips of other constructibles are left alone.
        public static volatile string DistrictName = string.Empty;

        private static volatile Dictionary<int, PlacementDetails> latest = new Dictionary<int, PlacementDetails>();

        public static bool TryGet(int tileIndex, out PlacementDetails details) => latest.TryGetValue(tileIndex, out details);

        // Buyable tiles of the latest evaluation and their offers.
        public static IEnumerable<KeyValuePair<int, BuyablePlacement.Offer>> Offers()
        {
            foreach (var pair in latest)
            {
                if (pair.Value.Offer is BuyablePlacement.Offer offer)
                {
                    yield return new KeyValuePair<int, BuyablePlacement.Offer>(pair.Key, offer);
                }
            }
        }

        // Also sets NeighbourTile.ProvidesSynergy for every source found here: the vanilla hover feedback
        // (DistrictPlacementTileFeedback) draws its edge arrows towards neighbours with GainsSynergy or ProvidesSynergy.
        // SynergyHighlightPatch highlights the source tiles themselves.
        public static void Publish(DistrictPlacementEvaluation eval, FitScore.Scorer scorer, Dictionary<int, BuyablePlacement.Offer> offers, Dictionary<int, List<SynergyTriggers.Trigger>> triggers,
            Dictionary<int, List<NeighbourEffects.Effect>> neighbourEffects, Amplitude.StaticString district)
        {
            var all = new Dictionary<int, PlacementDetails>(eval.ValidTileCount);
            for (int i = 0; i < eval.ValidTileCount; i++)
            {
                ref var tile = ref eval.ValidTiles[i];
                var details = new PlacementDetails { Score = scorer.Of(in tile) };
                if (triggers.TryGetValue(tile.TileIndex, out var fired))
                {
                    details.Triggers = fired;
                }
                if (neighbourEffects.TryGetValue(tile.TileIndex, out var effects))
                {
                    details.NeighbourEffects = effects;
                }
                if (offers.TryGetValue(tile.TileIndex, out var offer))
                {
                    details.Offer = offer;
                }
                FimsInfo multiplier = tile.NewBonusMultiplier;

                for (int m = 0; tile.NeighbourTiles != null && m < tile.NeighbourTiles.Length; m++)
                {
                    ref var neighbour = ref tile.NeighbourTiles[m];
                    if (neighbour.TileIndex < 0)
                    {
                        continue;
                    }
                    string name = null;

                    FimsInfo received = tile.NewSynergies != null ? tile.NewSynergies[m] * multiplier : default;
                    if (tile.OldSynergies != null)
                    {
                        received = received - tile.OldSynergies[m];
                    }
                    string receivedText = YieldText.Format(received);
                    if (receivedText.Length > 0)
                    {
                        neighbour.ProvidesSynergy = true;
                        name = SourceNames.Describe(neighbour.TileIndex);
                        details.ReceivedFrom[neighbour.TileIndex] = receivedText;
                        AddEntries(details.Entries, received, Direction.From, name);
                    }

                    if (!neighbour.WillLevelUp)
                    {
                        if (YieldText.Format(neighbour.DeltaSynergy).Length > 0)
                        {
                            AddEntries(details.Entries, neighbour.DeltaSynergy, Direction.To, name ?? SourceNames.Describe(neighbour.TileIndex));
                        }
                    }
                }

                details.NaturalYields = tile.NaturalYields;
                AddEntries(details.Entries, details.Score.OnTileYields, Direction.OnTile, null);
                details.Entries.RemoveAll(e => (float)e.Amount > -0.5f && (float)e.Amount < 0.5f);
                details.Entries.Sort((a, b) => ((float)b.Amount).CompareTo((float)a.Amount));
                all[tile.TileIndex] = details;
            }
            DistrictName = district.ToString();
            latest = all;
        }

        private static void AddEntries(List<Entry> entries, in FimsInfo yields, Direction direction, string definition)
        {
            Add(entries, UIResourceType.Food, yields.Food, direction, definition);
            Add(entries, UIResourceType.Industry, yields.Industry, direction, definition);
            Add(entries, UIResourceType.Money, yields.Money, direction, definition);
            Add(entries, UIResourceType.Science, yields.Science, direction, definition);
            Add(entries, UIResourceType.Influence, yields.Influence, direction, definition);
            Add(entries, UIResourceType.Approval, yields.Approval, direction, definition);
        }

        private static void Add(List<Entry> entries, UIResourceType type, FixedPoint amount, Direction direction, string definition)
        {
            if (amount == FixedPoint.Zero)
            {
                return;
            }
            // Merge neighbours of the same kind, e.g. three river tiles into one "+6 from River" line.
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.Type == type && entry.Direction == direction && entry.Definition == definition)
                {
                    entry.Amount += amount;
                    entry.Count++;
                    entries[i] = entry;
                    return;
                }
            }
            entries.Add(new Entry { Type = type, Amount = amount, Direction = direction, Definition = definition, Count = 1 });
        }
    }
}
