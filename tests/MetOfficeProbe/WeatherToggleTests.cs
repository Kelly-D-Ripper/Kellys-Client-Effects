using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

public sealed partial class MetDriver
{
    private void TestWeatherSunAndRainFeatureToggles(ConfigFile config)
    {
        var keys = new[] { "SunDiscEnabled", "RainVisualsEnabled", "RainStreaksEnabled", "CloudPassageMistEnabled" };
        var entries = keys.Select(k => (ConfigEntry<bool>)config[new ConfigDefinition("Weather", k)]).ToArray();
        bool[] saved = entries.Select(e => e.Value).ToArray();
        object disc = Field(manager, "sunDisc");
        var camera = SceneSingleton<CameraStateManager>.i.mainCamera;
        try
        {
            entries[0].Value = false; Call(manager, "LateUpdate");
            Need(Field(disc, "owner") == null && Field(disc, "material") == null && !(bool)Field(disc, "hooked"),
                "sun-disc switch restores native material and removes camera hook");
            entries[0].Value = true; Call(manager, "LateUpdate");
            Need((GameObject)Field(disc, "owner") == LevelInfo.i.sunObject,
                "sun disc reattaches to current native sun after re-enable");

            Call(manager, "EnsureRainSystems");
            object emitter = Field(manager, "rainEmitter");
            Need(emitter != null, "real rain emitter available for child-switch transitions");
            entries[1].Value = true; entries[2].Value = true; entries[3].Value = false;
            Call(manager, "UpdateRainSystems", 123f);
            Call(emitter, "UpdateRain", Vector3.zero, Vector3.zero, 1f, camera, 1f, 1f, 1f, 0f);
            Need((bool)Prop(emitter, "Playing") && (float)Prop(emitter, "EmissionNow") > 0f,
                "falling rain plays while cloud-passage mist is disabled");
            var ps = (ParticleSystem)Field(emitter, "ps");
            ps.Emit(4);
            Need(ps.particleCount > 0, "rain transition fixture has live particles");
            entries[2].Value = false; Call(manager, "UpdateRainSystems", 123f);
            Need(ps.particleCount == 0 && !ps.isPlaying, "disabling rain children clears live streaks immediately");
            entries[3].Value = true; Call(manager, "UpdateRainSystems", 123f);
            Call(emitter, "UpdateRain", Vector3.zero, Vector3.zero, 1f, camera, 1f, 1f, 1f, 0f);
            Need(!ps.isPlaying, "disabled falling rain remains off while cloud mist is enabled");
            Call(emitter, "UpdateRain", Vector3.zero, Vector3.zero, 0f, camera, 1f, 1f, 1f, 1f);
            Need(ps.isPlaying && (float)Prop(emitter, "EmissionNow") > 0f, "cloud mist plays independently of falling rain");
            entries[1].Value = false; Call(manager, "UpdateRainSystems", 123f);
            Need(!ps.isPlaying && ps.particleCount == 0, "rain master overrides both child switches and clears particles");
        }
        finally
        {
            for (int i = 0; i < entries.Length; i++) entries[i].Value = saved[i];
            Call(manager, "LateUpdate");
        }
    }

