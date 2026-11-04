using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliClientExperimental.Weather
{
    // Private experiment only: the upstream bundled canopy shader's adapted source is missing.
    internal static class LocalWeatherBundle
    {
        private static AssetBundle bundle;
        private static bool attempted;
        private static readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();
        internal static Shader GetOceanShader() => GetShader("Boscali/Ocean");
        internal static Shader GetTerrainShader() => GetShader("Boscali/TerrainRain");
        internal static Shader GetShader(string name)
        {
            if (shaders.TryGetValue(name, out Shader cached)) return cached;
            if (!attempted)
            {
                attempted = true;
                try
                {
                    using (Stream stream = typeof(LocalWeatherBundle).Assembly.GetManifestResourceStream("BoscaliClientExperimental.weatherrain.bundle"))
                    {
                        if (stream == null || stream.Length > 4 * 1024 * 1024) return null;
                        using (var memory = new MemoryStream()) { stream.CopyTo(memory); bundle = AssetBundle.LoadFromMemory(memory.ToArray()); }
                    }
                }
                catch (Exception e) { Debug.LogWarning("[Boscali experiment] Weather shader bundle failed: " + e.Message); }
            }
            if (bundle == null) { shaders[name] = null; return null; }
            foreach (Shader shader in bundle.LoadAllAssets<Shader>())
                if (shader != null && shader.name == name && shader.isSupported) { shaders[name] = shader; return shader; }
            shaders[name] = null;
            Debug.LogWarning("[Unified effects] Weather shader unavailable: " + name + "; effect will fall back or remain disabled.");
            return null;
        }
    }
}
