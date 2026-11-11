using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KropClientEffects
{
    internal enum AudioKind { World, Mechanical, Turbine, Impact }
    internal static class AudioFilters
    {
        private sealed class SourceState { internal AudioSource Source; internal AudioKind Kind; internal float? HitDistance; internal bool Bypass; internal AudioFeature Feature; }
        private sealed class HostState
        {
            internal GameObject Host; internal AudioLowPassFilter Filter;
            internal bool Created, BaseEnabled; internal float BaseCutoff, BaseQ, LastCutoff;
            internal float IdleSince;
            internal readonly List<SourceState> Sources = new List<SourceState>();
        }
        private static readonly Dictionary<GameObject, HostState> Hosts = new Dictionary<GameObject, HostState>();
        private static readonly List<GameObject> Remove = new List<GameObject>();
        private static readonly List<AudioSource> Siblings = new List<AudioSource>();
        private static readonly Collider[] HitBuffer = new Collider[32];
        private static readonly Dictionary<AudioSource, bool> ProtectedWarnings = new Dictionary<AudioSource, bool>();
        private static readonly List<AudioSource> DeadWarnings = new List<AudioSource>();
        private static float nextWarningCleanup;
        private static float ejectedAt = float.NegativeInfinity;
        internal static bool InCockpit()
        {
            var camera = SceneSingleton<CameraStateManager>.i;
            return camera != null && camera.currentState == camera.cockpitState;
        }
        internal static void Register(AudioSource source, AudioKind kind = AudioKind.World, float? hitDistance = null)
            => RegisterOptional(source, kind, hitDistance, AudioFeature.Base);
        internal static void RegisterOptional(AudioSource source, AudioKind kind, float? hitDistance, AudioFeature feature)
        {
            if (!Settings.Active || source == null || !AudioRefinements.Enabled(feature) || IsProtected(source)) return;
            GameObject obj = source.gameObject;
            // A filter on the AudioListener's object affects the whole mix, not just this source.
            if (obj.GetComponent<AudioListener>() != null) return;
            if (!Hosts.TryGetValue(obj, out HostState host))
            {
                if (Hosts.Count >= AudioConfig.MaxHosts.Value) return;
                var filter = obj.GetComponent<AudioLowPassFilter>();
                host = new HostState { Host = obj, Filter = filter,
                    BaseEnabled = filter != null && filter.enabled,
                    BaseCutoff = filter != null ? filter.cutoffFrequency : 22000f,
                    BaseQ = filter != null ? filter.lowpassResonanceQ : 1f,
                    LastCutoff = -1f, IdleSince = Time.unscaledTime };
                Hosts.Add(obj, host);
            }
            SourceState state = null;
            for (int i=0;i<host.Sources.Count;i++) if (host.Sources[i].Source==source) { state=host.Sources[i]; break; }
            if (state == null)
            {
                state = new SourceState { Source = source, Bypass = source.bypassEffects };
                host.Sources.Add(state);
            }
            state.Kind = kind; state.HitDistance = hitDistance; state.Feature = feature;
            // Vanilla explosion filters can update their own cutoff when a pooled source replays.
            if (!host.Created && host.Filter != null && !Mathf.Approximately(host.Filter.cutoffFrequency, host.LastCutoff))
            {
                host.BaseCutoff = host.Filter.cutoffFrequency;
                host.BaseEnabled = host.Filter.enabled; host.BaseQ = host.Filter.lowpassResonanceQ;
            }
        }
        private static bool IsProtected(AudioSource source)
            => source != null && (ProtectedWarnings.ContainsKey(source) ||
                (SoundManager.i != null && source.outputAudioMixerGroup != null && source.outputAudioMixerGroup == SoundManager.i.MissileAlertMixer));
        internal static void ProtectWarning(AudioSource source)
        {
            if (!Settings.Active || source == null) return;
            if (!ProtectedWarnings.ContainsKey(source))
            {
                if (ProtectedWarnings.Count >= 512) return; // mixed-source guard remains the fallback
                ProtectedWarnings.Add(source, source.bypassEffects);
            }
            source.bypassEffects = true;
        }
        internal static float? OwnHitDistance(Vector3 position)
        {
            if (!AudioConfig.OwnHit.Value || !InCockpit() || !GameManager.GetLocalAircraft(out Aircraft aircraft)
                || aircraft == null || aircraft.cockpit == null || Vector3.Distance(position, aircraft.transform.position) > 40f) return null;
            int count = Physics.OverlapSphereNonAlloc(position, 0.25f, HitBuffer);
            float? result = null;
            for (int i = 0; i < count; i++)
            {
                var damageable = HitBuffer[i] != null ? HitBuffer[i].gameObject.GetComponent<IDamageable>() : null;
                if (damageable != null && damageable.GetUnit() == aircraft)
                    result = Vector3.Distance(position, aircraft.cockpit.xform.position);
                HitBuffer[i] = null;
            }
            return result;
        }
        internal static void Ejected() { if (Settings.Active) ejectedAt = Time.unscaledTime; }
        internal static void EnteredCockpit() { ejectedAt = float.NegativeInfinity; }
        internal static void Tick()
        {
            var camera = SceneSingleton<CameraStateManager>.i;
            if (camera == null) { RestoreAll(); return; }
            AudioHooks.DiscoverGroundAudio();
            ExtendedAudioHooks.Tick();
            bool cockpit = AudioConfig.Cockpit.Value && InCockpit();
            string airframe = GameManager.GetLocalAircraft(out Aircraft local) && local?.definition != null ? local.definition.jsonKey : "";
            bool helicopter = airframe == "UtilityHelo1" || airframe == "AttackHelo1" || airframe == "Aryx_LightHelicopter1";
            float cabinFraction = cockpit ? ReadOnlyCabinAcoustics.MuffleFraction(local) : 1f;
            float release = AudioConfig.Ejection.Value && !float.IsNegativeInfinity(ejectedAt)
                ? Mathf.Clamp01((Time.unscaledTime - ejectedAt) / Mathf.Max(0.001f, AudioConfig.EjectionSeconds.Value)) : 0f;
            Remove.Clear();
            foreach (var pair in Hosts)
            {
                HostState host = pair.Value;
                if (host.Host == null) { Remove.Add(pair.Key); continue; }
                for (int i = host.Sources.Count - 1; i >= 0; i--)
                {
                    SourceState tracked = host.Sources[i];
                    if (tracked.Source != null && AudioRefinements.Enabled(tracked.Feature)) continue;
                    if (tracked.Source != null) tracked.Source.bypassEffects = ProtectedWarnings.ContainsKey(tracked.Source) || tracked.Bypass;
                    host.Sources.RemoveAt(i);
                }
                if (host.Sources.Count == 0)
                {
                    Restore(host);
                    if (host.Created && host.Filter != null) UnityEngine.Object.Destroy(host.Filter);
                    Remove.Add(pair.Key); continue;
                }
                // Audio filters affect every source on a GameObject. Skip mixed hosts rather
                // than filtering an unrelated UI/RWR/voice source that shares the same object.
                bool safe = host.Host.GetComponent<AudioListener>() == null;
                Siblings.Clear();
                host.Host.GetComponents(Siblings);
                foreach (AudioSource sibling in Siblings)
                {
                    if (sibling == null) continue;
                    if (IsProtected(sibling))
                    {
                        // Mixer routing identifies warnings even when their setup hook
                        // was missed. Identification alone does not bypass a shared
                        // AudioLowPassFilter: restore the host until they are exempt.
                        if (!sibling.bypassEffects) { safe = false; break; }
                        continue;
                    }
                    bool registered = false;
                    foreach (SourceState state in host.Sources)
                        if (state.Source == sibling && AudioRefinements.Enabled(state.Feature)) { registered = true; break; }
                    if (!registered) { safe = false; break; }
                }
                if (!safe) { Restore(host); continue; }
                float cutoff = 22000f;
                bool anyPlaying = false;
                foreach (var state in host.Sources)
                {
                    if (!AudioRefinements.Enabled(state.Feature) || IsProtected(state.Source) || !state.Source.isPlaying) continue;
                    anyPlaying = true;
                    float distance = Vector3.Distance(state.Source.transform.position, camera.transform.position);
                    float atmospheric = EffectMath.AtmosphericCutoff(distance, AudioConfig.Atmosphere.Value,
                        AudioConfig.AtmosphereStrength.Value, AudioConfig.MinimumCutoff.Value);
                    float fraction = 1f;
                    if (state.Kind == AudioKind.Mechanical && AudioConfig.Mechanical.Value) fraction *= AudioConfig.MechanicalStrength.Value;
                    if (state.Kind == AudioKind.Turbine && helicopter && AudioConfig.Helicopter.Value) fraction *= AudioConfig.HelicopterStrength.Value;
                    if (state.Kind == AudioKind.Impact && state.HitDistance.HasValue && AudioConfig.OwnHit.Value)
                    {
                        fraction *= Mathf.Clamp01(state.HitDistance.Value / Mathf.Max(1f, AudioConfig.OwnHitMeters.Value));
                        // An own-airframe strike is a transmitted local sound, not open-air distance absorption.
                        atmospheric = 22000f;
                    }
                    float cabin = cockpit ? Mathf.Lerp(22000f, AudioConfig.CockpitCutoff.Value, fraction * cabinFraction * (1f - release)) : 22000f;
                    cutoff = Mathf.Min(cutoff, Mathf.Min(atmospheric, cabin));
                }
                if (anyPlaying) host.IdleSince = Time.unscaledTime;
                if (!anyPlaying || cutoff >= 21999f)
                {
                    Restore(host);
                    if (!anyPlaying && Time.unscaledTime - host.IdleSince > 30f)
                    { if (host.Created && host.Filter != null) UnityEngine.Object.Destroy(host.Filter); Remove.Add(pair.Key); }
                    continue;
                }
                if (host.Filter == null)
                {
                    host.Filter = host.Host.AddComponent<AudioLowPassFilter>(); host.Created = true;
                }
                host.Filter.enabled = true;
                float baseCutoff = !host.Created && host.BaseEnabled ? host.BaseCutoff : 22000f;
                host.LastCutoff = Mathf.Min(baseCutoff, cutoff);
                host.Filter.cutoffFrequency = host.LastCutoff;
                host.Filter.lowpassResonanceQ = 1f; // no resonant ringing against quiet engines
                foreach (var state in host.Sources) if (state.Source != null && !IsProtected(state.Source)) state.Source.bypassEffects = false;
            }
            foreach (var key in Remove) Hosts.Remove(key);
            if (Time.unscaledTime >= nextWarningCleanup)
            {
                nextWarningCleanup = Time.unscaledTime + 2f; DeadWarnings.Clear();
                foreach (var pair in ProtectedWarnings) if (pair.Key == null) DeadWarnings.Add(pair.Key);
                foreach (var key in DeadWarnings) ProtectedWarnings.Remove(key);
            }
        }
        private static void Restore(HostState host)
        {
            foreach (var state in host.Sources) if (state.Source != null) state.Source.bypassEffects = ProtectedWarnings.ContainsKey(state.Source) || state.Bypass;
            if (host.Filter == null) return;
            if (host.Created) host.Filter.enabled = false;
            else
            {
                // Do not overwrite a cutoff another mod/vanilla changed after our last write.
                if (Mathf.Approximately(host.Filter.cutoffFrequency, host.LastCutoff)) host.Filter.cutoffFrequency = host.BaseCutoff;
                host.Filter.enabled = host.BaseEnabled; host.Filter.lowpassResonanceQ = host.BaseQ;
            }
            host.LastCutoff = -1f;
        }
        private static void RestoreAll() { foreach (var host in Hosts.Values) Restore(host); }
        internal static void Clear()
        {
            RestoreAll();
            foreach (var host in Hosts.Values) if (host.Created && host.Filter != null) UnityEngine.Object.Destroy(host.Filter);
            Hosts.Clear(); Siblings.Clear(); ejectedAt = float.NegativeInfinity;
            foreach (var pair in ProtectedWarnings) if (pair.Key != null) pair.Key.bypassEffects = pair.Value;
            ProtectedWarnings.Clear(); DeadWarnings.Clear(); nextWarningCleanup = 0f;
            ReadOnlyCabinAcoustics.Clear(); ExtendedAudioHooks.Clear();
        }
    }
    internal static class AudioHooks
    {
        internal sealed class Spec
        {
            internal Type Type; internal string Method; internal AudioKind Kind; internal string[] Fields;
            internal Spec(Type type, string method, AudioKind kind, params string[] fields) { Type=type; Method=method; Kind=kind; Fields=fields; }
        }
        internal static readonly Spec[] Specs = {
            new Spec(typeof(Gun), "ShotSound", AudioKind.World, "sources", "recoilSound"),
            new Spec(typeof(Turbojet), "Animate", AudioKind.World, "turbineAudio"),
            new Spec(typeof(Turbofan), "Animate", AudioKind.World, "turbineAudio"),
            new Spec(typeof(TurbineEngine), "Animate", AudioKind.Turbine, "turbineAudio"),
            new Spec(typeof(ConstantSpeedProp), "PropAnimate", AudioKind.World, "propAudio"),
            new Spec(typeof(DuctedFan), "Animate", AudioKind.World, "fanSource"),
            new Spec(typeof(PropFan), "FixedUpdate", AudioKind.World, "source"),
            new Spec(typeof(JetNozzle), "AudioEffects", AudioKind.World, "thrustAudio", "afterburners"),
            new Spec(typeof(RotorShaft), "AnimateRotor", AudioKind.World, "rotorSource"),
            new Spec(typeof(AoAFeedback), "RunAoAFeedback", AudioKind.World, "_source"),
            new Spec(typeof(CameraStateManager), "Update", AudioKind.World, "windNoiseExternal"),
            new Spec(typeof(BayDoor), "Update", AudioKind.Mechanical, "doorAudioSource"),
            new Spec(typeof(SwingWingController), "FixedUpdate", AudioKind.Mechanical, "swingSource"),
            new Spec(typeof(Airbrake), "Update", AudioKind.World, "airbrakeSound"),
            new Spec(typeof(LandingGear), "FixedUpdate", AudioKind.World, "tireNoiseSound", "tireSkidSound"),
            new Spec(typeof(LandingGear), "PlayLatchSound", AudioKind.Mechanical, "foldSoundSource"),
            new Spec(typeof(Hangar), "CreateAudioSources", AudioKind.World, "oneShotSource", "loopSource"),
            new Spec(typeof(ExplosionAudioManager.ManagedExplosion), "Play", AudioKind.World, "audioSource")
        };
        private static readonly Dictionary<MethodBase,List<Tuple<FieldInfo,AudioKind>>> Fields = new Dictionary<MethodBase,List<Tuple<FieldInfo,AudioKind>>>();
        private static readonly FieldInfo GroundIdle = AccessTools.Field(typeof(GroundVehicle), "engineIdleSound");
        private static readonly FieldInfo GroundDrive = AccessTools.Field(typeof(GroundVehicle), "engineDriveSound");
        private static float nextGroundScan;
        internal static void DiscoverGroundAudio()
        {
            // GroundVehicle.Update fatally aborts the installed Unity Mono runtime while
            // MonoMod imports its method body. Observe its audio fields without a detour.
            if (Time.unscaledTime < nextGroundScan || HostsDisabled()) return;
            nextGroundScan = Time.unscaledTime + 2f;
            foreach (var vehicle in UnityEngine.Object.FindObjectsOfType<GroundVehicle>())
            {
                AudioFilters.Register(GroundIdle?.GetValue(vehicle) as AudioSource);
                AudioFilters.Register(GroundDrive?.GetValue(vehicle) as AudioSource);
            }
        }
        private static bool HostsDisabled() => !AudioConfig.Atmosphere.Value && !AudioConfig.Cockpit.Value;
        internal static void Apply(Harmony harmony)
        {
            ExtendedAudioHooks.Apply(harmony);
            foreach (var spec in Specs)
            {
                try
                {
                    var method = AccessTools.Method(spec.Type, spec.Method);
                    if (method == null) throw new MissingMethodException(spec.Type.Name, spec.Method);
                    if (!Fields.TryGetValue(method, out var fields))
                    {
                        fields = new List<Tuple<FieldInfo,AudioKind>>(); Fields.Add(method, fields);
                        UnityEngine.Debug.Log("[KROP Client Effects] Installing audio hook " + method.DeclaringType.FullName + "::" + method.Name);
                        harmony.Patch(method, postfix: new HarmonyMethod(typeof(AudioHooks), method.IsStatic ? nameof(StaticPostfix) : nameof(Postfix)));
                    }
                    foreach (string name in spec.Fields)
                    {
                        var field = AccessTools.Field(spec.Type, name);
                        if (field == null) throw new MissingFieldException(spec.Type.Name, name);
                        fields.Add(Tuple.Create(field, spec.Kind));
                    }
                    if (spec.Type == typeof(LandingGear) && spec.Method == "FixedUpdate")
                        fields.Add(Tuple.Create(AccessTools.Field(typeof(LandingGear),"foldSoundSource"), AudioKind.Mechanical));
                }
                catch (Exception ex) { Plugin.Log.LogWarning("Audio hook skipped: " + ex.Message); }
            }
        }
        private static void StaticPostfix(MethodBase __originalMethod) { Postfix(null, __originalMethod); }
        private static void Postfix(object __instance, MethodBase __originalMethod)
        {
            if (!Settings.Active || !Fields.TryGetValue(__originalMethod, out var fields)) return;
            foreach (var pair in fields)
            {
                if (!pair.Item1.IsStatic && __instance == null) continue;
                object value = pair.Item1.GetValue(__instance);
                if (value is AudioSource source) AudioFilters.Register(source, pair.Item2);
                else if (value is Array array)
                    foreach (object item in array)
                    {
                        if (item is AudioSource audio) AudioFilters.Register(audio, pair.Item2);
                        else if (item != null) AudioFilters.Register(AccessTools.Field(item.GetType(), "source")?.GetValue(item) as AudioSource, pair.Item2);
                    }
            }
            // Latch audio is added dynamically by vanilla on this same gear GameObject.
            if (__instance is LandingGear gear && __originalMethod.Name == "PlayLatchSound")
                foreach (var source in gear.GetComponents<AudioSource>()) AudioFilters.Register(source, AudioKind.Mechanical);
        }
    }
    [HarmonyPatch(typeof(ParticleEffectManager.PrefabEffect), "Play")]
    internal static class ImpactFilterPatch
    {
        private static void Postfix(ParticleEffectManager.PrefabEffect __instance, Vector3 position)
        {
            if (Settings.Active) AudioFilters.Register(__instance.source, AudioKind.Impact, AudioFilters.OwnHitDistance(position));
        }
    }
    [HarmonyPatch(typeof(Aircraft), "StartEjectionSequence")]
    internal static class EjectionFilterPatch { private static void Postfix(Aircraft __instance) { if (GameManager.IsLocalAircraft(__instance)) AudioFilters.Ejected(); } }
    [HarmonyPatch(typeof(PilotPlayerState), "EnterState")]
    internal static class CockpitResetPatch { private static void Postfix() { AudioFilters.EnteredCockpit(); } }
}

