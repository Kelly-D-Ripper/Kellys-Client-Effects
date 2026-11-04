using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BoscaliClientExperimental.Weather
{
    internal static class WeatherFeature
    {
        public static void Install(BaseUnityPlugin plugin, Harmony harmony)
        {
            var ocean = Plugin.EffectsHost.AddComponent<OceanDressing>();
            ocean.Configure(plugin.Config.Bind("Ocean", "Ocean waves", false,
                "Experimental local ocean shader. Restores native water when disabled."), BepInEx.Logging.Logger.CreateLogSource("BoscaliExperimentalOcean"));
            var controller = Plugin.EffectsHost.AddComponent<WeatherController>();
            controller.Configure(plugin.Config.Bind("Ocean", "Sea spray and foam wakes", false,
                "Experimental client-local spray and foam wakes under low aircraft. Applies immediately."),
                plugin.Config.Bind("Rain", "Local test rain", false,
                "Show local falling rain for testing. This does not change or synchronize the mission weather."),
                plugin.Config.Bind("Rain", "Local rain intensity", 0.5f,
                new ConfigDescription("Visual intensity for local test rain. This is a manual test value, not a weather forecast.", new AcceptableValueRange<float>(0f, 1f))),
                plugin.Config.Bind("Rain", "Local rain haze", false,
                "Experimental local haze while local test rain is active; changes render settings only."),
                plugin.Config.Bind("Rain", "Wet terrain", false,
                "Experimental additional terrain rendering pass while local test rain is active."),
                plugin.Config.Bind("Rain", "Canopy droplets", false,
                "Shader runoff on readable cockpit glass while local test rain is active, with particle fallback."), ocean,
                plugin.Config.Bind("Atmosphere", "Sun shafts", false, "Experimental screen-space sun shafts; client-local."),
                plugin.Config.Bind("Rain", "Moving local rain front", false,
                    "Experimental client-local rain band, gated by mission cloud conditions and altitude. May differ from other players. Manual rain overrides it."));
        }
    }

    internal sealed class WeatherController : MonoBehaviour
    {
        private ConfigEntry<bool> enabledSetting;
        private ConfigEntry<bool> rainEnabled;
        private ConfigEntry<float> rainIntensity;
        private ConfigEntry<bool> hazeEnabled;
        private ConfigEntry<bool> wetEnabled;
        private ConfigEntry<bool> canopyEnabled;
        private OceanDressing ocean;
        private ConfigEntry<bool> sunShaftsEnabled;
        private ConfigEntry<bool> movingRainEnabled;
        private readonly LocalRainFront localFront = new LocalRainFront();
        private readonly SunShaftsPass sunShafts = new SunShaftsPass();
        private readonly SeaSpray spray = new SeaSpray();
        private readonly RainAtmosphere atmosphere = new RainAtmosphere();
        private readonly TerrainRainDressing terrain = new TerrainRainDressing();
        private GameObject rainRoot;
        private ProceduralRainEmitter rain;
        private CanopyDropletEmitter canopy;
        private readonly CanopyShaderDressing canopyShader = new CanopyShaderDressing();
        private readonly CanopySceneCopy canopyScene = new CanopySceneCopy();
        private LevelInfo lastLevel;
        private int canopyAircraftId;

        internal void Configure(ConfigEntry<bool> setting, ConfigEntry<bool> rainSetting, ConfigEntry<float> intensity, ConfigEntry<bool> haze, ConfigEntry<bool> wet, ConfigEntry<bool> canopySetting, OceanDressing oceanDressing, ConfigEntry<bool> shafts, ConfigEntry<bool> movingRain)
        {
            enabledSetting = setting;
            rainEnabled = rainSetting;
            rainIntensity = intensity;
            hazeEnabled = haze;
            wetEnabled = wet;
            canopyEnabled = canopySetting;
            ocean = oceanDressing;
            sunShaftsEnabled = shafts;
            movingRainEnabled = movingRain;
        }

        private void LateUpdate()
        {
            try { UpdateEffects(); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Boscali experiment] Rain/spray disabled after error: " + e);
                enabled = false;
            }
        }

        private void UpdateEffects()
        {
            LevelInfo level = LevelInfo.i;
            if (lastLevel != level)
            {
                spray.Reset();
                ClearRain();
                localFront.Reset();
                lastLevel = level;
            }
            if (Application.isBatchMode || level == null || Datum.origin == null || GameManager.gameState == GameState.Encyclopedia)
            {
                spray.Reset();
                ClearRain();
                sunShafts.Dispose();
                return;
            }
            Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
            if (camera == null) { spray.Reset(); ClearRain(); sunShafts.Dispose(); return; }
            sunShafts.Tick(level, camera, sunShaftsEnabled != null && sunShaftsEnabled.Value, 1f - Mathf.Clamp01(level.conditions));
            // The wake fades in a heavy swell; native water uses a zero wave height.
            if (enabledSetting != null && enabledSetting.Value && level.ocean != null)
                spray.Tick(camera, Datum.LocalSeaY, ocean != null ? ocean.WaveHeight : 0f, true, Time.deltaTime);
            else spray.Reset();

            float visualRain = rainEnabled != null && rainEnabled.Value ? Mathf.Clamp01(rainIntensity.Value) :
                movingRainEnabled != null && movingRainEnabled.Value ? localFront.Sample(level, camera) : 0f;
            if (visualRain > 0.001f)
            {
                if (rain == null)
                {
                    rainRoot = new GameObject("BoscaliLocalTestRain") { hideFlags = HideFlags.DontSave };
                    rainRoot.transform.SetParent(transform, false);
                    rain = rainRoot.AddComponent<ProceduralRainEmitter>();
                    rain.Initialize(camera);
                }
                rain.SetTint(RenderSettings.fogColor);
                rain.UpdateRain(Vector3.zero, Vector3.zero, visualRain, camera);
                if (hazeEnabled.Value)
                    atmosphere.Apply(visualRain, camera.transform.position.y < Datum.LocalSeaY, height: camera.transform.position.y - Datum.LocalSeaY);
                else atmosphere.Restore();
                if (wetEnabled.Value)
                {
                    Light sun = WeatherLighting.Source(level);
                    terrain.SetLighting(sun != null ? -sun.transform.forward : Vector3.up,
                        sun != null ? sun.color * sun.intensity : Color.black,
                        RenderSettings.fogColor, RenderSettings.fogDensity, Time.time);
                    terrain.Update(level.LoadedMapSettings != null ? level.LoadedMapSettings.transform : null,
                        camera, visualRain, Time.deltaTime);
                }
                else terrain.Reset();
                UpdateCanopy(camera, visualRain);
            }
            else { ClearRain(); atmosphere.Restore(); }
        }

        private void ClearRain()
        {
            atmosphere.Restore();
            terrain.Reset();
            if (rainRoot != null) Destroy(rainRoot);
            rainRoot = null;
            rain = null;
            canopy = null;
            CanopyGlassResolver.ResetForScene();
            canopyAircraftId = 0;
            canopyShader.Detach();
            canopyScene.Dispose();
        }

        private void UpdateCanopy(Camera camera, float visualRain)
        {
            var cameras = SceneSingleton<CameraStateManager>.i;
            if (!canopyEnabled.Value || cameras == null || cameras.currentState != cameras.cockpitState)
            {
                if (canopy != null) canopy.UpdateDrops(default, 0f);
                canopyShader.Detach();
                canopyScene.Dispose();
                return;
            }
            Aircraft aircraft = cameras.followingUnit as Aircraft;
            if (aircraft == null) GameManager.GetLocalAircraft(out aircraft);
            int currentAircraftId = aircraft != null ? aircraft.gameObject.GetInstanceID() : 0;
            if (currentAircraftId != canopyAircraftId)
            {
                canopyAircraftId = currentAircraftId;
                canopyShader.Detach();
                canopyScene.Dispose();
                CanopyGlassResolver.ResetForScene();
            }
            Transform cockpit = aircraft != null && aircraft.cockpit != null ? aircraft.cockpit.transform : null;
            Camera eye = cameras.cockpitCamRender != null && cameras.cockpitCamRender.enabled ? cameras.cockpitCamRender : camera;
            var surfaces = CanopyGlassResolver.Resolve(cockpit, eye.transform.position, eye.cullingMask);
            if (canopy == null)
            {
                var root = new GameObject("BoscaliCanopyDrops") { hideFlags = HideFlags.DontSave };
                root.transform.SetParent(rainRoot.transform, false);
                canopy = root.AddComponent<CanopyDropletEmitter>();
                canopy.Initialize();
            }
            Vector3 wind = LevelInfo.i != null ? LevelInfo.i.windVelocity : Vector3.zero;
            Vector3 velocity = aircraft != null && aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
            Vector3 slip = wind - velocity;
            float intensity = Mathf.Clamp01(visualRain);
            canopyShader.UpdateSim(surfaces, intensity, Mathf.Clamp01(slip.magnitude / 250f), slip, Time.deltaTime);
            Light sun = WeatherLighting.Source(LevelInfo.i);
            canopyShader.SetLighting(sun != null ? -sun.transform.forward : Vector3.zero,
                sun != null ? sun.color * sun.intensity : Color.black, RenderSettings.fogColor, true);
            canopyScene.Arm(eye);
            canopyShader.SetScene(canopyScene.Ready(eye) ? canopyScene.Texture : null);
            bool drawn = canopyShader.Draw(surfaces, eye, intensity, 1f);
            if (!drawn) canopyScene.Dispose();
            canopy.UpdateDrops(surfaces.Count > 0 ? surfaces[0] : default, drawn ? 0f : intensity);
        }

        private void OnDisable() { spray.Reset(); ClearRain(); sunShafts.Dispose(); }
        private void OnDestroy() { spray.Reset(); ClearRain(); sunShafts.Dispose(); }
    }
}
