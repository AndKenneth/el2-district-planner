using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Path = System.IO.Path;
using System.Text;
using Amplitude;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Simulation;
using BepInEx;

namespace DistrictPlanner
{
    // Appends one JSON line per district placement evaluation to BepInEx/DistrictPlanner/evaluations.jsonl,
    // for offline analysis of how scores break down into own yields vs neighbour effects. "buyable" lists tiles that
    // need a Foundation bought with Influence first (see BuyableTiles).
    internal static class EvaluationDump
    {
        internal static readonly string FilePath = Path.Combine(Paths.BepInExRootPath, "DistrictPlanner", "evaluations.jsonl");

        // Set by DumpCommand while it evaluates each available construction.
        internal static string CurrentFailureFlags;

        // The cursor re-evaluates on every world frame change; skip lines identical to the last one per settlement/district.
        private static readonly Dictionary<string, int> LastHashByKey = new Dictionary<string, int>();

        public static void Write(DistrictPlacementEvaluation eval, DistrictDefinition extension, SimulationEntityGUID settlementGuid, int[] vanillaTiers, Func<DistrictPlacementEvaluation.ValidTile, FitScore> score)
        {
            var sb = new StringBuilder();
            sb.Append("{\"source\":").Append(Str(DumpCommand.CurrentDumpId == null ? "cursor" : "dump"));
            sb.Append(",\"dumpId\":").Append(Str(DumpCommand.CurrentDumpId));
            sb.Append(",\"district\":").Append(Str(extension.Name.ToString()));
            sb.Append(",\"failureFlags\":").Append(Str(CurrentFailureFlags));
            sb.Append(",\"settlement\":").Append(Str(settlementGuid.ToString()));
            sb.Append(",\"extractResource\":").Append(Bool(extension.ExtractResource));
            sb.Append(",\"tiles\":[");
            // Only the game's own valid tiles; appended buyable tiles are listed under "buyable".
            for (int i = 0; i < vanillaTiers.Length; i++)
            {
                if (i > 0) sb.Append(',');
                AppendTile(sb, eval.ValidTiles[i], vanillaTiers[i], score(eval.ValidTiles[i]));
            }
            sb.Append(']');

            if (Amplitude.Mercury.Sandbox.Sandbox.SimulationEntityRepository.TryGetSimulationEntity(settlementGuid, out Settlement settlement))
            {
                sb.Append(",\"buyable\":[");
                var buyable = BuyableTiles.Find(extension, settlement, eval);
                for (int i = 0; i < buyable.Count; i++)
                {
                    var c = buyable[i];
                    if (i > 0) sb.Append(',');
                    string extra = ",\"influenceCost\":" + Num(c.InfluenceCost)
                        + ",\"foundationState\":" + Str(c.State.ToString())
                        + ",\"foundationFailures\":" + Str(c.FailureFlags.ToString())
                        + ",\"canBuildFoundation\":" + Bool(c.CanBuildFoundation);
                    AppendTile(sb, c.Tile, -1, score(c.Tile), extra);
                }
                sb.Append(']');
            }
            sb.Append('}');

            string body = sb.ToString();
            string key = settlementGuid + "/" + extension.Name;
            int hash = body.GetHashCode();
            if (DumpCommand.CurrentDumpId == null && LastHashByKey.TryGetValue(key, out int last) && last == hash)
            {
                return;
            }
            LastHashByKey[key] = hash;

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.AppendAllText(FilePath, "{\"time\":" + Str(DateTime.Now.ToString("s")) + "," + body.Substring(1) + "\n");
        }