    // Real Unity particle systems and exact compiled visual classes. No weather key,
    // aircraft physics, multiplayer authority or installed configuration is changed.
    private void TestWeatherSkyFeatureToggles()
    {
        var weather = ModuleInfos()["aaron.boscali.weather-unified"].Instance;
        var asm = weather.GetType().Assembly;
        var type = asm.GetType("BoscaliSummer.Modules.Weather.Visuals.SkyPhenomena", true);
        object sky = Activator.CreateInstance(type, true);
        var strikeType = asm.GetType("BoscaliSummer.Modules.Weather.Domain.LightningEvent", true);
        var hash = asm.GetType("BoscaliSummer.Modules.Weather.Domain.WeatherMath", true).GetMethod("Hash01", F);
        uint jetSeed = 0, spriteSeed = 0;
        for (uint seed = 1; seed < 10000 && (jetSeed == 0 || spriteSeed == 0); seed++)
        {
            float value = (float)hash.Invoke(null, new object[] { seed, 3, 7, 0 });
            if (value < .015f) jetSeed = seed;
            else if (value <= .07f) spriteSeed = seed;
        }
        object Strike(uint seed)
        {
            object strike = Activator.CreateInstance(strikeType);
            FI(strike, "X").SetValue(strike, 60000f);
            FI(strike, "Top").SetValue(strike, 12000f);
            FI(strike, "Seed").SetValue(strike, seed);
            return strike;
        }
        int Transients() => Field(sky, "sprites") is ParticleSystem transientSystem ? transientSystem.particleCount : 0;
        void Fire(uint seed) => Call(sky, "Strike", Vector3.zero, Strike(seed));
        try
        {
            Need(jetSeed != 0 && spriteSeed != 0, "deterministic eligible sprite and jet strike seeds found");
            FI(sky, "night").SetValue(sky, 1f);
            Call(sky, "ConfigureFeatures", false, true, false); Fire(jetSeed);
            Need(Transients() == 0, "blue jet disabled prevents particle allocation");
            Fire(spriteSeed);
            Need(Transients() > 0, "red sprites emit while aurora and jets are disabled");
            Call(sky, "ConfigureFeatures", false, true, true); Fire(jetSeed);
            var ps = (ParticleSystem)Field(sky, "sprites");
            var particles = new ParticleSystem.Particle[48];
            int count = ps.GetParticles(particles);
            Need(particles.Take(count).Any(p => p.randomSeed == 1u) && particles.Take(count).Any(p => p.randomSeed == 2u),
                "red sprites and blue jets coexist in bounded shared system");
            Call(sky, "ConfigureFeatures", false, false, true);
            count = ps.GetParticles(particles);
            Need(count > 0 && particles.Take(count).All(p => p.randomSeed == 1u),
                "disabling red sprites removes only red particles immediately");
            int jets = count; Fire(spriteSeed);
            Need(Transients() == jets, "disabled red sprite cannot emit through later strike");
            Call(sky, "ConfigureFeatures", false, false, false);
            Need(Transients() == 0 && Field(sky, "root") == null, "all sky children off release particle root");
            Call(sky, "ConfigureFeatures", true, true, true);
            var camera = SceneSingleton<CameraStateManager>.i.mainCamera;
            for (int i = 0; i < 120; i++) Call(sky, "Tick", camera, -1f, 1f, 1f, 123u, 0f);
            Need((int)Prop(sky, "AuroraParticles") > 0, "aurora can create particles after re-enable");
            Call(sky, "ConfigureFeatures", false, true, true);
            Need((int)Prop(sky, "AuroraParticles") == 0 && (float)Prop(sky, "AuroraLevel") == 0f,
                "aurora off clears existing curtains immediately");
        }
        finally { Call(sky, "Reset"); }
    }

