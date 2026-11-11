using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KropClientEffects
{
    internal static class AudioPropagationHooks
    {
        private static readonly MethodInfo NativePlay = AccessTools.Method(typeof(AudioSource), "Play", Type.EmptyTypes);
        private static readonly MethodInfo NativeShot = AccessTools.Method(typeof(AudioSource), "PlayOneShot", new[] { typeof(AudioClip) });
        private static readonly MethodInfo NativeShotVolume = AccessTools.Method(typeof(AudioSource), "PlayOneShot", new[] { typeof(AudioClip), typeof(float) });
        private static readonly FieldInfo GunRecoil = AccessTools.Field(typeof(Gun), "recoilSound");
        internal static void Apply(Harmony harmony)
        {
            // Only replace playback instructions in these presentation methods, never the whole
            // method. Damage, firing, bullet creation, authority checks and network calls remain.
            Patch(harmony, typeof(Gun), "ShotSound");
            Patch(harmony, typeof(ParticleEffectManager.PrefabEffect), "Play");
            // ExplosionAudioManager already propagates its sounds. Do NOT wrap it.
            // Existing ExtendedAudioHooks wrappers cover release/ejection calls independently.
        }
        private static void Patch(Harmony harmony, Type type, string name)
        {
            try
            {
                var method = AccessTools.Method(type,name) ?? throw new MissingMethodException(type.FullName,name);
                harmony.Patch(method,transpiler:new HarmonyMethod(typeof(AudioPropagationHooks),nameof(Transpiler)));
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Sound propagation hook unavailable: " + type.Name + "." + name + ": " + ex.Message); }
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                string replacement = instruction.Calls(NativePlay) ? nameof(Play) : instruction.Calls(NativeShot) ? nameof(OneShot) : instruction.Calls(NativeShotVolume) ? nameof(OneShotVolume) : null;
                if (replacement != null)
                { instruction.opcode = System.Reflection.Emit.OpCodes.Call; instruction.operand = AccessTools.Method(typeof(AudioPropagationHooks),replacement); count++; }
                yield return instruction;
            }
            if (count == 0) throw new InvalidOperationException("No supported native playback instruction found.");
        }
        private static bool Recoil(AudioSource source)
        {
            if (source == null) return false;
            Gun gun = source.GetComponentInParent<Gun>();
            return gun != null && ReferenceEquals(GunRecoil?.GetValue(gun),source);
        }
        private static bool OwnImpact(AudioSource source)
            => source != null && AudioFilters.OwnHitDistance(source.transform.position).HasValue;
        private static void Play(AudioSource source)
        {
            if (!AudioPropagation.EventActive) { source.Play(); return; }
            AudioFilters.Register(source);
            if (source.loop)
            { source.Play(); AudioPropagation.ObserveLoop(source,false,source.GetComponentInParent<Gun>() != null); }
            else if (Recoil(source) || OwnImpact(source)) source.Play();
            else AudioPropagation.Play(source);
        }
        private static void OneShot(AudioSource source, AudioClip clip)
        {
            if (!AudioPropagation.EventActive || Recoil(source) || OwnImpact(source)) { source.PlayOneShot(clip); return; }
            AudioFilters.Register(source); AudioPropagation.PlayOneShot(source,clip);
        }
        private static void OneShotVolume(AudioSource source, AudioClip clip,float volume)
        {
            if (!AudioPropagation.EventActive || Recoil(source) || OwnImpact(source)) { source.PlayOneShot(clip,volume); return; }
            AudioFilters.Register(source); AudioPropagation.PlayOneShotVolume(source,clip,volume);
        }

        internal static void ObserveBase(object owner, AudioSource source)
        {
            if (owner is Gun) { AudioPropagation.ObserveLoop(source,false,true); return; }
            if (owner is Turbojet || owner is Turbofan || owner is TurbineEngine ||
                owner is ConstantSpeedProp || owner is DuctedFan || owner is PropFan || owner is JetNozzle || owner is RotorShaft)
                AudioPropagation.ObserveLoop(source,true);
        }
        internal static void ObserveExtended(AudioSource source, AudioFeature feature)
        {
            if (feature == AudioFeature.ShipEngines) AudioPropagation.ObserveLoop(source,true);
            if (feature == AudioFeature.MissileFlight || feature == AudioFeature.VlsBoosters)
                AudioPropagation.ObserveLoop(source,false);
        }
    }
}
