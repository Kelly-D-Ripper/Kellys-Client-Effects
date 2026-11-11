using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

public sealed partial class MetDriver
{
    // Run only in the isolated mission fixture. Every changed setting and flight/camera value is restored.
    private void TestCockpitEffectToggles()
    {
        var modules = ModuleInfos();
        var feel = modules["aaron.boscali.cockpitfeel"].Instance;
        var audio = modules["aaron.boscali.cockpit-audio"].Instance;
        var core = modules["aaron.boscali.unified"].Instance;
        var saved = new Dictionary<ConfigEntryBase, object>();
        ConfigEntry<T> Setting<T>(ConfigFile config, string section, string key)
        {
            Need(config.TryGetEntry<T>(new ConfigDefinition(section, key), out var entry), "granular cockpit setting exists: " + section + "/" + key);
            if (!saved.ContainsKey(entry)) saved.Add(entry, entry.BoxedValue);
            return entry;
        }
        var feelType = feel.GetType().Assembly.GetType("BoscaliCockpitFeel.FeelRuntime", true);
        object runtime = feelType.GetField("Live", F).GetValue(null);
        Need(runtime != null && GameManager.GetLocalAircraft(out _), "cockpit motion fixture has real runtime and local aircraft");
        GameManager.GetLocalAircraft(out var aircraft);
        var cameras = SceneSingleton<CameraStateManager>.i;
        var oldMode = CameraStateManager.cameraMode;
        object oldFollowing = Field(cameras, "followingUnit"), oldState = Field(cameras, "<currentState>k__BackingField");
        float oldSpeed = aircraft.speed, oldRadar = aircraft.radarAlt;
        var oldGear = aircraft.gearState;
        Vector3 oldVelocity = aircraft.rb.velocity;
        var testObjects = new List<GameObject>();
        AudioClip silent = null;
        object activeAudioRuntime = null;
        try
        {
            string[] feelChildren = { "Ground roll vibration", "Gun recoil", "Touchdown jolts", "Landing gear jolts", "Transonic vibration", "Sonic crossing jolt" };
            foreach (string key in feelChildren)
                Need((bool)Setting<bool>(feel.Config, "Cockpit Feel", key).DefaultValue, "new motion child preserves prior default: " + key);
            string[] audioChildren = { "Breathing cycles", "G strain", "G suit hiss", "Heartbeat", "Recovery gasps", "Structural creaks", "Harness strain", "Impact foley", "Gear lever", "Canopy closing" };
            foreach (string key in audioChildren)
                Need((bool)Setting<bool>(audio.Config, "Cockpit Audio", key).DefaultValue, "new audio child preserves prior default: " + key);
            foreach (string key in new[] { "Pull up warning", "Terrain warning", "Over G warning", "Bingo warning" })
                Need((bool)Setting<bool>(core.Config, "Audio", key).DefaultValue, "new warning child preserves prior default: " + key);
            Need(!(bool)Setting<bool>(core.Config, "Audio", "Cockpit voice warnings").DefaultValue, "cockpit voice warning master still defaults off");
            var ground = Setting<bool>(feel.Config, "Cockpit Feel", "Ground roll vibration");
            var shakeMaster = Setting<bool>(feel.Config, "Cockpit Feel", "Cockpit shake");
            var groundStrength = Setting<float>(feel.Config, "Cockpit Feel", "Ground roll vibration strength");
            Need((float)groundStrength.DefaultValue == 1f, "ground roll default amplitude is preserved");
            foreach (string key in new[] { "Head inertia", "Mach buffet", "Comfort motion", "Instrument glow", "Cockpit night lighting", "Cockpit damage shading" })
                Setting<bool>(feel.Config, "Cockpit Feel", key).Value = false;
            foreach (string key in feelChildren) Setting<bool>(feel.Config, "Cockpit Feel", key).Value = key == "Ground roll vibration";
            shakeMaster.Value = true;
            Setting<float>(feel.Config, "Cockpit Feel", "Cockpit shake strength").Value = 1f;
            groundStrength.Value = 1f;
            CameraStateManager.cameraMode = CameraMode.cockpit;
            FI(cameras, "followingUnit").SetValue(cameras, aircraft);
            FI(cameras, "<currentState>k__BackingField").SetValue(cameras, cameras.cockpitState);
            aircraft.speed = 72f; aircraft.radarAlt = 0f;
            Call(runtime, "Update");
            Need((bool)Field(runtime, "presenting"), "real cockpit runtime accepts local cockpit view");
            object shake = Field(runtime, "shake");
            Vector3 rollAngles = (Vector3)Prop(shake, "AnglesDeg");
            Need(rollAngles.sqrMagnitude > 0.00000001f, "takeoff-speed ground roll produces rotational output");
            var pivot = cameras.cameraPivot;
            Quaternion original = pivot.localRotation;
            Call(runtime, "Apply", cameras);
            Need((bool)Prop(Field(runtime, "offset"), "IsApplied") && QuaternionDifference(original, pivot.localRotation) > 0.000001f,
                "ground-roll output reaches actual cockpit camera pivot");
            Call(runtime, "Remove");
            Need(QuaternionDifference(original, pivot.localRotation) < 0.000001f, "owned ground-roll camera write is reversibly removed");
            groundStrength.Value = 3f; Call(runtime, "Update");
            Need(((Vector3)Prop(shake, "AnglesDeg") - rollAngles * 3f).sqrMagnitude < 0.000001f, "independent runway strength scales actual motion");
            ground.Value = false; Call(runtime, "Update");
            Need(((Vector3)Prop(shake, "AnglesDeg")).sqrMagnitude < 0.00000001f, "ground-roll child stops motion immediately");
            ground.Value = true; shakeMaster.Value = false; Call(runtime, "Update");
            Need(((Vector3)Prop(shake, "AnglesDeg")).sqrMagnitude < 0.00000001f, "disabled existing shake master remains authoritative");
            shakeMaster.Value = true; aircraft.radarAlt = 5f; Call(runtime, "Update");
            Need(((Vector3)Prop(shake, "AnglesDeg")).sqrMagnitude < 0.00000001f, "ground roll ends after takeoff");
            aircraft.radarAlt = 0f; aircraft.speed = 0f; Call(runtime, "Update");
            Need(((Vector3)Prop(shake, "AnglesDeg")).sqrMagnitude < 0.00000001f, "stationary aircraft has no runway vibration");
            aircraft.speed = 72f; Call(runtime, "Update"); Call(runtime, "Apply", cameras);
            CameraStateManager.cameraMode = CameraMode.orbit;
            Call(runtime, "Update");
            Need(!(bool)Field(runtime, "presenting") && !(bool)Prop(Field(runtime, "offset"), "IsApplied") && ((Vector3)Prop(shake, "AnglesDeg")).sqrMagnitude == 0f,
                "external-camera runtime gate clears cockpit shake and camera ownership");

            // Exercise actual event callbacks, not just the displayed booleans.
            Call(shake, "SetFeatures", false, true, true, true, 1f);
            aircraft.rb.velocity = new Vector3(0f, -6f, 0f);
            Call(shake, "Tick", aircraft, true, true, false, 1f, 0f, 0f, 0f, .02f);
            int touchdowns = (int)Prop(shake, "Touchdowns");
            Call(shake, "OnTouchdown");
            object envelope = Field(shake, "events");
            Need((int)Prop(shake, "Touchdowns") == touchdowns + 1 && (float)Prop(envelope, "Landing") > 0f, "touchdown callback produces landing envelope");
            Call(shake, "SetFeatures", false, true, false, true, 1f);
            Call(shake, "OnTouchdown");
            Need((int)Prop(shake, "Touchdowns") == touchdowns + 1 && (float)Prop(envelope, "Landing") == 0f, "touchdown toggle stops callback and clears existing tail");
            bool ValidRecoilWeapon(Gun candidate) => candidate != null && candidate.info != null &&
                candidate.info.massPerRound > 0f && candidate.info.muzzleVelocity > 0f;
            var gun = aircraft.GetComponentsInChildren<Gun>(true).FirstOrDefault(ValidRecoilWeapon);
            // The mission's selected aircraft can legitimately be unarmed. A loaded inactive
            // gun/prefab is sufficient: OnShot only reads momentum, and no native firing is invoked.
            if (gun == null) gun = Resources.FindObjectsOfTypeAll<Gun>().FirstOrDefault(ValidRecoilWeapon);
            Need(gun != null, "gun recoil fixture has an existing loaded weapon with valid projectile momentum");
            int shots = (int)Prop(shake, "Shots"); Call(shake, "OnShot", gun);
            Need((int)Prop(shake, "Shots") == shots + 1 && (float)Prop(envelope, "Recoil") > 0f, "gun callback produces recoil envelope");
            Call(shake, "SetFeatures", false, false, false, true, 1f); Call(shake, "OnShot", gun);
            Need((int)Prop(shake, "Shots") == shots + 1 && (float)Prop(envelope, "Recoil") == 0f, "recoil toggle stops callback and clears existing tail");
            int locks = (int)Prop(shake, "GearLocks");
            aircraft.gearState = LandingGear.GearState.Extending;
            Call(shake, "Tick", aircraft, true, true, false, 1f, 0f, 0f, 0f, .02f);
            aircraft.gearState = LandingGear.GearState.LockedExtended;
            Call(shake, "Tick", aircraft, true, true, false, 1f, 0f, 0f, 0f, .02f);
            Need((int)Prop(shake, "GearLocks") == locks + 1 && (float)Prop(envelope, "GearLock") != 0f, "gear-lock event produces its separate envelope");
            Call(shake, "SetFeatures", false, false, false, false, 1f);
            Need((float)Prop(envelope, "GearLock") == 0f, "gear toggle clears latch envelope");
            Call(shake, "SetMachFeatures", false, true);
            Call(shake, "Tick", aircraft, true, false, true, 1f, .97f, 0f, 0f, .02f);
            Call(shake, "Tick", aircraft, true, false, true, 1f, 1.01f, 0f, 0f, .02f);
            Need((float)Prop(envelope, "Sonic") > 0f, "sonic crossing works independently of extra shake and continuous buffet");
            Call(shake, "SetMachFeatures", true, false);
            Need((float)Prop(envelope, "Sonic") == 0f, "sonic toggle clears existing crossing impulse");
            Call(shake, "Tick", aircraft, true, false, true, 1f, 1f, 0f, 0f, .02f);
            Need(((Vector3)Prop(shake, "AnglesDeg")).sqrMagnitude > 0.00000001f, "continuous transonic vibration remains independent");
            Call(shake, "SetMachFeatures", false, false);
            Call(shake, "Tick", aircraft, true, false, true, 1f, 1f, 0f, 0f, .02f);
            Need(((Vector3)Prop(shake, "AnglesDeg")).sqrMagnitude == 0f, "both Mach children off produce no residual motion");

            // Silent Unity sources verify independent live stop behaviour without playing fixture audio.
            silent = AudioClip.Create("Cockpit toggle silent fixture", 44100, 1, 44100, false);
            var host = new GameObject("Cockpit audio toggle fixture"); testObjects.Add(host);
            var breathType = audio.GetType().Assembly.GetType("BoscaliSummer.Modules.Immersion.Audio.PilotBreathingAudio", true);
            var breath = host.AddComponent(breathType);
            string[] voices = { "breath", "effort", "body", "heart", "recovery" };
            foreach (string voice in voices)
            {
                var source = host.AddComponent<AudioSource>(); source.clip = silent; source.volume = 0f; source.loop = true;
                FI(breath, voice).SetValue(breath, source);
            }
            for (int i = 0; i < voices.Length; i++)
            {
                foreach (string voice in voices) ((AudioSource)Field(breath, voice)).Play();
                object[] flags = { true, true, true, true, true }; flags[i] = false;
                Call(breath, "SetFeatures", flags);
                Need(!((AudioSource)Field(breath, voices[i])).isPlaying && voices.Where((x, j) => j != i).All(x => ((AudioSource)Field(breath, x)).isPlaying),
                    "audio child stops only its own live voice: " + audioChildren[i]);
            }
            Call(breath, "Release");
            TestSplitFoleyVoices(audio.GetType().Assembly, host, silent, aircraft);
            // The actual wrapper reads the new persisted configuration entries, not a parallel test-only flag set.
            CameraStateManager.cameraMode = CameraMode.cockpit;
            aircraft.speed = 0f; aircraft.radarAlt = 0f;
            Setting<bool>(audio.Config, "Cockpit Audio", "PilotBreathing").Value = true;
            Setting<bool>(audio.Config, "Cockpit Audio", "AirframeCreaks").Value = true;
            Setting<bool>(audio.Config, "Cockpit Audio", "ControlFoley").Value = true;
            Setting<bool>(audio.Config, "Cockpit Audio", "CanopyWind").Value = false;
            object audioRuntime = audio.GetType().Assembly.GetType("BoscaliCockpitAudio.CockpitAudioRuntime", true).GetField("Live", F).GetValue(null);
            activeAudioRuntime = audioRuntime;
            foreach (string key in audioChildren) Setting<bool>(audio.Config, "Cockpit Audio", key).Value = false;
            Call(audioRuntime, "Update");
            foreach (string field in new[] { "breathOn", "strainOn", "suitOn", "heartOn", "recoveryOn" })
                Need(!(bool)Field(Field(audioRuntime, "breath"), field), "real audio runtime routes persisted child off: " + field);
            foreach (string field in new[] { "harnessOn", "impactOn" })
                Need(!(bool)Field(Field(audioRuntime, "foley"), field), "real foley runtime routes persisted child off: " + field);
            foreach (string field in new[] { "gearOn", "canopyOn" })
                Need(!(bool)Field(Field(audioRuntime, "controlsAudio"), field), "real control foley routes persisted child off: " + field);
            Need(!(bool)Field(Field(audioRuntime, "airframe"), "primed"), "disabled structural creaks reset force history in actual wrapper");
            Call(audioRuntime, "ResetScene");
            TestWarningChildPriority(core.GetType().Assembly);
        }
        finally
        {
            Call(runtime, "ResetEffects");
            if (activeAudioRuntime != null) Call(activeAudioRuntime, "ResetScene");
            foreach (var pair in saved) pair.Key.BoxedValue = pair.Value;
            aircraft.speed = oldSpeed; aircraft.radarAlt = oldRadar; aircraft.gearState = oldGear; aircraft.rb.velocity = oldVelocity;
            CameraStateManager.cameraMode = oldMode;
            FI(cameras, "followingUnit").SetValue(cameras, oldFollowing); FI(cameras, "<currentState>k__BackingField").SetValue(cameras, oldState);
            foreach (var obj in testObjects) UnityEngine.Object.Destroy(obj);
            if (silent != null) UnityEngine.Object.Destroy(silent);
        }
    }

