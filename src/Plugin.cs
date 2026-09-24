using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DistrictPlanner
{
    [BepInPlugin(Guid, "District Planner", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "nz.digistruct.el2.districtplanner";
        public const string Version = BuildInfo.Version;

        internal static ManualLogSource Log;
        private Harmony harmony;
        internal static ConfigEntry<bool> FixRecommendationTiers;
        internal static ConfigEntry<float> SynergyWeight;
        internal static ConfigEntry<float> NeighbourLevelUpWeight;
        internal static ConfigEntry<float> NeighbourFoundationWeight;
        internal static ConfigEntry<float> BuyCostPaybackTurns;
        internal static ConfigEntry<float> NegativeSynergyPenalty;
        internal static ConfigEntry<float> RecommendMinAdvantage;
        internal static ConfigEntry<float> RecommendRelativeAdvantage;
        internal static ConfigEntry<bool> RecommendBestPerTerritory;
        internal static ConfigEntry<bool> OfferBuyableTiles;
        internal static ConfigEntry<int> BuyableMaxRecommended;
        internal static ConfigEntry<bool> ShowBuyCostOnTiles;
        internal static ConfigEntry<bool> ShowSynergyOnPins;
        internal static ConfigEntry<bool> CollapseOtherPins;
        internal static ConfigEntry<bool> ShowNeighbourGains;
        internal static ConfigEntry<bool> SplitHoverEffects;
        internal static ConfigEntry<int> CollapseRadius;
        internal static ConfigEntry<bool> CountQueuedConstructions;
        internal static ConfigEntry<string> SynergyTileHighlight;
        internal static ConfigEntry<string> TextBuyQuestion;
        internal static ConfigEntry<bool> LogEvaluations;
        internal static ConfigEntry<bool> DumpEvaluations;
        internal static ConfigEntry<KeyboardShortcut> DumpKey;

        private void Awake()
        {
            Log = Logger;
            // Sections are numbered so the F1 Configuration Manager lists them in this order; within a section, settings
            // keep the order they are bound in. Advanced settings are hidden there unless "Advanced settings" is ticked.
            const string recommend = "1. Recommendations", buyable = "2. Buyable tiles", display = "3. Display", text = "4. Text", debug = "5. Debug";

            FixRecommendationTiers = Bind(recommend, "Enabled", true,
                "Recommend tiles by how well the district fits its surroundings (synergy, level-ups, negatives) instead of the game's raw score, which only ever recommends tiles tied for the single best score.");
            SynergyWeight = Bind(recommend, "SynergyWeight", 3f,
                "How much more adjacency and on-tile bonuses count than other yields. 1 = the game's weighting.", new AcceptableValueRange<float>(1f, 10f));
            NeighbourLevelUpWeight = Bind(recommend, "NeighbourLevelUpWeight", 0.5f,
                "How much yields from neighbours levelling up count; most districts placed on the same tile would trigger them too.", new AcceptableValueRange<float>(0f, 2f));
            NeighbourFoundationWeight = Bind(recommend, "NeighbourFoundationWeight", 0.5f,
                "How much the natural yields of tiles a district raises a Foundation on count (e.g. a Keep); those tiles could also be bought with Influence.", new AcceptableValueRange<float>(0f, 2f));
            NegativeSynergyPenalty = Bind(recommend, "NegativeSynergyPenalty", 1000f,
                "Subtracted from a tile where the district takes or gives a penalty (e.g. -5 Approval for housing next to industry). The default only recommends such a tile when there is no clean one; 0 disables.",
                new AcceptableValueRange<float>(0f, 1000f));
            CountQueuedConstructions = Bind(recommend, "CountQueuedConstructions", true,
                "Count synergy with districts in the construction queues as if they were built (the game ignores them).");
            RecommendBestPerTerritory = Bind(recommend, "BestPerTerritory", true,
                "Always recommend the best tile of each territory of the city (its own and each attached one).");
            RecommendMinAdvantage = Bind(recommend, "MinAdvantage", 1f,
                "A tile is only recommended if it beats the typical (median) tile by at least this many points...", new AcceptableValueRange<float>(0f, 20f), advanced: true);
            RecommendRelativeAdvantage = Bind(recommend, "RelativeAdvantage", 0.1f,
                "...or by this fraction of the median, whichever is larger.", new AcceptableValueRange<float>(0f, 1f), advanced: true);

            OfferBuyableTiles = Bind(buyable, "Enabled", true,
                "Let a district be placed on tiles that need a Foundation bought with Influence first; clicking one asks to buy the Foundation and place the district.");
            BuyableMaxRecommended = Bind(buyable, "MaxRecommended", 3,
                "Recommend up to this many buyable tiles, when they beat the typical tile. The others can still be placed on, their cost shows on hover.", new AcceptableValueRange<int>(0, 10));
            BuyCostPaybackTurns = Bind(buyable, "PaybackTurns", 30f,
                "Buyable tiles away from the city (only buyable through another city's district) pay for their price above the cheapest Foundation on offer over this many turns. Lower avoids expensive tiles more.",
                new AcceptableValueRange<float>(5f, 100f));
            ShowBuyCostOnTiles = Bind(buyable, "ShowCostOnTiles", true,
                "Show the Foundation cost on recommended buyable tiles, as the city's Foundation mode does.");

            ShowSynergyOnPins = Bind(display, "Details", true,
                "Explain the hovered tile: on-tile bonuses on its pin, and in the district tooltip which bonuses and penalties apply there and what changes for each neighbour.");
            ShowNeighbourGains = Bind(display, "NeighbourLabels", true,
                "While hovering a tile, label every neighbour that gives or gains yields, not only the ones that level up.");
            SplitHoverEffects = Bind(display, "SplitHoverEffects", false,
                "While hovering a tile, its pin shows only the new district's own yields and each neighbour's change is on that neighbour. Off: the pin shows the combined total, as in the game.");
            CollapseOtherPins = Bind(display, "HideNearbyPins", true,
                "While hovering a tile, hide the other candidate pins near it.");
            CollapseRadius = Bind(display, "HideNearbyPinsRadius", 2,
                "How many tiles around the hovered one HideNearbyPins clears.", new AcceptableValueRange<int>(1, 5));
            SynergyTileHighlight = Bind(display, "SynergyTileHighlight", "ConstructibleTileHighlighted",
                "Tile highlight drawn on neighbours that give or gain synergy while a tile is hovered (a game TileFeedback name, e.g. ConstructibleTileHover); empty to disable.",
                advanced: true);

            // Every other word comes from the game's own translations (Words); it has none for this question.
            TextBuyQuestion = Bind(text, "BuyQuestion", "",
                "Replaces the question asked when clicking a buyable tile; {0} is the Influence cost. Empty: the built-in wording in the game's language.",
                advanced: true);

            LogEvaluations = Bind(debug, "LogEvaluations", false,
                "Log each placement evaluation's best tiles, scores and timing to LogOutput.log.", advanced: true);
            DumpEvaluations = Bind(debug, "DumpEvaluations", false,
                "Append every placement evaluation to BepInEx/DistrictPlanner/evaluations.jsonl (grows quickly).", advanced: true);
            DumpKey = Bind(debug, "DumpKey", new KeyboardShortcut(KeyCode.D, KeyCode.LeftControl, KeyCode.LeftShift),
                "Evaluate every district for every city of the local empire and append them all to evaluations.jsonl.", advanced: true);

            // Scores are computed when the placement is evaluated: evaluate again so a change shows right away.
            Config.SettingChanged += (sender, args) => ReevaluateOpenPlacement();

            harmony = new Harmony(Guid);
            if (GameDataReady())
            {
                PatchAll();
            }
            else
            {
                Log.LogInfo($"District Planner {Version} waiting for game data before patching");
            }
        }

        // Patch classes whose target could not be patched, e.g. renamed by a game update, or that threw (Guard).
        internal static readonly System.Collections.Generic.HashSet<System.Type> FailedPatches = new System.Collections.Generic.HashSet<System.Type>();

        private bool patched;

        // Patching a method makes Mono run its class's static constructor. If that constructor reads game data that
        // isn't loaded yet, it throws and the class stays broken for the rest of the session, so the game fails with
        // "Unexpected error" (ConstructibleHelper from build 25488140 on). The mod does nothing before a game is loaded
        // anyway: patch only once the game data is there.
        // One patch class at a time, unlike Harmony.PatchAll, which stops at the first failure: a game update that
        // breaks one patch then only disables that feature, and the log names it.
        private void PatchAll()
        {
            patched = true;
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception e)
                {
                    FailedPatches.Add(type);
                    Log.LogError($"Could not patch {type.Name}, its feature is off (game update?): {e.GetBaseException().Message}");
                }
            }
            Log.LogInfo($"District Planner {Version} loaded (game {Application.version}, Steam build {SteamBuildId()}, {FailedPatches.Count} patches failed)");
            ReevaluateOpenPlacement();
        }

        // What patched classes' static constructors read: the game's UI mappers (ConstructibleHelper.DefaultNeighborsName).
        private static bool GameDataReady()
        {
            try
            {
                return Amplitude.Mercury.Utils.DataUtils?.EnumUIMappers != null
                    && Amplitude.Mercury.Utils.DataUtils.EnumUIMappers.TryGetEnumUIMapper(Amplitude.Mercury.Data.World.UITileType.District, out var mapper)
                    && mapper != null;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        // The game's Steam build, to tell game updates apart (the Unity version string rarely changes).
        private static string SteamBuildId()
        {
            try
            {
                string manifest = System.IO.Path.Combine(Paths.GameRootPath, "..", "..", "appmanifest_3407390.acf");
                var match = System.Text.RegularExpressions.Regex.Match(System.IO.File.ReadAllText(manifest), "\"buildid\"\\s+\"(\\d+)\"");
                return match.Success ? match.Groups[1].Value : "unknown";
            }
            catch (System.Exception)
            {
                return "unknown";
            }
        }

        private int order;

        private ConfigEntry<T> Bind<T>(string section, string key, T value, string description, AcceptableValueBase range = null, bool advanced = false) =>
            Config.Bind(section, key, value, new ConfigDescription(description, range,
                new ConfigurationManagerAttributes { Order = --order, IsAdvanced = advanced ? true : (bool?)null }));

        // ScriptEngine hot reload destroys this instance before loading the new build; remove this build's patches.
        private void OnDestroy()
        {
            PlacementPinPatch.RestoreAll();
            harmony?.UnpatchSelf();
            Log.LogInfo("District Planner unloaded");
        }

#if DEBUG
        private float nextReloadCheck;
#endif

        private void Update()
        {
            if (DumpKey.Value.IsDown())
            {
                Log.LogInfo("Dump requested");
                DumpCommand.Request();
            }
            if (!patched && GameDataReady())
            {
                PatchAll();
            }
            if (Guard.UnpatchFailed(harmony))
            {
                // A failed evaluation patch may have left the open placement half rescored.
                ReevaluateOpenPlacement();
            }
#if DEBUG
            if (Time.unscaledTime >= nextReloadCheck)
            {
                nextReloadCheck = Time.unscaledTime + 1f;
                CheckReloadRequest();
                LocalizationDump.CheckRequest();
            }
#endif
        }

        // After a hot reload the plugin's per-tile data is empty, while an open placement cursor keeps its evaluation
        // until something changes. Mark the cursor snapshot stale so it evaluates again, through the new patches.
        private static void ReevaluateOpenPlacement()
        {
            try
            {
                var controller = Amplitude.Mercury.Presentation.Presentation.PresentationCursorController;
                var snapshot = Amplitude.Mercury.Interop.Snapshots.DistrictPlacementCursorSnapshot;
                if (!(controller?.CurrentCursor is Amplitude.Mercury.Presentation.DistrictPlacementCursor) || snapshot == null)
                {
                    return;
                }
                foreach (var data in snapshot.data)
                {
                    data.LastDistrictFrame = -1;
                }
                snapshot.Start();
            }
            catch (System.Exception e)
            {
                Log.LogWarning("Could not re-evaluate the open placement: " + e.Message);
            }
        }

#if DEBUG
        // Development builds only. ScriptEngine's file watcher gets no change events under Proton and F6 needs the game window
        // focused, so `dotnet build` writes BepInEx/DistrictPlanner/reload.request and this asks ScriptEngine to reload.
        private static void CheckReloadRequest()
        {
            string trigger = System.IO.Path.Combine(Paths.BepInExRootPath, "DistrictPlanner", "reload.request");
            if (!System.IO.File.Exists(trigger)
                || !BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.bepis.bepinex.scriptengine", out var scriptEngine))
            {
                return;
            }
            System.IO.File.Delete(trigger);
            Log.LogInfo("Reload requested by trigger file");
            AccessTools.Method(scriptEngine.Instance.GetType(), "ReloadPlugins")?.Invoke(scriptEngine.Instance, null);
        }
#endif
    }
}
