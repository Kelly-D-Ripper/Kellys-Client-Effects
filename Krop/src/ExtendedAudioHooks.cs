using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace KropClientEffects
{
    // Observe actual native sources. The only call replacements below add a filter registration
    // immediately before the SAME Play call, or observe the SAME instantiated VFX object.
    // No source is stopped/replaced, no RPC is sent, and no gameplay instruction is removed.
    internal static class ExtendedAudioHooks
    {
        private sealed class FieldBinding
        {
            internal FieldInfo Field, Nested; internal AudioFeature Feature; internal AudioKind Kind;
        }
        private static readonly Dictionary<MethodBase, List<FieldBinding>> Bindings = new Dictionary<MethodBase, List<FieldBinding>>();
        private static readonly Dictionary<MethodBase, AudioFeature> PlaybackFeatures = new Dictionary<MethodBase, AudioFeature>();
        private static readonly MethodInfo Play = AccessTools.Method(typeof(AudioSource), "Play", Type.EmptyTypes);
        private static readonly MethodInfo OneShot = AccessTools.Method(typeof(AudioSource), "PlayOneShot", new[] { typeof(AudioClip) });
        private static readonly MethodInfo OneShotVolume = AccessTools.Method(typeof(AudioSource), "PlayOneShot", new[] { typeof(AudioClip), typeof(float) });
        private static FieldInfo alarmSource;
        private static float nextAmbientScan;
        private static readonly List<NativeEffectAudioObserver> Observers = new List<NativeEffectAudioObserver>();

        internal static void Apply(Harmony harmony)
        {
            Bind(harmony, typeof(TurbineEngine), "Update", AudioFeature.TurbineStartup, AudioKind.Turbine, "startupAudio");
            Bind(harmony, typeof(TurbineFireSound), "Update", AudioFeature.EngineFire, AudioKind.World, "source");
            Bind(harmony, typeof(ControlSurface), "Aero", AudioFeature.SplitAirbrakes, AudioKind.World, "splitSource");
            Bind(harmony, typeof(Aircraft), "ThrowSparks", AudioFeature.AirframeScrape, AudioKind.World, "scrapeSource");
            Bind(harmony, typeof(MissileLauncher), "Fire", AudioFeature.GroundLaunchers, AudioKind.World, "launchSound");
            Bind(harmony, typeof(VLSBooster), "FixedUpdate", AudioFeature.VlsBoosters, AudioKind.World, "audioSources");
            Bind(harmony, typeof(Laser), "LaserSound", AudioFeature.Lasers, AudioKind.World, "sources");
            Bind(harmony, typeof(ShipPropulsion), "FixedUpdate", AudioFeature.ShipEngines, AudioKind.World, "engineSound", "thrustSound");
            Bind(harmony, typeof(RandomSound), "Start", AudioFeature.Ambient, AudioKind.World, "source");
            Bind(harmony, typeof(Missile), "FixedUpdate", AudioFeature.MissileFlight, AudioKind.World, "flightSound");
            Bind(harmony, typeof(Missile), "StartMissile", AudioFeature.MissileFlight, AudioKind.World, "flightSound");
            Type motor = AccessTools.Inner(typeof(Missile), "Motor");
            Bind(harmony, motor, "Thrust", AudioFeature.MissileStartup, AudioKind.World, "startupSource");
            Bind(harmony, motor, "Thrust", AudioFeature.MissileFlight, AudioKind.World, "audioSources");
            Bind(harmony, motor, "Activate", AudioFeature.MissileFlight, AudioKind.World, "audioSources");
            BindNested(harmony, typeof(DuctedThrustSystem), "Update", "intakeDoors", "IntakeDoor", "intakeSound", AudioFeature.LiftIntakes);

            PatchPlayback(harmony, typeof(MountedMissile), "PlayLaunchSound", AudioFeature.PylonRelease);
            PatchPlayback(harmony, typeof(EjectionSeat), "FireEffects", AudioFeature.EjectionSeat);
            PatchPlayback(harmony, typeof(EscapeCapsule), "CreateLaunchSound", AudioFeature.EscapeCapsule);
            PatchPlayback(harmony, typeof(Canopy), "EjectionSequence", AudioFeature.CanopyJettison, true);
            PatchPlayback(harmony, typeof(FlareEjector), "EjectFlare", AudioFeature.CountermeasureEjection, true);
            PatchPlayback(harmony, typeof(ChaffEjector), "EjectChaff", AudioFeature.CountermeasureEjection, true);

            TryPatch("missile-warning protection", () => {
                Type alarm = AccessTools.Inner(typeof(ThreatList), "MissileAlarm");
                alarmSource = AccessTools.Field(alarm, "alarmSource") ?? throw new MissingFieldException("MissileAlarm.alarmSource");
                harmony.Patch(AccessTools.Method(alarm, "Setup"), postfix: new HarmonyMethod(typeof(ExtendedAudioHooks), nameof(WarningPostfix)));
                harmony.Patch(AccessTools.Method(alarm, "ManageAlarmSound"), postfix: new HarmonyMethod(typeof(ExtendedAudioHooks), nameof(WarningPostfix)));
            });
        }

        private static void Bind(Harmony harmony, Type type, string methodName, AudioFeature feature, AudioKind kind, params string[] fields)
        {
            TryPatch(feature + " / " + methodName, () => {
                if (type == null) throw new TypeLoadException(methodName);
                MethodInfo method = AccessTools.Method(type, methodName) ?? throw new MissingMethodException(type.FullName, methodName);
                var added = new List<FieldBinding>();
                foreach (string field in fields)
                    added.Add(new FieldBinding { Field = AccessTools.Field(type, field) ?? throw new MissingFieldException(type.FullName, field), Feature = feature, Kind = kind });
                if (!Bindings.TryGetValue(method, out var entries))
                {
                    entries = new List<FieldBinding>(); Bindings.Add(method, entries);
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(ExtendedAudioHooks), nameof(FieldsPostfix)));
                }
                entries.AddRange(added);
            });
        }
        private static void BindNested(Harmony harmony, Type type, string methodName, string array, string nestedType, string source, AudioFeature feature)
        {
            TryPatch(feature.ToString(), () => {
                MethodInfo method = AccessTools.Method(type, methodName) ?? throw new MissingMethodException(type.FullName, methodName);
                var binding = new FieldBinding {
                    Field = AccessTools.Field(type, array) ?? throw new MissingFieldException(array),
                    Nested = AccessTools.Field(AccessTools.Inner(type, nestedType), source) ?? throw new MissingFieldException(source),
                    Feature = feature, Kind = AudioKind.World
                };
                if (!Bindings.TryGetValue(method, out var entries))
                {
                    entries = new List<FieldBinding>(); Bindings.Add(method, entries);
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(ExtendedAudioHooks), nameof(FieldsPostfix)));
                }
                entries.Add(binding);
            });
        }
        private static void FieldsPostfix(object __instance, MethodBase __originalMethod)
        {
            if (!Settings.Active || !Bindings.TryGetValue(__originalMethod, out var fields)) return;
            foreach (var field in fields)
            {
                if (!AudioRefinements.Enabled(field.Feature)) continue;
                object value = field.Field.GetValue(__instance);
                if (value is AudioSource source) Register(source, field.Feature, field.Kind);
                else if (value is Array array) foreach (object item in array)
                {
                    if (item == null) continue;
                    Register(field.Nested != null ? field.Nested.GetValue(item) as AudioSource : item as AudioSource, field.Feature, field.Kind);
                }
            }
        }
        private static void PatchPlayback(Harmony harmony, Type type, string name, AudioFeature feature, bool async = false)
        {
            TryPatch(feature.ToString(), () => {
                MethodInfo method = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
                if (async)
                {
                    var state = method.GetCustomAttribute<AsyncStateMachineAttribute>();
                    Type stateType = state?.StateMachineType;
                    if (stateType == null)
                        foreach (Type candidate in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                            if (candidate.Name.StartsWith("<" + name + ">", StringComparison.Ordinal)) { stateType = candidate; break; }
                    method = AccessTools.Method(stateType, "MoveNext") ?? throw new MissingMethodException(name + " state machine");
                }
                PlaybackFeatures.Add(method, feature);
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(ExtendedAudioHooks), nameof(PlaybackTranspiler)));
            });
        }
        private static IEnumerable<CodeInstruction> PlaybackTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            AudioFeature feature = PlaybackFeatures[__originalMethod];
            var result = new List<CodeInstruction>(); int matches = 0;
            foreach (var instruction in instructions)
            {
                string replacement = instruction.Calls(Play) ? nameof(PlayNow) : instruction.Calls(OneShot) ? nameof(PlayOneShotNow) : instruction.Calls(OneShotVolume) ? nameof(PlayOneShotVolumeNow) : null;
                if (replacement == null) { result.Add(instruction); continue; }
                var argument = new CodeInstruction(OpCodes.Ldc_I4, (int)feature);
                argument.labels.AddRange(instruction.labels); instruction.labels.Clear();
                // Begin blocks move with their first stack instruction; end blocks must still
                // close AFTER the replacement call, not between its arguments and execution.
                for (int block = instruction.blocks.Count - 1; block >= 0; block--)
                {
                    if (instruction.blocks[block].blockType == ExceptionBlockType.EndExceptionBlock) continue;
                    argument.blocks.Insert(0, instruction.blocks[block]); instruction.blocks.RemoveAt(block);
                }
                result.Add(argument);
                instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(ExtendedAudioHooks), replacement);
                result.Add(instruction); matches++;
            }
            if (matches == 0) throw new InvalidOperationException("Expected native AudioSource playback was not found in " + __originalMethod.Name);
            return result;
        }
        private static void PlayNow(AudioSource source, int feature) { Register(source, (AudioFeature)feature); source.Play(); }
        private static void PlayOneShotNow(AudioSource source, AudioClip clip, int feature) { Register(source, (AudioFeature)feature); source.PlayOneShot(clip); }
        private static void PlayOneShotVolumeNow(AudioSource source, AudioClip clip, float volume, int feature) { Register(source, (AudioFeature)feature); source.PlayOneShot(clip, volume); }
        private static void WarningPostfix(object __instance) { AudioFilters.ProtectWarning(alarmSource?.GetValue(__instance) as AudioSource); }
        internal static void Register(AudioSource source, AudioFeature feature, AudioKind kind = AudioKind.World)
        { if (Settings.Active && AudioRefinements.Enabled(feature)) AudioFilters.RegisterOptional(source, kind, null, feature); }

        internal static void Tick()
        {
            if (!AudioRefinements.Enabled(AudioFeature.Ambient) || Time.unscaledTime < nextAmbientScan) return;
            nextAmbientScan = Time.unscaledTime + 2f;
            // Only this optional family needs discovery: native RandomSound.Start may predate enabling it.
            foreach (var ambient in UnityEngine.Object.FindObjectsOfType<RandomSound>()) Register(ambient.source, AudioFeature.Ambient);
        }
        internal static void Observe(GameObject effect, AudioFeature feature)
        {
            if (!Settings.Active || !AudioRefinements.Enabled(feature) || effect == null) return;
            foreach (AudioSource source in effect.GetComponentsInChildren<AudioSource>(true)) Register(source, feature);
            // Start() on prefab components can create/configure sources one frame later.
            if (Observers.Count >= 64) return;
            var observer = effect.AddComponent<NativeEffectAudioObserver>(); observer.Feature = feature; Observers.Add(observer);
        }
        internal static void Forget(NativeEffectAudioObserver observer) { Observers.Remove(observer); }
        internal static void Clear()
        {
            foreach (var observer in Observers) if (observer != null) UnityEngine.Object.Destroy(observer);
            Observers.Clear(); nextAmbientScan = 0f;
        }
        private static void TryPatch(string name, Action patch)
        {
            try { patch(); }
            catch (Exception ex) { Plugin.Log.LogWarning("Optional audio hook unavailable (" + name + "): " + ex.Message); }
        }
    }

    internal sealed class NativeEffectAudioObserver : MonoBehaviour
    {
        internal AudioFeature Feature;
        private int frames;
        private void LateUpdate()
        {
            if (Settings.Active && AudioRefinements.Enabled(Feature))
                foreach (var source in GetComponentsInChildren<AudioSource>(true)) ExtendedAudioHooks.Register(source, Feature);
            if (++frames >= 2 || !Settings.Active || !AudioRefinements.Enabled(Feature)) Destroy(this);
        }
        private void OnDestroy() { ExtendedAudioHooks.Forget(this); }
    }

    [HarmonyPatch(typeof(FuelTank), "Fireball")]
    internal static class FuelIgnitionAudioPatch
    {
        private static void Postfix(GameObject ___fireballSpawn) { ExtendedAudioHooks.Observe(___fireballSpawn, AudioFeature.FuelIgnition); }
    }

    [HarmonyPatch(typeof(Missile.Warhead), "Detonate")]
    internal static class WarheadAudioObservationPatch
    {
        private static readonly MethodInfo NativeInstantiate = FindInstantiate();
        private static MethodInfo FindInstantiate()
        {
            foreach (var method in typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "Instantiate" || !method.IsGenericMethodDefinition) continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[1].ParameterType == typeof(Transform)) return method.MakeGenericMethod(typeof(GameObject));
            }
            return null;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            if (NativeInstantiate == null || !list.Exists(i => i.Calls(NativeInstantiate)))
                throw new InvalidOperationException("Native warhead effect Instantiate contract changed; audio observation omitted.");
            return Transpilers.MethodReplacer(list, NativeInstantiate, AccessTools.Method(typeof(WarheadAudioObservationPatch), nameof(InstantiateObserved)));
        }
        private static GameObject InstantiateObserved(GameObject original, Transform parent)
        {
            GameObject instance = UnityEngine.Object.Instantiate(original, parent);
            ExtendedAudioHooks.Observe(instance, AudioFeature.WarheadEffects);
            return instance;
        }
    }
}
