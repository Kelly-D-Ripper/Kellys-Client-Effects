using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KropClientEffects
{
    // This entry point is read by the weather owner through reflection. It never
    // writes the saved Boscali spray preference; disabling KROP restores it.
    public static class VisualRefinements
    {
        public static bool KropWaterWakesActive => Settings.Active && AirWakeConfig.Enabled != null && AirWakeConfig.Enabled.Value
            && VisualRefinementSettings.WaterWakes != null && VisualRefinementSettings.WaterWakes.Value;

        private sealed class Effect
        {
            internal GameObject Root;
            internal Transform Owner;
            internal readonly List<AudioSource> Audio = new List<AudioSource>();
            internal float Expires;
        }
        private static readonly List<Effect> Effects = new List<Effect>();
        private static GameObject cookoffPrefab;
        private static float nextPrefabScan;
        private static bool previousWater, previousEmbers;
        private static bool cloneWarning;

        internal static int ActiveFuelEffects => Effects.Count;

        internal static void FuelIgnited(FuelTank tank, GameObject nativeFireball, UnitPart part)
        {
            if (!Settings.Active || VisualRefinementSettings.FuelExplosions == null || !VisualRefinementSettings.FuelExplosions.Value
                || tank == null || nativeFireball == null || Datum.origin == null || GameManager.IsHeadless
                || Effects.Count >= VisualRefinementSettings.FuelLimit.Value || EffectRandom.value >= VisualRefinementSettings.FuelChance.Value) return;
            var camera = SceneSingleton<CameraStateManager>.i;
            if (camera == null || (camera.transform.position - tank.transform.position).sqrMagnitude >
                VisualRefinementSettings.FuelDistance.Value * VisualRefinementSettings.FuelDistance.Value) return;
            var prefab = FindCookoffPrefab();
            if (prefab == null) return;
            GameObject root = null;
            try
            {
                root = new GameObject("Unified local fuel effects");
                root.SetActive(false);
                root.transform.SetParent(Datum.origin, false);
                root.transform.position = tank.transform.position;
                root.transform.rotation = tank.transform.rotation;
                var effect = new Effect { Root = root, Owner = tank.transform,
                    Expires = Time.time + VisualRefinementSettings.FuelLifetime.Value };
                Vector3 velocity = part != null && part.rb != null ? part.rb.velocity : Vector3.zero;
                // Never instantiate the source GameObject or any of its components.
                // The new hierarchy contains only these native rendering/audio types.
                var playback = ParticleVisualCopy.Copy(prefab.transform, root.transform, velocity,
                    VisualRefinementSettings.ExtendedEmbers.Value, effect.Audio);
                if (root.GetComponentsInChildren<ParticleSystem>(true).Length == 0)
                { UnityEngine.Object.Destroy(root); return; }
                root.SetActive(true);
                foreach (var ps in playback.Particles)
                    if (ps.gameObject.activeInHierarchy) ps.Play(false);
                if (VisualRefinementSettings.FuelAudio.Value)
                    foreach (var source in playback.Audio)
                        if (source != null && source.gameObject.activeInHierarchy && source.clip != null)
                        { source.Play(); AudioFilters.Register(source); }
                Effects.Add(effect);
            }
            catch (Exception ex)
            {
                if (root != null) UnityEngine.Object.Destroy(root);
                if (!cloneWarning) { cloneWarning = true; Plugin.Log.LogWarning("Local fuel effect skipped: " + ex.Message); }
            }
        }

        private static GameObject FindCookoffPrefab()
        {
            if (cookoffPrefab != null) return cookoffPrefab;
            if (Time.unscaledTime < nextPrefabScan) return null;
            nextPrefabScan = Time.unscaledTime + 30f;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
                if (candidate.name == "AmmoCookoffVehicle" && !candidate.scene.IsValid()) { cookoffPrefab = candidate; break; }
            return cookoffPrefab;
        }

        internal static void Tick()
        {
            bool water = KropWaterWakesActive;
            if (water != previousWater)
            {
                // Clear old provider particles immediately, avoiding residual wash
                // while the weather owner switches back to its Boscali renderer.
                Patches.AirWakeEffectPatch.ClearProviderParticles(); previousWater = water;
            }
            bool embers = VisualRefinementSettings.ExtendedEmbers != null && VisualRefinementSettings.ExtendedEmbers.Value;
            bool clear = !Settings.Active || VisualRefinementSettings.FuelExplosions == null || !VisualRefinementSettings.FuelExplosions.Value
                || embers != previousEmbers;
            previousEmbers = embers;
            for (int i = Effects.Count - 1; i >= 0; i--)
            {
                var effect = Effects[i];
                if (clear || effect.Root == null || Time.time >= effect.Expires)
                { if (effect.Root != null) UnityEngine.Object.Destroy(effect.Root); Effects.RemoveAt(i); continue; }
                if (effect.Owner != null) effect.Root.transform.position = effect.Owner.position;
                if (!VisualRefinementSettings.FuelAudio.Value)
                    foreach (var source in effect.Audio) if (source != null && source.isPlaying) source.Stop();
            }
        }
        internal static void Clear()
        {
            foreach (var effect in Effects) if (effect.Root != null) UnityEngine.Object.Destroy(effect.Root);
            Effects.Clear(); cookoffPrefab = null; nextPrefabScan = 0f; cloneWarning = false;
            previousWater = false; previousEmbers = false;
        }
    }

    [HarmonyPatch(typeof(FuelTank), "Fireball")]
    internal static class LocalFuelVariationPatch
    {
        private static void Prefix(GameObject ___fireballSpawn, out bool __state) { __state = ___fireballSpawn != null; }
        private static void Postfix(FuelTank __instance, GameObject ___fireballSpawn, UnitPart ___part, bool __state)
        { if (!__state && ___fireballSpawn != null) VisualRefinements.FuelIgnited(__instance, ___fireballSpawn, ___part); }
    }

    // Explicit component/module allowlist. A prefab's MonoBehaviours, damage,
    // colliders, joints, rigidbodies, lights and network identity never get copied
    // and therefore cannot run Awake/OnEnable, even temporarily.
    internal static class ParticleVisualCopy
    {
        private const int MaxNodes = 96, MaxSystems = 12, MaxAudio = 8, MaxParticles = 512;
        internal sealed class Playback
        {
            internal readonly List<ParticleSystem> Particles = new List<ParticleSystem>();
            internal readonly List<AudioSource> Audio = new List<AudioSource>();
        }
        private sealed class Context
        {
            internal int Nodes, Systems;
            internal Vector3 Velocity;
            internal bool Embers;
            internal List<AudioSource> Audio;
            internal readonly Dictionary<ParticleSystem, ParticleSystem> Map = new Dictionary<ParticleSystem, ParticleSystem>();
            internal readonly Playback Starts = new Playback();
        }
        private static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> Properties = new Dictionary<Type, Dictionary<string, PropertyInfo>>();
        internal static Playback Copy(Transform source, Transform target, Vector3 velocity, bool embers, List<AudioSource> audio)
        {
            var context = new Context { Velocity = velocity, Embers = embers, Audio = audio };
            CopyNode(source, target, context, 0);
            // Remap only local visual birth/death subemitters. Never carry an
            // external system reference or collision/trigger event into the copy.
            foreach (var pair in context.Map)
            {
                var input = pair.Key.subEmitters; var output = pair.Value.subEmitters;
                output.enabled = input.enabled;
                for (int i = 0; i < Math.Min(input.subEmittersCount, 12); i++)
                {
                    var type = input.GetSubEmitterType(i);
                    if (type != ParticleSystemSubEmitterType.Birth && type != ParticleSystemSubEmitterType.Death) continue;
                    var original = input.GetSubEmitterSystem(i);
                    if (original != null && context.Map.TryGetValue(original, out var copy))
                        output.AddSubEmitter(copy, type, input.GetSubEmitterProperties(i), input.GetSubEmitterEmitProbability(i));
                }
            }
            return context.Starts;
        }
        private static void CopyNode(Transform source, Transform target, Context context, int depth)
        {
            if (++context.Nodes > MaxNodes || depth > 12) return;
            target.localScale = source.localScale;
            var original = source.GetComponent<ParticleSystem>();
            if (original != null && context.Systems < MaxSystems)
            {
                context.Systems++;
                var copy = target.gameObject.AddComponent<ParticleSystem>();
                copy.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                CopySystem(original, copy, context.Velocity, context.Embers && depth == 0);
                context.Map.Add(original, copy);
                if (original.main.playOnAwake) context.Starts.Particles.Add(copy);
            }
            foreach (var originalAudio in source.GetComponents<AudioSource>())
            {
                if (context.Audio.Count >= MaxAudio || originalAudio.clip == null) break;
                var copy = target.gameObject.AddComponent<AudioSource>();
                copy.playOnAwake = false; copy.clip = originalAudio.clip;
                copy.enabled = originalAudio.enabled;
                copy.outputAudioMixerGroup = originalAudio.outputAudioMixerGroup;
                copy.loop = originalAudio.loop; copy.volume = Mathf.Clamp(originalAudio.volume, 0f, 1f);
                copy.pitch = Mathf.Clamp(originalAudio.pitch, 0.25f, 3f);
                copy.spatialBlend = 1f; copy.dopplerLevel = 0f;
                copy.minDistance = Mathf.Clamp(originalAudio.minDistance, 1f, 200f);
                copy.maxDistance = Mathf.Clamp(originalAudio.maxDistance, copy.minDistance + 1f, 6000f);
                copy.rolloffMode = originalAudio.rolloffMode;
                if (copy.rolloffMode == AudioRolloffMode.Custom) copy.SetCustomCurve(AudioSourceCurveType.CustomRolloff, originalAudio.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
                context.Audio.Add(copy);
                if (originalAudio.enabled && originalAudio.playOnAwake) context.Starts.Audio.Add(copy);
            }
            for (int i = 0; i < source.childCount && context.Nodes < MaxNodes; i++)
            {
                var child = source.GetChild(i); var obj = new GameObject(child.name);
                obj.transform.SetParent(target, false); obj.transform.localPosition = child.localPosition; obj.transform.localRotation = child.localRotation;
                CopyNode(child, obj.transform, context, depth + 1); obj.SetActive(child.gameObject.activeSelf);
            }
        }

        private static void Module<T>(T source, T target, string fields) where T : struct
        {
            var type = typeof(T);
            if (!Properties.TryGetValue(type, out var table))
            {
                table = new Dictionary<string, PropertyInfo>();
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0) table[prop.Name] = prop;
                Properties.Add(type, table);
            }
            object from = source, to = target;
            foreach (var name in fields.Split(' '))
                if (table.TryGetValue(name, out var prop)) prop.SetValue(to, prop.GetValue(from, null), null);
        }

        private static void CopySystem(ParticleSystem source, ParticleSystem target, Vector3 velocity, bool embers)
        {
            Module(source.main, target.main, "duration loop prewarm startDelay startLifetime startSpeed startSize3D startSize startSizeX startSizeY startSizeZ startRotation3D startRotation startRotationX startRotationY startRotationZ flipRotation startColor gravityModifier simulationSpeed scalingMode emitterVelocityMode");
            var main = target.main;
            main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None;
            main.maxParticles = Math.Min(source.main.maxParticles, MaxParticles);
            main.simulationSpace = ParticleSystemSimulationSpace.Custom; main.customSimulationSpace = Datum.origin;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom; main.emitterVelocity = velocity;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
            target.useAutoRandomSeed = false; target.randomSeed = (uint)EffectRandom.Range(1, int.MaxValue);
            Module(source.emission, target.emission, "enabled rateOverTime rateOverDistance");
            var bursts = new ParticleSystem.Burst[Math.Min(source.emission.burstCount, 16)];
            if (bursts.Length > 0)
            {
                for (int i = 0; i < bursts.Length; i++) bursts[i] = source.emission.GetBurst(i);
                var emission = target.emission; emission.SetBursts(bursts);
            }
            Module(source.shape, target.shape, "enabled shapeType angle radius radiusThickness arc arcMode arcSpread arcSpeed length scale rotation position alignToDirection randomDirectionAmount sphericalDirectionAmount randomPositionAmount mesh meshShapeType useMeshMaterialIndex meshMaterialIndex useMeshColors normalOffset texture textureClipChannel textureClipThreshold textureColorAffectsParticles textureAlphaAffectsParticles textureBilinearFiltering textureUVChannel");
            Module(source.velocityOverLifetime, target.velocityOverLifetime, "enabled x y z orbitalX orbitalY orbitalZ orbitalOffsetX orbitalOffsetY orbitalOffsetZ radial speedModifier space");
            Module(source.limitVelocityOverLifetime, target.limitVelocityOverLifetime, "enabled separateAxes limit limitX limitY limitZ dampen space drag multiplyDragByParticleSize multiplyDragByParticleVelocity");
            Module(source.forceOverLifetime, target.forceOverLifetime, "enabled x y z space randomized");
            Module(source.colorOverLifetime, target.colorOverLifetime, "enabled color");
            Module(source.colorBySpeed, target.colorBySpeed, "enabled color range");
            Module(source.sizeOverLifetime, target.sizeOverLifetime, "enabled separateAxes size x y z");
            Module(source.sizeBySpeed, target.sizeBySpeed, "enabled separateAxes size x y z range");
            Module(source.rotationOverLifetime, target.rotationOverLifetime, "enabled separateAxes x y z");
            Module(source.rotationBySpeed, target.rotationBySpeed, "enabled separateAxes x y z range");
            Module(source.noise, target.noise, "enabled separateAxes strength strengthX strengthY strengthZ frequency damping octaveCount octaveMultiplier octaveScale quality scrollSpeed remapEnabled remap remapX remapY remapZ positionAmount rotationAmount sizeAmount");
            Module(source.textureSheetAnimation, target.textureSheetAnimation, "enabled mode numTilesX numTilesY animation frameOverTime startFrame cycleCount rowIndex rowMode uvChannelMask timeMode fps speedRange");
            var sheets = target.textureSheetAnimation;
            // Even a newly created Grid-mode system reports an empty sprite slot.
            // Passing that null to AddSprite throws inside Unity; sprites are meaningful
            // only in Sprites mode and a missing sprite must never abort an effect copy.
            if (source.textureSheetAnimation.mode == ParticleSystemAnimationMode.Sprites)
            {
                for (int i = sheets.spriteCount - 1; i >= 0; i--) sheets.RemoveSprite(i);
                int validSprites = 0;
                for (int i = 0; i < Math.Min(source.textureSheetAnimation.spriteCount, 64); i++)
                {
                    var sprite = source.textureSheetAnimation.GetSprite(i);
                    if (sprite == null) continue;
                    sheets.AddSprite(sprite); validSprites++;
                }
                if (validSprites == 0) sheets.enabled = false;
            }
            var collision = target.collision; collision.enabled = false;
            var trigger = target.trigger; trigger.enabled = false;
            var external = target.externalForces; external.enabled = false;
            var lights = target.lights; lights.enabled = false;
            var inheritance = target.inheritVelocity; inheritance.enabled = true;
            inheritance.mode = ParticleSystemInheritVelocityMode.Initial; inheritance.curve = new ParticleSystem.MinMaxCurve(1f);
            if (embers)
            {
                main.startLifetime = Scaled(main.startLifetime, 1.5f);
                var limit = target.limitVelocityOverLifetime; limit.drag = Scaled(limit.drag, 0.15f);
            }
            var originalRenderer = source.GetComponent<ParticleSystemRenderer>();
            var renderer = target.GetComponent<ParticleSystemRenderer>();
            if (originalRenderer != null)
            {
                renderer.sharedMaterials = originalRenderer.sharedMaterials; renderer.enabled = originalRenderer.enabled;
                renderer.renderMode = originalRenderer.renderMode; renderer.alignment = originalRenderer.alignment;
                renderer.mesh = originalRenderer.mesh; renderer.normalDirection = originalRenderer.normalDirection;
                var streams = new List<ParticleSystemVertexStream>(); originalRenderer.GetActiveVertexStreams(streams); renderer.SetActiveVertexStreams(streams);
                renderer.sortMode = originalRenderer.sortMode; renderer.sortingFudge = originalRenderer.sortingFudge;
                renderer.minParticleSize = originalRenderer.minParticleSize; renderer.maxParticleSize = originalRenderer.maxParticleSize;
                renderer.lengthScale = originalRenderer.lengthScale; renderer.velocityScale = originalRenderer.velocityScale;
                renderer.cameraVelocityScale = originalRenderer.cameraVelocityScale; renderer.pivot = originalRenderer.pivot;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = originalRenderer.receiveShadows;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
        }
        private static ParticleSystem.MinMaxCurve Scaled(ParticleSystem.MinMaxCurve value, float scale)
        {
            if (value.mode == ParticleSystemCurveMode.Constant) value.constant *= scale;
            else if (value.mode == ParticleSystemCurveMode.TwoConstants) { value.constantMin *= scale; value.constantMax *= scale; }
            else value.curveMultiplier *= scale;
            return value;
        }
    }
}
