using System;
using HarmonyLib;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KropClientEffects
{
    // Based on KROP's rendered-brightness metering, bounded to two 16x16 async
    // readbacks per second. Native ambient gain and bloom threshold still run.
    internal static class NightVisionGain
    {
        private static RenderTexture target;
        private static bool pending, subscribed, owned;
        private static int generation;
        private static float nextSample, measured = .53f, correction, nativeValue;
        private static bool nativeOverride;
        private static ColorAdjustments adjusted;
        private static bool warned, samplingFailed;
        private static readonly MeterPass Pass = new MeterPass();
        internal static int SamplesRequested { get; private set; }
        internal static int SamplesCompleted { get; private set; }
        internal static float LastMeasured => measured;
        internal static bool Available => SystemInfo.supportsAsyncGPUReadback;
        private static bool Enabled => Settings.Active && NightVisionEffects.AutoGain.Value && NightVisionEffects.NativeActive && Available && !samplingFailed;

        internal static void EnsureSubscribed()
        {
            if (!Available || subscribed || !Enabled) return;
            subscribed = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        internal static void BeforeNative(ColorAdjustments color)
        {
            if (adjusted != color) Restore();
            // Vanilla's UpdateGain rate limiter may return without writing. Restore
            // the last native value first, so our correction never compounds itself.
            Restore();
        }

        internal static void AfterNative(ColorAdjustments color)
        {
            if (!Enabled || color == null) { Clear(); return; }
            EnsureSubscribed();
            ApplyFromNative(color, Time.unscaledDeltaTime);
        }

        internal static float TargetCorrection(float brightness)
        {
            // Upstream's two thresholds were reversed; keep a valid dead band.
            float targetBrightness = Mathf.Clamp(brightness, .5164319f, .5446009f);
            return Mathf.Clamp((targetBrightness - brightness) * 4.647888f, -4f, 4f);
        }

        internal static void ApplyFromNative(ColorAdjustments color, float delta)
        {
            adjusted = color;
            nativeValue = color.postExposure.value; nativeOverride = color.postExposure.overrideState;
            owned = true;
            correction = Mathf.MoveTowards(correction, TargetCorrection(measured), .5f * Mathf.Clamp(delta, 0f, .25f));
            color.postExposure.Override(Mathf.Clamp(nativeValue + correction, -8f, 8f));
        }

        private static void Restore()
        {
            if (owned && adjusted != null)
            { adjusted.postExposure.value = nativeValue; adjusted.postExposure.overrideState = nativeOverride; }
            owned = false;
        }

        private static bool IsViewCamera(Camera camera)
        {
            var manager = SceneSingleton<CameraStateManager>.i;
            if (manager == null || manager.mainCamera == null || camera == null || camera.cameraType != CameraType.Game) return false;
            if (camera == manager.mainCamera) return true;
            var data = manager.mainCamera.GetUniversalAdditionalCameraData();
            if (data.renderType != CameraRenderType.Base) return false;
            var stack = data.cameraStack;
            if (stack == null) return false;
            for (int i = 0; i < stack.Count; i++) if (stack[i] == camera && camera.isActiveAndEnabled) return true;
            return false;
        }

        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!Enabled || pending || Time.unscaledTime < nextSample || !IsViewCamera(camera)) return;
            camera.GetUniversalAdditionalCameraData().scriptableRenderer?.EnqueuePass(Pass);
        }

        private sealed class MeterPass : ScriptableRenderPass
        {
            internal MeterPass() { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing; }
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                // A cockpit uses a base world camera plus overlays. Meter the last
                // camera's post-processed result, once, while URP still owns it.
                if (!Enabled || pending || Time.unscaledTime < nextSample || !renderingData.cameraData.resolveFinalTarget ||
                    !IsViewCamera(renderingData.cameraData.camera)) return;
                Capture(context, renderingData.cameraData.renderer);
            }
        }

        private static void Capture(ScriptableRenderContext context, ScriptableRenderer renderer)
        {
            nextSample = Time.unscaledTime + .5f;
            try
            {
                RTHandle source = renderer.cameraColorTargetHandle;
                if (source == null || source.rt == null)
                    throw new InvalidOperationException("URP did not expose an intermediate camera colour texture");
                if (target == null)
                {
                    target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32)
                    { name = "Unified NVG gain sample", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                    target.Create();
                }
                CommandBuffer command = CommandBufferPool.Get("Unified NVG gain sample");
                try
                {
                    command.Blit(source, target);
                    // The readback belongs in the SAME command buffer as the copy.
                    // Requesting it immediately from the CPU can read the previous
                    // black texture before ScriptableRenderContext submits its blit.
                    pending = true;
                    int requestedGeneration = generation;
                    command.RequestAsyncReadback(target, 0, TextureFormat.RGBA32, request => Complete(request, requestedGeneration));
                    command.SetRenderTarget(source, renderer.cameraDepthTargetHandle);
                    context.ExecuteCommandBuffer(command);
                    SamplesRequested++;
                }
                finally { CommandBufferPool.Release(command); }
            }
            catch (Exception ex)
            {
                pending = false; samplingFailed = true;
                if (!warned) { warned = true; Plugin.Log?.LogWarning("NVG brightness sampling unavailable; using native gain. " + ex.GetType().Name); }
                Clear();
            }
        }

        private static void Complete(AsyncGPUReadbackRequest request, int requestedGeneration)
        {
            pending = false;
            if (generation != requestedGeneration || !Enabled)
            { ReleaseTarget(); return; }
            if (request.hasError)
            {
                samplingFailed = true;
                if (!warned) { warned = true; Plugin.Log?.LogWarning("NVG brightness readback failed; retaining native gain for this session."); }
                Clear(); return;
            }
            NativeArray<Color32> colors = request.GetData<Color32>();
            float brightest = 0f;
            for (int i = 0; i < colors.Length; i++)
            {
                Color32 c = colors[i];
                brightest = Mathf.Max(brightest, (.3f * c.r + .59f * c.g + .11f * c.b) / 255f);
            }
            if (colors.Length > 0) { measured = brightest; SamplesCompleted++; }
        }

        private static void ReleaseTarget()
        {
            if (target == null) return;
            target.Release(); UnityEngine.Object.Destroy(target); target = null;
        }

        internal static void Clear()
        {
            Restore(); adjusted = null; correction = 0f; measured = .53f;
            if (subscribed) { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; subscribed = false; }
            // A pending request owns the render texture until its callback. It is
            // invalidated, not destroyed beneath the GPU; no second request starts.
            if (target != null || pending) generation++;
            if (!pending) ReleaseTarget();
        }
    }

    [HarmonyPatch(typeof(NightVision), "UpdateGain")]
    internal static class NightVisionGainPatch
    {
        private static void Prefix(ColorAdjustments ___colorAdjustments) { NightVisionGain.BeforeNative(___colorAdjustments); }
        private static void Postfix(ColorAdjustments ___colorAdjustments) { NightVisionGain.AfterNative(___colorAdjustments); }
    }
}
