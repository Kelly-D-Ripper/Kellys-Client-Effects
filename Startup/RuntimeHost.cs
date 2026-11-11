using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;

namespace UnifiedEffects.Startup
{
    public static class RuntimeHost
    {
        private static bool installed;
        public static void BeforePlugins()
        {
            if (installed || StartupPatcher.Headless) return;
            try
            {
                new Harmony("aaron.boscali.optional-host").Patch(AccessTools.Method(typeof(Chainloader), "Start"),
                    transpiler: new HarmonyMethod(typeof(RuntimeHost), nameof(AfterDiscovery)));
                installed = true;
            }
            catch (Exception e) { Logger.CreateLogSource("Unified Effects startup").LogWarning("Plugin host protection unavailable: " + e.GetBaseException().Message); }
        }
        private static IEnumerable<CodeInstruction> AfterDiscovery(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(); int matches = 0;
            foreach (var instruction in instructions)
            {
                result.Add(instruction);
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(TypeLoader) && method.Name == "FindPluginTypes" &&
                    method.IsGenericMethod && method.GetGenericArguments().SequenceEqual(new[] { typeof(PluginInfo) }))
                { result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RuntimeHost), nameof(Protect)))); matches++; }
            }
            if (matches != 1) throw new InvalidOperationException("BepInEx plugin discovery changed.");
            return result;
        }
        private static Dictionary<string, List<PluginInfo>> Protect(Dictionary<string, List<PluginInfo>> discovered)
        {
            // Protect the owner even when no optional cloud add-on or mod manager
            // is installed. A clean BepInEx configuration does not hide its host.
            // This is a lifetime fix only. Never filter plugins or change their preferences.
            var ids = discovered.Values.SelectMany(x => x).Select(x => x.Metadata.GUID);
            if (!ids.Contains("aaron.boscali.weather-unified")) return discovered;
            try
            {
                // Before any plugin Awake/OnEnable: avoids interrupting existing coroutines.
                var host = typeof(Chainloader).GetProperty("ManagerObject", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                if (host == null) throw new InvalidOperationException("Plugin manager is missing.");
                var flags = host.GetType().GetProperty("hideFlags", BindingFlags.Public | BindingFlags.Instance);
                int current = Convert.ToInt32(flags.GetValue(host, null));
                int protection = Convert.ToInt32(Enum.Parse(flags.PropertyType, "HideAndDontSave"));
                if ((current & protection) != protection) flags.SetValue(host, Enum.ToObject(flags.PropertyType, current | protection), null);
                flags.DeclaringType.GetMethod("DontDestroyOnLoad", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { host });
                Logger.CreateLogSource("Unified Effects startup").LogInfo("Unified Effects plugin host protected before plugin startup.");
            }
            catch (Exception e) { Logger.CreateLogSource("Unified Effects startup").LogWarning("Plugin host protection unavailable: " + e.GetBaseException().Message); }
            return discovered;
        }
    }
}
