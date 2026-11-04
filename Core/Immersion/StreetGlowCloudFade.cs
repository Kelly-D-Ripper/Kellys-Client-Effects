using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliClientExperimental.Immersion
{
    // Better Night's street lights are shared materials. Scale only while an individual
    // camera renders, and restore their exact previous values even for nested cameras.
    // This is a whole-view weather heuristic, not a per-pixel cloud-occlusion test.
    internal sealed partial class StreetGlowCloudFade : MonoBehaviour
    {
        private ConfigEntry<bool> enabledSetting;
        private ConfigEntry<bool> altitudeSetting;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int WarmId = Shader.PropertyToID("_IntensityWarm");
        private static readonly int AtmosId = Shader.PropertyToID("_AtmosRange");
        private static readonly FieldInfo CloudLayer = AccessTools.Field(typeof(LevelInfo), "cloudLayer");
        private static readonly FieldInfo CurrentWeather = AccessTools.Field(typeof(CloudLayer), "currentWeatherSet");
        private FieldInfo orbField, yellowField, orbActiveField, groundField;
        private readonly List<Snapshot> cameras = new List<Snapshot>();
        private readonly Dictionary<int, HeightSample> heights = new Dictionary<int, HeightSample>();
        private float nextHeightCleanup;

        internal static void Install(Plugin plugin)
        {
            var value = plugin.Config.Bind("Lighting", "Reduce street glow above clouds", true,
                "Fade Better Night street glow when a camera is above substantial cloud. This is an approximate whole-view fade.");
            var altitude = plugin.Config.Bind("Lighting", "Reduce street glow at altitude", true,
                "Fade Better Night street glow as the camera climbs above nearby ground or sea, independently of cloud cover. Client-local; applies now.");
            var effect = Plugin.EffectsHost.AddComponent<StreetGlowCloudFade>();
            effect.Initialize(value, altitude);
            effect.InitializeCompact(plugin.Config);
        }

        private void Initialize(ConfigEntry<bool> setting, ConfigEntry<bool> altitude) { enabledSetting = setting; altitudeSetting = altitude; }
        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            RenderPipelineManager.endFrameRendering += EndFrame;
        }
        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RenderPipelineManager.endFrameRendering -= EndFrame;
            for (int i = cameras.Count - 1; i >= 0; --i) Restore(cameras[i]);
            cameras.Clear();
            heights.Clear();
            fallbackPool.Clear();
        }

        internal static float Fade(float cloudHeight, float cameraHeight, float cloudCoverage)
        {
            // Clear skies and positions below the native cloud base remain unchanged.
            float coverage = Mathf.InverseLerp(0.1f, 0.9f, cloudCoverage);
            float above = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cloudHeight, cloudHeight + 500f, cameraHeight));
            return 1f - 0.85f * coverage * above;
        }

        // At low altitude the original lights stay intact. As a camera climbs, distant
        // lamps become much less dominant, even when there are no clouds below it.
        // This is a whole-camera approximation: it is not distance per individual lamp.
        internal static float AltitudeFade(float heightAboveSurface)
        {
            float climb = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(300f, 2000f, heightAboveSurface));
            return 1f - 0.55f * climb;
        }

        internal static float AltitudeRangeScale(float heightAboveSurface)
        {
            float climb = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(300f, 2000f, heightAboveSurface));
            return 1f - 0.88f * climb;
        }

        private float HeightAboveSurface(Camera camera)
        {
            int id = camera.GetInstanceID();
            float now = Time.unscaledTime;
            Vector3 eye = camera.transform.position;
            float seaY = Datum.LocalSeaY;
            if (heights.TryGetValue(id, out var sample) && sample.camera == camera && now < sample.until &&
                Mathf.Abs(eye.y - sample.eye.y) < 40f &&
                (new Vector2(eye.x - sample.eye.x, eye.z - sample.eye.z)).sqrMagnitude < 62500f &&
                Mathf.Abs(seaY - sample.seaY) < 1f)
                return sample.height;

            float aboveSea = Mathf.Max(0f, eye.y - seaY);
            float height = aboveSea;
            // An overhead surface or dynamic aircraft must not count as the terrain.
            // If no land is below the eye, use the ocean datum as the ground reference.
            if (aboveSea > 0f && Physics.Raycast(eye, Vector3.down, out RaycastHit hit,
                aboveSea, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                height = Mathf.Clamp(hit.distance, 0f, aboveSea);
            heights[id] = new HeightSample { camera = camera, height = height, until = now + 0.5f, eye = eye, seaY = seaY };
            if (now >= nextHeightCleanup)
            {
                nextHeightCleanup = now + 10f;
                var stale = new List<int>();
                foreach (var entry in heights)
                    if (entry.Value.camera == null || now > entry.Value.until + 10f) stale.Add(entry.Key);
                foreach (int key in stale) heights.Remove(key);
            }
            return height;
        }

        private void Resolve()
        {
            if (orbField != null && groundField != null) return;
            var street = AccessTools.TypeByName("RealisticNight.StreetLights");
            var ground = AccessTools.TypeByName("RealisticNight.StreetLightFX");
            if (street == null || ground == null) return;
            orbField = AccessTools.Field(street, "mat");
            yellowField = AccessTools.Field(street, "orbYellowMat");
            orbActiveField = AccessTools.Field(street, "orbActive");
            groundField = AccessTools.Field(ground, "mat");
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            bool cloudEnabled = enabledSetting != null && enabledSetting.Value;
            bool altitudeEnabled = altitudeSetting != null && altitudeSetting.Value;
            bool compact = compactSetting != null && compactSetting.Value;
            if ((!cloudEnabled && !altitudeEnabled && !compact) || camera == null) return;
            var level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null) return;
            float height = altitudeEnabled ? HeightAboveSurface(camera) : 0f;
            float fade = altitudeEnabled ? AltitudeFade(height) : 1f;
            float rangeScale = altitudeEnabled ? AltitudeRangeScale(height) : 1f;
            if (cloudEnabled && CloudLayer != null && CloudLayer.GetValue(level) is Component layer)
            {
                // CurrentWeatherSet.coverage is the native layer's whole-map coverage value.
                // If the layer has not selected a set yet, approximate from conditions.
                var weather = CurrentWeather?.GetValue(layer) as WeatherSet;
                float coverage = weather != null ? weather.coverage : Mathf.InverseLerp(0.25f, 0.6f, level.conditions);
                fade = Mathf.Min(fade, Fade(layer.transform.position.y, camera.transform.position.y, coverage));
            }
            ApplyProfile(camera, fade, rangeScale, compact,
                compact && altitudeEnabled ? Mathf.Min(fade, CompactGroundFade(height)) : fade);
        }

        private void ApplyCamera(Camera camera, float fade, float rangeScale)
        {
            ApplyProfile(camera, fade, rangeScale, false, fade);
        }

        private void ApplyProfile(Camera camera, float fade, float rangeScale, bool compact, float groundFade)
        {
            if (!compact && fade >= 0.9999f && rangeScale >= 0.9999f && cameras.Count == 0) return;
            Resolve();
            if (orbField == null || groundField == null) return;
            bool orbActive = orbActiveField != null && (bool)orbActiveField.GetValue(null);
            var s = new Snapshot { camera = camera,
                orb = orbActive ? orbField.GetValue(null) as Material : null,
                yellow = orbActive ? yellowField?.GetValue(null) as Material : null,
                ground = groundField.GetValue(null) as Material };
            Capture(s);
            Snapshot basis = cameras.Count == 0 ? s : cameras[0];
            if (s.orb != null) s.orb.SetColor(ColorId, Scale(s.orb == basis.orb ? basis.orbColor : s.orbColor, fade));
            if (s.yellow != null) s.yellow.SetColor(ColorId, Scale(s.yellow == basis.yellow ? basis.yellowColor : s.yellowColor, fade));
            if (s.ground != null) {
                s.ground.SetFloat(IntensityId, (s.ground == basis.ground ? basis.intensity : s.intensity) * fade);
                s.ground.SetFloat(WarmId, (s.ground == basis.ground ? basis.warm : s.warm) * fade);
            }
            SetRange(s.orb, s.orb == basis.orb ? basis.orbRange : s.orbRange, rangeScale);
            SetRange(s.yellow, s.yellow == basis.yellow ? basis.yellowRange : s.yellowRange, rangeScale);
            SetRange(s.ground, s.ground == basis.ground ? basis.groundRange : s.groundRange, rangeScale);
            ApplyCompact(s, basis, compact, fade, groundFade, rangeScale);
            cameras.Add(s);
        }
        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            // Cameras that did not need a fade do not own a snapshot.
            if (cameras.Count == 0 || cameras[cameras.Count - 1].camera != camera) return;
            int index = cameras.Count - 1;
            Restore(cameras[index]);
            cameras.RemoveAt(index);
        }
        private void EndFrame(ScriptableRenderContext context, Camera[] renderedCameras)
        {
            // Interrupted camera renders must not leave shared materials attenuated.
            for (int i = cameras.Count - 1; i >= 0; --i) Restore(cameras[i]);
            cameras.Clear();
        }
        private static Color Scale(Color value, float fade) => new Color(value.r * fade, value.g * fade, value.b * fade, value.a);
        private static void SetRange(Material material, float range, float scale)
        {
            // Better Night's shaders expose _AtmosRange as a uniform without listing it
            // in ShaderLab Properties; HasProperty therefore returns false for it.
            if (material != null)
                material.SetFloat(AtmosId, Mathf.Min(range, Mathf.Max(100f, range * scale)));
        }
        private static void Capture(Snapshot s)
        {
            if (s.orb != null) { s.orbColor = s.orb.GetColor(ColorId); s.orbRange = s.orb.GetFloat(AtmosId); }
            if (s.yellow != null) { s.yellowColor = s.yellow.GetColor(ColorId); s.yellowRange = s.yellow.GetFloat(AtmosId); }
            if (s.ground != null) { s.intensity = s.ground.GetFloat(IntensityId); s.warm = s.ground.GetFloat(WarmId); s.groundRange = s.ground.GetFloat(AtmosId); }
            CaptureCompact(s);
        }
        private static void Restore(Snapshot s)
        {
            if (s.orb != null) { s.orb.SetColor(ColorId, s.orbColor); SetRange(s.orb, s.orbRange, 1f); }
            if (s.yellow != null) { s.yellow.SetColor(ColorId, s.yellowColor); SetRange(s.yellow, s.yellowRange, 1f); }
            if (s.ground != null) { s.ground.SetFloat(IntensityId, s.intensity); s.ground.SetFloat(WarmId, s.warm); SetRange(s.ground, s.groundRange, 1f); }
            RestoreCompact(s);
        }
        private sealed class Snapshot
        {
            internal Camera camera;
            internal Material orb, yellow, ground;
            internal Color orbColor, yellowColor;
            internal float intensity, warm;
            internal float orbRange, yellowRange, groundRange;
            internal float orbSize, yellowSize, poolRadius, poolFalloff;
            internal bool hasGroundActive, groundActive;
            internal UnityEngine.Rendering.Universal.ScriptableRenderPass pass;
            internal UnityEngine.Rendering.Universal.RenderPassEvent passEvent;
            internal List<FallbackSnapshot> fallback;
            internal int fallbackCount;
            internal Dictionary<int, FallbackSnapshot> fallbackIndex;
        }
        private struct HeightSample
        {
            internal Camera camera;
            internal float height, until;
            internal Vector3 eye;
            internal float seaY;
        }
    }
}
