using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;

namespace UnifiedEffects
{
    // Private modules retain their original ConfigFile identities, but are not
    // additional loader plugins. Never insert these entries into Chainloader.
    public static class ModuleRegistry
    {
        private static readonly Dictionary<string, PluginInfo> modules = new Dictionary<string, PluginInfo>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PluginInfo> available = new Dictionary<string, PluginInfo>(StringComparer.Ordinal);
        private static int externalCount = -1;

        internal static void Register(BaseUnityPlugin module)
        {
            if (module == null || module.Info == null) throw new ArgumentNullException(nameof(module));
            modules.Add(module.Info.Metadata.GUID, module.Info);
            externalCount = -1;
        }

        // Called by the two adapted private modules and the settings page.
        // Reuse dictionary/PluginInfo references: no per-frame allocations.
        public static Dictionary<string, PluginInfo> GetPluginInfos()
        {
            var external = Chainloader.PluginInfos;
            if (externalCount != external.Count)
            {
                available.Clear();
                foreach (var pair in external) available[pair.Key] = pair.Value;
                foreach (var pair in modules) available[pair.Key] = pair.Value;
                externalCount = external.Count;
            }
            return available;
        }
    }
}
