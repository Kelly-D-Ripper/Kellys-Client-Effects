using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using BoscaliSummer.Modules.Immersion.Runtime;
using BoscaliSummer.Modules.Immersion.Domain;
using BoscaliSummer.Modules.Immersion.Visuals;

namespace BoscaliSummer
{
    // Logging seam for the original MFD/damage implementations.
    internal static class Plugin { internal static BepInEx.Logging.ManualLogSource Logger; }
}

namespace BoscaliSummer.Core.Game
{
    internal static class GameAccess
    {
        private static readonly System.Reflection.FieldInfo CockpitRenderers = AccessTools.Field(typeof(Aircraft), "cockpitRenderers");
        internal static Renderer[] GetCockpitRenderers(Aircraft aircraft) => aircraft == null ? null : CockpitRenderers?.GetValue(aircraft) as Renderer[];
    }
}

namespace BoscaliCockpitFeel
{
    [BepInPlugin(Id, "Unified Cockpit Feel", "0.6.0")]
    [BepInIncompatibility("com.marci.boscalisummer")]
    public sealed class CockpitFeelPlugin : BaseUnityPlugin
    {
        public const string Id = "aaron.boscali.cockpitfeel";
        private void Awake()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || GameManager.IsHeadless) return;
            BoscaliSummer.Plugin.Logger = Logger;
            var root = new GameObject("Boscali cockpit feel") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(root);
            root.AddComponent<FeelRuntime>().Configure(Config, new Harmony(Id));
        }
    }

    internal sealed class FeelRuntime : MonoBehaviour
    {
        internal static FeelRuntime Live;
        private ConfigEntry<bool> headOn, shakeOn, machOn, comfort, glowOn, floodOn, damageOn;
        private ConfigEntry<bool> groundRollOn, recoilOn, touchdownOn, gearOn;
        private ConfigEntry<bool> transonicOn, sonicJoltOn;
        private ConfigEntry<float> headStrength, shakeStrength, groundRollStrength;
        private readonly HeadMotion head = new HeadMotion();
        private readonly CockpitShake shake = new CockpitShake();
        private readonly CockpitCameraOffset offset = new CockpitCameraOffset();
        private readonly MfdGlow glow = new MfdGlow();
        private readonly CockpitLights flood = new CockpitLights();
        private readonly SurfaceImmersion damage = new SurfaceImmersion();
        private Harmony harmony;
        private Aircraft aircraft;
        private Camera view;
        private bool presenting;
        private Quaternion rotation = Quaternion.identity;

        internal void Configure(ConfigFile config, Harmony owner)
        {
            const string section = "Cockpit Feel";
            headOn = config.Bind(section, "Head inertia", true, "Boscali rotational head inertia in cockpit view.");
            headStrength = config.Bind(section, "Head inertia strength", 1f, new ConfigDescription("Head movement strength.", new AcceptableValueRange<float>(0f, 2f)));
            shakeOn = config.Bind(section, "Cockpit shake", true, "Cockpit recoil, touchdown, landing gear and ground vibration.");
            shakeStrength = config.Bind(section, "Cockpit shake strength", 1f, new ConfigDescription("Shake strength.", new AcceptableValueRange<float>(0f, 2f)));
            groundRollOn = config.Bind(section, "Ground roll vibration", true, "Runway vibration during taxi, takeoff and landing roll. Requires Cockpit shake and cockpit view; this is camera motion, not controller force feedback.");
            groundRollStrength = config.Bind(section, "Ground roll vibration strength", 1f, new ConfigDescription("Runway vibration multiplier. Cockpit shake strength and Comfort motion also apply.", new AcceptableValueRange<float>(0f, 3f)));
            recoilOn = config.Bind(section, "Gun recoil", true, "Added cockpit gun recoil. Requires Cockpit shake.");
            touchdownOn = config.Bind(section, "Touchdown jolts", true, "Added cockpit landing impulse. Requires Cockpit shake.");
            gearOn = config.Bind(section, "Landing gear jolts", true, "Small cockpit impulse as the landing gear locks. Requires Cockpit shake.");
            machOn = config.Bind(section, "Mach buffet", true, "Transonic buffeting and crossing impulse; independent of other shake.");
            transonicOn = config.Bind(section, "Transonic vibration", true, "Continuous vibration near the speed of sound. Requires Mach buffet.");
            sonicJoltOn = config.Bind(section, "Sonic crossing jolt", true, "Small rotational impulse when crossing the sound barrier. Requires Mach buffet; no audio delay.");
            comfort = config.Bind(section, "Comfort motion", false, "Reduce added camera motion to one quarter.");
            glowOn = config.Bind(section, "Instrument glow", true, "Night glow on verified cockpit display slots.");
            floodOn = config.Bind(section, "Cockpit night lighting", true, "Warm local cockpit floodlight at night.");
            damageOn = config.Bind(section, "Cockpit damage shading", true, "Subtle darkening on verified opaque slots as aircraft is damaged.");
            harmony = owner;
            Live = this;
            harmony.PatchAll(typeof(FeelRuntime).Assembly);
            SceneManager.sceneUnloaded += SceneUnloaded;
        }

        private void FixedUpdate()
        {
            if (!presenting) return;
            head.Measure(aircraft, Time.fixedDeltaTime);
            if (head.Discontinuity) shake.ResetMeasurements();
        }
        private void Update()
        {
            var cameras = SceneSingleton<CameraStateManager>.i;
            var next = cameras != null ? cameras.followingUnit as Aircraft : null;
            var camera = cameras != null ? cameras.mainCamera : null;
            bool cockpit = cameras != null && CameraStateManager.cameraMode == CameraMode.cockpit && cameras.currentState == cameras.cockpitState;
            Pilot pilot = next != null && next.pilots != null && next.pilots.Length > 0 ? next.pilots[0] : null;
            if (!cockpit || camera == null || next == null || !GameManager.GetLocalAircraft(out Aircraft own) || next != own || next.disabled || next.rb == null || (pilot != null && (pilot.dead || pilot.ejected)))
            { if (presenting) ResetEffects(); return; }
            if (next != aircraft || camera != view) { ResetEffects(); aircraft = next; view = camera; }
            presenting = true;
            float dt = Time.deltaTime > .25f ? 0f : Mathf.Clamp(Time.deltaTime, 0f, .05f);
            head.Step(dt, headStrength.Value, headOn.Value, comfort: comfort.Value);
            float mach = next.speed / ImmersionMath.SpeedOfSound(next.transform.GlobalPosition().y);
            shake.SetFeatures(groundRollOn.Value, recoilOn.Value, touchdownOn.Value, gearOn.Value, groundRollStrength.Value);
            shake.SetMachFeatures(transonicOn.Value, sonicJoltOn.Value);
            shake.Tick(next, true, shakeOn.Value, machOn.Value, shakeStrength.Value, mach, 0f, 0f, dt);
            Vector3 angles = headOn.Value ? head.AnglesDeg : Vector3.zero;
            angles.z = -angles.z;
            angles += shake.AnglesDeg;
            float strength = Mathf.Max(headOn.Value ? headStrength.Value : 0f, shakeOn.Value || machOn.Value ? shakeStrength.Value : 0f);
            var composed = ImmersionMath.ComposeMotion(angles.x, angles.y, angles.z, strength, comfort.Value);
            rotation = Quaternion.Euler(composed.pitch, composed.yaw, composed.roll);
            var level = NetworkSceneSingleton<LevelInfo>.i;
            var sun = level != null ? level.sun : null;
            glow.Tick(next, true, level != null ? level.GetAmbientLight() : 1f, glowOn.Value);
            flood.Tick(next, camera, sun != null ? -sun.transform.forward.y : 1f, floodOn.Value, dt);
            damage.Tick(next, true, damageOn.Value);
        }
        internal void Apply(CameraStateManager cameras)
        {
            offset.Remove();
            if (presenting && cameras != null && cameras.currentState == cameras.cockpitState && cameras.cameraPivot != null)
                offset.Apply(cameras.cameraPivot, rotation);
        }
        internal void Remove() => offset.Remove();
        internal void Shot(Gun gun) { if (presenting && gun != null && ReferenceEquals(gun.attachedUnit, aircraft)) shake.OnShot(gun); }
        private void SceneUnloaded(Scene scene) => ResetEffects();
        private void ResetEffects()
        {
            offset.Remove(); head.Reset(); shake.Release(); glow.Release(); flood.Release(); damage.Reset();
            aircraft = null; view = null; presenting = false; rotation = Quaternion.identity;
        }
        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= SceneUnloaded; ResetEffects(); harmony?.UnpatchSelf();
            if (Live == this) Live = null;
        }
    }
    [HarmonyPatch(typeof(CameraCockpitState), "UpdateState")]
    internal static class HeadPatch { private static void Postfix(CameraStateManager cam) => FeelRuntime.Live?.Apply(cam); }
    [HarmonyPatch(typeof(CameraCockpitState), "LeaveState")]
    internal static class LeavePatch { private static void Prefix() => FeelRuntime.Live?.Remove(); }
    [HarmonyPatch(typeof(Gun), "SpawnBullet")]
    internal static class GunPatch { private static void Postfix(Gun __instance) => FeelRuntime.Live?.Shot(__instance); }
}
