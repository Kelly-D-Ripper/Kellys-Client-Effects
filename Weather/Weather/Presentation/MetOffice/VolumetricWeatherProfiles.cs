namespace BoscaliSummer.Modules.Weather.Presentation
{
    // Pack-authored targets for NOAtmoFX's public weather API, not NOAtmoFX quality
    // presets or replicas of Boscali's authored three-layer skies. The matching
    // Boscali sky supplies our precipitation and atmosphere while the add-on
    // generates its own cloud shapes. Nothing here enables a disabled effect.
    internal readonly struct VolumetricWeatherProfile
    {
        internal readonly string Id, SimpleName, TechnicalName;
        internal readonly byte BoscaliSkyIndex;
        internal readonly float Conditions, CloudBaseMetres, HumidityBias,
            InstabilityBias, CoverageScale, LightningScale;
        internal readonly int Front;

        internal VolumetricWeatherProfile(string id, string simpleName, string technicalName,
            byte boscaliSkyIndex, float conditions, float cloudBaseMetres, float humidityBias,
            float instabilityBias, float coverageScale, int front, float lightningScale)
        {
            Id = id;
            SimpleName = simpleName;
            TechnicalName = technicalName;
            BoscaliSkyIndex = boscaliSkyIndex;
            Conditions = conditions;
            CloudBaseMetres = cloudBaseMetres;
            HumidityBias = humidityBias;
            InstabilityBias = instabilityBias;
            CoverageScale = coverageScale;
            Front = front;
            LightningScale = lightningScale;
        }

        internal string Name(bool simple) => simple ? SimpleName : TechnicalName;
    }

    internal static class VolumetricWeatherProfiles
    {
        // Humidity and instability are additional biases, not absolute values.
        // Low stable decks use negative instability; storms supply a positive
        // bias so they remain convective when the add-on's day cycle cools down.
        // Coverage zero removes cumulus in the clear profile. The player's own
        // upper-cloud, fog, lightning and other feature preferences still apply.
        private static readonly VolumetricWeatherProfile[] Profiles =
        {
            new VolumetricWeatherProfile("clear", "Clear sky", "Clear / dry air",
                0, 0f, 2400f, -0.30f, -0.20f, 0f, 0, 0f),
            new VolumetricWeatherProfile("scattered", "Scattered clouds", "Scattered cumulus",
                14, 0.22f, 1500f, -0.02f, 0.08f, 1f, 0, 0f),
            new VolumetricWeatherProfile("overcast", "Thick cloud cover", "Overcast stratiform deck",
                29, 0.65f, 900f, 0.10f, -0.30f, 1.25f, 0, 0f),
            new VolumetricWeatherProfile("low-cloud", "Low cloud and fog", "Low stratus / moist stable air",
                28, 0.55f, 250f, 0.20f, -0.30f, 1.25f, 0, 0f),
            new VolumetricWeatherProfile("heavy-rain", "Heavy rain", "Nimbostratus / warm front",
                39, 0.80f, 1000f, 0.18f, -0.25f, 1.30f, 2, 0f),
            new VolumetricWeatherProfile("thunderstorm", "Thunderstorm", "Cumulonimbus / cold front",
                44, 0.92f, 900f, 0.08f, 0.30f, 1.15f, 1, 1f),
            new VolumetricWeatherProfile("extreme-storm", "Extreme storm", "Severe convection / low cloud",
                47, 1f, 300f, 0.30f, 0.50f, 1.50f, 1, 1.50f),
        };

        internal static int Count => Profiles.Length;
        internal static VolumetricWeatherProfile Get(int index) => Profiles[index];
        internal static bool TryGet(int index, out VolumetricWeatherProfile profile)
        {
            if (index < 0 || index >= Profiles.Length)
            {
                profile = default;
                return false;
            }
            profile = Profiles[index];
            return true;
        }
    }
}
