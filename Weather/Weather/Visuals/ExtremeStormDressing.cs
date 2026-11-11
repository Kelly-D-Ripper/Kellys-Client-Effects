using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// An opt-in thicker native cloud deck and a small, terrain-anchored mist patch around the camera.
    /// No replacement cloud shader, scene scans, collisions, shadows or gameplay changes. Mist
    /// positions use Datum space and at most three bounded ground probes every quarter second.
    /// </summary>
    internal sealed class ExtremeStormDressing : IClientEffect
    {
        private const int Side = 6, Count = Side * Side, SamplesPerTick = 3;
        private const float Spacing = 650f, SampleInterval = 0.25f;
        private static readonly FieldInfo LayerField = typeof(LevelInfo).GetField("cloudLayer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ThicknessField = typeof(CloudLayer).GetField("layerThickness", BindingFlags.Instance | BindingFlags.NonPublic);
        private CloudLayer layer;
        private LevelInfo deckOwner;
        private float nextDeckCheck;
        private float baseThickness, writtenThickness;
        private bool deckApplied;
        private GameObject root;
        private ParticleSystem mist;
        private Material material;
        private readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[Count];
        private readonly Vector3[] positions = new Vector3[Count];
        private readonly int[] gridX = new int[Count], gridZ = new int[Count];
        private readonly bool[] valid = new bool[Count];
        private readonly float[] born = new float[Count];
        private readonly RaycastHit[] hits = new RaycastHit[8];
        private float nextSample;
        private int cursor;
        private Transform origin;
        private bool failed;
        private bool cloudDeckEnabled = true;

        public string EffectId => "extreme-storm";
        internal bool DeckApplied => deckApplied;
        internal int MistParticles => mist != null ? mist.particleCount : 0;

        internal void SetCloudDeckEnabled(bool enabled)
        {
            cloudDeckEnabled = enabled;
            if (!enabled) RestoreDeck();
        }

        internal void Tick(LevelInfo level, Camera camera, bool enabled, bool mistEnabled, float dt)
        {
            if (!enabled || level == null || camera == null || Datum.origin == null) { ReleaseFx(); return; }
            if (cloudDeckEnabled) ApplyDeck(level);
            else RestoreDeck();
            if (!mistEnabled || camera.transform.position.y < Datum.LocalSeaY - 1f) { ReleaseMist(); return; }
            if (origin != Datum.origin) ReleaseMist();
            if (!EnsureMist()) return;
            Vector3 eye = camera.transform.position;
            GlobalPosition global = eye.ToGlobalPosition();
            int cx = Mathf.FloorToInt(global.x / Spacing), cz = Mathf.FloorToInt(global.z / Spacing);
            float now = Time.unscaledTime;
            if (now >= nextSample)
            {
                nextSample = now + SampleInterval;
                for (int n = 0; n < SamplesPerTick; n++)
                {
                    int logical = cursor++ % Count;
                    int gx = cx - Side / 2 + logical % Side, gz = cz - Side / 2 + logical / Side;
                    int slot = Mod(gz, Side) * Side + Mod(gx, Side);
                    if (valid[slot] && gridX[slot] == gx && gridZ[slot] == gz) continue;
                    float jitterX = Mathf.PerlinNoise(gx * 0.71f, gz * 0.43f) * 180f - 90f;
                    float jitterZ = Mathf.PerlinNoise(gz * 0.63f, gx * 0.37f) * 180f - 90f;
                    Vector3 target = new Vector3(eye.x + (gx + 0.5f) * Spacing + jitterX - global.x,
                        Datum.LocalSeaY + 12000f, eye.z + (gz + 0.5f) * Spacing + jitterZ - global.z);
                    int count = Physics.RaycastNonAlloc(target, Vector3.down, hits, 14000f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    float groundY = Datum.LocalSeaY;
                    for (int h = 0; h < count; h++)
                        if (hits[h].collider != null && hits[h].rigidbody == null)
                            groundY = Mathf.Max(groundY, hits[h].point.y);
                    // Dynamic aircraft/vehicles are ignored; a missed ray falls back to sea height.
                    target.y = groundY + 95f;
                    positions[slot] = origin.InverseTransformPoint(target);
                    gridX[slot] = gx; gridZ[slot] = gz; born[slot] = now; valid[slot] = true;
                }
            }
            int alive = 0;
            float grey = Mathf.Clamp(RenderSettings.fogColor.grayscale, 0.01f, 0.65f);
            for (int i = 0; i < Count; i++)
            {
                if (!valid[i]) continue;
                Vector3 at = origin.TransformPoint(positions[i]);
                float distance = new Vector2(at.x - eye.x, at.z - eye.z).magnitude;
                float alpha = ExtremeStormProfile.MistAlpha(now - born[i], distance);
                if (alpha <= 0.001f) continue;
                particles[alive++] = new ParticleSystem.Particle
                {
                    position = positions[i], startSize3D = new Vector3(1350f, 420f, 1350f),
                    startColor = new Color(grey, grey, grey, alpha), startLifetime = 86400f, remainingLifetime = 86400f,
                    randomSeed = (uint)(i + 1),
                };
            }
            mist.SetParticles(particles, alive);
        }

        private static int Mod(int value, int divisor) => (value % divisor + divisor) % divisor;

        private void ApplyDeck(LevelInfo level)
        {
            if (LayerField == null || ThicknessField == null) return;
            if (deckOwner == level && layer != null && Time.unscaledTime < nextDeckCheck) return;
            CloudLayer current = LayerField.GetValue(level) as CloudLayer;
            if (current != layer) RestoreDeck();
            if (current == null) return;
            deckOwner = level;
            nextDeckCheck = Time.unscaledTime + 1f;
            layer = current;
            float thickness = (float)ThicknessField.GetValue(layer);
            if (!deckApplied || !RainSkyMath.IsOwnWrite(writtenThickness, thickness)) baseThickness = thickness;
            writtenThickness = Mathf.Max(baseThickness, ExtremeStormProfile.NativeThickness);
            ThicknessField.SetValue(layer, writtenThickness);
            deckApplied = true;
        }

        private bool EnsureMist()
        {
            if (mist != null) return true;
            if (failed) return false;
            Material source = SmokeFx.Puff;
            if (source == null) { failed = true; return false; }
            origin = Datum.origin;
            material = new Material(source) { name = "ExtremeStormTerrainMist" };
            root = new GameObject("Extreme storm terrain mist") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.SetParent(origin, false);
            mist = root.AddComponent<ParticleSystem>();
            mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = mist.main;
            main.loop = true; main.playOnAwake = false; main.maxParticles = Count;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.simulationSpeed = 0f; main.startSpeed = 0f; main.startSize3D = true;
            var emission = mist.emission; emission.enabled = false;
            var shape = mist.shape; shape.enabled = false;
            var renderer = mist.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = 1f;
            mist.Play();
            return true;
        }

        private void RestoreDeck()
        {
            if (deckApplied && layer != null && ThicknessField != null &&
                RainSkyMath.IsOwnWrite(writtenThickness, (float)ThicknessField.GetValue(layer)))
                ThicknessField.SetValue(layer, baseThickness);
            deckApplied = false; layer = null; deckOwner = null; nextDeckCheck = 0f;
        }

        private void ReleaseMist()
        {
            if (root != null) { root.SetActive(false); Object.Destroy(root); }
            if (material != null) Object.Destroy(material);
            root = null; mist = null; material = null; origin = null;
            for (int i = 0; i < Count; i++) valid[i] = false;
            cursor = 0; nextSample = 0f;
        }

        public void ReleaseFx() { RestoreDeck(); ReleaseMist(); failed = false; }
        public void DescribeFx(IDictionary<string, object> state)
        {
            state["fx.extreme-storm.deck"] = DeckApplied;
            state["fx.extreme-storm.mist"] = MistParticles;
        }
    }
}
