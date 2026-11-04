using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliClientExperimental.Immersion
{
    internal sealed partial class StreetGlowCloudFade
    {
        private ConfigEntry<bool> compactSetting;
        private ConfigEntry<float> poolRadiusSetting, poolBrightnessSetting;
        private static readonly int SizeId = Shader.PropertyToID("_Size");
        private static readonly int PoolRadiusId = Shader.PropertyToID("_Range");
        private static readonly int PoolFalloffId = Shader.PropertyToID("_FalloffK");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static FieldInfo groundActiveField, groundFeatureField, groundPassField, fallbackRenderersField;
        private readonly List<FallbackFrame> fallbackPool = new List<FallbackFrame>();

        private void InitializeCompact(ConfigFile config)
        {
            compactSetting = config.Bind("Lighting", "Compact street lighting", true,
                "Smaller street-light pools and lamp glows, shorter light reach, and pools drawn before transparent clouds. Applies per camera; existing Better Night settings are preserved.");
            poolRadiusSetting = config.Bind("Lighting", "Street pool radius", 35f,
                new ConfigDescription("Maximum ground-light radius in metres while compact street lighting is enabled.", new AcceptableValueRange<float>(15f, 60f)));
            poolBrightnessSetting = config.Bind("Lighting", "Street pool brightness", 1f,
                new ConfigDescription("Ground-light brightness multiplier while compact street lighting is enabled. Zero removes the added ground-light pass.", new AcceptableValueRange<float>(0f, 2f)));
        }

        internal static float CompactGroundFade(float heightAboveSurface)
        {
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(200f, 1400f, heightAboveSurface));
        }

        internal static float CompactFalloff(float radius, float previousRadius, float previousFalloff)
        {
            // _FalloffK is scaled by the square of the original radius in Better Night.
            // Preserve a user's stronger centre weighting when shrinking the pool.
            float normalized = Mathf.Sqrt(Mathf.Max(0f, previousFalloff)) * Mathf.Max(1f, previousRadius) / 5f;
            normalized = Mathf.Max(.55f, normalized);
            radius = Mathf.Max(1f, radius);
            return normalized * normalized * 25f / (radius * radius);
        }

        private static void ResolveCompact()
        {
            if (groundActiveField != null && fallbackRenderersField != null) return;
            Type ground = AccessTools.TypeByName("RealisticNight.StreetLightFX");
            Type streets = AccessTools.TypeByName("RealisticNight.StreetLights");
            Type feature = AccessTools.TypeByName("RealisticNight.StreetLightRendererFeature");
            if (ground != null)
            {
                groundActiveField = AccessTools.Field(ground, "Active");
                groundFeatureField = AccessTools.Field(ground, "feature");
            }
            if (feature != null) groundPassField = AccessTools.Field(feature, "pass");
            if (streets != null) fallbackRenderersField = AccessTools.Field(streets, "renderers");
        }

        private static void CaptureCompact(Snapshot s)
        {
            ResolveCompact();
            if (s.orb != null) s.orbSize = s.orb.GetFloat(SizeId);
            if (s.yellow != null) s.yellowSize = s.yellow.GetFloat(SizeId);
            if (s.ground != null)
            {
                s.poolRadius = s.ground.GetFloat(PoolRadiusId);
                s.poolFalloff = s.ground.GetFloat(PoolFalloffId);
            }
            s.hasGroundActive = groundActiveField != null;
            if (s.hasGroundActive) s.groundActive = (bool)groundActiveField.GetValue(null);
            object feature = groundFeatureField?.GetValue(null);
            s.pass = feature != null ? groundPassField?.GetValue(feature) as ScriptableRenderPass : null;
            if (s.pass != null) s.passEvent = s.pass.renderPassEvent;
        }

        private void ApplyCompact(Snapshot s, Snapshot basis, bool compact, float orbFade, float groundFade, float rangeScale)
        {
            Snapshot orb = s.orb == basis.orb ? basis : s;
            Snapshot yellow = s.yellow == basis.yellow ? basis : s;
            Snapshot ground = s.ground == basis.ground ? basis : s;
            if (s.orb != null)
            {
                s.orb.SetFloat(SizeId, compact ? Mathf.Min(orb.orbSize, .65f) : orb.orbSize);
                if (compact) SetRange(s.orb, Mathf.Min(orb.orbRange * rangeScale, 8000f), 1f);
            }
            if (s.yellow != null)
            {
                s.yellow.SetFloat(SizeId, compact ? Mathf.Min(yellow.yellowSize, .65f) : yellow.yellowSize);
                if (compact) SetRange(s.yellow, Mathf.Min(yellow.yellowRange * rangeScale, 8000f), 1f);
            }
            float brightness = compact ? Mathf.Clamp(poolBrightnessSetting?.Value ?? 1f, 0f, 2f) : 1f;
            if (s.ground != null)
            {
                float radius = compact ? Mathf.Min(ground.poolRadius, Mathf.Clamp(poolRadiusSetting?.Value ?? 35f, 15f, 60f)) : ground.poolRadius;
                s.ground.SetFloat(PoolRadiusId, radius);
                s.ground.SetFloat(PoolFalloffId, compact ? CompactFalloff(radius, ground.poolRadius, ground.poolFalloff) : ground.poolFalloff);
                s.ground.SetFloat(IntensityId, (compact ? Mathf.Min(ground.intensity, 1.5f) : ground.intensity) * groundFade * brightness);
                s.ground.SetFloat(WarmId, (compact ? Mathf.Min(ground.warm, 2f) : ground.warm) * groundFade * brightness);
                if (compact) SetRange(s.ground, Mathf.Min(ground.groundRange * rangeScale, 1200f), 1f);
            }
            // A fully faded pool should not enqueue two full-screen colour targets.
            if (s.hasGroundActive)
                groundActiveField.SetValue(null, basis.groundActive && (!compact || groundFade * brightness > .0001f));
            // Opaque scene depth cannot describe transparent clouds. Composite ground
            // lighting first, so later transparency can cover it instead of being relit.
            if (s.pass != null)
                s.pass.renderPassEvent = compact ? RenderPassEvent.BeforeRenderingTransparents :
                    (s.pass == basis.pass ? basis.passEvent : s.passEvent);
            if (s.orb == null) ApplyFallback(s, basis, compact, orbFade, rangeScale);
        }

        private void ApplyFallback(Snapshot s, Snapshot basis, bool compact, float fade, float rangeScale)
        {
            if (!(fallbackRenderersField?.GetValue(null) is List<MeshRenderer> renderers)) return;
            int depth = cameras.Count;
            while (fallbackPool.Count <= depth) fallbackPool.Add(new FallbackFrame());
            FallbackFrame frame = fallbackPool[depth];
            frame.used = 0;
            frame.index.Clear();
            s.fallback = frame.items;
            s.fallbackCount = 0;
            s.fallbackIndex = frame.index;
            float originalRange = 25000f;
            if (streetAtmosSettingField == null)
            {
                Type plugin = AccessTools.TypeByName("RealisticNight.RealisticNightPlugin");
                if (plugin != null) streetAtmosSettingField = AccessTools.Field(plugin, "StreetLightAtten");
            }
            if (streetAtmosSettingField?.GetValue(null) is ConfigEntry<float> setting)
                originalRange = Mathf.Max(1f, setting.Value);
            for (int i = 0; i < renderers.Count; ++i)
            {
                MeshRenderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled) continue;
                if (frame.used == frame.items.Count) frame.items.Add(new FallbackSnapshot());
                FallbackSnapshot item = frame.items[frame.used++];
                item.renderer = renderer;
                item.scale = renderer.transform.localScale;
                item.rotation = renderer.transform.rotation;
                item.forcedOff = renderer.forceRenderingOff;
                renderer.GetPropertyBlock(item.original);
                renderer.GetPropertyBlock(item.applied);
                frame.index[renderer.GetInstanceID()] = item;
                FallbackSnapshot source = item;
                if (basis != s && basis.fallbackIndex != null && basis.fallbackIndex.TryGetValue(renderer.GetInstanceID(), out var outer))
                    source = outer;
                float size = Mathf.Min(source.scale.x, Mathf.Min(source.scale.y, source.scale.z));
                if (compact) size = Mathf.Min(size, .65f);
                renderer.transform.localScale = compact ? new Vector3(size, size, size) : source.scale;
                renderer.transform.rotation = s.camera.transform.rotation;
                float amount = fade;
                // Fallback shaders do not implement _AtmosRange. Its original per-lamp
                // exponential colour attenuation was already written by UpdateFrame.
                // Read the source setting rather than a nonexistent shader property.
                float shortened = Mathf.Max(1f, originalRange * rangeScale);
                if (compact) shortened = Mathf.Min(shortened, 8000f);
                float distance = Vector3.Distance(s.camera.transform.position, renderer.transform.position);
                amount *= Mathf.Exp(Mathf.Min(0f, distance * (1f / originalRange - 1f / shortened)));
                item.applied.SetColor(ColorId, Scale(source.original.GetColor(ColorId), amount));
                item.applied.SetColor(BaseColorId, Scale(source.original.GetColor(BaseColorId), amount));
                renderer.SetPropertyBlock(item.applied);
                renderer.forceRenderingOff = source.forcedOff || amount < .0001f;
            }
            s.fallbackCount = frame.used;
        }

        private static FieldInfo streetAtmosSettingField;

        private static void RestoreCompact(Snapshot s)
        {
            if (s.orb != null) s.orb.SetFloat(SizeId, s.orbSize);
            if (s.yellow != null) s.yellow.SetFloat(SizeId, s.yellowSize);
            if (s.ground != null)
            {
                s.ground.SetFloat(PoolRadiusId, s.poolRadius);
                s.ground.SetFloat(PoolFalloffId, s.poolFalloff);
            }
            if (s.hasGroundActive) groundActiveField.SetValue(null, s.groundActive);
            if (s.pass != null) s.pass.renderPassEvent = s.passEvent;
            if (s.fallback == null) return;
            for (int i = s.fallbackCount - 1; i >= 0; --i)
            {
                FallbackSnapshot item = s.fallback[i];
                if (item.renderer == null) continue;
                item.renderer.SetPropertyBlock(item.original);
                item.renderer.transform.localScale = item.scale;
                item.renderer.transform.rotation = item.rotation;
                item.renderer.forceRenderingOff = item.forcedOff;
            }
        }

        private sealed class FallbackFrame
        {
            internal readonly List<FallbackSnapshot> items = new List<FallbackSnapshot>();
            internal readonly Dictionary<int, FallbackSnapshot> index = new Dictionary<int, FallbackSnapshot>();
            internal int used;
        }

        private sealed class FallbackSnapshot
        {
            internal MeshRenderer renderer;
            internal Vector3 scale;
            internal Quaternion rotation;
            internal bool forcedOff;
            internal readonly MaterialPropertyBlock original = new MaterialPropertyBlock();
            internal readonly MaterialPropertyBlock applied = new MaterialPropertyBlock();
        }
    }
}
