using BoscaliSummer.Core.Math;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>The native angular sun mesh, with stable unlit radiance and per-pixel cloud extinction.</summary>
    internal sealed class SunDisc
    {
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int CloudMaskId = Shader.PropertyToID("_SunCloudMask");
        private static readonly int FallbackId = Shader.PropertyToID("_SunFallbackTransmission");
        private const float Peak = 12000f;

        private GameObject owner;
        private MeshRenderer renderer;
        private Material original, material;
        private Material fallbackOwner;
        private Color fallbackBase, fallbackWritten;
        private Camera camera;
        private bool hooked;

        /// <summary>Automation: the glow written this frame.</summary>
        internal Color Shown { get; private set; }

        internal void Tick(LevelInfo level, Camera view, float cloudTransmission)
        {
            GameObject disc = level != null ? level.sunObject : null;
            if (disc == null || level.sun == null || view == null) { Release(); return; }
            if (disc != owner)
            {
                Release();
                owner = disc;
                renderer = disc.GetComponent<MeshRenderer>();
                original = renderer != null ? renderer.sharedMaterial : null;
                Shader shader = CanopyShaderBundle.GetSunDiscShader();
                if (original != null && shader != null && shader.isSupported)
                {
                    material = new Material(shader) { name = "Boscali Sun Disc", hideFlags = HideFlags.HideAndDontSave };
                    renderer.sharedMaterial = material;
                    RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                    hooked = true;
                }
            }
            camera = view;
            if (original == null || !disc.activeInHierarchy) { Shown = Color.black; return; }

            float elevation = Mathf.Asin(Mathf.Clamp(-level.sun.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            SunDiscMath.Transmission(elevation, view.transform.GlobalPosition().y, out float r, out float g, out float b);
            // The CPU line is for lighting. Raising its sparse result to the eighth power made the whole disc pulse.
            // The shader and the later cloud composite instead extinguish the sun independently at each pixel.
            float scale = Peak * SunDiscMath.AboveHorizon(elevation);
            if (material == null) scale *= Mathf.Pow(Mathf.Clamp01(cloudTransmission), 8f);
            Shown = new Color(r * scale, g * scale, b * scale, 1f);
            if (material != null) material.SetColor(EmissionId, Shown);
            else WriteFallbackEmission(Shown);
            if (material != null) material.SetFloat(FallbackId, Mathf.Pow(Mathf.Clamp01(cloudTransmission), 8f));
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera rendering)
        {
            if (material == null) return;
            // Only the world camera owns the resolved cloud view; auxiliary views must not sample its cover.
            material.SetFloat(CloudMaskId, rendering == camera ? 1f : 0f);
        }

        private void WriteFallbackEmission(Color value)
        {
            // A missing replacement shader must not leave the native sun permanently
            // brightened. Only touch the material still owned by this renderer.
            if (original == null || renderer == null || renderer.sharedMaterial != original ||
                !original.HasProperty(EmissionId)) { RestoreFallbackEmission(); return; }
            if (fallbackOwner != original) RestoreFallbackEmission();
            Color current = original.GetColor(EmissionId);
            if (fallbackOwner == null || !RainAtmosphere.SameColor(current, fallbackWritten))
                fallbackBase = current;
            fallbackOwner = original;
            fallbackWritten = value;
            original.SetColor(EmissionId, value);
        }

        private void RestoreFallbackEmission()
        {
            // Preserve a later native/foreign write; restore only the value still ours.
            if (fallbackOwner != null && fallbackOwner.HasProperty(EmissionId) &&
                RainAtmosphere.SameColor(fallbackOwner.GetColor(EmissionId), fallbackWritten))
                fallbackOwner.SetColor(EmissionId, fallbackBase);
            fallbackOwner = null;
        }

        internal void Release()
        {
            if (hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            hooked = false;
            RestoreFallbackEmission();
            if (renderer != null && material != null && renderer.sharedMaterial == material) renderer.sharedMaterial = original;
            if (material != null) Object.Destroy(material);
            owner = null;
            renderer = null;
            original = material = null;
            camera = null;
            Shown = Color.black;
        }
    }
}
