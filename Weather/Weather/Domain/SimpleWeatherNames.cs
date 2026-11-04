using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    // Keep this explicit and in library order: these describe the actual presets,
    // not a calculation from their changing local weather sample.
    internal static class SimpleWeatherNames
    {
        private static readonly string[] Names = {
            "Clear blue sky", "Thin wispy clouds", "Wispy clouds and patches", "Thin milky cloud", "Ripples of high cloud", "Clear with haze", "High patchy cloud",
            "A few fluffy clouds", "Fluffy and wispy clouds", "Rows of fluffy clouds", "A few small clouds", "Patchy clouds at two heights", "Fluffy clouds under high veil", "Clear after rain",
            "Scattered fluffy clouds", "Growing clouds, patchy rain", "Isolated storm clouds", "Ripples over fluffy clouds", "Scattered low cloud", "Clouds at two heights", "Scattered showers",
            "Mostly cloudy, gaps", "High cloud with gaps", "Clouds at three heights", "Fluffy clouds under a layer", "Low cloud, mostly covered", "Thickening cloud", "Line of growing clouds",
            "Very low cloud", "Grey cloud at two heights", "Two thick cloud layers", "Solid low cloud, clear above", "Thick high cloud", "Thick cloud before rain",
            "Steady rain", "Low cloud and drizzle", "Line of heavy showers", "Showers under grey cloud", "Low cloud and light drizzle", "Heavy steady rain", "Frequent heavy showers",
            "Cluster of thunderstorms", "Storms in steady rain", "Severe isolated storm", "Line of severe storms", "Evening thunderstorms", "Storm under thick cloud", "Extreme storm and low fog"
        };

        internal static string Get(int index) => index >= 0 && index < Names.Length ? Names[index] : "Unknown weather";
        internal static int Count => Names.Length;
    }
}
