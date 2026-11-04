using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal static class NativeCloudCeiling
    {
        // The game's CloudLayer spawns at cloudHeight + spawnOpacity * layerThickness,
        // with a diameter of up to 1.1 * cloudSizeMax (Mathf.Lerp clamps sizeFactor).
        // Particle edges are approximate; no camera-dependent scene search is needed.
        internal static float? Estimate(float height, float thickness, float maxSize)
        {
            bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
            if (!Finite(height) || !Finite(thickness) || !Finite(maxSize) || height <= 0f ||
                thickness < 0f || maxSize <= 0f || thickness > 5000f || maxSize > 10000f) return null;
            return height + thickness + maxSize * .55f;
        }

        internal static float Fade(float altitude, float? ceiling)
        {
            if (!ceiling.HasValue || float.IsNaN(altitude) || float.IsInfinity(altitude)) return 1f;
            // Fade over the upper 300m to avoid a visible on/off plane.
            return 1f - WeatherMath.Smoothstep(ceiling.Value - 300f, ceiling.Value, altitude);
        }
    }
}
