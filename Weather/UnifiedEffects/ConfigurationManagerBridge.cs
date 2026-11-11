using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace UnifiedEffects
{
    // Configuration Manager's F1 search normally sees loader plugins and scene
    // objects. Our private modules are deliberately hidden from both inventories.
    // Add only their real owners to this optional menu's search result; their
    // original ConfigEntry objects remain the only source of setting values.
    internal static class ConfigurationManagerBridge
    {
        internal const string PluginId = "com.bepis.bepinex.configurationmanager";
        private static Harmony harmony;
        internal static bool IsActive => harmony != null;
        private static readonly FieldInfo DescriptionField = typeof(ConfigEntryBase).GetField("<Description>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly HashSet<string> DormantKeys = new HashSet<string>(StringComparer.Ordinal) {
            "aaron.boscali.weather-unified/Weather/CinematicCloudsEnabled",
            "aaron.boscali.weather-unified/Weather/CloudHalfResolution",
            "aaron.boscali.weather-unified/Weather/CloudTemporalUpdate",
            "aaron.boscali.unified/Immersion/Contrails",
            "aaron.boscali.unified/Immersion/DustWakes",
            "aaron.boscali.betternight/8. Street Lights - Range & Destruction/Destructible",
            "aaron.boscali.betternight/2. Night/Extra Ambient When NVG Active"
        };
        private static readonly Dictionary<ConfigEntryBase, HiddenEntry> HiddenEntries = new Dictionary<ConfigEntryBase, HiddenEntry>();
        private sealed class ConfigurationManagerAttributes { public bool? Browsable = false; }
        private sealed class HiddenEntry
        {
            internal readonly ConfigDescription Original, Decorated;
            internal readonly object Tag = new ConfigurationManagerAttributes();
            internal HiddenEntry(ConfigEntryBase entry)
            {
                Original = entry.Description;
                Decorated = new ConfigDescription(Original?.Description ?? "", Original?.AcceptableValues,
                    (Original?.Tags ?? Array.Empty<object>()).Concat(new[] { Tag }).ToArray());
            }
        }

        internal static void Shutdown()
        {
            NoAtmoConfigurationDrawers.DetachAll();
            RestoreDormantEntries();
            harmony?.UnpatchSelf();
            harmony = null;
        }

        internal static void Initialize(ManualLogSource log)
        {
            if (harmony != null || !Chainloader.PluginInfos.TryGetValue(PluginId, out var plugin) || plugin.Instance == null) return;
            try
            {
                var searcher = plugin.Instance.GetType().Assembly.GetType("ConfigurationManager.SettingSearcher", false);
                var find = searcher == null ? null : AccessTools.DeclaredMethod(searcher, "FindPlugins", Type.EmptyTypes);
                if (find == null || !find.IsStatic || find.ReturnType != typeof(BaseUnityPlugin[]))
                {
                    log.LogWarning("F1 compatibility unavailable: Configuration Manager's plugin search API changed. FX MFD controls remain available.");
                    return;
                }
                var patcher = new Harmony("aaron.boscali.weather-unified.configuration-manager");
                patcher.Patch(find, postfix: new HarmonyMethod(typeof(ConfigurationManagerBridge), nameof(IncludePrivateOwners)));
                harmony = patcher;
                log.LogInfo("F1 Configuration Manager can discover the eight private effects modules without adding DOORMAN entries.");
            }
            catch (Exception error)
            {
                log.LogWarning("F1 compatibility could not be enabled: " + error.Message + ". FX MFD controls remain available.");
            }
        }

        private static void IncludePrivateOwners(ref BaseUnityPlugin[] __result)
        {
            // This runs when F1 rebuilds its menu, not in a frame update.
            List<BaseUnityPlugin> expanded = null;
            foreach (var info in ModuleRegistry.PrivateModules)
            {
                var owner = info.Instance;
                if (owner == null || (__result != null && Array.IndexOf(__result, owner) >= 0)) continue;
                if (expanded == null) expanded = __result == null ? new List<BaseUnityPlugin>() : new List<BaseUnityPlugin>(__result);
                if (!expanded.Contains(owner)) expanded.Add(owner);
            }
            if (expanded != null) __result = expanded.ToArray();
            HideDormantEntries(__result);
        }

        private static void HideDormantEntries(BaseUnityPlugin[] owners)
        {
            if (DescriptionField == null || owners == null) return;
            foreach (var owner in owners)
            {
                if (owner == null || !owner.Info.Metadata.GUID.StartsWith("aaron.boscali.", StringComparison.Ordinal)) continue;
                foreach (var setting in owner.Config)
                {
                    var entry = setting.Value;
                    string identity = owner.Info.Metadata.GUID + "/" + setting.Key.Section + "/" + setting.Key.Key;
                    if (!DormantKeys.Contains(identity) || HiddenEntries.ContainsKey(entry)) continue;
                    var hidden = new HiddenEntry(entry);
                    DescriptionField.SetValue(entry, hidden.Decorated);
                    HiddenEntries.Add(entry, hidden);
                }
            }
        }

        internal static void RestoreDormantEntries()
        {
            foreach (var pair in HiddenEntries)
            {
                if (ReferenceEquals(pair.Key.Description, pair.Value.Decorated)) DescriptionField.SetValue(pair.Key, pair.Value.Original);
                else if (pair.Key.Description?.Tags != null && pair.Key.Description.Tags.Any(x => ReferenceEquals(x, pair.Value.Tag)))
                {
                    var current = pair.Key.Description;
                    DescriptionField.SetValue(pair.Key, new ConfigDescription(current.Description, current.AcceptableValues,
                        current.Tags.Where(x => !ReferenceEquals(x, pair.Value.Tag)).ToArray()));
                }
            }
            HiddenEntries.Clear();
        }
    }
}