    // Call inside the existing extreme-storm fixture after its native deck and mist
    // are live. Exercise actual config -> WeatherManager -> visual wiring.
    private void TestWeatherExtremeFeatureToggles(ConfigFile config)
    {
        var keys = new[] { "ExtremeStormCloudDeckEnabled", "ExtremeStormGroundMistEnabled", "ExtremeStormFogEnabled" };
        var entries = keys.Select(k => (ConfigEntry<bool>)config[new ConfigDefinition("Weather", k)]).ToArray();
        bool[] saved = entries.Select(e => e.Value).ToArray();
        object dressing = Field(manager, "extremeDressing");
        float conditions = LevelInfo.i.conditions, cloudBase = LevelInfo.i.cloudHeight;
        byte pin = (byte)Prop(Field(manager, "fieldKey"), "SkyPin");
        try
        {
            entries[0].Value = false; Call(manager, "UpdateRainSystems", 123f);
            Need(!(bool)Prop(dressing, "DeckApplied"), "cloud-deck toggle releases native thickness override");
            Need(Field(dressing, "root") != null, "cloud-deck toggle preserves separate terrain mist");
            entries[0].Value = true; entries[1].Value = false; Call(manager, "UpdateRainSystems", 123f);
            Need((bool)Prop(dressing, "DeckApplied") && (int)Prop(dressing, "MistParticles") == 0 && Field(dressing, "root") == null,
                "ground-mist toggle clears particles without removing native deck");
            entries[1].Value = true; Call(manager, "UpdateRainSystems", 123f);
            Need(Field(dressing, "root") != null, "ground mist rebuilds after re-enable");
            entries[2].Value = false; Call(manager, "UpdateRainSystems", 123f);
            Need(RenderSettings.fogDensity <= .000551f, "extreme-fog toggle removes severe visibility floor");
            entries[2].Value = true; Call(manager, "UpdateRainSystems", 123f);
            Need((bool)Prop(dressing, "DeckApplied"), "re-enabling extreme fog preserves deck");
            Need(LevelInfo.i.conditions == conditions && LevelInfo.i.cloudHeight == cloudBase &&
                (byte)Prop(Field(manager, "fieldKey"), "SkyPin") == pin,
                "extreme visual switches do not change preset or native weather conditions");
        }
        finally
        {
            for (int i = 0; i < entries.Length; i++) entries[i].Value = saved[i];
            Call(manager, "UpdateRainSystems", 123f);
        }
    }

    // Call from TestSeaSpray while its fixed-wing over-water fixture is live.
    private void TestWeatherSeaFeatureToggles()
    {
        var weather = ModuleInfos()["aaron.boscali.weather-unified"].Instance;
        Type type = weather.GetType().Assembly.GetType("BoscaliSummer.Modules.Weather.Visuals.OceanDressing", true);
        var ocean = Resources.FindObjectsOfTypeAll(type).Single();
        var spray = Field(ocean, "spray");
        var camera = SceneSingleton<CameraStateManager>.i.mainCamera;
        var keys = new[] { "SeaSprayParticlesEnabled", "SeaFoamWakeEnabled", "SeaSurfaceWashEnabled" };
        var entries = keys.Select(k => (ConfigEntry<bool>)weather.Config[new ConfigDefinition("Weather", k)]).ToArray();
        bool[] saved = entries.Select(e => e.Value).ToArray();
        int Count(string name) => (int)Prop(spray, name);
        void Window() { for (int i = 0; i < 4; i++) Call(ocean, "Tick", LevelInfo.i, camera, GameState.Multiplayer, .05f); }
        try
        {
            foreach (var entry in entries) entry.Value = true;
            Window(); Need(Count("Particles") > 0 && Count("WakeParticles") > 0 && Count("WashCount") > 0,
                "sea children initially emit independently of weather authority");
            entries[0].Value = false; Window();
            Need(Count("Particles") == 0 && Count("WakeParticles") > 0 && Count("WashCount") > 0,
                "spray child off clears spray and preserves foam and wash");
            entries[1].Value = false; Window();
            Need(Count("Particles") == 0 && Count("WakeParticles") == 0 && Count("WashCount") > 0,
                "foam child off clears foam and preserves surface wash");
            entries[2].Value = false; Window();
            Need(Count("WashCount") == 0 && Field(spray, "root") == null,
                "all sea children off release particles and clear shader wash");
            foreach (var entry in entries) entry.Value = true;
            Window(); Need(Count("Particles") > 0 && Count("WakeParticles") > 0 && Count("WashCount") > 0,
                "sea children rebuild after re-enable");
        }
        finally { for (int i = 0; i < entries.Length; i++) entries[i].Value = saved[i]; Window(); }
    }
}