    private void TestSplitFoleyVoices(System.Reflection.Assembly audio, GameObject host, AudioClip silent, Aircraft aircraft)
    {
        var foleyType = audio.GetType("BoscaliSummer.Modules.Immersion.Audio.CockpitFoley", true);
        object foley = Activator.CreateInstance(foleyType, true);
        Call(foley, "Initialize", host);
        var impact = (AudioSource)Field(foley, "source"); var harness = (AudioSource)Field(foley, "harness");
        foreach (var source in new[] { impact, harness }) { source.clip = silent; source.loop = true; source.volume = 0f; source.Play(); }
        Call(foley, "SetFeatures", true, false);
        Need(!impact.isPlaying && harness.isPlaying, "impact disable preserves playing harness voice");
        int played = (int)Prop(foley, "Played"); Call(foley, "Hit", 100f, 100f);
        Need((int)Prop(foley, "Played") == played, "disabled impact child blocks actual hit callback");
        impact.Play(); Call(foley, "SetFeatures", false, true);
        Need(impact.isPlaying && !harness.isPlaying, "harness disable preserves playing impact voice");
        Call(foley, "Reset"); Need(!impact.isPlaying && !harness.isPlaying, "foley reset stops both independent voices");
        Call(foley, "Release");

        var controlType = audio.GetType("BoscaliCockpitAudio.ControlFoley", true);
        object controls = Activator.CreateInstance(controlType, F, null, new object[] { host }, null);
        var gear = (AudioSource)Field(controls, "source"); var canopy = (AudioSource)Field(controls, "canopySource");
        foreach (var source in new[] { gear, canopy }) { source.clip = silent; source.loop = true; source.volume = 0f; source.Play(); }
        Call(controls, "SetFeatures", false, true);
        Need(!gear.isPlaying && canopy.isPlaying, "gear-lever disable preserves canopy voice");
        aircraft.speed = 0f; aircraft.radarAlt = 0f; Call(controls, "Tick", aircraft);
        Need((float)Field(controls, "canopyAt") > 0f, "ground-start canopy event is scheduled when enabled");
        gear.Play(); Call(controls, "SetFeatures", true, false);
        Need(gear.isPlaying && !canopy.isPlaying && (float)Field(controls, "canopyAt") < 0f, "canopy disable stops only its voice and cancels pending close");
        Call(controls, "Disable", aircraft);
        Need(!gear.isPlaying && !canopy.isPlaying, "control master disable stops all child voices");
        Call(controls, "Release");
    }

