using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using BoscaliSummer.Modules.Immersion.Visuals;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoscaliClientExperimental.Immersion
{
    internal static class ImmersionFeature
    {
        private static ManualLogSource log;
        public static void Install(BaseUnityPlugin plugin, Harmony harmony)
        {
            if (Application.isBatchMode) return;
            log = BepInEx.Logging.Logger.CreateLogSource("BoscaliClientExperimental.Immersion");
            var service = Plugin.EffectsHost.AddComponent<ImmersionRuntime>();
            service.Configure(plugin.Config);
        }
        internal static void LogWarning(string message) => log?.LogWarning(message);
        internal static void LogInfo(string message) => log?.LogInfo(message);
    }

    internal sealed class ImmersionRuntime : MonoBehaviour
    {
        private ConfigEntry<bool> glareEnabled, visorEnabled, contrailsEnabled, dustEnabled;
        private ConfigEntry<float> visorReflection;
        private readonly Contrails contrails = new Contrails();
        private readonly DustWake dust = new DustWake();
        private readonly SunGlare glare = new SunGlare();
        private readonly VisorOverlay visor = new VisorOverlay();
        private bool overlapReported;
        private Transform datum;

        internal void Configure(ConfigFile config)
        {
            glareEnabled = config.Bind("Immersion", "SunGlare", true, "Sun glare in cockpit view. Experimental; restart or toggle to compare.");
            visorEnabled = config.Bind("Immersion", "HelmetVisor", true, "Helmet glass tint, scratches and reflected sunlight in cockpit view. Does not include a reflected pilot body.");
            visorReflection = config.Bind("Immersion", "HelmetReflection", 1f, new ConfigDescription("Visor reflection strength.", new AcceptableValueRange<float>(0f, 1f)));
            contrailsEnabled = config.Bind("Immersion", "Contrails", true, "Client visual contrails; suppressed while KROP or QoL Realism Fixes is present.");
            dustEnabled = config.Bind("Immersion", "DustWakes", true, "Client low-level dust wakes; suppressed while KROP or QoL Realism Fixes is present.");
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }
        private void OnSceneUnloaded(Scene scene) => ResetEffects();
        private void Update()
        {
            if (Application.isBatchMode || glareEnabled == null) return;
            var cameras = SceneSingleton<CameraStateManager>.i;
            var camera = cameras != null ? cameras.mainCamera : null;
            var nextDatum = Datum.origin;
            if (datum != nextDatum) { ResetEffects(); datum = nextDatum; }
            if (camera == null || nextDatum == null)
            {
                ResetEffects();
                return;
            }
            bool overlap = Chainloader.PluginInfos.ContainsKey("aaron.krop.client-effects") ||
                           Chainloader.PluginInfos.ContainsKey("aaron.boscali.krop-unified") ||
                           Chainloader.PluginInfos.ContainsKey("pavehog727.qolrealismfixes");
            if (overlap && !overlapReported)
            {
                ImmersionFeature.LogInfo("KROP or QoL Realism Fixes detected: Boscali contrails and dust wakes suppressed to prevent duplicate effects.");
                overlapReported = true;
            }
            var level = NetworkSceneSingleton<LevelInfo>.i;
            Light sun = level != null ? level.sun : null;
            float light = sun == null ? 1f : Mathf.Lerp(.25f, 1f, Mathf.Clamp01(Vector3.Dot(-sun.transform.forward, Vector3.up) * 5f + .3f));
            float dt = Mathf.Clamp(Time.deltaTime, 0f, .05f);
            contrails.Tick(camera.transform.position, contrailsEnabled.Value && !overlap, light);
            dust.Tick(camera.transform.position, dustEnabled.Value && !overlap, light, dt);
            Aircraft plane = cameras.followingUnit as Aircraft;
            bool cockpit = CameraStateManager.cameraMode == CameraMode.cockpit && cameras.currentState == cameras.cockpitState &&
                           plane != null && !plane.disabled && plane.rb != null;
            if (!cockpit || (!glareEnabled.Value && !visorEnabled.Value))
            {
                // Cloud occlusion and the sun projection are only used by these cockpit effects.
                // Skip those queries when neither can draw, while immediately hiding stale effects.
                glare.Tick(level, camera, false, dt);
                visor.Hide();
                return;
            }
            float transmission = level != null ? 1f - Mathf.Clamp01(level.GetCloudOcclusion(camera.transform.position)) : 1f;
            // Keep sun visibility updating for the visor even if the glare drawing is disabled.
            glare.Tick(level, camera, cockpit && (glareEnabled.Value || visorEnabled.Value), dt, transmission);
            glare.SetDrawingEnabled(glareEnabled.Value);
            float ambient = level != null ? level.GetAmbientLight() : 1f;
            float facing = sun != null ? Mathf.Clamp01((Vector3.Dot(camera.transform.forward, -sun.transform.forward) - .5f) / .45f) : 0f;
            Vector3 sunPosition = sun != null ? camera.WorldToViewportPoint(camera.transform.position - sun.transform.forward) : new Vector3(.5f, .5f, -1f);
            visor.Tick(glare.Visibility * facing * facing, ambient, cockpit && visorEnabled.Value, dt, sunPosition, facing, .55f, visorReflection.Value);
        }
        private void ResetEffects()
        {
            contrails.Reset(); dust.Reset(); glare.Release(); visor.Release(); datum = null;
        }
        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            ResetEffects();
            SunGlare.ReleaseData(); PilotShaderBundle.Release();
            Contrails.ReleaseMaterial(); BoscaliSummer.Core.Fx.SmokeFx.Release();
        }
    }
}
