using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

// Deliberately forbidden source-prefab behavior. The test source is always
// inactive, and a successful renderer copy must never instantiate this component.
public sealed class KropForbiddenPrefabSpy : MonoBehaviour
{
    internal static int Calls;
    private void Awake() { Calls++; }
    private void OnEnable() { Calls++; }
}

public sealed partial class MetDriver
{
    // Called from TestSeaSpray after that fixture has placed its real fixed-wing
    // aircraft low and fast over water. This method does not move the aircraft,
    // change its velocity, invoke native FixedUpdate, or change gameplay state.
    private void TestKropWaterProvider(Aircraft plane)
    {
        var modules = ModuleInfos();
        var krop = modules["aaron.boscali.krop-unified"].Instance;
        var asm = krop.GetType().Assembly;
        var runtime = asm.GetType("KropClientEffects.VisualRefinements", true);
        var wakePatch = asm.GetType("KropClientEffects.Patches.AirWakeEffectPatch", true);
        var objects = asm.GetType("KropClientEffects.VisualObjects", true);
        var weather = modules["aaron.boscali.weather-unified"].Instance;
        var oceanType = weather.GetType().Assembly.GetType("BoscaliSummer.Modules.Weather.Visuals.OceanDressing", true);
        var ocean = (MonoBehaviour)Resources.FindObjectsOfTypeAll(oceanType).Single();
        var spray = Field(ocean, "spray");
        var oceanTick = oceanType.GetMethod("Tick", F);
        var visualTick = runtime.GetMethod("Tick", F);
        var wakeTick = wakePatch.GetMethod("Postfix", F);
        var camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
        var master = krop.Config[new ConfigDefinition("00 General", "Enable pack")];
        var wake = krop.Config[new ConfigDefinition("04 Low-flight wake", "Air wake effects")];
        var speed = krop.Config[new ConfigDefinition("04 Low-flight wake", "Minimum speed kmh")];
        var altitude = krop.Config[new ConfigDefinition("04 Low-flight wake", "Maximum height m")];
        var spacing = krop.Config[new ConfigDefinition("04 Low-flight wake", "Burst spacing m")];
        var water = krop.Config[new ConfigDefinition("08 Visual refinements", "KROP water wakes")];
        var boscali = weather.Config[new ConfigDefinition("Weather", "SeaSprayEnabled")];
        var saved = new Dictionary<ConfigEntryBase, object>();
        foreach (var entry in new[] { master, wake, speed, altitude, spacing, water, boscali }) saved[entry] = entry.BoxedValue;
        var oldSystems = new HashSet<ParticleSystem>(((IEnumerable)wakePatch.GetField("_providerSystems", F).GetValue(null)).Cast<ParticleSystem>());
        var random = UnityEngine.Random.state;
        var added = new List<ParticleSystem>();
        int Count(string name) => (int)Prop(spray, name);
        void OceanWindow()
        {
            for (int i = 0; i < 4; i++) oceanTick.Invoke(ocean, new object[] { LevelInfo.i, camera, GameState.Multiplayer, .05f });
        }
        void WakeWindow()
        {
            for (int i = 0; i < 16; i++) wakeTick.Invoke(null, new object[] { plane });
        }
        try
        {
            Need(plane != null && plane.rb != null && !plane.disabled && camera != null && Datum.origin != null && Time.fixedDeltaTime > 0f,
                "KROP water live aircraft and fixed-step fixture ready");
            Need(!(bool)wakePatch.GetMethod("HasRotor", F).Invoke(null, new object[] { plane }), "KROP water fixture uses supported fixed-wing aircraft");
            master.BoxedValue = true; wake.BoxedValue = true; speed.BoxedValue = 400f;
            altitude.BoxedValue = 30f; spacing.BoxedValue = 10f; water.BoxedValue = false; boscali.BoxedValue = true;
            visualTick.Invoke(null, null); OceanWindow();
            Need(Count("Particles") > 0 && Count("WakeParticles") > 0 && Count("WashCount") > 0,
                "Boscali spray and foam are live before selecting KROP provider");

            water.BoxedValue = true; visualTick.Invoke(null, null); WakeWindow();
            var stateTable = wakePatch.GetField("_states", F).GetValue(null);
            var stateArgs = new object[] { plane, null };
            Need((bool)stateTable.GetType().GetMethod("TryGetValue").Invoke(stateTable, stateArgs), "KROP water fixture has tracked its actual aircraft");
            var systems = ((ParticleSystem[])stateArgs[1].GetType().GetField("Systems", F).GetValue(stateArgs[1])) ?? Array.Empty<ParticleSystem>();
            added.AddRange(systems.Where(x => !oldSystems.Contains(x)));
            Need(systems.Length >= 2 && systems.Sum(x => x.particleCount) > 0, "real KROP low-water postfix emits particles");
            Need(systems.All(x => x.main.simulationSpace == ParticleSystemSimulationSpace.Custom && x.main.customSimulationSpace == Datum.origin),
                "real KROP water particles retain floating-origin space");
            Need(systems.All(x => x.GetComponent<ParticleSystemRenderer>().sharedMaterial != null &&
                x.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader != null && x.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader.isSupported),
                "real KROP water renderer has supported material shader");
            var emitted = systems.First(x => x.particleCount > 0);
            var sample = new ParticleSystem.Particle[Math.Min(emitted.particleCount, 32)];
            Need(emitted.GetParticles(sample) > 0 && sample[0].startColor.b > sample[0].startColor.r,
                "KROP fixture emitted water tint rather than land dust");
            OceanWindow();
            Need(Count("Particles") == 0 && Count("WakeParticles") == 0 && Count("WashCount") == 0 && (bool)boscali.BoxedValue,
                "KROP provider suppresses live Boscali wash without rewriting its preference");

            water.BoxedValue = false; visualTick.Invoke(null, null); OceanWindow();
            Need(systems.Sum(x => x.particleCount) == 0 && Count("Particles") > 0 && Count("WakeParticles") > 0 && Count("WashCount") > 0 && (bool)boscali.BoxedValue,
                "KROP water switch off clears its particles and restores Boscali spray and foam");
            water.BoxedValue = true; visualTick.Invoke(null, null); WakeWindow(); OceanWindow();
            Need(systems.Sum(x => x.particleCount) > 0 && Count("Particles") == 0, "KROP water can be enabled again during the same mission");
            master.BoxedValue = false; visualTick.Invoke(null, null); OceanWindow();
            Need(systems.Sum(x => x.particleCount) == 0 && Count("Particles") > 0 && Count("WakeParticles") > 0 && (bool)boscali.BoxedValue,
                "KROP master off releases provider and restores Boscali without config mutation");
            Need(UnityEngine.Random.state.Equals(random), "real KROP water provider leaves gameplay random state unchanged");
        }
        finally
        {
            wakePatch.GetMethod("ClearProviderParticles", F).Invoke(null, null);
            foreach (var system in added) if (system != null) DestroyImmediate(system.gameObject);
            // Also collect newly created systems if an assertion failed before the
            // local list was populated; leave all pre-existing objects untouched.
            foreach (var system in ((IEnumerable)wakePatch.GetField("_providerSystems", F).GetValue(null)).Cast<ParticleSystem>().ToArray())
                if (system != null && !oldSystems.Contains(system)) DestroyImmediate(system.gameObject);
            objects.GetMethod("Tick", F).Invoke(null, null);
            foreach (var pair in saved) pair.Key.BoxedValue = pair.Value;
            visualTick.Invoke(null, null);
            oceanType.GetMethod("ResetForScene", F).Invoke(ocean, null);
            UnityEngine.Random.state = random;
        }
    }

