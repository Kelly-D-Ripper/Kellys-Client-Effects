using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>Opt-in, presentation-only storm profile; existing sky numbers and mission pools stay unchanged.</summary>
    internal static class ExtremeStormProfile
    {
        internal const int SkyIndex = 47;
        internal const float UpperDeckBase = 1800f;
        internal const float NativeThickness = 1100f;
        internal const float GroundFogDensity = 0.0032f;
        internal const float RainHazeDensity = 0.0011f;

        internal static bool IsSelected(WeatherKey key) => key != null && key.SkyPin == SkyIndex;

        internal static void ApplyNativeTargets(ref NativeValues values)
        {
            // Ordinary presets keep the conservative 0.6 condition cap. The explicit extreme
            // option asks vanilla for its heaviest cloud/weather set and places it overhead.
            values.Conditions = 1f;
            values.CloudHeight = UpperDeckBase;
        }

        internal static float FogFloor(float altitude, float heightAboveGround, float cloudTop)
        {
            if (!Scalar.IsFinite(altitude) || !Scalar.IsFinite(heightAboveGround) || !Scalar.IsFinite(cloudTop)) return 0f;
            float ground = 1f - WeatherMath.Smoothstep(120f, 650f, Math.Max(0f, heightAboveGround));
            float ceiling = 1f - WeatherMath.Smoothstep(Math.Max(UpperDeckBase, cloudTop), Math.Max(UpperDeckBase, cloudTop) + 500f, altitude);
            return (RainHazeDensity + (GroundFogDensity - RainHazeDensity) * ground) * ceiling;
        }

        internal static float MistAlpha(float age, float distance)
        {
            if (!Scalar.IsFinite(age) || !Scalar.IsFinite(distance)) return 0f;
            return 0.38f * WeatherMath.Smoothstep(0f, 2f, age) *
                (1f - WeatherMath.Smoothstep(1400f, 2400f, Math.Max(0f, distance)));
        }
    }
}
