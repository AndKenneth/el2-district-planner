using System;
using System.IO;
using Amplitude.Mercury.Data.Simulation;
using Amplitude.Mercury.Interop;
using Amplitude.Mercury.Sandbox;
using HarmonyLib;

namespace DistrictPlanner
{
    // SimulationEvaluator uses static working buffers and must only run on the sandbox thread. The hotkey (Unity
    // thread) sets a flag, or a BepInEx/DistrictPlanner/dump.request file is created from outside the game; the next
    // Snapshots.Synchronize (sandbox thread, every simulation frame) evaluates every district for every settlement
    // of the local empire.
    [HarmonyPatch(typeof(Snapshots), nameof(Snapshots.Synchronize))]
    internal static class DumpCommand
    {
        private static volatile bool requested;

        private static readonly string TriggerPath = Path.Combine(Path.GetDirectoryName(EvaluationDump.FilePath), "dump.request");
        private static DateTime nextTriggerCheck;

        // Set while the bulk dump runs, so the per-evaluation patch tags lines and skips log spam.
        internal static string CurrentDumpId;

        public static void Request() => requested = true;

        private static void Postfix()
        {
            if (DateTime.UtcNow >= nextTriggerCheck)
            {
                nextTriggerCheck = DateTime.UtcNow.AddSeconds(1);
                if (File.Exists(TriggerPath))
                {
                    File.Delete(TriggerPath);
                    Plugin.Log.LogInfo("Dump requested by trigger file");
                    requested = true;
                }
            }

            if (!requested)
            {
                return;
            }
            requested = false;

            try
            {
                CurrentDumpId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var empire = Sandbox.MajorEmpires[SandboxManager.Sandbox.LocalEmpireIndex];
                int settlementCount = 0, evaluationCount = 0;
                for (int s = 0; s < empire.Settlements.Count; s++)
                {
                    var settlement = empire.Settlements[s];
                    settlementCount++;
                    EvaluationDump.WriteSettlement(settlement);
                    // The per-settlement list is rebuilt lazily (e.g. when the city screen opens) and is empty right after load.
                    empire.DepartmentOfIndustry.UpdateSettlementAvailableConstructionsIfNecessary(settlement);
                    for (int i = 0; i < settlement.AvailableConstructionsCount; i++)
                    {
                        ref var available = ref settlement.AvailableConstructions[i];
                        if (!(available.ConstructibleDefinition is DistrictDefinition district))
                        {
                            continue;
                        }
                        EvaluationDump.CurrentFailureFlags = available.FailureFlags.ToString();
                        Sandbox.SimulationEvaluator.EvaluateDistrictPlacement(district, settlement.GUID, new DistrictPlacementEvaluation());
                        evaluationCount++;
                    }
                }
                Plugin.Log.LogInfo($"Dump {CurrentDumpId}: {evaluationCount} district evaluations across {settlementCount} settlements -> {EvaluationDump.FilePath}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Dump failed: {e}");
            }
            finally
            {
                CurrentDumpId = null;
                EvaluationDump.CurrentFailureFlags = null;
            }
        }
    }
}
