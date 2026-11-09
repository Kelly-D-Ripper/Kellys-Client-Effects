using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace UnifiedEffects
{
    internal static class ModuleLoader
    {
        private const string Prefix = "UnifiedEffects.Modules.";
        private static GameObject host;
        private static bool attempted;
        private static readonly string[] Order = {
            "KROP.UnifiedEffects.dll", "Boscali.CockpitAudio.dll", "RealisticNight.Unified.dll",
            "Boscali.CockpitFeel.dll", "Boscali.AirfieldLighting.dll", "Boscali.UnifiedMissileBoom.dll",
            "BoscaliUnified.dll", "ClientEffectsControlPanel.dll"
        };

        internal static void Initialize(ManualLogSource log)
        {
            if (attempted) return;
            attempted = true;
            // Resolve and validate every payload before creating any components.
            // Namespaces remain isolated so each module's Harmony and resources
            // have the same assembly identity as in the tested multi-file pack.
            var types = new List<Type>();
            var names = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name), StringComparer.OrdinalIgnoreCase);
            foreach (string file in Order)
                if (names.Contains(Path.GetFileNameWithoutExtension(file)))
                    throw new InvalidOperationException("Old Unified Effects component already loaded: " + file + ". Remove the old pack DLLs before installing the single-plugin build; keep your config files.");
            foreach (string file in Order)
            {
                byte[] bytes;
                using (var stream = typeof(ModuleLoader).Assembly.GetManifestResourceStream(Prefix + file))
                {
                    if (stream == null) throw new InvalidOperationException("Missing embedded effects component: " + file);
                    using (var buffer = new MemoryStream()) { stream.CopyTo(buffer); bytes = buffer.ToArray(); }
                }
                var assembly = Assembly.Load(bytes);
                var entries = assembly.GetTypes().Where(t => typeof(BaseUnityPlugin).IsAssignableFrom(t) &&
                    t.GetCustomAttributes(typeof(BepInPlugin), false).Length == 1).ToArray();
                if (entries.Length != 1) throw new InvalidOperationException("Expected one private module entry: " + file);
                types.Add(entries[0]);
            }
            host = new GameObject("Unified Effects private modules") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(host);
            foreach (Type type in types)
            {
                var component = host.AddComponent(type) as BaseUnityPlugin;
                if (component == null) throw new InvalidOperationException("Cannot start embedded module: " + type.FullName);
                ModuleRegistry.Register(component);
                log.LogInfo("Embedded effects module instantiated: " + component.Info.Metadata.GUID);
            }
            log.LogInfo("Unified Effects: eight private modules loaded from this DLL; one public plugin entry.");
        }
    }
}
