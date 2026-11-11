using System;
using System.Reflection;
using UnityEngine;

namespace UnifiedEffects
{
    // Observe the private module's live switch without writing either provider's config.
    // If it is unavailable, Boscali remains the existing ocean presentation provider.
    internal static class KropWaterWakeBridge
    {
        private static Func<bool> active;
        private static float nextLookup;
        internal static bool Active
        {
            get
            {
                if (active == null && Time.unscaledTime >= nextLookup)
                {
                    nextLookup = Time.unscaledTime + 5f;
                    try
                    {
                        if (ModuleRegistry.GetPluginInfos().TryGetValue("aaron.boscali.krop-unified", out var info) && info.Instance != null)
                        {
                            var type = info.Instance.GetType().Assembly.GetType("KropClientEffects.VisualRefinements", false);
                            var getter = type?.GetProperty("KropWaterWakesActive", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
                            if (getter != null) active = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
                        }
                    }
                    catch { active = null; }
                }
                if (active == null) return false;
                try { return active(); }
                catch { active = null; nextLookup = Time.unscaledTime + 5f; return false; }
            }
        }
    }
}
