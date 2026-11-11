using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KropClientEffects
{
    // KROP's local post-processing treatments, with explicit parameter ownership
    // and cleanup. The game's selected/active NVG state is never written here.
    internal static class NightVisionEffects
    {
        internal const string Section = "09 Night vision";
        internal static ConfigEntry<bool> Phosphor, BloomEnabled, GrainEnabled, Sparkles, NuclearSpeckles, AutoGain, Transition;
        internal static ConfigEntry<int> PhosphorColour, SpeckCount;
        internal static ConfigEntry<float> BloomStrength, GrainStrength;
        internal static bool Active { get; private set; }
        private static VolumeProfile profile;
        private static ChannelMixer mixer, mixerBaseline;
        private static ColorAdjustments phosphorAdjustments;
        private static bool saturationOwned, saturationOverride;
        private static float saturationValue;
        private static FilmGrain grain, grainBaseline;
        private static Bloom bloom;
        private static bool mixerAdded, grainAdded, bloomOwned, bloomOverride, scatterOverride;
        private static float bloomValue, scatterValue;
        private static Texture2D noise;
        private static Color32[] pixels, scratch;
        private static float nextNoiseRotation;
        private static float detonationTime = float.NegativeInfinity;
        private static readonly System.Random RandomSource = new System.Random();
        private static readonly System.Reflection.FieldInfo ActiveField = AccessTools.Field(typeof(NightVision), "nightVisActive");

        internal static void Initialize()
        {
            Phosphor = Settings.Toggle(Section, "Phosphor tint", false, "Local green, white or amber NVG phosphor. Does not switch NVG on.");
            PhosphorColour = Settings.Count(Section, "Phosphor colour", 0, 0, 2, "0 = green, 1 = white, 2 = amber. Requires Phosphor tint.");
            BloomEnabled = Settings.Toggle(Section, "NVG bloom", false, "Extra bloom only while the game's NVGs are active.");
            BloomStrength = Settings.Number(Section, "NVG bloom strength", 35f, 0f, 80f, "NVG bloom intensity. Original intensity restored when disabled.");
            GrainEnabled = Settings.Toggle(Section, "NVG grain", false, "Fine animated static only while NVG is active.");
            GrainStrength = Settings.Number(Section, "NVG grain strength", .55f, 0f, 1f, "Intensity of the local NVG static texture.");
            Sparkles = Settings.Toggle(Section, "NVG sparkles", false, "Small white NVG sensor specks. Local visual effect only.");
            SpeckCount = Settings.Count(Section, "NVG speck count", 12, 0, 100, "Baseline sensor specks. Requires NVG sparkles.");
            NuclearSpeckles = Settings.Toggle(Section, "Nuclear NVG speckles", false, "Temporary extra NVG sensor specks when an existing nuclear detonation appears in view. Does not create or change a detonation.");
            AutoGain = Settings.Toggle(Section, "NVG auto gain", false, "Sample local rendered brightness twice per second and adapt NVG gain. Requires supported async GPU readback; otherwise vanilla gain remains.");
            Transition = Settings.Toggle(Section, "NVG transition", false, "Raise/lower wipe for a manual NVG toggle. Does not automatically activate NVG on a camera change.");
        }

        internal static Color Tint(int index)
        {
            if (index == 1) return new Color32(0xe5, 0xf2, 0xe1, 0xff);
            if (index == 2) return new Color32(0xd3, 0x99, 0x34, 0xff);
            return new Color32(0x4b, 0xf7, 0x0c, 0xff);
        }

        internal static void Update(bool active, Volume volume)
        {
            Active = Settings.Active && active && volume != null && volume.enabled;
            if (!Active)
            {
                RestoreProfile(); NightVisionGain.Clear(); NightVisionOverlay.SetSpecks(0);
                detonationTime = float.NegativeInfinity;
                return;
            }
            bool needsProfile = Phosphor.Value || BloomEnabled.Value || GrainEnabled.Value;
            VolumeProfile current = needsProfile ? volume.profile : null;
            if (current != profile) { RestoreProfile(); profile = current; }
            if (profile != null)
            {
                UpdateMixer(); UpdateBloom(); UpdateGrain();
            }
            else RestoreProfile();
            int count = Sparkles.Value ? SpeckCount.Value : 0;
            if (NuclearSpeckles.Value) count += NuclearBoost(Time.unscaledTime - detonationTime);
            else detonationTime = float.NegativeInfinity;
            NightVisionOverlay.SetSpecks(Mathf.Clamp(count, 0, 400));
            if (AutoGain.Value) NightVisionGain.EnsureSubscribed(); else NightVisionGain.Clear();
        }

        internal static void Tick()
        {
            // Handle a disabled/destroyed native component even if its Update no longer runs.
            var native = NightVision.i;
            if (!Settings.Active || native == null || !native.isActiveAndEnabled)
            { Clear(); return; }
            if (!Transition.Value) NightVisionOverlay.CancelTransition();
            NightVisionOverlay.Tick();
            if (!AutoGain.Value || !Active) NightVisionGain.Clear();
        }

        internal static void CameraChanged()
        {
            NightVisionOverlay.CancelTransition(); NightVisionOverlay.SetSpecks(0);
            NightVisionGain.Clear(); RestoreProfile();
            detonationTime = float.NegativeInfinity;
            Active = false;
        }

        internal static void Clear()
        {
            Active = false; detonationTime = float.NegativeInfinity;
            RestoreProfile(); NightVisionGain.Clear(); NightVisionOverlay.Clear();
            ReleaseNoise();
        }

        private static void UpdateMixer()
        {
            if (!Phosphor.Value) { RestoreMixer(); return; }
            if (mixer == null)
            {
                mixerAdded = !profile.TryGet(out mixer);
                if (mixerAdded) mixer = profile.Add<ChannelMixer>(true);
                else mixerBaseline = UnityEngine.Object.Instantiate(mixer);
            }
            mixer.active = true;
            Color tint = Tint(PhosphorColour.Value);
            Row(mixer.redOutRedIn, mixer.redOutGreenIn, mixer.redOutBlueIn, tint.r);
            Row(mixer.greenOutRedIn, mixer.greenOutGreenIn, mixer.greenOutBlueIn, tint.g);
            Row(mixer.blueOutRedIn, mixer.blueOutGreenIn, mixer.blueOutBlueIn, tint.b);
            // The native NVG profile desaturates by -80 after the channel mixer.
            // KROP's boost preserves the chosen phosphor rather than allowing
            // that later stage to wash it back to grey. Own this one parameter;
            // native colour filter, contrast and exposure remain independent.
            if (!saturationOwned && profile.TryGet(out phosphorAdjustments))
            {
                saturationValue = phosphorAdjustments.saturation.value;
                saturationOverride = phosphorAdjustments.saturation.overrideState;
                saturationOwned = true;
            }
            if (saturationOwned && phosphorAdjustments != null) phosphorAdjustments.saturation.Override(80f);
        }

        private static void Row(ClampedFloatParameter r, ClampedFloatParameter g, ClampedFloatParameter b, float tint)
        { r.Override(30f * tint); g.Override(59f * tint); b.Override(11f * tint); }

        private static void RestoreMixer()
        {
            if (mixer != null)
            {
                if (mixerAdded) { if (profile != null) profile.components.Remove(mixer); UnityEngine.Object.Destroy(mixer); }
                else if (mixerBaseline != null)
                {
                    mixer.active = mixerBaseline.active;
                    for (int i = 0; i < mixer.parameters.Count; i++)
                    { mixer.parameters[i].SetValue(mixerBaseline.parameters[i]); mixer.parameters[i].overrideState = mixerBaseline.parameters[i].overrideState; }
                }
            }
            if (mixerBaseline != null) UnityEngine.Object.Destroy(mixerBaseline);
            mixer = mixerBaseline = null; mixerAdded = false;
            if (saturationOwned && phosphorAdjustments != null)
            {
                phosphorAdjustments.saturation.value = saturationValue;
                phosphorAdjustments.saturation.overrideState = saturationOverride;
            }
            saturationOwned = false; phosphorAdjustments = null;
        }

        private static void UpdateBloom()
        {
            if (!BloomEnabled.Value) { RestoreBloom(); return; }
            if (!bloomOwned && profile.TryGet(out bloom))
            {
                bloomValue = bloom.intensity.value; bloomOverride = bloom.intensity.overrideState;
                scatterValue = bloom.scatter.value; scatterOverride = bloom.scatter.overrideState;
                bloomOwned = true;
            }
            if (bloom != null && bloomOwned)
            { bloom.intensity.Override(BloomStrength.Value); bloom.scatter.Override(.8f); }
        }

        private static void RestoreBloom()
        {
            if (bloom != null && bloomOwned)
            {
                bloom.intensity.value = bloomValue; bloom.intensity.overrideState = bloomOverride;
                bloom.scatter.value = scatterValue; bloom.scatter.overrideState = scatterOverride;
            }
            bloomOwned = false; bloom = null;
        }

        private static void UpdateGrain()
        {
            if (!GrainEnabled.Value) { RestoreGrain(); return; }
            if (grain == null)
            {
                grainAdded = !profile.TryGet(out grain);
                if (grainAdded) grain = profile.Add<FilmGrain>(true);
                else grainBaseline = UnityEngine.Object.Instantiate(grain);
            }
            EnsureNoise();
            grain.active = true;
            grain.type.Override(FilmGrainLookup.Custom); grain.texture.Override(noise);
            grain.response.Override(0f); grain.intensity.Override(GrainStrength.Value);
        }

        private static void RestoreGrain()
        {
            if (grain != null)
            {
                if (grainAdded) { if (profile != null) profile.components.Remove(grain); UnityEngine.Object.Destroy(grain); }
                else if (grainBaseline != null)
                {
                    grain.active = grainBaseline.active;
                    for (int i = 0; i < grain.parameters.Count; i++)
                    { grain.parameters[i].SetValue(grainBaseline.parameters[i]); grain.parameters[i].overrideState = grainBaseline.parameters[i].overrideState; }
                }
            }
            if (grainBaseline != null) UnityEngine.Object.Destroy(grainBaseline);
            grain = grainBaseline = null; grainAdded = false;
            ReleaseNoise();
        }

        private static void ReleaseNoise()
        {
            if (noise != null) UnityEngine.Object.Destroy(noise);
            noise = null; pixels = scratch = null;
        }

        private static void RestoreProfile()
        { RestoreMixer(); RestoreBloom(); RestoreGrain(); profile = null; }

        private static void EnsureNoise()
        {
            const int size = 512;
            if (noise == null)
            {
                noise = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "Unified NVG grain", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
                pixels = new Color32[size * size]; scratch = new Color32[pixels.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte value = (byte)(RandomSource.NextDouble() < .5915493 ? 160 : 96);
                    pixels[i] = new Color32(value, value, value, value);
                }
                noise.SetPixels32(pixels); noise.Apply(false, false);
                nextNoiseRotation = Time.unscaledTime + 3f;
            }
            else if (Time.unscaledTime >= nextNoiseRotation)
            {
                nextNoiseRotation = Time.unscaledTime + 3f;
                int turn = RandomSource.Next(4);
                if (turn == 0) return;
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    int index = turn == 1 ? x * size + size - 1 - y : turn == 2 ? (size - 1 - y) * size + size - 1 - x : (size - 1 - x) * size + y;
                    scratch[index] = pixels[y * size + x];
                }
                var swap = pixels; pixels = scratch; scratch = swap;
                noise.SetPixels32(pixels); noise.Apply(false, false);
            }
        }

        internal static int NuclearBoost(float elapsed)
        { return elapsed >= 0f && elapsed < 128f ? Mathf.Clamp(Mathf.RoundToInt(300f * Mathf.Exp(-elapsed / 20f)), 0, 300) : 0; }

        internal static void Detonation(Vector3 position)
        {
            if (!Settings.Active || !NuclearSpeckles.Value || !Active) return;
            var camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (camera == null) return;
            Vector3 view = camera.WorldToViewportPoint(position);
            if (view.z > 0f && view.x >= 0f && view.x <= 1f && view.y >= 0f && view.y <= 1f)
                detonationTime = Time.unscaledTime;
        }

        internal static bool NativeActive => NightVision.i != null && ActiveField != null && (bool)ActiveField.GetValue(NightVision.i);
    }

    [HarmonyPatch(typeof(NightVision), "Update")]
    internal static class NightVisionPresentationPatch
    {
        private static void Postfix(bool ___nightVisActive, Volume ___postProcessing)
        { NightVisionEffects.Update(___nightVisActive, ___postProcessing); }
    }
    [HarmonyPatch(typeof(NightVision), "NightVis_OnSwitchCam")]
    internal static class NightVisionCameraPresentationPatch
    { private static void Postfix() { NightVisionEffects.CameraChanged(); } }
    [HarmonyPatch(typeof(MushroomCloud), "Start")]
    internal static class NightVisionNuclearPresentationPatch
    { private static void Postfix(MushroomCloud __instance) { if (__instance != null) NightVisionEffects.Detonation(__instance.transform.position); } }
}
