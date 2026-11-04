using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using UnityEngine.SceneManagement;
using BoscaliSummer.Modules.Immersion.Visuals;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    [DefaultExecutionOrder(200)] // Native view/controls update at order 2; Weather finishes at 100.
    internal sealed class CockpitPilot : MonoBehaviour
    {
        private static readonly FieldInfo PilotRenderer = AccessTools.Field(typeof(Pilot), "skinnedMeshRenderer");
        private static readonly FieldInfo PilotAnimator = AccessTools.Field(typeof(Pilot), "animator");
        private static readonly FieldInfo Joysticks = AccessTools.Field(typeof(Cockpit), "joysticks");
        private static readonly FieldInfo Throttles = AccessTools.Field(typeof(Cockpit), "throttles");
        private static readonly Type JoystickType = AccessTools.Inner(typeof(Cockpit), "Joystick");
        private static readonly Type ThrottleType = AccessTools.Inner(typeof(Cockpit), "Throttle");
        private static readonly FieldInfo StickTransform = AccessTools.Field(JoystickType, "transform");
        private static readonly FieldInfo ThrottleTransform = AccessTools.Field(ThrottleType, "transform");
        private static readonly FieldInfo StickRange = AccessTools.Field(JoystickType, "range");
        private static readonly FieldInfo ThrottleRange = AccessTools.Field(ThrottleType, "range");
        private static readonly FieldInfo ThrottleRotation = AccessTools.Field(ThrottleType, "rotation");
        private static readonly FieldInfo ThrottleMotion = AccessTools.Field(ThrottleType, "motion");
        private readonly CockpitPilotRig rig = new CockpitPilotRig();
        private readonly Kneeboard kneeboard = new Kneeboard();
        private ConfigEntry<bool> bodyEnabled, controlMotionEnabled, kneeboardEnabled;
        private ManualLogSource logger;
        private Aircraft aircraft;
        private Pilot pilot;
        private Camera camera;
        private Cockpit controls;
        private bool controlsBound, stickExpected, throttleExpected;
        private int bindFrame;
        private float retryAt, controlsRetryAt;
        private Rigidbody trackedBody;
        private Vector3 lastVelocity, lastPosition, forceG = Vector3.up;
        private bool forcePrimed;
        private delegate bool PilotMotionReader(out Aircraft aircraft, out Vector3 force, out float chestLift, out float exertion);
        private PilotMotionReader motionReader;
        private float bridgeRetryAt;
        private int warnedAircraft;
        private int reportedControls = -1;

        internal void Configure(ConfigFile config, ManualLogSource log)
        {
            bodyEnabled = config.Bind("Immersion", "PilotBodyEnabled", true,
                "Shows the local first-person pilot body in cockpit view. No pilot reflections are rendered.");
            controlMotionEnabled = config.Bind("Immersion", "PilotControlMotionEnabled", true,
                "Animate the local pilot's arms and feet with cockpit controls when the pilot body is visible.");
            kneeboardEnabled = config.Bind("Immersion", "KneeboardEnabled", true,
                "Show the local pilot's mission-card kneeboard when the pilot body is enabled.");
            logger = log;
            SceneManager.sceneUnloaded += SceneUnloaded;
        }

        private void SceneUnloaded(Scene scene) => ResetForScene();

        private void FixedUpdate()
        {
            var rb = aircraft != null && controlMotionEnabled != null && controlMotionEnabled.Value ? aircraft.CockpitRB() : null;
            float dt = Time.fixedDeltaTime;
            if (rb == null || dt <= 0f) { trackedBody = null; forcePrimed = false; forceG = Vector3.up; return; }
            Vector3 velocity = rb.velocity;
            GlobalPosition global = rb.transform.GlobalPosition();
            Vector3 position = new Vector3(global.x, global.y, global.z);
            if (rb != trackedBody || !forcePrimed)
            {
                trackedBody = rb; forcePrimed = true; forceG = Vector3.up;
                lastVelocity = velocity; lastPosition = position; return;
            }
            bool relocated = (position - (lastPosition + lastVelocity * dt)).sqrMagnitude > 250f * 250f;
            Vector3 specific = rb.transform.InverseTransformDirection((velocity - lastVelocity) / dt - Physics.gravity) / 9.81f;
            lastVelocity = velocity; lastPosition = position;
            if (relocated || specific.sqrMagnitude > 10000f || !BoscaliSummer.Core.Math.Scalar.IsFinite(specific.sqrMagnitude)) { forceG = Vector3.up; return; }
            forceG = Vector3.Lerp(forceG, Vector3.ClampMagnitude(specific, 15f), Mathf.Clamp01(dt / .08f));
        }

        private void LateUpdate()
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Aircraft followed = cameras != null ? cameras.followingUnit as Aircraft : null;
            Camera renderCamera = cameras != null ? cameras.cockpitCamRender : null;
            Pilot followedPilot = followed != null && followed.pilots != null && followed.pilots.Length > 0 ? followed.pilots[0] : null;
            if (Application.isBatchMode || bodyEnabled == null || !bodyEnabled.Value || cameras == null ||
                CameraStateManager.cameraMode != CameraMode.cockpit || cameras.currentState != cameras.cockpitState ||
                !GameManager.GetLocalAircraft(out Aircraft ownAircraft) || followed != ownAircraft ||
                followed == null || followed.disabled || followed.cockpit == null || followedPilot == null ||
                followedPilot.dead || followedPilot.ejected || renderCamera == null || !renderCamera.enabled || cameras.mainCamera == null)
            { if (aircraft != null) ResetForScene(); return; }
            if (followed != aircraft || followedPilot != pilot || renderCamera != camera)
            {
                ResetForScene(); aircraft = followed; pilot = followedPilot; camera = renderCamera;
            }
            if (!rig.Valid)
            {
                rig.Release();
                if (Time.unscaledTime < retryAt) return;
                retryAt = Time.unscaledTime + 2f;
                if (!Bind()) return;
            }
            {
                rig.CopySeatedPose();
                if ((!controlsBound || controls == null || (stickExpected && !rig.StickBound) || (throttleExpected && !rig.ThrottleBound)) &&
                    Time.frameCount > bindFrame + 1 && Time.unscaledTime >= controlsRetryAt)
                {
                    // Native controls can appear after the pilot. Retry binding once a second,
                    // then retain only cached transforms for the per-frame hand constraint.
                    controlsRetryAt = Time.unscaledTime + 1f;
                    BindControls();
                }
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                float ambient = level != null ? level.GetAmbientLight() : 1f;
                float light = Mathf.Clamp01(ambient * .8f + .18f);
                rig.SetLight(light);
                rig.SetBodyVisible(true);
                if (kneeboard.Bound && (!kneeboardEnabled.Value || kneeboard.RightThigh == rig.StickOnRight))
                { rig.SetCompanion(null); kneeboard.Detach(); }
                if (kneeboardEnabled.Value && !kneeboard.Bound && (controlsBound || Time.frameCount > bindFrame + 30) && kneeboard.TryBeginBind())
                {
                    bool rightThigh = !rig.StickOnRight;
                    kneeboard.Bind(rig.Bone(rightThigh ? "thigh_R" : "thigh_L"), rig.Bone(rightThigh ? "shin_R" : "shin_L"),
                        aircraft.cockpit.transform.up, rightThigh, aircraft, PilotShaderBundle.GetBody());
                    rig.SetCompanion(kneeboard.Renderer);
                }
                kneeboard.SetLight(Mathf.Clamp(light, .06f, 1f));
                kneeboard.Tick();
                Quaternion view = cameras.mainCamera.transform.rotation;
                Quaternion lookLocal = Quaternion.Inverse(aircraft.cockpit.transform.rotation) * view;
                ControlInputs inputs = aircraft.GetInputs();
                Vector3 poseForce = forceG;
                float chestLift = .5f;
                if (motionReader == null && Time.unscaledTime >= bridgeRetryAt)
                {
                    bridgeRetryAt = Time.unscaledTime + 5f;
                    if (Chainloader.PluginInfos.TryGetValue("aaron.boscali.cockpit-audio", out var audio))
                    {
                        var method = audio.Instance.GetType().Assembly.GetType("BoscaliCockpitAudio.PilotMotionBridge")?.GetMethod("TryGet");
                        if (method != null) motionReader = Delegate.CreateDelegate(typeof(PilotMotionReader), method, false) as PilotMotionReader;
                    }
                }
                if (motionReader != null && motionReader(out Aircraft physiologyAircraft, out Vector3 liveForce, out float lift, out _) && physiologyAircraft == aircraft)
                { poseForce = liveForce; chestLift = lift; }
                rig.Pose(inputs != null ? inputs.yaw : 0f, poseForce, chestLift, lookLocal, Time.deltaTime,
                    controlMotionEnabled.Value, false);
            }
        }

        private bool Bind()
        {
            if (PilotRenderer == null || PilotAnimator == null) { WarnUnavailable(); return false; }
            var source = PilotRenderer.GetValue(pilot) as SkinnedMeshRenderer;
            var animator = PilotAnimator.GetValue(pilot) as Animator;
            if (!rig.Bind(source, animator, PilotShaderBundle.GetBody(), camera)) { WarnUnavailable(); return false; }
            rig.SetFrame(aircraft.cockpit.transform);
            bindFrame = Time.frameCount;
            controlsBound = false;
            controlsRetryAt = 0f;
            reportedControls = -1;
            return true;
        }

        private void WarnUnavailable()
        {
            int id = aircraft != null ? aircraft.GetInstanceID() : 0;
            if (warnedAircraft == id) return;
            warnedAircraft = id;
            logger?.LogWarning("[Immersion] Cockpit pilot skipped: unavailable native rig or pilot shader bundle.");
        }

        private void BindControls()
        {
            // Aircraft.cockpit is the damage/physics UnitPart (usually AeroPart),
            // not the Cockpit behaviour that owns and animates the control arrays.
            // Native layouts put that behaviour on this part or a child cockpit_int.
            if (controls == null)
            {
                controls = aircraft.cockpit.GetComponentInChildren<Cockpit>(true);
                rig.SetControls(null, null);
            }
            Array sticks = controls != null ? Joysticks?.GetValue(controls) as Array : null;
            Array throttles = controls != null ? Throttles?.GetValue(controls) as Array : null;
            stickExpected = sticks != null && sticks.Length > 0;
            throttleExpected = throttles != null && throttles.Length > 0;
            rig.SetControlHandedness(!ThrottleOnRight(throttles));
            bool stickRight = rig.StickOnRight;
            Vector3 stickGrip = Vector3.zero, throttleGrip = Vector3.zero;
            Quaternion stickRotation = Quaternion.identity, throttleRotation = Quaternion.identity;
            Transform stick = rig.StickBound ? null : ChooseGrip(sticks, StickTransform, rig.StickHand,
                rig.Bone(stickRight ? "upperarm_R" : "upperarm_L"), stickRight, true, out stickGrip, out stickRotation);
            Transform throttle = rig.ThrottleBound ? null : ChooseGrip(throttles, ThrottleTransform, rig.ThrottleHand,
                rig.Bone(stickRight ? "upperarm_L" : "upperarm_R"), !stickRight, false, out throttleGrip, out throttleRotation);
            rig.AttachControls(stick, stickGrip, stickRotation, throttle, throttleGrip, throttleRotation);
            if (stick != null) FitControlReach(sticks, StickTransform, stick, stickGrip, rig.Bone(stickRight ? "upperarm_R" : "upperarm_L"), true);
            if (throttle != null) FitControlReach(throttles, ThrottleTransform, throttle, throttleGrip, rig.Bone(stickRight ? "upperarm_L" : "upperarm_R"), false);
            if (stick != null || throttle != null) rig.CopySeatedPose();
            controlsBound = sticks != null && throttles != null && (stickExpected || throttleExpected) &&
                (!stickExpected || rig.StickBound) && (!throttleExpected || rig.ThrottleBound);
            int status = (controls != null ? 1 : 0) | (stickExpected ? 2 : 0) | (throttleExpected ? 4 : 0) | (!stickRight ? 32 : 0) |
                (rig.StickBound ? 8 : 0) | (rig.ThrottleBound ? 16 : 0);
            if (status != reportedControls)
            {
                reportedControls = status;
                logger?.LogInfo($"[Immersion] Cockpit pilot controls ({aircraft.name}): component={controls != null}, " +
                    $"stick={rig.StickBound}/{stickExpected} ({(stickRight ? "right" : "left")}), throttle={rig.ThrottleBound}/{throttleExpected}.");
            }
        }

        private static Transform ChooseGrip(Array controls, FieldInfo transformField, Transform hand, Transform shoulder,
            bool rightHand, bool joystick, out Vector3 localGrip, out Quaternion localRotation)
        {
            localGrip = Vector3.zero;
            localRotation = Quaternion.identity;
            if (controls == null || transformField == null || hand == null || shoulder == null) return null;
            Transform chosen = null;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < controls.Length && i < 8; i++)
            {
                object control = controls.GetValue(i);
                Transform lever = control != null ? transformField.GetValue(control) as Transform : null;
                if (lever == null) continue;
                if (!CockpitPilotRig.TryFindGrip(lever, hand, shoulder, rightHand, joystick, out Vector3 grip, out Quaternion rotation, out float distance) || distance >= nearest) continue;
                chosen = lever; localGrip = grip; localRotation = rotation; nearest = distance;
            }
            return chosen;
        }

        private void FitControlReach(Array array, FieldInfo transformField, Transform lever, Vector3 grip, Transform shoulder, bool joystick)
        {
            if (array == null || shoulder == null) return;
            for (int i = 0; i < array.Length && i < 8; i++)
            {
                object control = array.GetValue(i);
                if (control == null || transformField.GetValue(control) as Transform != lever) continue;
                object rangeValue = (joystick ? StickRange : ThrottleRange)?.GetValue(control);
                if (!(rangeValue is float range) || !BoscaliSummer.Core.Math.Scalar.IsFinite(range)) return;
                bool rotation = joystick || ThrottleRotation?.GetValue(control) is bool rotates && rotates;
                bool motion = !joystick && ThrottleMotion?.GetValue(control) is bool moves && moves;
                float maximum = 0f;
                int cases = joystick ? 9 : 2;
                for (int sample = 0; sample < cases; sample++)
                {
                    Quaternion turn = rotation ? (joystick ? Quaternion.Euler((sample / 3 - 1) * range, 0, -(sample % 3 - 1) * range)
                        : Quaternion.Euler(sample * range, 0, 0)) : lever.localRotation;
                    Vector3 position = motion ? new Vector3(0, 0, sample * range) : lever.localPosition;
                    Vector3 point = position + turn * Vector3.Scale(grip, lever.localScale);
                    Vector3 world = lever.parent != null ? lever.parent.TransformPoint(point) : point;
                    maximum = Mathf.Max(maximum, Vector3.Distance(shoulder.position, world));
                }
                rig.FitControlReach(joystick, maximum);
                return;
            }
        }

        private bool ThrottleOnRight(Array throttles)
        {
            if (throttles == null || ThrottleTransform == null || rig.Chest == null || rig.Frame == null) return false;
            float nearest = float.PositiveInfinity, side = 0f;
            for (int i = 0; i < throttles.Length && i < 8; i++)
            {
                object control = throttles.GetValue(i);
                Transform lever = control != null ? ThrottleTransform.GetValue(control) as Transform : null;
                if (lever == null) continue;
                MeshFilter[] meshes = lever.GetComponentsInChildren<MeshFilter>(true);
                for (int m = 0; m < meshes.Length && m < 16; m++)
                {
                    MeshFilter mesh = meshes[m];
                    if (mesh == null || mesh.sharedMesh == null) continue;
                    Vector3 offset = mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center) - rig.Chest.position;
                    float distance = offset.sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance; side = Vector3.Dot(offset, rig.Frame.right);
                }
            }
            // Shared centre consoles sit to the captain's right; side-stick jets and
            // helicopters retain their native left throttle/collective assignment.
            return side > .1f;
        }

        public void ResetForScene()
        {
            rig.SetCompanion(null); kneeboard.Release(); rig.Release(); aircraft = null; pilot = null; camera = null; controls = null;
            controlsBound = stickExpected = throttleExpected = false;
            trackedBody = null; forcePrimed = false; forceG = Vector3.up;
            retryAt = controlsRetryAt = 0; warnedAircraft = 0; reportedControls = -1;
        }
        private void OnDisable() => ResetForScene();
        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= SceneUnloaded;
            ResetForScene();
        }
    }
}
