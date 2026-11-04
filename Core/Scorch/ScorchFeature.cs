// Adapted from Boscali Summer 0.2.12, commit d11a507424439f8d84c089afa681bcf86aacb44c.
// Copyright (c) 2026 GrabowMar. See BOSCALISUMMER-LICENSE.txt.
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BoscaliClientExperimental.Scorch
{
    internal static class ScorchFeature
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Limit;
        internal static ManualLogSource Log;

        public static void Install(BaseUnityPlugin plugin, Harmony harmony)
        {
            Enabled = plugin.Config.Bind("Impact marks", "Enabled", false,
                "Experimental local cosmetic explosive impact decals. No terrain deformation.");
            Limit = plugin.Config.Bind("Impact marks", "Maximum marks", 100,
                "Maximum active pooled decals, clamped to 10-300.");
            Log = BepInEx.Logging.Logger.CreateLogSource("BoscaliClientExperimental.ImpactMarks");
            var target = AccessTools.Method(typeof(Missile), "UserCode_RpcDetonate_897349600");
            if (target == null)
            {
                Log.LogWarning("Impact marks unavailable: missile detonation RPC was not found.");
                return;
            }
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(ScorchFeature), nameof(OnDetonate)));
                var destruction = AccessTools.Method(typeof(MapBuilding), "Destruct");
                if (destruction != null)
                    harmony.Patch(destruction, prefix: new HarmonyMethod(typeof(ScorchFeature), nameof(OnBuildingDestruct)));
                else Log.LogWarning("Impact marks: building cleanup hook unavailable; wall marks may remain after destruction.");
                Plugin.EffectsHost.AddComponent<ScorchManager>();
            }
            catch (Exception e) { Log.LogWarning("Impact marks setup failed: " + e); }
        }

        private static void OnDetonate(Unit relativeUnit, Vector3 pos, bool armed, bool hitTerrain, float ___blastYield)
        {
            if (!armed || Enabled == null || !Enabled.Value || GameManager.IsHeadless) return;
            try
            {
                Vector3 world = relativeUnit != null ? relativeUnit.transform.TransformPoint(pos) : pos + Datum.origin.position;
                ScorchManager.Instance?.Submit(world.ToGlobalPosition(), ___blastYield, hitTerrain);
            }
            catch (Exception e) { Log.LogWarning("Impact mark skipped: " + e.Message); }
        }

        private static void OnBuildingDestruct(MapBuilding __instance)
        {
            try { if (__instance != null) ScorchManager.Instance?.Release(__instance.GetInstanceID()); }
            catch (Exception e) { Log.LogWarning("Impact mark cleanup skipped: " + e.Message); }
        }
    }

    internal sealed class ScorchManager : MonoBehaviour
    {
        private struct Pending { public GlobalPosition Position; public float Yield; public bool HitTerrain; }
        private struct Placement { public GlobalPosition Position; public Vector3 Normal; public float Yield; public int Owner; }
        private sealed class Mark { public GameObject Object; public int Owner; }
        internal static ScorchManager Instance;
        private readonly Queue<Pending> pending = new Queue<Pending>();
        private readonly Queue<Placement> placements = new Queue<Placement>();
        private readonly List<Mark> marks = new List<Mark>();
        private readonly Collider[] overlap = new Collider[32];
        private int next;
        private bool clearedDisabled;
        private bool loggedProcessingError;
        private float warmAt;

        private void Awake() { Instance = this; SceneManager.sceneLoaded += OnSceneLoaded; warmAt = Time.unscaledTime + 2f; }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Clear(); }
        private void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; Clear(); if (Instance == this) Instance = null; }
        internal void Submit(GlobalPosition pos, float yield, bool hitTerrain)
        {
            if (pending.Count < 32 && !float.IsNaN(yield) && !float.IsInfinity(yield))
                pending.Enqueue(new Pending { Position = pos, Yield = yield, HitTerrain = hitTerrain });
        }
        private void Update()
        {
            if (ScorchFeature.Enabled == null || !ScorchFeature.Enabled.Value)
            {
                if (!clearedDisabled) Clear();
                clearedDisabled = true;
                return;
            }
            clearedDisabled = false;
            try
            {
                TrimToLimit();
                if (Time.unscaledTime >= warmAt && !ScorchMaterial.Step()) return;
                for (int i = 0; i < 2 && pending.Count > 0 && placements.Count < 6; i++) FindSurface(pending.Dequeue());
                for (int i = 0; i < 3 && placements.Count > 0; i++) Stamp(placements.Dequeue());
            }
            catch (Exception e)
            {
                pending.Clear(); placements.Clear();
                if (!loggedProcessingError) { ScorchFeature.Log.LogWarning("Impact mark processing skipped: " + e); loggedProcessingError = true; }
            }
        }
        private void FindSurface(Pending explosion)
        {
            if (GameAssets.i == null || GameAssets.i.scorchMarkDecal == null || Datum.origin == null) return;
            Vector3 local = explosion.Position.ToLocalPosition();
            // A short downward probe marks actual ground impacts, not airborne detonations.
            if (explosion.HitTerrain && Physics.Raycast(local + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 4f,
                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) && ground.normal.y >= 0.5f)
            {
                AddCluster(ground.point, ground.normal, explosion.Yield, OwnerOf(ground.collider));
                return;
            }
            float radius = Mathf.Clamp(8f + explosion.Yield * 0.8f, 8f, 40f);
            int count = Physics.OverlapSphereNonAlloc(local, radius, overlap, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Collide);
            Collider nearest = null; int owner = 0; float nearestSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider c = overlap[i]; if (c == null) continue;
                float distance = (c.bounds.center - local).sqrMagnitude;
                if (distance >= nearestSq) continue;
                int candidate = OwnerOf(c); if (candidate == 0) continue;
                nearestSq = distance; nearest = c; owner = candidate;
            }
            if (nearest == null) return;
            Vector3 direction = (nearest.bounds.center - local).normalized;
            if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
            if (Physics.Raycast(local - direction * 1.5f, direction, out RaycastHit wall, 41f, PhysicsLayers.StaticsMask))
            {
                // Do not assign a wall mark to a different building caught by the ray.
                int actualOwner = OwnerOf(wall.collider);
                if (IsSameOwner(owner, actualOwner)) AddCluster(wall.point, wall.normal, explosion.Yield, actualOwner);
            }
        }
        private void AddCluster(Vector3 point, Vector3 normal, float yield, int owner)
        {
            Add(point, normal, yield, owner);
            int count = yield >= 6f ? 3 : yield >= 0.75f ? 2 : 1;
            Vector3 tangent = Vector3.Cross(normal, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.right;
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;
            float spread = Size(yield) * 0.32f;
            if (count > 1) Add(point + tangent * spread + bitangent * spread * 0.3f, normal, yield * 0.45f, owner);
            if (count > 2) Add(point - tangent * spread * 0.75f - bitangent * spread * 0.45f, normal, yield * 0.32f, owner);
        }
        private void Add(Vector3 point, Vector3 normal, float yield, int owner)
        { placements.Enqueue(new Placement { Position = point.ToGlobalPosition(), Normal = normal, Yield = yield, Owner = owner }); }
        private void TrimToLimit()
        {
            int limit = Mathf.Clamp(ScorchFeature.Limit.Value, 10, 300);
            while (marks.Count > limit) { var last = marks[marks.Count - 1]; if (last.Object != null) Destroy(last.Object); marks.RemoveAt(marks.Count - 1); }
            if (marks.Count == 0) next = 0; else next %= marks.Count;
        }
        private void Stamp(Placement p)
        {
            int limit = Mathf.Clamp(ScorchFeature.Limit.Value, 10, 300);
            TrimToLimit();
            Mark mark;
            if (marks.Count < limit)
            {
                if (GameAssets.i == null || GameAssets.i.scorchMarkDecal == null || Datum.origin == null) return;
                var source = GameAssets.i.scorchMarkDecal.GetComponent<DecalProjector>();
                if (source == null) return;
                // Only the renderer component is created; do not clone unknown prefab scripts,
                // colliders, children, or network components into a local cosmetic effect.
                var obj = new GameObject("BoscaliClientExperimental.ImpactMark");
                try
                {
                    obj.SetActive(false);
                    obj.transform.SetParent(Datum.origin, false);
                    var decal = obj.AddComponent<DecalProjector>();
                    decal.material = source.material;
                    decal.uvScale = source.uvScale; decal.uvBias = source.uvBias;
                    decal.pivot = source.pivot; decal.scaleMode = source.scaleMode;
                    decal.fadeScale = source.fadeScale;
                    mark = new Mark { Object = obj }; marks.Add(mark);
                }
                catch { Destroy(obj); throw; }
            }
            else { mark = marks[next]; next = (next + 1) % marks.Count; }
            if (mark.Object == null) return;
            var projector = mark.Object.GetComponent<DecalProjector>();
            if (projector == null) return;
            mark.Owner = p.Owner;
            float size = Size(p.Yield), depth = Mathf.Clamp(size * 0.22f, 1.6f, 4f);
            var t = mark.Object.transform;
            t.rotation = Quaternion.LookRotation(-p.Normal, Vector3.up);
            t.position = p.Position.ToLocalPosition() + p.Normal * (depth * 0.45f);
            projector.renderingLayerMask = ~0u;
            projector.size = new Vector3(size, size, depth);
            projector.startAngleFade = 45f; projector.endAngleFade = 70f;
            projector.fadeFactor = 0.98f; projector.drawDistance = 3500f;
            if (ScorchMaterial.Value != null) projector.material = ScorchMaterial.Value;
            mark.Object.SetActive(true);
        }
        private static float Size(float yield) => Mathf.Clamp(5f + Mathf.Max(0f, yield) * 0.85f, 5f, 22f);
        private static int OwnerOf(Collider c)
        {
            if (c == null) return 0;
            var map = c.GetComponentInParent<MapBuilding>(); if (map != null) return map.GetInstanceID();
            var building = c.GetComponentInParent<Building>(); return building != null ? building.GetInstanceID() : 0;
        }
        private static bool IsSameOwner(int expected, int actual) => expected != 0 && actual == expected;
        internal void Release(int id)
        {
            int n = placements.Count;
            for (int i = 0; i < n; i++) { Placement p = placements.Dequeue(); if (p.Owner != id) placements.Enqueue(p); }
            foreach (var mark in marks) if (mark.Owner == id) { mark.Owner = 0; if (mark.Object != null) mark.Object.SetActive(false); }
        }
        private void Clear()
        {
            foreach (var mark in marks) if (mark.Object != null) Destroy(mark.Object);
            marks.Clear(); pending.Clear(); placements.Clear(); next = 0; loggedProcessingError = false;
            ScorchMaterial.Reset(); warmAt = Time.unscaledTime + 2f;
        }
    }
}
