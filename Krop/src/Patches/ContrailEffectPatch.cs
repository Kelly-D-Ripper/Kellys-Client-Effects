using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace KropClientEffects.Patches
{
    [HarmonyPatch(typeof(Aircraft), "FixedUpdate")]
    internal static class ContrailEffectPatch
    {
        internal static float MinAltitudeMeters => ContrailConfig.MinAltitude.Value;
        internal static float MaxAltitudeMeters => Mathf.Max(MinAltitudeMeters + 1f, ContrailConfig.MaxAltitude.Value);
        private const float FastSpeedThresholdKmh = 600f;
        private const float FastSpeedThresholdMetersPerSecond = FastSpeedThresholdKmh / 3.6f;
        private static float SegmentLengthFastMeters => ContrailConfig.Spacing.Value;
        private static float SegmentLengthSlowMeters => ContrailConfig.Spacing.Value * 0.5f;
        private static float LifetimeSeconds => ContrailConfig.Lifetime.Value; // 3 minutes
        private static float FadeStartSeconds => LifetimeSeconds * (5f / 6f); // 2.5 minutes
        private static float WidenDurationSeconds => Mathf.Min(LifetimeSeconds, ContrailConfig.Widen.Value);
        private const int MaxParticles = 1000;
        private static float WindSettleTimeConstantSeconds => ContrailConfig.WindSettle.Value;
        private const float WindUpdateIntervalSeconds = 0.1f;
        private const float MinRunningRpmRatio = 0.2f;

        private static readonly AccessTools.FieldRef<JetNozzle, Transform> JetThrustTransformRef =
            AccessTools.FieldRefAccess<JetNozzle, Transform>("thrustTransform");
        private static readonly AccessTools.FieldRef<JetNozzle, float> JetRpmRatioRef =
            AccessTools.FieldRefAccess<JetNozzle, float>("rpmRatio");
        private static readonly AccessTools.FieldRef<PropFan, Transform> PropFanThrustTransformRef =
            AccessTools.FieldRefAccess<PropFan, Transform>("thrustTransform");
        private static readonly AccessTools.FieldRef<PropFan, float> PropFanRpmRatioRef =
            AccessTools.FieldRefAccess<PropFan, float>("rpmRatio");
        private static readonly AccessTools.FieldRef<ConstantSpeedProp, GameObject> ConstantSpeedPropHubRef =
            AccessTools.FieldRefAccess<ConstantSpeedProp, GameObject>("hubVisible");
        private static readonly AccessTools.FieldRef<ConstantSpeedProp, float> ConstantSpeedPropRpmRatioRef =
            AccessTools.FieldRefAccess<ConstantSpeedProp, float>("rpmRatio");
        private class EngineSource
        {
            public Component EngineComponent;
            public Transform PositionTransform;
            public System.Func<float> GetRpmRatio;
        }

        private class AircraftState
        {
            public bool EnginesFound;
            public EngineSource[] Engines;
            public float NextLogTime;
        }
        internal class EngineState
        {
            public bool IsMissile;
            public GameObject EffectObject;
            public ParticleSystem System;
            public float EmitCounter;
            public bool WasEmitting;
            public float TimeSinceWindUpdate;
            public float LastEmitTime;
            public float NextTuningTime;
            public bool HasTuning;
            public float TunedStartWidth, TunedEndWidth, TunedLifetime, TunedWiden;
        }

        private static ConditionalWeakTable<Aircraft, AircraftState> _aircraftStates =
            new ConditionalWeakTable<Aircraft, AircraftState>();
        private static ConditionalWeakTable<Component, EngineState> _engineStates =
            new ConditionalWeakTable<Component, EngineState>();
        private static readonly ParticleSystem.Particle[] _particleBuffer = new ParticleSystem.Particle[MaxParticles];

        private static void Postfix(Aircraft __instance)
        {
            if (!Settings.Active || !ContrailConfig.Enabled.Value || GameManager.IsHeadless || __instance == null || __instance.rb == null)
            {
                return;
            }
            // Owner cleanup removes trails as soon as the unit is disabled. Wrecks can
            // still receive FixedUpdate, so do not recreate trails on the next tick.
            if (__instance.disabled) return;

            AircraftState aircraftState = _aircraftStates.GetValue(__instance, _ => new AircraftState());
            if (!aircraftState.EnginesFound)
            {
                var engines = new System.Collections.Generic.List<EngineSource>();
                foreach (UnitPart part in __instance.partLookup)
                {
                    if (part == null)
                    {
                        continue;
                    }

                    JetNozzle jetNozzle = part.GetComponentInChildren<JetNozzle>(true);
                    if (jetNozzle != null)
                    {
                        Transform thrustTransform = JetThrustTransformRef(jetNozzle);
                        if (thrustTransform != null)
                        {
                            engines.Add(new EngineSource
                            {
                                EngineComponent = jetNozzle,
                                PositionTransform = thrustTransform,
                                GetRpmRatio = () => JetRpmRatioRef(jetNozzle),
                            });
                        }
                        continue;
                    }

                    PropFan propFan = part.GetComponentInChildren<PropFan>(true);
                    if (propFan != null)
                    {
                        Transform thrustTransform = PropFanThrustTransformRef(propFan);
                        if (thrustTransform != null)
                        {
                            engines.Add(new EngineSource
                            {
                                EngineComponent = propFan,
                                PositionTransform = thrustTransform,
                                GetRpmRatio = () => PropFanRpmRatioRef(propFan),
                            });
                        }
                        continue;
                    }

                    ConstantSpeedProp constantSpeedProp = part.GetComponentInChildren<ConstantSpeedProp>(true);
                    if (constantSpeedProp != null)
                    {
                        GameObject hub = ConstantSpeedPropHubRef(constantSpeedProp);
                        if (hub != null)
                        {
                            engines.Add(new EngineSource
                            {
                                EngineComponent = constantSpeedProp,
                                PositionTransform = hub.transform,
                                GetRpmRatio = () => ConstantSpeedPropRpmRatioRef(constantSpeedProp),
                            });
                        }
                        continue;
                    }
                }
                aircraftState.Engines = engines.ToArray();
                aircraftState.EnginesFound = true;
                if (VerboseLoggingConfig.Enabled.Value)
                {
                    SoundPropagation.Log.LogInfo(
                        $"[ContrailDiag] '{__instance.name}' found {aircraftState.Engines.Length} engine(s) "
                        + $"across {__instance.partLookup.Count} part(s).");
                }
            }
            if (aircraftState.Engines == null || aircraftState.Engines.Length == 0)
            {
                return;
            }

            float altitude = __instance.transform.position.y - Datum.LocalSeaY;
            bool shouldEmit = altitude >= MinAltitudeMeters && altitude <= MaxAltitudeMeters;
            Vector3 velocity = __instance.rb.velocity;

            bool logNow = VerboseLoggingConfig.Enabled.Value && Time.timeSinceLevelLoad >= aircraftState.NextLogTime;
            if (logNow)
            {
                aircraftState.NextLogTime = Time.timeSinceLevelLoad + 1f;
                SoundPropagation.Log.LogInfo(
                    $"[ContrailDiag] '{__instance.name}' altitude={altitude:F0}m (range={MinAltitudeMeters:F0}-{MaxAltitudeMeters:F0}) "
                    + $"shouldEmit={shouldEmit} engines={aircraftState.Engines.Length}");
            }

            foreach (EngineSource engine in aircraftState.Engines)
            {
                if (engine.EngineComponent == null || engine.PositionTransform == null)
                {
                    continue;
                }
                float rpmRatio = engine.GetRpmRatio();
                EngineState state = _engineStates.GetValue(engine.EngineComponent, _ => new EngineState());

                if (logNow)
                {
                    SoundPropagation.Log.LogInfo(
                        $"[ContrailDiag] '{__instance.name}' engine rpmRatio={rpmRatio:F2} "
                        + $"(threshold={MinRunningRpmRatio:F2}) effectCreated={state.EffectObject != null} "
                        + (state.System != null
                            ? $"particleCount={state.System.particleCount} isPlaying={state.System.isPlaying}"
                            : "system=null"));
                }

                if (!shouldEmit || rpmRatio < MinRunningRpmRatio)
                {
                    StopEmitting(state);
                    continue;
                }

                DriveContrail(state, engine.PositionTransform.position, velocity);
            }
        }
        internal static void DriveContrail(EngineState state, Vector3 position, Vector3 velocity)
        {
            if (state.EffectObject == null)
            {
                CreateContrail(state);
            }
            if (state.System == null)
            {
                return;
            }

            if (!state.WasEmitting)
            {
                state.System.Clear();
                state.EmitCounter = 0f;
            }
            state.WasEmitting = true;
            state.LastEmitTime = Time.time;

            ApplyWind(state, position);
            if (Time.time >= state.NextTuningTime) { ApplyTuningIfChanged(state); state.NextTuningTime = Time.time + 0.5f; }
            state.EffectObject.transform.position = position;

            float segmentLength = velocity.magnitude >= FastSpeedThresholdMetersPerSecond
                ? SegmentLengthFastMeters
                : SegmentLengthSlowMeters;
            state.EmitCounter += Time.fixedDeltaTime * velocity.magnitude / segmentLength;
            if (state.EmitCounter < 1f)
            {
                return;
            }
            state.EmitCounter = 0f;

            GlobalPosition emitGlobalPosition = position.ToGlobalPosition();

            ParticleSystem.EmitParams emitParams = default;
            emitParams.position = emitGlobalPosition.AsVector3();
            emitParams.velocity = LevelInfo.i != null ? LevelInfo.i.GetWind(emitGlobalPosition) : Vector3.zero;
            emitParams.startLifetime = LifetimeSeconds;
            state.System.Emit(emitParams, 1);
        }
        internal static void StopEmitting(EngineState state)
        {
            state.WasEmitting = false;
        }
        internal static void ApplyWind(EngineState state, Vector3 samplePosition)
        {
            state.TimeSinceWindUpdate += Time.fixedDeltaTime;
            if (state.TimeSinceWindUpdate < WindUpdateIntervalSeconds)
            {
                return;
            }
            float elapsed = state.TimeSinceWindUpdate;
            state.TimeSinceWindUpdate = 0f;
            Vector3 wind = LevelInfo.i != null
                ? LevelInfo.i.GetWind(samplePosition.ToGlobalPosition())
                : Vector3.zero;

            int count = state.System.GetParticles(_particleBuffer);
            if (count == 0)
            {
                return;
            }
            float t = 1f - Mathf.Exp(-elapsed / WindSettleTimeConstantSeconds);
            for (int i = 0; i < count; i++)
            {
                _particleBuffer[i].velocity = Vector3.Lerp(_particleBuffer[i].velocity, wind, t);
            }
            state.System.SetParticles(_particleBuffer, count);
        }
        internal static bool ApplyTuningIfChanged(EngineState state)
        {
            float start=ContrailConfig.StartThickness, end=ContrailConfig.EndThickness, lifetime=LifetimeSeconds, widen=WidenDurationSeconds;
            if(state.HasTuning && state.TunedStartWidth==start && state.TunedEndWidth==end && state.TunedLifetime==lifetime && state.TunedWiden==widen)return false;
            ApplyTuning(state.System);
            state.TunedStartWidth=start;state.TunedEndWidth=end;state.TunedLifetime=lifetime;state.TunedWiden=widen;state.HasTuning=true;
            return true;
        }
        internal static void ApplyTuning(ParticleSystem system)
        {
            float startThickness = ContrailConfig.StartThickness;
            float endThickness = ContrailConfig.EndThickness;

            ParticleSystem.MainModule main = system.main;
            main.startSize = new ParticleSystem.MinMaxCurve(startThickness);

            float widenFraction = WidenDurationSeconds / LifetimeSeconds;
            float endMultiplier = endThickness / startThickness;
            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(widenFraction, endMultiplier),
                new Keyframe(1f, endMultiplier)));
            float fadeStartFraction = FadeStartSeconds / LifetimeSeconds;
            const float fadeInFraction = 0.01f;
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient fadeGradient = new Gradient();
            fadeGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, fadeInFraction),
                    new GradientAlphaKey(1f, fadeStartFraction),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = fadeGradient;
        }

        internal static void ClearAll()
        {
            _aircraftStates = new ConditionalWeakTable<Aircraft, AircraftState>();
            _engineStates = new ConditionalWeakTable<Component, EngineState>();
            MissileContrailPatch.ClearAll();
        }

        private static void CreateContrail(EngineState state)
        {
            Material material = EffectTextures.GetContrailMaterial();
            if (material == null)
            {
                return;
            }
            GameObject effectObject = VisualObjects.Create("Contrail", state.IsMissile ? "trail-missile" : "trail-aircraft", ContrailConfig.MaxEmitters.Value);
            if (effectObject == null) return;
            effectObject.transform.SetParent(Datum.origin, worldPositionStays: false);

            ParticleSystem system = effectObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.duration = 30f;
            main.startLifetime = LifetimeSeconds;
            main.startSize = new ParticleSystem.MinMaxCurve(ContrailConfig.StartThickness);
            main.startColor = Color.white;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.maxParticles = MaxParticles;
            main.playOnAwake = false;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.Ribbon;
            trails.ratio = 1f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(1f);
            trails.minVertexDistance = 0.2f;
            trails.textureMode = ParticleSystemTrailTextureMode.RepeatPerSegment;
            trails.worldSpace = false;
            trails.dieWithParticles = true;
            trails.ribbonCount = 1;
            trails.generateLightingData = true;
            trails.inheritParticleColor = true;
            trails.sizeAffectsWidth = true;
            trails.sizeAffectsLifetime = false;
            trails.shadowBias = 0.5f;
            ParticleSystemRenderer renderer = effectObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.None;
            renderer.material = material;
            renderer.trailMaterial = material;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            state.EffectObject = effectObject;
            state.System = system;
            state.WasEmitting = false;
            state.NextTuningTime = 0f;
            state.HasTuning = false;
            effectObject.AddComponent<ContrailAutoCleanup>().State = state;
            system.Play();
        }
    }
    internal sealed class ContrailAutoCleanup : MonoBehaviour
    {
        private const float CheckIntervalSeconds = 5f;
        private const float IdleSecondsBeforeCleanup = 5f;

        internal ContrailEffectPatch.EngineState State;
        private float _nextCheckTime;

        private void FixedUpdate()
        {
            if (State != null && State.System != null && Time.time - State.LastEmitTime > 0.1f)
                ContrailEffectPatch.ApplyWind(State, transform.position);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheckTime)
            {
                return;
            }
            _nextCheckTime = Time.unscaledTime + CheckIntervalSeconds;

            if (State == null || State.System == null)
            {
                Destroy(gameObject);
                return;
            }
            if (Time.time - State.LastEmitTime > IdleSecondsBeforeCleanup && State.System.particleCount == 0)
            {
                State.EffectObject = null;
                State.System = null;
                State.WasEmitting = false;
                Destroy(gameObject);
            }
        }
    }
}


