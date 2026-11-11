using BepInEx.Configuration;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Configuration
{
    /// <summary>
    /// Configuration settings for the Weather feature, dynamic transitions, and procedural rain.
    /// </summary>
    internal sealed class WeatherSettings
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> TapeMoves { get; }
        public ConfigEntry<int> Pace { get; }
        public ConfigEntry<WeatherSeedMode> SeedMode { get; }
        public ConfigEntry<int> FixedSeed { get; }
        public ConfigEntry<float> MinConditions { get; }
        public ConfigEntry<float> MaxConditions { get; }
        public ConfigEntry<float> WindVariability { get; }
        public ConfigEntry<float> TurbulenceMultiplier { get; }

        public ConfigEntry<bool> RainVisualsEnabled { get; }
        public ConfigEntry<bool> RainStreaksEnabled { get; }
        public ConfigEntry<bool> CloudPassageMistEnabled { get; }
        public ConfigEntry<bool> RainAudioEnabled { get; }
        public ConfigEntry<bool> ThunderAudioEnabled { get; }
        public ConfigEntry<bool> CloudPassageShakeEnabled { get; }
        public ConfigEntry<bool> CinematicCloudsEnabled { get; }
        public ConfigEntry<bool> SunShaftsEnabled { get; }
        public ConfigEntry<bool> SunDiscEnabled { get; }
        public ConfigEntry<bool> SkyPhenomenaEnabled { get; }
        public ConfigEntry<bool> AuroraEnabled { get; }
        public ConfigEntry<bool> RedSpritesEnabled { get; }
        public ConfigEntry<bool> BlueJetsEnabled { get; }
        public ConfigEntry<bool> ExtremeStormCloudDeckEnabled { get; }
        public ConfigEntry<bool> ExtremeStormGroundMistEnabled { get; }
        public ConfigEntry<bool> ExtremeStormFogEnabled { get; }
        public ConfigEntry<bool> DarkNightsEnabled { get; }
        public ConfigEntry<bool> OceanWavesEnabled { get; }
        public ConfigEntry<bool> SeaSprayEnabled { get; }
        public ConfigEntry<bool> SeaSprayParticlesEnabled { get; }
        public ConfigEntry<bool> SeaFoamWakeEnabled { get; }
        public ConfigEntry<bool> SeaSurfaceWashEnabled { get; }
        public ConfigEntry<bool> CloudHalfResolution { get; }
        public ConfigEntry<bool> CloudTemporalUpdate { get; }
        public ConfigEntry<float> RainDensity { get; }
        public ConfigEntry<bool> CanopyRainEnabled { get; }
        public ConfigEntry<bool> CanopyShaderEnabled { get; }
        public ConfigEntry<bool> TerrainRainEnabled { get; }
        public ConfigEntry<bool> RainAtmosphereEnabled { get; }
        public ConfigEntry<bool> LightningEnabled { get; }
        public ConfigEntry<bool> ReducedFlashes { get; }

        public ConfigEntry<KeyCode> ConsoleKey { get; }
        public ConfigEntry<bool> ConsoleKeyRequiresCtrl { get; }
        public ConfigEntry<bool> ConsoleKeyOFallback { get; }

        public WeatherSettings(ConfigFile config)
        {
            const string section = "Weather";

            Enabled = config.Bind(section, "Enabled", true,
                "Enable the Weather module: the ENV bezel screen, native weather transitions and local rain.");

            // A new key on purpose: BepInEx keeps the player's stored DynamicWeatherEnabled=false, which
            // must not freeze the new weather. LegacyConfigMigration purges the old key.
            TapeMoves = config.Bind(section, "TapeMoves", true,
                "Let weather systems cross the map over mission time. Off holds the sky where it is " +
                "(the weather console can still change it).");

            Pace = config.Bind(section, "Pace", TapePace.DefaultPace,
                new ConfigDescription(
                    "How fast weather systems cross the map, 1 (slow) to 6 (fast). A stronger mission wind " +
                    "speeds them up.",
                    new AcceptableValueRange<int>(TapePace.MinPace, TapePace.MaxPace)));

            SeedMode = config.Bind(section, "SeedMode", WeatherSeedMode.Random,
                "Where the weather comes from: Random (a fresh sky every launch), Mission (the same sky " +
                "for the same mission) or Fixed (FixedSeed). Host only.");

            FixedSeed = config.Bind(section, "FixedSeed", 0,
                new ConfigDescription(
                    "The seed used when SeedMode is Fixed, 1 to 16777215. 0 means Random.",
                    new AcceptableValueRange<int>(0, WeatherSeed.MaxFixed)));

            MinConditions = config.Bind(section, "MinConditions", 0.05f,
                new ConfigDescription(
                    "Minimum weather condition index (0.0 = completely clear).",
                    new AcceptableValueRange<float>(0.0f, 0.40f)));

            MaxConditions = config.Bind(section, "MaxConditions", 0.88f,
                new ConfigDescription(
                    "Maximum weather condition index (0.8+ enables rain and heavy overcast).",
                    new AcceptableValueRange<float>(0.50f, 1.0f)));

            WindVariability = config.Bind(section, "WindVariability", 0.5f,
                new ConfigDescription(
                    "Wind heading variation as weather changes (0 = static, 1 = 45 degrees). Mission wind speed is preserved.",
                    new AcceptableValueRange<float>(0.0f, 1.0f)));

            TurbulenceMultiplier = config.Bind(section, "TurbulenceMultiplier", 1.0f,
                new ConfigDescription(
                    "Multiplier for the mission baseline turbulence; rain presets never amplify it automatically.",
                    new AcceptableValueRange<float>(0.1f, 3.0f)));

            RainVisualsEnabled = config.Bind(section, "RainVisualsEnabled", true,
                "Master for local rain-streak particles, canopy rain and rain haze. " +
                "Terrain wetness has a separate switch. Applies without restarting.");

            RainStreaksEnabled = config.Bind(section, "RainStreaksEnabled", true,
                "Falling rain streaks around the camera. Requires RainVisualsEnabled; canopy rain and cloud mist have separate switches.");
            CloudPassageMistEnabled = config.Bind(section, "CloudPassageMistEnabled", true,
                "Fine moisture particles while crossing cloud. Requires RainVisualsEnabled; does not change cloud shake, sounds or aircraft physics.");

            RainAudioEnabled = config.Bind(section, "RainAudioEnabled", true,
                "Client-local rain rush and canopy patter through the game's effects mixer. Applies now.");
            ThunderAudioEnabled = config.Bind(section, "ThunderAudioEnabled", true,
                "Additional immediate thunder rumble in the extreme-storm preset. Requires RainAudioEnabled; native thunder is unchanged.");

            CloudPassageShakeEnabled = config.Bind(section, "CloudPassageShakeEnabled", true,
                "Cockpit camera jolts and buffet while crossing clouds. Visual motion only; " +
                "does not change aircraft physics or cloud/rain sounds. Applies now.");

            CinematicCloudsEnabled = config.Bind(section, "CinematicCloudsEnabled", true,
                "Render fly-through volumetric clouds for the current weather state. " +
                "Falls back to native clouds when the shader is unavailable; applies now.");

            SunShaftsEnabled = config.Bind(section, "SunShaftsEnabled", true,
                "Beams of light around the sun, cut by clouds, terrain and aircraft. " +
                "A quarter-resolution pass, drawn only while the sun is in or near the view; applies now.");
            SunDiscEnabled = config.Bind(section, "SunDiscEnabled", true,
                "Weather-tinted sun disc and its cloud extinction. Off restores the native sun material and brightness; sun shafts remain separate.");

            SkyPhenomenaEnabled = config.Bind(section, "SkyPhenomenaEnabled", true,
                "Aurora curtains on clear nights, and red sprites and blue jets above distant night storms. " +
                "Two small particle systems, built only at night; applies now.");
            AuroraEnabled = config.Bind(section, "AuroraEnabled", true,
                "Northern aurora curtains on suitable clear nights. Requires SkyPhenomenaEnabled; applies now.");
            RedSpritesEnabled = config.Bind(section, "RedSpritesEnabled", true,
                "Rare red sprite flashes above distant night thunderstorms. Requires SkyPhenomenaEnabled and visible storm lightning.");
            BlueJetsEnabled = config.Bind(section, "BlueJetsEnabled", true,
                "Rare blue jets above distant night thunderstorms. Requires SkyPhenomenaEnabled and visible storm lightning.");
            ExtremeStormCloudDeckEnabled = config.Bind(section, "ExtremeStormCloudDeckEnabled", true,
                "Extra thickness of the native cloud deck in the extreme-storm preset. Visual only; off restores native thickness without changing weather.");
            ExtremeStormGroundMistEnabled = config.Bind(section, "ExtremeStormGroundMistEnabled", true,
                "Terrain-hugging mist particles in the extreme-storm preset. Requires RainAtmosphereEnabled; separate from distance fog.");
            ExtremeStormFogEnabled = config.Bind(section, "ExtremeStormFogEnabled", true,
                "Extra reduced-visibility fog in the extreme-storm preset. Requires RainAtmosphereEnabled; terrain mist has a separate switch.");

            DarkNightsEnabled = config.Bind(section, "DarkNightsEnabled", false,
                "Darker nights: ambient light, haze and the sky dome dim to about a third under a new moon " +
                "(two fifths under a full one) and turn moonlit blue-grey, so stars and lights stand out. " +
                "Rides on RainAtmosphereEnabled; client-local; applies now.");

            OceanWavesEnabled = config.Bind(section, "OceanWavesEnabled", true,
                "Waves on the game's sea that follow the wind and build under storms: swell, whitecaps, gale streaks and surf. " +
                "Off restores the game's own water. Client-local; applies now.");

            SeaSprayEnabled = config.Bind(section, "SeaSprayEnabled", true,
                "Spray and foam wakes behind aircraft flying low over water. Independent of the weather master and ocean waves; " +
                "client-local in single-player and multiplayer. Applies now.");
            SeaSprayParticlesEnabled = config.Bind(section, "SeaSprayParticlesEnabled", true,
                "Airborne spray sheets from low-flying aircraft over water. Requires SeaSprayEnabled; the KROP water-wake provider takes priority when enabled.");
            SeaFoamWakeEnabled = config.Bind(section, "SeaFoamWakeEnabled", true,
                "Foam patches left on the water behind low-flying aircraft. Requires SeaSprayEnabled; the KROP water-wake provider takes priority when enabled.");
            SeaSurfaceWashEnabled = config.Bind(section, "SeaSurfaceWashEnabled", true,
                "Ruffled water beneath low-flying aircraft in the enhanced ocean shader. Requires SeaSprayEnabled and OceanWavesEnabled; client-local.");

            CloudHalfResolution = config.Bind(section, "CloudHalfResolution", false,
                "Experimental lower-cost half-resolution cloud pass. Falls back to native clouds if the pass cannot run. " +
                "Off (default) draws Boscali clouds at full resolution and can be GPU intensive. Applies now.");

            CloudTemporalUpdate = config.Bind(section, "CloudTemporalUpdate", true,
                "With half-resolution clouds: march a quarter of the cloud pixels each frame and reproject " +
                "the rest from the last frame (about four times cheaper again). Applies now.");

            RainDensity = config.Bind(section, "RainDensity", 1.0f,
                new ConfigDescription(
                    "Density multiplier for falling rain streaks. The 2500-particle budget stays fixed.",
                    new AcceptableValueRange<float>(0.25f, 2.0f)));

            CanopyRainEnabled = config.Bind(section, "CanopyRainEnabled", true,
                "Enable rain droplets on the local aircraft's canopy glass (falls back silently " +
                "when no canopy glass is found).");

            CanopyShaderEnabled = config.Bind(section, "CanopyShaderEnabled", true,
                "Draw rain and airflow runoff on automatically discovered nearby glass submeshes, " +
                "preserving original glass materials. Requires CanopyRainEnabled. Residual drops dry with airspeed. " +
                "Fallback particles require readable glass geometry; otherwise canopy effects stay off.");

            TerrainRainEnabled = config.Bind(section, "TerrainRainEnabled", true,
                "Wet darkening, puddles and sun glints on nearby native terrain, with gradual drying. " +
                "At most eight visible terrain submeshes receive an extra texture-free pass.");

            LightningEnabled = config.Bind(section, "LightningEnabled", true,
                "Spatial storm lightning visuals. Native thunder remains unchanged; no additional delayed thunder is added.");
            ReducedFlashes = config.Bind(section, "ReducedFlashes", false,
                "Suppress visible storm bolts and cloud flashes while independently enabled storm audio remains available.");

            RainAtmosphereEnabled = config.Bind(section, "RainAtmosphereEnabled", true,
                "Thicken and grey the haze and dim ambient light under local rain. Layered on the " +
                "game's own sky each frame and restored when the rain stops.");

            ConsoleKey = config.Bind(section, "ConsoleKey", KeyCode.O,
                "Opens the weather console (host: set and hold states, force storm set-pieces, " +
                "re-roll the cloud layout; clients: read-only). Set to None to disable this configured key; also disable ConsoleKeyOFallback to remove the O shortcut.");

            ConsoleKeyRequiresCtrl = config.Bind(section, "ConsoleKeyRequiresCtrl", true,
                "Require holding Ctrl with the weather console key (Ctrl+O by default).");

            ConsoleKeyOFallback = config.Bind(section, "ConsoleKeyOFallback", true,
                "Also open the console with O or Ctrl+O, in addition to the configured shortcut. Disable if O conflicts with another game binding.");

        }
    }
}