    private void TestKropVisualRefinements()
    {
        var info = ModuleInfos()["aaron.boscali.krop-unified"];
        var asm = info.Instance.GetType().Assembly;
        var copier = asm.GetType("KropClientEffects.ParticleVisualCopy", true);
        var runtime = asm.GetType("KropClientEffects.VisualRefinements", true);
        var settings = asm.GetType("KropClientEffects.VisualRefinementSettings", true);
        var config = info.Instance.Config;
        var options = new[] { "Varied fuel explosions", "Fuel explosion audio", "Extended burning embers", "KROP water wakes", "Delayed lifeboat beacons" };
        var saved = new Dictionary<ConfigEntry<bool>, bool>();
        foreach (var name in options)
        {
            Need(config.TryGetEntry<bool>(new ConfigDefinition("08 Visual refinements", name), out var entry), "optional visual setting bound: " + name);
            saved[entry] = entry.Value;
            Need((bool)entry.DefaultValue == false, "new visual feature defaults off: " + name);
        }
        ConfigEntry<bool> fuel = (ConfigEntry<bool>)settings.GetField("FuelExplosions", F).GetValue(null);
        ConfigEntry<bool> sound = (ConfigEntry<bool>)settings.GetField("FuelAudio", F).GetValue(null);
        ConfigEntry<bool> embers = (ConfigEntry<bool>)settings.GetField("ExtendedEmbers", F).GetValue(null);
        ConfigEntry<bool> water = (ConfigEntry<bool>)settings.GetField("WaterWakes", F).GetValue(null);
        ConfigEntry<bool> beacon = (ConfigEntry<bool>)settings.GetField("DelayedBeacons", F).GetValue(null);
        var chance = config[new ConfigDefinition("08 Visual refinements", "Fuel explosion chance")]; object oldChance = chance.BoxedValue;
        var master = config[new ConfigDefinition("00 General", "Enable pack")];
        object oldMaster = master.BoxedValue;
        var wake = config[new ConfigDefinition("04 Low-flight wake", "Air wake effects")]; object oldWake = wake.BoxedValue;
        var boatBeacons = config[new ConfigDefinition("07 Cosmetic lifeboats", "Blinking beacons")]; object oldBoatBeacons = boatBeacons.BoxedValue;
        var random = UnityEngine.Random.state;
        GameObject source = null, clone = null, boat = null;
        AudioClip clip = null;
        try
        {
            source = new GameObject("KROP source prefab fixture"); source.SetActive(false);
            KropForbiddenPrefabSpy.Calls = 0; source.AddComponent<KropForbiddenPrefabSpy>();
            source.AddComponent<BoxCollider>(); source.AddComponent<Rigidbody>();
            var original = source.AddComponent<ParticleSystem>(); original.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = original.main; main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f); main.maxParticles = 9999;
            main.stopAction = ParticleSystemStopAction.Callback;
            var collision = original.collision; collision.enabled = true;
            var trigger = original.trigger; trigger.enabled = true;
            var originalAudio = source.AddComponent<AudioSource>(); originalAudio.playOnAwake = false;
            clip = AudioClip.Create("KROP visual audio fixture", 256, 1, 8000, false); originalAudio.clip = clip;
            var sub = new GameObject("Ember child"); sub.transform.SetParent(source.transform, false);
            var childPs = sub.AddComponent<ParticleSystem>(); childPs.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var emptySprites = childPs.textureSheetAnimation;
            emptySprites.mode = ParticleSystemAnimationMode.Sprites; emptySprites.enabled = true;
            Need(emptySprites.spriteCount > 0 && emptySprites.GetSprite(0) == null, "null sprite fixture includes Unity's empty default slot");
            var subEmitters = original.subEmitters; subEmitters.enabled = true;
            subEmitters.AddSubEmitter(childPs, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            for (int i = 0; i < 14; i++)
            {
                var extra = new GameObject("Population bound fixture " + i); extra.transform.SetParent(source.transform, false);
                extra.AddComponent<ParticleSystem>().Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            clone = new GameObject("KROP safe clone fixture"); clone.SetActive(false);
            clone.transform.SetParent(Datum.origin, false);
            var audio = new List<AudioSource>();
            var beforeRandom = UnityEngine.Random.state;
            copier.GetMethod("Copy", F).Invoke(null, new object[] { source.transform, clone.transform, new Vector3(100f, 0f, 0f), true, audio });
            Need(UnityEngine.Random.state.Equals(beforeRandom), "fuel renderer copy preserves gameplay RNG state");
            var components = clone.GetComponentsInChildren<Component>(true);
            Need(components.All(c => c is Transform || c is ParticleSystem || c is ParticleSystemRenderer || c is AudioSource), "fuel renderer copy contains only allowlisted visual/audio components");
            Need(clone.GetComponentInChildren<KropForbiddenPrefabSpy>(true) == null && KropForbiddenPrefabSpy.Calls == 0, "source prefab callbacks never cloned or executed");
            Need(clone.GetComponentInChildren<Collider>(true) == null && clone.GetComponentInChildren<Rigidbody>(true) == null, "fuel copy has no colliders or rigidbodies");
            var cloned = clone.GetComponent<ParticleSystem>();
            Need(cloned.textureSheetAnimation.mode == ParticleSystemAnimationMode.Grid, "default grid animation copies without adding null sprites");
            var clonedChild = clone.transform.Find("Ember child").GetComponent<ParticleSystem>();
            Need(clonedChild.textureSheetAnimation.mode == ParticleSystemAnimationMode.Sprites && !clonedChild.textureSheetAnimation.enabled,
                "sprite animation with only null slots safely disables instead of throwing");
            Need(cloned != null && cloned.main.maxParticles <= 512, "fuel particle population capped per system");
            Need(clone.GetComponentsInChildren<ParticleSystem>(true).Length == 12, "large source prefab stops at twelve copied particle systems");
            Need(cloned.main.stopAction == ParticleSystemStopAction.None && !cloned.collision.enabled && !cloned.trigger.enabled && !cloned.externalForces.enabled, "particle callbacks collision triggers and external forces disabled");
            Need(cloned.main.simulationSpace == ParticleSystemSimulationSpace.Custom && cloned.main.customSimulationSpace == Datum.origin, "fuel particles use origin-safe custom simulation");
            Need(Mathf.Approximately(cloned.main.startLifetime.constantMin, 6f) && Mathf.Approximately(cloned.main.startLifetime.constantMax, 12f), "extended ember lifetimes scale both constant endpoints");
            Need(cloned.subEmitters.subEmittersCount == 1 && cloned.subEmitters.GetSubEmitterSystem(0) != childPs
                && cloned.subEmitters.GetSubEmitterSystem(0).transform.IsChildOf(clone.transform), "subemitters remap inside local visual hierarchy");
            Need(audio.Count == 1 && audio[0] != originalAudio && !audio[0].playOnAwake, "fuel sound uses a separate non-autoplay local source");
            clone.SetActive(true);
            Need(KropForbiddenPrefabSpy.Calls == 0, "activating the safe copy cannot run source prefab callbacks");

            // Exercise provider ownership without writing the Boscali preference.
            var weather = ModuleInfos()["aaron.boscali.weather-unified"].Instance.Config;
            ConfigEntry<bool> boscaliSpray;
            Need(weather.TryGetEntry<bool>(new ConfigDefinition("Weather", "SeaSprayEnabled"), out boscaliSpray), "Boscali spray preference exists for provider handoff");
            bool savedSpray = boscaliSpray.Value;
            master.BoxedValue = true; wake.BoxedValue = true; water.Value = true;
            Need((bool)runtime.GetProperty("KropWaterWakesActive", F).GetValue(null), "KROP water provider selected when its local controls are enabled");
            wake.BoxedValue = false;
            Need(!(bool)runtime.GetProperty("KropWaterWakesActive", F).GetValue(null), "disabled wake master releases water provider");
            wake.BoxedValue = true; master.BoxedValue = false;
            Need(!(bool)runtime.GetProperty("KropWaterWakesActive", F).GetValue(null) && boscaliSpray.Value == savedSpray, "KROP master releases provider without changing saved Boscali preference");
            master.BoxedValue = true;

            // Synthetic effect exercises cleanup without igniting a real aircraft.
            var effectType = runtime.GetNestedType("Effect", F);
            var effect = Activator.CreateInstance(effectType, true);
            effectType.GetField("Root", F).SetValue(effect, clone);
            effectType.GetField("Expires", F).SetValue(effect, Time.time + 30f);
            var audioList = (IList)effectType.GetField("Audio", F).GetValue(effect); audioList.Add(audio[0]);
            var effects = (IList)runtime.GetField("Effects", F).GetValue(null); effects.Add(effect);
            fuel.Value = true; embers.Value = false; sound.Value = false;
            runtime.GetField("previousEmbers", F).SetValue(null, false);
            audio[0].loop = true; audio[0].Play();
            runtime.GetMethod("Tick", F).Invoke(null, null);
            Need(!audio[0].isPlaying, "disabling extra fuel audio stops existing owned sounds");
            fuel.Value = false;
            runtime.GetMethod("Tick", F).Invoke(null, null);
            Need((int)runtime.GetProperty("ActiveFuelEffects", F).GetValue(null) == 0, "disabling varied fuel explosions immediately removes tracked added effects");
            clone = null; // runtime owns its deferred destruction

            boat = new GameObject("Beacon timing fixture"); boat.SetActive(false);
            var boatType = asm.GetType("KropClientEffects.LifeboatVisual", true);
            var visual = boat.AddComponent(boatType);
            boatType.GetMethod("Initialize", F).Invoke(visual, null);
            float ready = (float)boatType.GetField("beaconReadyAt", F).GetValue(visual);
            Need(ready >= Time.time && ready <= Time.time + 5.01f, "new lifeboat beacon delay stays within KROP 0-5 seconds");
            beacon.Value = true;
            boatBeacons.BoxedValue = true;
            boatType.GetField("beaconReadyAt", F).SetValue(visual, Time.time + 5f);
            boatType.GetMethod("Update", F).Invoke(visual, null);
            Need(!(bool)boatType.GetField("previousBeaconOn", F).GetValue(visual), "delayed beacon stays off before activation");
            beacon.Value = false;
            boatType.GetField("beaconPhase", F).SetValue(visual, -(Time.time % 1.5f));
            boatType.GetMethod("Update", F).Invoke(visual, null);
            Need((bool)boatType.GetField("previousBeaconOn", F).GetValue(visual), "disabling beacon delay restores current blinking phase immediately");

            var native = (GameObject)runtime.GetMethod("FindCookoffPrefab", F).Invoke(null, null);
            if (native == null) Note("OPEN native AmmoCookoffVehicle prefab is not loaded in this fixture; synthetic copier tests passed, native appearance needs a ground-vehicle mission");
            else
            {
                clone = new GameObject("KROP native cookoff visual fixture"); clone.SetActive(false);
                clone.transform.SetParent(Datum.origin, false);
                clone.transform.position = camera.transform.position + camera.transform.forward * 80f + camera.transform.up * 12f;
                var nativeAudio = new List<AudioSource>();
                var playback = copier.GetMethod("Copy", F).Invoke(null, new object[] { native.transform, clone.transform, Vector3.zero, true, nativeAudio });
                var starts = (IEnumerable)playback.GetType().GetField("Particles", F).GetValue(playback);
                var nativeSystems = clone.GetComponentsInChildren<ParticleSystem>(true);
                Need(nativeSystems.Length > 0 && nativeSystems.Length <= 12, "loaded native cookoff renderer copied within system cap");
                Need(clone.GetComponentsInChildren<Component>(true).All(c => c is Transform || c is ParticleSystem || c is ParticleSystemRenderer || c is AudioSource), "loaded native cookoff copy also excludes damaging and scripted behaviours");
                var renderers = clone.GetComponentsInChildren<ParticleSystemRenderer>(true);
                Need(renderers.Any(r => r.sharedMaterials.Any(m => m != null)) && renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).All(m => m.shader != null && m.shader.isSupported), "native cookoff copy uses supported non-missing shaders");
                clone.SetActive(true);
                int starting = 0;
                foreach (ParticleSystem ps in starts)
                    if (ps.gameObject.activeInHierarchy) { ps.Simulate(1.2f, false, true, false); ps.Pause(false); starting++; }
                Need(starting > 0 && nativeSystems.Sum(p => p.particleCount) > 0, "native cookoff playback produces particles without prefab scripts");
                Note("INFO native cookoff systems=" + nativeSystems.Length + " start systems=" + starting + " particles=" + nativeSystems.Sum(p => p.particleCount));
                Capture("krop-fuel-visuals.png");
                DestroyImmediate(clone); clone = null;
                Capture("krop-fuel-visuals-off.png");

                // Exercise the actual local effect entry point, but never invoke
                // FuelTank.Fireball or ignite/damage/change a native aircraft.
                var tank = Resources.FindObjectsOfTypeAll<FuelTank>().FirstOrDefault(t => t != null && t.gameObject.scene.IsValid() &&
                    t.gameObject.activeInHierarchy && (t.transform.position - camera.transform.position).sqrMagnitude < 1000000f);
                if (tank == null) Note("OPEN no nearby live fuel tank for local FuelIgnited entry-point test; native allowlist renderer was tested");
                else
                {
                    fuel.Value = true; sound.Value = false; chance.BoxedValue = 1f;
                    var beforeIgnition = UnityEngine.Random.state;
                    runtime.GetMethod("FuelIgnited", F).Invoke(null, new object[] { tank, native, null });
                    Need((int)runtime.GetProperty("ActiveFuelEffects", F).GetValue(null) == 1, "actual local FuelIgnited entry point creates one bounded effect without native ignition");
                    var active = (IList)runtime.GetField("Effects", F).GetValue(null);
                    var fuelRoot = (GameObject)effectType.GetField("Root", F).GetValue(active[0]);
                    Need(fuelRoot.GetComponentsInChildren<Component>(true).All(c => c is Transform || c is ParticleSystem || c is ParticleSystemRenderer || c is AudioSource),
                        "actual local fuel entry point contains no gameplay or network components");
                    Need(UnityEngine.Random.state.Equals(beforeIgnition), "actual local fuel entry point leaves gameplay random state unchanged");
                    fuel.Value = false; runtime.GetMethod("Tick", F).Invoke(null, null);
                    Need((int)runtime.GetProperty("ActiveFuelEffects", F).GetValue(null) == 0, "actual local fuel entry point cleans up when disabled");
                }
            }
        }
        finally
        {
            runtime.GetMethod("Clear", F).Invoke(null, null);
            foreach (var pair in saved) pair.Key.Value = pair.Value;
            master.BoxedValue = oldMaster; wake.BoxedValue = oldWake;
            boatBeacons.BoxedValue = oldBoatBeacons;
            chance.BoxedValue = oldChance;
            UnityEngine.Random.state = random;
            if (source != null) DestroyImmediate(source);
            if (clone != null) DestroyImmediate(clone);
            if (boat != null) DestroyImmediate(boat);
            if (clip != null) DestroyImmediate(clip);
        }
    }
}