        // One line per settlement at the start of a bulk dump: its construction queue, for queue-aware scoring.
        public static void WriteSettlement(Settlement settlement)
        {
            var sb = new StringBuilder();
            sb.Append("{\"time\":").Append(Str(DateTime.Now.ToString("s")));
            sb.Append(",\"source\":\"settlement\",\"dumpId\":").Append(Str(DumpCommand.CurrentDumpId));
            sb.Append(",\"settlement\":").Append(Str(settlement.GUID.ToString()));
            sb.Append(",\"worldPosition\":").Append(settlement.WorldPosition);
            sb.Append(",\"status\":").Append(Str(settlement.SettlementStatus.ToString()));
            sb.Append(",\"queue\":[");
            var queue = settlement.ConstructionQueue.Entity?.Constructions;
            for (int i = 0; queue != null && i < queue.Length; i++)
            {
                ref var c = ref queue.Data[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"constructible\":").Append(Str(c.ConstructibleDefinition?.Name.ToString()));
                sb.Append(",\"tile\":").Append(c.WorldPosition);
                sb.Append(",\"isStarted\":").Append(Bool(c.IsStarted));
                sb.Append(",\"invested\":").Append(Num(c.InvestedResource));
                sb.Append(",\"cost\":").Append(Num(c.Cost));
                sb.Append(",\"failureFlags\":").Append(Str(c.FailureFlags.ToString()));
                sb.Append('}');
            }
            sb.Append("]}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.AppendAllText(FilePath, sb.ToString());
        }

        private static void AppendTile(StringBuilder sb, in DistrictPlacementEvaluation.ValidTile t, int vanillaTier, FitScore score, string extra = null)
        {
            sb.Append("{\"tile\":").Append(t.TileIndex);
            sb.Append(",\"tier\":").Append(t.FimsTier);
            sb.Append(",\"vanillaTier\":").Append(vanillaTier);
            sb.Append(",\"score\":").Append(Num(score.Raw));
            sb.Append(",\"fit\":").Append(Num(score.Fit));
            sb.Append(",\"adjacency\":").Append(Num(score.Adjacency));
            sb.Append(",\"onTile\":").Append(Num(score.OnTile));
            sb.Append(",\"neighbourLevelUp\":").Append(Num(score.NeighbourLevelUp));
            sb.Append(",\"neighbourFoundation\":").Append(Num(score.NeighbourFoundation));
            sb.Append(",\"buyCost\":").Append(Num(score.BuyCost));
            sb.Append(",\"negativeSynergy\":").Append(Bool(score.HasNegativeSynergy));
            sb.Append(",\"current\":").Append(Str(DistrictAt(t.TileIndex)));
            sb.Append(",\"resource\":").Append(Str(t.Resource.ToString()));
            sb.Append(",\"hasFoundations\":").Append(Bool(t.HasFoundations));
            sb.Append(",\"willLevelUp\":").Append(Bool(t.WillLevelUp));
            sb.Append(",\"nextLevel\":").Append(t.NextDistrictLevel);
            sb.Append(",\"natural\":").Append(Fims(t.NaturalYields));
            sb.Append(",\"deltaDistrict\":").Append(Fims(t.DeltaDistrictGains));
            sb.Append(",\"deltaSettlement\":").Append(Fims(t.DeltaSettlementGains));
            sb.Append(",\"deltaEmpire\":").Append(Fims(t.DeltaEmpireGains));
            sb.Append(",\"deltaSynergy\":").Append(Fims(t.DeltaSynergySum));
            sb.Append(",\"deltaOwnTotal\":").Append(Fims(t.DeltaTotal));
            sb.Append(",\"deltaNeighbours\":").Append(Fims(t.DeltaNeighbourSum));
            sb.Append(extra);
            sb.Append(",\"neighbours\":[");
            bool first = true;
            if (t.NeighbourTiles != null)
            {
                foreach (var n in t.NeighbourTiles)
                {
                    if (n.TileIndex < 0) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"tile\":").Append(n.TileIndex);
                    sb.Append(",\"district\":").Append(Str(DistrictAt(n.TileIndex)));
                    sb.Append(",\"queued\":").Append(Str(QueuedAt(n.TileIndex)));
                    sb.Append(",\"willLevelUp\":").Append(Bool(n.WillLevelUp));
                    sb.Append(",\"nextLevel\":").Append(n.NextDistrictLevel);
                    sb.Append(",\"willRaiseFoundation\":").Append(Bool(n.WillRaiseFoundation));
                    sb.Append(",\"providesSynergy\":").Append(Bool(n.ProvidesSynergy));
                    sb.Append(",\"deltaDistrict\":").Append(Fims(n.DeltaDistrictGains));
                    sb.Append(",\"deltaSynergy\":").Append(Fims(n.DeltaSynergy));
                    sb.Append(",\"deltaTotal\":").Append(Fims(n.DeltaTotal));
                    sb.Append('}');
                }
            }
            sb.Append("]}");
        }

        private static string DistrictAt(int tileIndex)
        {
            var world = Amplitude.Mercury.Sandbox.Sandbox.World;
            int index = world.DistrictInfoMap[tileIndex];
            return index >= 0 ? world.DistrictInfo[index].DistrictDefinitionName.ToString() : null;
        }

        private static string QueuedAt(int tileIndex)
        {
            var world = Amplitude.Mercury.Sandbox.Sandbox.World;
            int index = world.ConstructionInfoMap[tileIndex];
            return index >= 0 && world.ConstructionInfo[index].IsInQueue ? world.ConstructionInfo[index].ConstructibleDefinitionName.ToString() : null;
        }

        private static string Fims(in FimsInfo f)
        {
            var sb = new StringBuilder("{");
            void Field(string name, FixedPoint value)
            {
                if (value == FixedPoint.Zero) return;
                if (sb.Length > 1) sb.Append(',');
                sb.Append('"').Append(name).Append("\":").Append(Num(value));
            }
            Field("Food", f.Food);
            Field("Industry", f.Industry);
            Field("Money", f.Money);
            Field("Science", f.Science);
            Field("Influence", f.Influence);
            Field("Approval", f.Approval);
            Field("InfluenceBuyCost", f.InfluenceBuyCost);
            Field("Cadaver", f.Cadaver);
            Field("Fortification", f.Fortification);
            return sb.Append('}').ToString();
        }

        private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Num(FixedPoint value) => ((float)value).ToString("0.###", CultureInfo.InvariantCulture);

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Str(string value) =>
            string.IsNullOrEmpty(value) ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
