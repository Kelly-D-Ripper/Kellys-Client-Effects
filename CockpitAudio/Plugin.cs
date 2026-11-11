using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using BoscaliSummer.Modules.Immersion.Audio;
using BoscaliSummer.Modules.Immersion.Domain;
using BoscaliSummer.Modules.Immersion.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoscaliCockpitAudio
{
    [BepInPlugin(Id, "Boscali Cockpit Audio", "0.6.0")]
    [BepInIncompatibility("com.marci.boscalisummer")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "aaron.boscali.cockpit-audio";
        internal static ManualLogSource Log;
        private void Awake()
        {
            if (Application.isBatchMode || GameManager.IsHeadless) return;
            Log = Logger;
            var host = new GameObject("Boscali Cockpit Audio") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            host.AddComponent<CockpitAudioRuntime>().Initialize(Config);
        }
    }

    /// <summary>Read-only presentation data for the separate pilot body component.</summary>
    public static class PilotMotionBridge
    {
        public static bool TryGet(out Aircraft aircraft, out Vector3 forceG, out float chestLift, out float exertion)
        {
            var service = CockpitAudioRuntime.Live;
            if (service != null) return service.ReadMotion(out aircraft, out forceG, out chestLift, out exertion);
            aircraft = null; forceG = Vector3.up; chestLift = exertion = 0f; return false;
        }
    }

    internal sealed class CockpitAudioRuntime : MonoBehaviour
    {
        internal static CockpitAudioRuntime Live;
        private ConfigEntry<bool> breathing, creaks, wind, softRadar, controls;
        private ConfigEntry<bool> breathCycles, gStrain, gSuit, heartbeat, recoveryGasps;
        private ConfigEntry<bool> structuralCreaks, harnessStrain, impactFoley, gearLever, canopyClosing;
        internal bool SoftRadarActive => active && softRadar != null && softRadar.Value;
        private readonly PilotPhysiology physiology = new PilotPhysiology();
        private readonly PilotVitals vitals = new PilotVitals();
        private readonly HeadMotion forces = new HeadMotion();
        private AirframeAudio airframe;
        private CanopyWindAudio canopy;
        private PilotBreathingAudio breath;
        private CockpitFoley foley;
        private ControlFoley controlsAudio;
        private Harmony harmony;
        private Aircraft aircraft;
        private Camera camera;
        private double lastClock = double.NaN;
        private bool active;
        internal bool ReadMotion(out Aircraft current, out Vector3 forceG, out float chestLift, out float exertion)
        {
            current = aircraft; forceG = forces.ForceG; chestLift = physiology.ChestLift01; exertion = physiology.Exertion01;
            return active && current != null;
        }

        internal void Initialize(ConfigFile config)
        {
            breathing = config.Bind("Cockpit Audio", "PilotBreathing", true, "Oxygen mask breathing, G-strain, suit hiss and heartbeat in own cockpit.");
            creaks = config.Bind("Cockpit Audio", "AirframeCreaks", true, "Structural and harness creaks and cockpit hit foley in own cockpit.");
            wind = config.Bind("Cockpit Audio", "CanopyWind", true, "Cockpit wind and slipstream; independent of other audio settings.");
            softRadar = config.Bind("Cockpit Audio", "SoftRadarTones", false, "Optional Boscali radar warning beeps only in own cockpit. Off preserves native radar sounds immediately.");
            controls = config.Bind("Cockpit Audio", "ControlFoley", true, "Cockpit gear lever and ground-start canopy closing sounds.");
            breathCycles = config.Bind("Cockpit Audio", "Breathing cycles", true, "Oxygen-mask inhale and exhale. Requires PilotBreathing.");
            gStrain = config.Bind("Cockpit Audio", "G strain", true, "Pilot straining grunts and exertion gasps. Requires PilotBreathing.");
            gSuit = config.Bind("Cockpit Audio", "G suit hiss", true, "Anti-G suit inflation and venting. Requires PilotBreathing.");
            heartbeat = config.Bind("Cockpit Audio", "Heartbeat", true, "Pilot heartbeat under load. Requires PilotBreathing.");
            recoveryGasps = config.Bind("Cockpit Audio", "Recovery gasps", true, "Wake-up breath after regaining consciousness. Requires PilotBreathing.");
            structuralCreaks = config.Bind("Cockpit Audio", "Structural creaks", true, "Airframe stress creaking. Requires AirframeCreaks.");
            harnessStrain = config.Bind("Cockpit Audio", "Harness strain", true, "Harness creaks as G load changes. Requires AirframeCreaks.");
            impactFoley = config.Bind("Cockpit Audio", "Impact foley", true, "Cockpit hull thuds and hit pings. Requires AirframeCreaks.");
            gearLever = config.Bind("Cockpit Audio", "Gear lever", true, "Cockpit gear-lever movement sounds. Requires ControlFoley.");
            canopyClosing = config.Bind("Cockpit Audio", "Canopy closing", true, "Canopy closing sound when starting on the ground. Requires ControlFoley.");
            Live = this;
            harmony = new Harmony(Plugin.Id);
            var target = AccessTools.GetDeclaredMethods(typeof(Aircraft)).Find(m => m.Name.StartsWith("UserCode_RpcDamage_", StringComparison.Ordinal));
            if (target != null) harmony.Patch(target, postfix: new HarmonyMethod(typeof(CockpitAudioRuntime), nameof(OnHit)));
            else Plugin.Log?.LogWarning("Damage callback not found; hit foley and hit stress unavailable.");
            var radarStart = AccessTools.Method(typeof(RadarWarning), "Start");
            var radarPlay = AccessTools.Method(typeof(SoundManager), nameof(SoundManager.PlayRadarWarningOneShot));
            if (radarStart != null && radarPlay != null)
            {
                harmony.Patch(radarStart, postfix: new HarmonyMethod(typeof(RadarTones), nameof(RadarTones.Capture)));
                harmony.Patch(radarPlay, prefix: new HarmonyMethod(typeof(RadarTones), nameof(RadarTones.Replace)));
            }
            else Plugin.Log?.LogWarning("Radar tone hooks unavailable; native radar audio is unchanged.");
            SceneManager.sceneUnloaded += SceneUnloaded;
        }
        private static void OnHit(Aircraft __instance, DamageInfo __1)
        {
            var live = Live;
            if (live == null || !live.active || __instance != live.aircraft) return;
            float pierce = __1.pierceDamage.Decompress(), blast = __1.blastDamage.Decompress();
            live.vitals.AddHit(blast);
            if (live.creaks.Value && live.impactFoley.Value) live.foley?.Hit(pierce, blast);
        }
        private void SceneUnloaded(Scene scene) { ResetScene(); RadarTones.Clear(); }
        private void FixedUpdate()
        {
            if (!active) return;
            forces.Measure(aircraft, Time.fixedDeltaTime);
            if (forces.Discontinuity) airframe?.Silence();
        }
        private void Update()
        {
            var cameras = SceneSingleton<CameraStateManager>.i;
            Aircraft followed = cameras != null ? cameras.followingUnit as Aircraft : null;
            Camera current = cameras != null ? cameras.mainCamera : null;
            Pilot pilot = followed != null && followed.pilots != null && followed.pilots.Length > 0 ? followed.pilots[0] : null;
            bool own = GameManager.GetLocalAircraft(out Aircraft local) && local == followed;
            bool inCockpit = cameras != null && CameraStateManager.cameraMode == CameraMode.cockpit && cameras.currentState == cameras.cockpitState;
            if (!own || !inCockpit || followed == null || followed.disabled || followed.rb == null || current == null || (pilot != null && (pilot.dead || pilot.ejected)))
            {
                if (active) ResetScene();
                return;
            }
            if (aircraft != followed || camera != current) { ResetScene(); aircraft = followed; camera = current; }
            active = true;
            var mission = NetworkSceneSingleton<MissionManager>.i;
            double now = mission != null ? mission.MissionTime : Time.time;
            float dt = double.IsNaN(lastClock) ? 0f : (float)(now - lastClock);
            lastClock = now;
            if (dt < 0f || dt > .25f) { dt = 0f; airframe?.Silence(); }
            dt = Mathf.Min(dt, .05f);
            var cues = physiology.Step(vitals.Read(aircraft), dt);
            if (dt > 0f) vitals.ConsumeHits();
            if (creaks.Value)
            {
                if (airframe == null) { airframe = gameObject.AddComponent<AirframeAudio>(); airframe.Initialize(); }
                if (foley == null) { foley = new CockpitFoley(); foley.Initialize(gameObject); }
                airframe.TryRoute(); airframe.Tick(forces.ForceG, dt, structuralCreaks.Value, .55f, true);
                foley.SetFeatures(harnessStrain.Value, impactFoley.Value);
                foley.Tick(physiology.LoadG, dt, true);
            }
            else { airframe?.Silence(); foley?.Reset(); }
            if (wind.Value)
            {
                if (canopy == null) { canopy = gameObject.AddComponent<CanopyWindAudio>(); canopy.Initialize(); }
                canopy.TryRoute();
                var localAir = aircraft.rb.transform.InverseTransformDirection(aircraft.rb.velocity);
                float speed = aircraft.speed;
                canopy.Tick(speed, 0f, true, true, dt, 0f, aircraft.transform.GlobalPosition().y, Mathf.Clamp(localAir.x / Mathf.Max(40f, speed) * 3f, -1f, 1f));
            }
            else canopy?.Silence();
            if (breathing.Value)
            {
                if (breath == null) { breath = gameObject.AddComponent<PilotBreathingAudio>(); breath.Initialize(); }
                breath.SetFeatures(breathCycles.Value, gStrain.Value, gSuit.Value, heartbeat.Value, recoveryGasps.Value);
                breath.TryRoute(); breath.Tick(cues, physiology, true);
            }
            else breath?.Silence();
            if (controls.Value)
            {
                if (controlsAudio == null) controlsAudio = new ControlFoley(gameObject);
                controlsAudio.SetFeatures(gearLever.Value, canopyClosing.Value);
                controlsAudio.Tick(aircraft);
            }
            else controlsAudio?.Disable(aircraft);
        }
        private void ResetScene()
        {
            active = false; aircraft = null; camera = null; lastClock = double.NaN;
            airframe?.Silence(); canopy?.Silence(); breath?.Silence(true); foley?.Reset(); controlsAudio?.Reset();
            physiology.Reset(); vitals.Reset(); forces.Reset();
        }
        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= SceneUnloaded;
            ResetScene(); harmony?.UnpatchSelf();
            airframe?.Release(); canopy?.Release(); breath?.Release(); foley?.Release(); controlsAudio?.Release();
            if (Live == this) Live = null;
        }
    }
}
