using System.Collections.Generic;
using Amplitude;
using Amplitude.Mercury.Simulation;
using Amplitude.Mercury.UI;
using Amplitude.UI;

namespace DistrictPlanner
{
    // Names what is on a tile, to say where a synergy comes from or goes to. Describe runs on the sandbox thread and
    // returns the definition name (district, queued district, extractor, anomaly or deposit POI, terrain type), whose UIMapper holds the
    // localized title; Localize runs on the Unity thread and looks that title up, so names follow the game language.
    internal static class SourceNames
    {
        private static readonly Dictionary<string, string> LocalizedByDefinition = new Dictionary<string, string>();

        private const string QueuedPrefix = "queued:";

        public static string Describe(int tileIndex)
        {
            if (QueuedConstructions.Current.TryGetValue(tileIndex, out var queued))
            {
                return QueuedPrefix + queued.Name;
            }
            var world = Amplitude.Mercury.Sandbox.Sandbox.World;
            int districtIndex = world.DistrictInfoMap[tileIndex];
            if (districtIndex >= 0)
            {
                return world.DistrictInfo[districtIndex].DistrictDefinitionName.ToString();
            }

            ref var tile = ref world.TileInfo.Data[tileIndex];
            if (tile.PointOfInterest != byte.MaxValue)
            {
                return World.Tables.PointOfInterestDefinitions[tile.PointOfInterest].Name.ToString();
            }
            if (tile.RiverIndex != byte.MaxValue && !tile.IsWaterTile())
            {
                return "TerrainType_River";
            }
            return World.Tables.TerrainTypeDefinitions[tile.TerrainType].Name.ToString();
        }

        public static string Localize(string definitionName)
        {
            if (LocalizedByDefinition.TryGetValue(definitionName, out string name))
            {
                return name;
            }
            if (definitionName.StartsWith(QueuedPrefix))
            {
                name = Localize(definitionName.Substring(QueuedPrefix.Length)) + " (" + Plugin.TextQueued.Value + ")";
                LocalizedByDefinition[definitionName] = name;
                return name;
            }
            name = Utils.DataUtils.TryGetUIMapper(new StaticString(definitionName), out UIMapper mapper) && !string.IsNullOrEmpty(mapper.Title)
                ? mapper.Title
                : Fallback(definitionName);
            LocalizedByDefinition[definitionName] = name;
            return name;
        }

        // Used only when the game has no UIMapper for the definition: "District_Tier2_Money" -> "Tier2 Money".
        private static string Fallback(string definitionName)
        {
            foreach (string prefix in new[] { "District_", "POI_", "TerrainType_" })
            {
                if (definitionName.StartsWith(prefix))
                {
                    definitionName = definitionName.Substring(prefix.Length);
                }
            }
            return definitionName.Replace('_', ' ');
        }
    }
}
