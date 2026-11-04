using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace KropClientEffects.Patches
{
    [HarmonyPatch(typeof(Aircraft), "FixedUpdate")]
    internal static class AirWakeEffectPatch
    {
        private static float MinSpeedMetersPerSecond => AirWakeConfig.Speed.Value / 3.6f; // 400 km/h
        private static float MaxAltitudeMeters => AirWakeConfig.Altitude.Value; // matches the Medusa's own DownwashEffect
        private static float RaycastMaxDistance => MaxAltitudeMeters + 30f;
        private static float BurstSpacingMeters => AirWakeConfig.Spacing.Value;
        private const int PairsPerBurst = 2;
        private const int MaxBurstsPerTick = 4;
        private const float SpawnHeightMeters = 0.5f;
        private const float MinUpKickMetersPerSecond = 1f;
        private const float MaxUpKickMetersPerSecond = 5f;
        private const float InheritedSpeedVariation = 0.15f;
        private const float MinParticleSize = 8f;
        private const float MaxParticleSize = 14f;
        private const float MinSpinFraction = 0.6f;
        private const float MaxSpinFraction = 1.4f;
        private const float ReferenceMassKg = 10000f;
        private const float MassRefreshIntervalSeconds = 1f;
        private const float MinWeightFactor = 0.25f;
        private const float MaxWeightFactor = 4f;
        private const float DampingIntervalSeconds = 0.05f;
        private const int MaxParticlesPerSystem = 800;
        private const float ClockwiseSign = 1f;
        private static readonly Color WaterTint = new Color(0.80f, 0.90f, 0.95f, 1f);
        private const int RightSystem = 0;
        private const int LeftSystem = 1;

        private class AirWakeState
        {
            public bool IsHelicopter;
            public bool EffectAttempted;
            public GameObject[] EffectObjects;
            public ParticleSystem[] Systems;
            public ParticleSystemRenderer[] Renderers;
            public float EmitCounter;
            public bool Emitting;
            public float NextLogTime;
            public float TimeSinceDamp;
            public float Mass = ReferenceMassKg;
            public float NextMassRefreshTime;
        }

        private static ConditionalWeakTable<Aircraft, AirWakeState> _states =
            new ConditionalWeakTable<Aircraft, AirWakeState>();
        private static readonly ParticleSystem.Particle[] _particleBuffer =
            new ParticleSystem.Particle[MaxParticlesPerSystem];

        private static void Postfix(Aircraft __instance)
        {
            if (!Settings.Active || !AirWakeConfig.Enabled.Value || GameManager.IsHeadless || __instance == null || __instance.rb == null)
            {
                return;
            }
            // Owner cleanup removes these particle systems after a unit is disabled.
            // Do not recreate them while its wreck still receives FixedUpdate.
            if (__instance.disabled) return;

            AirWakeState state = _states.GetValue(__instance, _ => new AirWakeState());
            if (state.IsHelicopter)
            {
                return;
            }
            if (state.Systems != null && state.Systems[0] != null && state.Systems[1] != null)
            {
                DampParticles(state);
                ApplySpin(state);
            }

            bool shouldPlay = __instance.speed >= MinSpeedMetersPerSecond && __instance.radarAlt <= MaxAltitudeMeters;
            if (!shouldPlay)
            {
                SetEmitting(state, __instance, false, "speed/altitude out of range");
                return;
            }

            if (!state.EffectAttempted || (state.Systems != null && state.Systems[0] == null))
            {
                CreateEffect(__instance, state);
            }
            if (state.Systems == null || state.Systems[0] == null || state.Systems[1] == null)
            {
                return;
            }

            Vector3 aircraftPos = __instance.transform.position;

            bool hitWater = false;
            bool hitGround = false;
            float groundY = aircraftPos.y;
            float verticalDistance = RaycastMaxDistance;
            if (Physics.Raycast(aircraftPos, Vector3.down, out RaycastHit hitInfo, RaycastMaxDistance, PhysicsLayers.StaticsMask)
                && hitInfo.collider.sharedMaterial == GameAssets.i.terrainMaterial)
            {
                verticalDistance = hitInfo.distance;
                groundY = hitInfo.point.y;
                hitGround = true;
            }
            if (Datum.WaterPlane().Raycast(new Ray(aircraftPos, Vector3.down), out float enter) && enter < verticalDistance)
            {
                verticalDistance = enter;
                groundY = aircraftPos.y - enter;
                hitWater = true;
                hitGround = false;
            }

            if (VerboseLoggingConfig.Enabled.Value && Time.timeSinceLevelLoad >= state.NextLogTime)
            {
                state.NextLogTime = Time.timeSinceLevelLoad + 0.5f;
                SoundPropagation.Log.LogInfo(
                    $"[AirWakeDiag] '{__instance.name}' speed={__instance.speed:F1}m/s radarAlt={__instance.radarAlt:F1}m "
                    + $"hitGround={hitGround} hitWater={hitWater} verticalDistance={verticalDistance:F1}m groundY={groundY:F1} "
                    + $"mass={state.Mass:F0}kg particles={state.Systems[0].particleCount + state.Systems[1].particleCount}");
            }

            if (!hitGround || hitWater || !AirWakeConfig.Dust.Value)
            {
                SetEmitting(state, __instance, false, "no ground/water hit");
                return;
            }
            SetEmitting(state, __instance, true, hitWater ? "water hit" : "ground hit");

            Vector3 surfacePoint = new Vector3(aircraftPos.x, groundY + SpawnHeightMeters, aircraftPos.z);
            for (int i = 0; i < state.EffectObjects.Length; i++)
            {
                state.EffectObjects[i].transform.position = surfacePoint;
            }

            Vector3 velocity = __instance.rb.velocity;
            Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            float groundSpeed = horizontalVelocity.magnitude;
            if (groundSpeed < 1f)
            {
                return;
            }
            state.EmitCounter += Time.fixedDeltaTime * groundSpeed / BurstSpacingMeters;
            float alpha = Mathf.Clamp01(1f - verticalDistance / MaxAltitudeMeters);
            Color tint = hitWater
                ? WaterTint
                : Color.Lerp(Color.white, AirWakeConfig.LandColor, AirWakeConfig.TintStrength);
            Color32 startColor = new Color(tint.r, tint.g, tint.b, alpha);

            Vector3 forward = horizontalVelocity / groundSpeed;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float weightFactor = GetWeightFactor(__instance, state);

            int bursts = 0;
            while (state.EmitCounter >= 1f && bursts < MaxBurstsPerTick)
            {
                state.EmitCounter -= 1f;
                bursts++;
                EmitBurst(state, surfacePoint, forward, right, groundSpeed, weightFactor, startColor);
            }
            if (state.EmitCounter >= 1f)
            {
                state.EmitCounter = 0f;
            }
        }
        private static float GetWeightFactor(Aircraft aircraft, AirWakeState state)
        {
            if (Time.timeSinceLevelLoad >= state.NextMassRefreshTime)
            {
                state.NextMassRefreshTime = Time.timeSinceLevelLoad + MassRefreshIntervalSeconds;
                float mass = aircraft.rb != null ? aircraft.rb.mass : ReferenceMassKg;
                state.Mass = mass > 1f ? mass : ReferenceMassKg;
            }
            float factor = Mathf.Pow(state.Mass / ReferenceMassKg, AirWakeConfig.WeightScaling);
            return Mathf.Clamp(factor, MinWeightFactor, MaxWeightFactor);
        }

        private static void EmitBurst(
            AirWakeState state, Vector3 surfacePoint, Vector3 forward, Vector3 right,
            float groundSpeed, float weightFactor, Color32 startColor)
        {
            Vector3 emitPosition = surfacePoint.ToGlobalPosition().AsVector3();

            float inheritedSpeed = AirWakeConfig.InheritedSpeedPercent * 0.01f * groundSpeed;
            float lateralBase = AirWakeConfig.LateralSpeed * weightFactor;
            float randomness = AirWakeConfig.LateralRandomness;

            for (int pair = 0; pair < PairsPerBurst; pair++)
            {
                for (int side = 0; side < 2; side++)
                {
                    float sideSign = side == 0 ? 1f : -1f;
                    float lateral = lateralBase * EffectRandom.Range(1f - randomness, 1f + randomness);
                    float forwardSpeed = inheritedSpeed
                        * EffectRandom.Range(1f - InheritedSpeedVariation, 1f + InheritedSpeedVariation);

                    ParticleSystem.EmitParams emitParams = default;
                    emitParams.position = emitPosition;
                    emitParams.velocity =
                        forward * forwardSpeed
                        + right * (sideSign * lateral)
                        + Vector3.up * EffectRandom.Range(MinUpKickMetersPerSecond, MaxUpKickMetersPerSecond);
                    emitParams.startLifetime = AirWakeConfig.LifetimeSeconds;
                    emitParams.startSize = EffectRandom.Range(MinParticleSize, MaxParticleSize);
                    emitParams.rotation = EffectRandom.Range(0f, 360f);
                    emitParams.startColor = startColor;
                    state.Systems[side == 0 ? RightSystem : LeftSystem].Emit(emitParams, 1);
                }
            }
            for (int i = 0; i < AirWakeConfig.CenterFillParticles; i++)
            {
                float forwardSpeed = inheritedSpeed
                    * EffectRandom.Range(1f - InheritedSpeedVariation, 1f + InheritedSpeedVariation);

                ParticleSystem.EmitParams emitParams = default;
                emitParams.position = emitPosition;
                emitParams.velocity =
                    forward * forwardSpeed
                    + Vector3.up * EffectRandom.Range(MinUpKickMetersPerSecond, MaxUpKickMetersPerSecond);
                emitParams.startLifetime = AirWakeConfig.LifetimeSeconds;
                emitParams.startSize = EffectRandom.Range(MinParticleSize, MaxParticleSize);
                emitParams.rotation = EffectRandom.Range(0f, 360f);
                emitParams.startColor = startColor;
                state.Systems[EffectRandom.value < 0.5f ? RightSystem : LeftSystem].Emit(emitParams, 1);
            }
        }
        private static void DampParticles(AirWakeState state)
        {
            state.TimeSinceDamp += Time.fixedDeltaTime;
            if (state.TimeSinceDamp < DampingIntervalSeconds)
            {
                return;
            }
            float elapsed = state.TimeSinceDamp;
            state.TimeSinceDamp = 0f;

            float keep = Mathf.Exp(-elapsed / Mathf.Max(0.05f, AirWakeConfig.SettleTimeSeconds));
            for (int s = 0; s < state.Systems.Length; s++)
            {
                ParticleSystem system = state.Systems[s];
                if (system == null || system.particleCount == 0)
                {
                    continue;
                }
                int count = system.GetParticles(_particleBuffer);
                for (int i = 0; i < count; i++)
                {
                    _particleBuffer[i].velocity *= keep;
                }
                system.SetParticles(_particleBuffer, count);
            }
        }
        private static void ApplySpin(AirWakeState state)
        {
            float radPerSec = AirWakeConfig.SpinSpeedDegrees * Mathf.Deg2Rad;
            float clockwise = ClockwiseSign;
            SetSpin(state.Systems[RightSystem], clockwise * radPerSec);
            SetSpin(state.Systems[LeftSystem], -clockwise * radPerSec);
        }
        private static void ApplyRenderSettings(AirWakeState state)
        {
            for (int i = 0; i < state.Renderers.Length; i++)
            {
                if (state.Renderers[i] != null)
                {
                    state.Renderers[i].normalDirection = AirWakeConfig.NormalDirection;
                }
            }
        }

        private static void SetSpin(ParticleSystem system, float signedRadPerSec)
        {
            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(
                signedRadPerSec * MinSpinFraction, signedRadPerSec * MaxSpinFraction);
        }
        private static void SetEmitting(AirWakeState state, Aircraft aircraft, bool emitting, string reason)
        {
            if (state.Emitting == emitting)
            {
                return;
            }
            state.Emitting = emitting;
            if (!emitting)
            {
                state.EmitCounter = 0f;
            }
            if (VerboseLoggingConfig.Enabled.Value)
            {
                SoundPropagation.Log.LogInfo(
                    $"[AirWakeDiag] '{aircraft.name}' -> {(emitting ? "EMIT" : "STOP")} reason=\"{reason}\" "
                    + $"t={Time.timeSinceLevelLoad:F2} speed={aircraft.speed:F1}m/s radarAlt={aircraft.radarAlt:F1}m");
            }
        }
        private static void CreateEffect(Aircraft aircraft, AirWakeState state)
        {
            if (VisualObjects.Count("wake") + 2 > AirWakeConfig.MaxEmitters.Value * 2) return;
            state.EffectAttempted = true;
            bool verbose = VerboseLoggingConfig.Enabled.Value;

            if (HasRotor(aircraft))
            {
                state.IsHelicopter = true;
                if (verbose)
                {
                    SoundPropagation.Log.LogInfo($"[AirWakeDiag] '{aircraft.name}' skipped: RotorShaft present (helicopter).");
                }
                return;
            }

            Material material = EffectTextures.GetDustMaterial();
            if (material == null)
            {
                if (verbose)
                {
                    SoundPropagation.Log.LogWarning($"[AirWakeDiag] '{aircraft.name}' skipped: dust material unavailable.");
                }
                return;
            }

            state.EffectObjects = new GameObject[2];
            state.Systems = new ParticleSystem[2];
            state.Renderers = new ParticleSystemRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                state.EffectObjects[i] = VisualObjects.Create(i == RightSystem ? "AirWake_Right" : "AirWake_Left", "wake", AirWakeConfig.MaxEmitters.Value * 2);
                state.Systems[i] = BuildSystem(state.EffectObjects[i], material);
                state.Renderers[i] = state.EffectObjects[i].GetComponent<ParticleSystemRenderer>();
            }
            ApplySpin(state);
            ApplyRenderSettings(state);

            for (int i = 0; i < state.EffectObjects.Length; i++) VisualObjects.TrackOwner(state.EffectObjects[i], aircraft);

            if (verbose)
            {
                SoundPropagation.Log.LogInfo($"[AirWakeDiag] '{aircraft.name}' air-wake particle systems created.");
            }
        }

        private static bool HasRotor(Aircraft aircraft)
        {
            if (aircraft.GetComponentInChildren<RotorShaft>(true) != null) return true;
            // Aircraft parts have separate rigidbody hierarchies; a rotor is not
            // necessarily a Transform child of the aircraft's central object.
            foreach (UnitPart part in aircraft.partLookup)
                if (part != null && part.GetComponentInChildren<RotorShaft>(true) != null) return true;
            return false;
        }

        private static ParticleSystem BuildSystem(GameObject effectObject, Material material)
        {
            effectObject.transform.SetParent(Datum.origin, worldPositionStays: false);

            ParticleSystem system = effectObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.duration = 30f;
            main.startLifetime = AirWakeConfig.LifetimeSeconds;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = Datum.origin;
            main.maxParticles = MaxParticlesPerSystem;
            main.playOnAwake = false;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f; // manual Emit() only

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.2f)));

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient fadeGradient = new Gradient();
            fadeGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = fadeGradient;

            ParticleSystemRenderer renderer = effectObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = material;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            system.Play();
            return system;
        }

        internal static void ClearAll() { _states = new ConditionalWeakTable<Aircraft, AirWakeState>(); }

        private static void DestroyEffect(AirWakeState state)
        {
            if (state.EffectObjects == null)
            {
                return;
            }
            for (int i = 0; i < state.EffectObjects.Length; i++)
            {
                if (state.EffectObjects[i] != null)
                {
                    Object.Destroy(state.EffectObjects[i]);
                }
            }
        }
    }
}


