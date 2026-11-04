// Adapted from Boscali Summer 0.2.12, commit d11a507424439f8d84c089afa681bcf86aacb44c.
// Copyright (c) 2026 GrabowMar. See BOSCALISUMMER-LICENSE.txt.
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BoscaliClientExperimental.Scorch
{
    internal static class ScorchMaterial
    {
        private static Material[] scan;
        private static Material best;
        private static int index, score;
        private static bool done;
        internal static Material Value { get; private set; }
        internal static void Reset() { scan = null; best = null; index = score = 0; done = false; Value = null; }

        internal static bool Step()
        {
            if (done) return true;
            try
            {
                if (scan == null) { scan = Resources.FindObjectsOfTypeAll<Material>(); return false; }
                int end = Math.Min(index + 200, scan.Length);
                while (index < end) Score(scan[index++]);
                if (index < scan.Length) return false;
                FieldInfo field = AccessTools.Field(typeof(DecalSpawner), "decalMaterial");
                if (field != null)
                {
                    var spawners = Resources.FindObjectsOfTypeAll<DecalSpawner>();
                    foreach (var spawner in spawners) if (spawner != null) Score(field.GetValue(spawner) as Material);
                }
                Value = best;
                ScorchFeature.Log.LogInfo(Value != null ? "Impact marks material: " + Value.name : "Impact marks: no soot material found; using prefab default.");
            }
            catch (Exception e) { ScorchFeature.Log.LogWarning("Impact marks material scan failed: " + e.Message); }
            scan = null; best = null; done = true;
            return true;
        }
        private static void Score(Material candidate)
        {
            if (candidate == null || candidate.shader == null) return;
            string shader = candidate.shader.name.ToLowerInvariant(), name = candidate.name.ToLowerInvariant();
            int s = 0;
            if (shader.Contains("scorchmark")) s += 400;
            if (name.Contains("scorch")) s += 200;
            if (name.Contains("soot") || name.Contains("burn") || name.Contains("char")) s += 120;
            if (name.Contains("crater") || shader.Contains("crater")) s -= 300;
            if (name.Contains("shockwave") || shader.Contains("shockwave")) s -= 300;
            if (s > score) { score = s; best = candidate; }
        }
    }
}
