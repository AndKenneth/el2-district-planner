namespace DistrictPlanner
{
    // Read by name by BepInEx.ConfigurationManager (the F1 settings window), if installed; see
    // https://github.com/BepInEx/BepInEx.ConfigurationManager. Only the fields used here.
#pragma warning disable 0169, 0414, 0649
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
        public bool? IsAdvanced;
    }
#pragma warning restore 0169, 0414, 0649
}
