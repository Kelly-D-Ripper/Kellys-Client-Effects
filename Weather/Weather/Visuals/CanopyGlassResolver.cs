using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    internal readonly struct CanopySurface
    {
        internal readonly MeshRenderer Renderer;
        internal readonly Mesh Mesh;
        internal readonly int Submesh;
        internal readonly int Score;
        internal readonly LODGroup Lod;
        internal CanopySurface(MeshRenderer renderer, Mesh mesh, int submesh, int score)
        {
            Renderer = renderer; Mesh = mesh; Submesh = submesh; Score = score;
            Lod = renderer.GetComponentInParent<LODGroup>();
        }
    }

    // Search ownship near the cockpit eye, using metadata rather than native vertex reads.
    internal static class CanopyGlassResolver
    {
        internal const int MaxSurfaces = 8;
        private const int MaxNodes = 1024;
        private static readonly List<CanopySurface> surfaces = new List<CanopySurface>(MaxSurfaces);
        private static readonly List<Material> materials = new List<Material>(16);
        private static readonly Stack<Transform> pending = new Stack<Transform>(MaxNodes);
        private static int aircraftId, resolvedMask;
        private static float retryAt;

        internal static IReadOnlyList<CanopySurface> Resolve(Transform root, Vector3 eye, int layerMask = -1)
            => ResolveWithOwnedRenderers(root, eye, layerMask, null);

        internal static IReadOnlyList<CanopySurface> ResolveWithOwnedRenderers(Transform root, Vector3 eye, int layerMask, Renderer[] ownedRenderers)
        {
            if (root == null) { ResetForScene(); return surfaces; }
            if (aircraftId != root.GetInstanceID() || resolvedMask != layerMask)
            {
                ResetForScene();
                aircraftId = root.GetInstanceID();
                resolvedMask = layerMask;
            }
            bool removed = false;
            for (int i = surfaces.Count - 1; i >= 0; i--)
            {
                var pane = surfaces[i];
                if (pane.Renderer == null || pane.Mesh == null || !pane.Renderer.enabled ||
                    !pane.Renderer.gameObject.activeInHierarchy || pane.Renderer.forceRenderingOff ||
                    (layerMask & (1 << pane.Renderer.gameObject.layer)) == 0)
                {
                    surfaces.RemoveAt(i);
                    removed = true;
                }
            }
            // Camera/aircraft view changes may disable a formerly selected glass. Search
            // again promptly instead of caching disabled panes for the entire flight.
            if (removed) retryAt = 0f;
            if (surfaces.Count > 0 || Time.unscaledTime < retryAt) return surfaces;
            // An aircraft may finish activating its cockpit after the first attempts.
            // Keep trying at two-second intervals while the cockpit needs rain, rather
            // than giving up permanently after three attempts.
            retryAt = Time.unscaledTime + 2f;
            pending.Push(root);
            int visited = 0;
            try
            {
                while (pending.Count > 0 && visited < MaxNodes)
                {
                    Transform node = pending.Pop();
                    visited++;
                    if (node == null) continue;
                    AddRenderer(node.GetComponent<MeshRenderer>(), eye, layerMask);
                    // Bound transforms too: empty hierarchies must not defeat the search cap.
                    int children = Math.Min(node.childCount, MaxNodes - visited - pending.Count);
                    for (int i = children - 1; i >= 0; i--) pending.Push(node.GetChild(i));
                }
                // Some aircraft detach the canopy from the cockpit hierarchy. The game
                // still explicitly records those panes in THIS aircraft's cockpit-renderer
                // array. Scan only that ownership list, never nearby scene geometry.
                if (ownedRenderers != null)
                    for (int i = 0; i < Math.Min(ownedRenderers.Length, MaxNodes); i++)
                        AddRenderer(ownedRenderers[i] as MeshRenderer, eye, layerMask);
            }
            catch (MissingReferenceException) { surfaces.Clear(); }
            finally { pending.Clear(); materials.Clear(); }
            return surfaces;
        }

        private static void AddRenderer(MeshRenderer renderer, Vector3 eye, int layerMask)
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                !renderer.gameObject.activeInHierarchy || (layerMask & (1 << renderer.gameObject.layer)) == 0) return;
            var filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return;
            Bounds bounds = renderer.bounds;
            float distance = Vector3.Distance(bounds.ClosestPoint(eye), eye);
            if (!CanopyScoring.PlausibleBounds(distance, bounds.size.magnitude)) return;
            renderer.GetSharedMaterials(materials);
            int count = Math.Min(16, Math.Min(materials.Count, mesh.subMeshCount));
            for (int slot = 0; slot < count; slot++)
            {
                Material mat = materials[slot];
                if (mat == null || mat.shader == null || mesh.GetIndexCount(slot) == 0) continue;
                bool transparent = mat.renderQueue >= 2501 ||
                    mat.GetTag("RenderType", false) == "Transparent" ||
                    (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f);
                int score = CanopyScoring.ScoreSurface(renderer.name, mat.name, mat.shader.name,
                    transparent, distance, bounds.size.magnitude);
                if (score <= 0) continue;
                bool duplicate = false;
                for (int i = 0; i < surfaces.Count; i++)
                    if (surfaces[i].Renderer == renderer && surfaces[i].Submesh == slot) { duplicate = true; break; }
                if (duplicate) continue;
                int at = 0;
                while (at < surfaces.Count && surfaces[at].Score >= score) at++;
                if (at >= MaxSurfaces) continue;
                if (surfaces.Count == MaxSurfaces) surfaces.RemoveAt(MaxSurfaces - 1);
                surfaces.Insert(at, new CanopySurface(renderer, mesh, slot, score));
            }
        }

        internal static void ResetForScene()
        {
            surfaces.Clear(); materials.Clear(); pending.Clear();
            aircraftId = 0; retryAt = 0f;
        }
    }
}
