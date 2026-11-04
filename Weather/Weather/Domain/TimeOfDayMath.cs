using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>24-hour clock arithmetic, independent of the simulation's elapsed mission clock.</summary>
    internal static class TimeOfDayMath
    {
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static float Wrap(float hours)
        {
            if (!Finite(hours)) return 0f;
            float wrapped = hours % 24f;
            return wrapped < 0f ? wrapped + 24f : wrapped;
        }

        internal static float Delta(float from, float to) => Wrap(to - from + 12f) - 12f;

        internal static string ClockText(float hours)
        {
            int minutes = (int)Math.Floor(Wrap(hours) * 60f + 0.0001f) % 1440;
            return (minutes / 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
