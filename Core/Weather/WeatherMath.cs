using UnityEngine;

namespace BoscaliClientExperimental.Weather
{
    internal static class Scalar
    {
        internal static float Clamp01(float value) => Mathf.Clamp01(value);
        internal static float Lerp(float a, float b, float t) => a + (b - a) * t;
        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
    internal static class WeatherMath
    {
        internal static float HashRange(uint seed, int a, int b, int c, float min, float max)
            => Scalar.Lerp(min, max, Deterministic.UnitFloat(Deterministic.Hash((int)seed, a, b, c)));
        internal static float Smoothstep(float a, float b, float value)
        {
            float t = Mathf.Clamp01((value - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
