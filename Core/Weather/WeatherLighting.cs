using UnityEngine;
namespace BoscaliClientExperimental.Weather
{
    internal static class WeatherLighting
    {
        internal static Light Source(LevelInfo level) =>
            Visible(level != null ? level.sun : null) ? level.sun :
            Visible(level != null ? level.moon : null) ? level.moon : null;
        private static bool Visible(Light light) => light != null && light.isActiveAndEnabled && light.intensity > 0f;
    }
}