    private void TestWarningChildPriority(System.Reflection.Assembly core)
    {
        var type = core.GetType("BoscaliClientExperimental.Weather.WarningMath", true);
        object rules = Activator.CreateInstance(type, true);
        object state = Activator.CreateInstance(core.GetType("BoscaliClientExperimental.Weather.WarningInput", true));
        FI(state, "RadarAltM").SetValue(state, 100f); FI(state, "ClimbMps").SetValue(state, -30f);
        FI(state, "G").SetValue(state, 10f); FI(state, "Fuel01").SetValue(state, .1f); FI(state, "Airborne").SetValue(state, true);
        Need(Call(rules, "Step", state, 0f).ToString() == "PullUp", "default warning priority remains pull up first");
        Call(rules, "Reset"); Call(rules, "SetFeatures", false, true, true, true);
        Need(Call(rules, "Step", state, 0f).ToString() == "Terrain", "pull-up child off retains enabled terrain call when both ground-warning envelopes overlap");
        Call(rules, "Reset"); Call(rules, "SetFeatures", false, false, true, true);
        Need(Call(rules, "Step", state, 0f).ToString() == "OverG", "disabled ground voices cannot starve enabled lower-priority voice");
        Call(rules, "Reset"); Call(rules, "SetFeatures", false, false, false, true);
        Need(Call(rules, "Step", state, 0f).ToString() == "Bingo", "bingo child remains independent");
        Call(rules, "Reset"); Call(rules, "SetFeatures", false, false, false, false);
        Need(Call(rules, "Step", state, 0f).ToString() == "None", "all voice children off produce no warning");
    }

    private static float QuaternionDifference(Quaternion left, Quaternion right)
    {
        float sign = Quaternion.Dot(left, right) < 0f ? -1f : 1f;
        return Mathf.Abs(left.x - right.x * sign) + Mathf.Abs(left.y - right.y * sign) + Mathf.Abs(left.z - right.z * sign) + Mathf.Abs(left.w - right.w * sign);
    }
}
