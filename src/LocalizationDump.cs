#if DEBUG
using System.Collections;
using Amplitude.Framework;
using Amplitude.Framework.Localization;
using HarmonyLib;

namespace DistrictPlanner
{
    // Development only: BepInEx/DistrictPlanner/l10n.request writes every localisation key and its text in the current
    // language to BepInEx/DistrictPlanner/l10n-<language>.tsv, to find game keys for the mod's own words.
    internal static class LocalizationDump
    {
        public static void CheckRequest()
        {
            string dir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "DistrictPlanner");
            string trigger = System.IO.Path.Combine(dir, "l10n.request");
            if (!System.IO.File.Exists(trigger))
            {
                return;
            }
            System.IO.File.Delete(trigger);
            var service = Services.GetService<ILocalizationService>();
            var database = Traverse.Create(service).Property("LocalizationDatabase").GetValue() as IEnumerable;
            if (database == null)
            {
                Plugin.Log.LogWarning("l10n dump: no localization database");
                return;
            }
            var lines = new System.Collections.Generic.List<string>();
            foreach (var element in database)
            {
                string key = ((LocalizedStringElement)element).Name.ToString();
                string text = service.Localize(key, "") ?? "";
                lines.Add(key + "\t" + text.Replace("\n", "\\n").Replace("\t", " "));
            }
            string file = System.IO.Path.Combine(dir, "l10n-" + service.CurrentLanguage + ".tsv");
            System.IO.File.WriteAllLines(file, lines);
            Plugin.Log.LogInfo($"l10n dump: {lines.Count} keys to {file}");
        }
    }
}
#endif
