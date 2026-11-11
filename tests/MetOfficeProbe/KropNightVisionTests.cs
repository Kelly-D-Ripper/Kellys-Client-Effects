using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public sealed partial class MetDriver
{
    private async UniTask TestKropNightVision()
    {
        var plugin = ModuleInfos()["aaron.boscali.krop-unified"].Instance;
        var asm = plugin.GetType().Assembly;
        var effects = asm.GetType("KropClientEffects.NightVisionEffects", true);
        var gain = asm.GetType("KropClientEffects.NightVisionGain", true);
        var overlay = asm.GetType("KropClientEffects.NightVisionOverlay", true);
        object Invoke(Type type, string method, params object[] args) => type.GetMethod(method, F | BindingFlags.DeclaredOnly).Invoke(null, args);
        object Read(Type type, string field) => type.GetField(field, F).GetValue(null);
        object Property(Type type, string property) => type.GetProperty(property, F).GetValue(null, null);
        var saved = new Dictionary<ConfigEntryBase, object>();
        ConfigEntry<T> Entry<T>(string key)
        {
            var entry = (ConfigEntry<T>)plugin.Config[new ConfigDefinition("09 Night vision", key)];
            if (!saved.ContainsKey(entry)) saved.Add(entry, entry.BoxedValue);
            return entry;
        }
        string[] toggles = { "Phosphor tint", "NVG bloom", "NVG grain", "NVG sparkles", "Nuclear NVG speckles", "NVG auto gain", "NVG transition" };
        var master = (ConfigEntry<bool>)plugin.Config[new ConfigDefinition("00 General", "Enable pack")];
        bool oldMaster = master.Value;
        var host = new GameObject("NVG ownership fixture"); var volume = host.AddComponent<Volume>();
        volume.isGlobal = false; volume.enabled = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = profile;
        // Assign profile explicitly so fixtures and accessors refer to identical owned bytes.
        volume.profile = profile;
        var nativeMixer = profile.Add<ChannelMixer>(false); nativeMixer.active = false;
        nativeMixer.redOutRedIn.value = 17f; nativeMixer.redOutGreenIn.value = 3f; nativeMixer.redOutBlueIn.value = 7f;
        var nativeGrain = profile.Add<FilmGrain>(false); nativeGrain.active = false; nativeGrain.intensity.value = .21f;
        var nativeBloom = profile.Add<Bloom>(false); nativeBloom.intensity.value = 1.2f; nativeBloom.scatter.value = .42f; nativeBloom.threshold.value = 1.7f;
        var nativeColour = profile.Add<ColorAdjustments>(false); nativeColour.postExposure.value = 2f;
        nativeColour.saturation.Override(-80f);
        nativeColour.colorFilter.Override(new Color(.15f, .65f, .5f)); nativeColour.contrast.Override(5f);
        var actual = NightVision.i;
        Need(actual != null, "NVG probe has real native scene component");
        var actualVolume = (Volume)Field(actual, "postProcessing");
        var actualSelected = AccessTools.Field(typeof(NightVision), "nightVisSelected");
        var actualActive = AccessTools.Field(typeof(NightVision), "nightVisActive");
        bool oldSelected = (bool)actualSelected.GetValue(actual), oldActive = (bool)actualActive.GetValue(actual);
        bool oldVolumeEnabled = actualVolume.enabled, oldLevelPost = LevelInfo.i.PostProcessing.enabled;
        var cameraData = camera.GetUniversalAdditionalCameraData(); bool oldPost = cameraData.renderPostProcessing;
        actualVolume.profile.TryGet(out ColorAdjustments actualColour); actualVolume.profile.TryGet(out Bloom actualBloom);
        float oldActualGain = actualColour.postExposure.value, oldActualThreshold = actualBloom.threshold.value;
        float oldActualSaturation = actualColour.saturation.value;
        bool oldActualSaturationOverride = actualColour.saturation.overrideState;
        Note("NVG native colour baseline: saturation=" + actualColour.saturation.value +
            " override=" + actualColour.saturation.overrideState + " filter=" + actualColour.colorFilter.value +
            " filterOverride=" + actualColour.colorFilter.overrideState + " contrast=" + actualColour.contrast.value +
            " contrastOverride=" + actualColour.contrast.overrideState + " volumeWeight=" + actualVolume.weight);
        float oldGainUpdated = (float)Field(actual, "gainLastUpdated");
        VolumeProfile addedProfile = null;
        try
        {
            foreach (string key in toggles)
            {
                Need(Equals(Entry<bool>(key).DefaultValue, false), "NVG option defaults off: " + key);
                Entry<bool>(key).Value = false;
            }
            master.Value = true;
            Invoke(effects, "Clear"); Invoke(effects, "Update", true, volume);
            Need(nativeMixer.redOutRedIn.value == 17f && !nativeMixer.active && !nativeGrain.active,
                "NVG disabled appearance leaves original components unchanged");
            Need(nativeBloom.intensity.value == 1.2f && Read(effects, "noise") == null && Read(overlay, "root") == null,
                "all appearance options off allocate no textures or overlays");
            Need(!(bool)Read(gain, "subscribed"), "NVG auto gain off has no render subscription");

            Entry<bool>("Phosphor tint").Value = true;
            for (int c = 0; c < 3; c++)
            {
                Entry<int>("Phosphor colour").Value = c; Invoke(effects, "Update", true, volume);
                Color tint = (Color)Invoke(effects, "Tint", c);
                Need(nativeMixer.active && Math.Abs(nativeMixer.redOutGreenIn.value - 59f * tint.r) < .001f &&
                    Math.Abs(nativeMixer.greenOutBlueIn.value - 11f * tint.g) < .001f &&
                    Math.Abs(nativeMixer.blueOutRedIn.value - 30f * tint.b) < .001f, "actual URP mixer applies phosphor " + c);
                Need(nativeColour.saturation.value == 80f && nativeColour.saturation.overrideState &&
                    nativeColour.colorFilter.value == new Color(.15f, .65f, .5f) && nativeColour.contrast.value == 5f,
                    "phosphor reinforces hue without changing native filter or contrast " + c);
            }
            Entry<bool>("Phosphor tint").Value = false; Invoke(effects, "Update", true, volume);
            Need(!nativeMixer.active && nativeMixer.redOutRedIn.value == 17f && nativeMixer.redOutGreenIn.value == 3f &&
                nativeMixer.redOutBlueIn.value == 7f && !nativeMixer.redOutRedIn.overrideState,
                "disabling phosphor restores existing mixer values and overrides");
            Need(nativeColour.saturation.value == -80f && nativeColour.saturation.overrideState,
                "phosphor disable restores native minus-80 saturation and true override");
            nativeColour.saturation.value = -25f; nativeColour.saturation.overrideState = false;
            Entry<bool>("Phosphor tint").Value = true; Invoke(effects, "Update", true, volume); Invoke(effects, "Clear");
            Need(nativeColour.saturation.value == -25f && !nativeColour.saturation.overrideState,
                "phosphor clear restores prior saturation with false override");
            Entry<bool>("Phosphor tint").Value = false; nativeColour.saturation.Override(-80f);
            var random = UnityEngine.Random.state;
            Entry<bool>("NVG bloom").Value = true; Entry<bool>("NVG grain").Value = true;
            Invoke(effects, "Update", true, volume);
            var texture = (Texture2D)Read(effects, "noise"); var first = Read(effects, "pixels"); var second = Read(effects, "scratch");
            Need(texture != null && texture.width == 512 && texture.height == 512 && nativeGrain.texture.value == texture,
                "NVG grain uses bounded real 512px texture on native FilmGrain");
            Need(nativeGrain.active && nativeGrain.intensity.value == Entry<float>("NVG grain strength").Value && nativeGrain.texture.overrideState,
                "NVG grain intensity and custom texture are active");
            Need(nativeBloom.intensity.value == Entry<float>("NVG bloom strength").Value && nativeBloom.scatter.value == .8f &&
                nativeBloom.threshold.value == 1.7f, "NVG bloom changes owned intensity/scatter but preserves native threshold");
            effects.GetField("nextNoiseRotation", F).SetValue(null, 0f); Invoke(effects, "Update", true, volume);
            Need(ReferenceEquals(texture, Read(effects, "noise")) &&
                (ReferenceEquals(first, Read(effects, "pixels")) || ReferenceEquals(second, Read(effects, "pixels"))),
                "grain rotation reuses texture and two existing buffers");
            Need(UnityEngine.Random.state.Equals(random), "NVG noise leaves global gameplay RNG unchanged");
            Entry<bool>("NVG bloom").Value = false; Entry<bool>("NVG grain").Value = false; Invoke(effects, "Update", true, volume);
            Need(nativeBloom.intensity.value == 1.2f && nativeBloom.scatter.value == .42f && !nativeBloom.intensity.overrideState &&
                !nativeGrain.active && nativeGrain.intensity.value == .21f && nativeGrain.texture.value == null,
                "disabling bloom/grain restores existing parameters and texture");

            addedProfile = ScriptableObject.CreateInstance<VolumeProfile>(); addedProfile.Add<Bloom>(false);
            volume.profile = addedProfile; Entry<bool>("Phosphor tint").Value = true; Entry<bool>("NVG grain").Value = true;
            Invoke(effects, "Update", true, volume);
            Need(addedProfile.TryGet<ChannelMixer>(out _) && addedProfile.TryGet<FilmGrain>(out _), "missing URP overrides added locally");
            Invoke(effects, "CameraChanged");
            Need(!addedProfile.TryGet<ChannelMixer>(out _) && !addedProfile.TryGet<FilmGrain>(out _), "camera reset removes only added overrides");
            volume.profile = profile;
            foreach (string key in toggles) Entry<bool>(key).Value = false;

            Entry<bool>("NVG sparkles").Value = true; Entry<int>("NVG speck count").Value = 100;
            Invoke(effects, "Update", true, volume);
            var speckGraphic = (Graphic)Read(overlay, "specks");
            Need((int)speckGraphic.GetType().GetProperty("Count", F).GetValue(speckGraphic, null) == 100,
                "NVG sensor specks use requested count");
            Entry<bool>("Nuclear NVG speckles").Value = true;
            Invoke(effects, "Detonation", camera.transform.position + camera.transform.forward * 100f);
            Invoke(effects, "Update", true, volume);
            Need((int)speckGraphic.GetType().GetProperty("Count", F).GetValue(speckGraphic, null) == 400,
                "visible nuclear burst adds bounded 300-speck boost to local overlay");
            using (var mesh = new VertexHelper())
            {
                // Graphic also retains the obsolete Mesh overload. Select the
                // VertexHelper override explicitly instead of resolving by name.
                speckGraphic.GetType().GetMethod("OnPopulateMesh", F | BindingFlags.DeclaredOnly,
                    null, new[] { typeof(VertexHelper) }, null).Invoke(speckGraphic, new object[] { mesh });
                Need(mesh.currentVertCount == 1600 && mesh.currentIndexCount == 2400, "400 specks render as one bounded mesh");
            }
            Need(!speckGraphic.raycastTarget && speckGraphic.GetComponentInParent<GraphicRaycaster>() == null,
                "NVG overlay does not intercept mouse controls");
            Need((int)Invoke(effects, "NuclearBoost", 20f) > 100 && (int)Invoke(effects, "NuclearBoost", 128f) == 0,
                "nuclear speck burst decays and expires");
            Invoke(effects, "CameraChanged");
            Need(float.IsNegativeInfinity((float)Read(effects, "detonationTime")), "camera change clears nuclear speck history");
            Invoke(effects, "Update", true, volume);
            Invoke(effects, "Detonation", camera.transform.position - camera.transform.forward * 100f);
            Need(float.IsNegativeInfinity((float)Read(effects, "detonationTime")), "offscreen nuclear burst does not create NVG flash");
            Entry<bool>("NVG sparkles").Value = false; Entry<bool>("Nuclear NVG speckles").Value = false;
            Invoke(effects, "Update", true, volume);
            Need(!speckGraphic.gameObject.activeSelf, "speck toggles off immediately hide overlay");

            Entry<bool>("NVG transition").Value = true;
            int flips = 0;
            Invoke(overlay, "Begin", true, actual, (Action)(() => flips++));
            overlay.GetField("phaseStart", F).SetValue(null, Time.unscaledTime - 1f); Invoke(overlay, "Tick");
            Need(flips == 1 && (bool)Property(overlay, "Busy"), "raise/lower transition flips once under full cover");
            overlay.GetField("phaseStart", F).SetValue(null, Time.unscaledTime - 1f); Invoke(overlay, "Tick");
            Need(flips == 1 && !(bool)Property(overlay, "Busy"), "transition finishes without repeating its manual action");
            Invoke(overlay, "Begin", true, actual, (Action)(() => flips++)); Invoke(effects, "CameraChanged");
            overlay.GetField("phaseStart", F).SetValue(null, Time.unscaledTime - 1f); Invoke(overlay, "Tick");
            Need(flips == 1 && !(bool)Property(overlay, "Busy"), "camera switch cancels pending toggle rather than activating NVG later");

            if (!(bool)AccessTools.Method(typeof(NightVision), "BlockToggle").Invoke(actual, null))
            {
                actualSelected.SetValue(actual, false);
                NightVision.Toggle(); NightVision.Toggle();
                Need(!(bool)actualSelected.GetValue(actual) && (bool)Property(overlay, "Busy"), "actual manual toggle waits under animation and ignores repeat");
                overlay.GetField("phaseStart", F).SetValue(null, Time.unscaledTime - 1f); Invoke(overlay, "Tick");
                Need((bool)actualSelected.GetValue(actual), "actual native manual toggle executes at cover midpoint");
                Invoke(overlay, "CancelTransition"); NightVision.Toggle();
                Invoke(effects, "Clear"); Invoke(overlay, "Tick");
                Need((bool)actualSelected.GetValue(actual), "clearing a pending manual transition never fires a delayed toggle");
            }
            else Note("OPEN manual NVG animation native input path blocked by current game UI; transition timing/cancellation tested separately");
            Entry<bool>("NVG transition").Value = false;

            gain.GetField("measured", F).SetValue(null, .95f);
            Invoke(gain, "ApplyFromNative", nativeColour, .25f);
            Need(nativeColour.postExposure.value < 2f && nativeColour.postExposure.overrideState, "bright sample reduces native gain");
            Invoke(gain, "BeforeNative", nativeColour);
            Need(nativeColour.postExposure.value == 2f && !nativeColour.postExposure.overrideState, "native gain restored before its rate-limited update");
            Invoke(gain, "Clear"); gain.GetField("measured", F).SetValue(null, .1f);
            Invoke(gain, "ApplyFromNative", nativeColour, .25f);
            Need(nativeColour.postExposure.value > 2f, "dark sample increases gain within bounded speed");
            Invoke(gain, "Clear");
            Need(nativeColour.postExposure.value == 2f && !nativeColour.postExposure.overrideState &&
                (float)Invoke(gain, "TargetCorrection", .53f) == 0f, "gain cleanup restores parameters and valid dead band produces zero correction");

            // Render the real native NVG volume, not just a synthetic shader fixture.
            foreach (string key in toggles) Entry<bool>(key).Value = false;
            Invoke(effects, "Clear");
            actualSelected.SetValue(actual, true); actualActive.SetValue(actual, true);
            actualVolume.enabled = true; LevelInfo.i.PostProcessing.enabled = false; cameraData.renderPostProcessing = true;
            AccessTools.Field(typeof(NightVision), "gainLastUpdated").SetValue(actual, Time.unscaledTime - 10f);
            AccessTools.Method(typeof(NightVision), "UpdateGain").Invoke(actual, null);
            Invoke(effects, "Update", true, actualVolume); Capture("nvg-vanilla.png");
            Entry<bool>("Phosphor tint").Value = true;
            foreach (int colour in new[] { 0, 1, 2 })
            {
                Entry<int>("Phosphor colour").Value = colour; Invoke(effects, "Update", true, actualVolume);
                Need(actualColour.saturation.value == 80f && actualColour.saturation.overrideState,
                    "real native NVG profile uses upstream phosphor saturation " + colour);
                Capture("nvg-" + new[] { "green", "white", "amber" }[colour] + ".png");
            }
            Invoke(effects, "CameraChanged");
            Need(actualColour.saturation.value == oldActualSaturation && actualColour.saturation.overrideState == oldActualSaturationOverride,
                "camera change restores real native NVG saturation");
            Entry<int>("Phosphor colour").Value = 0; Entry<bool>("NVG grain").Value = true; Entry<bool>("NVG bloom").Value = true;
            Invoke(effects, "Update", true, actualVolume); Capture("nvg-bloom-grain.png");
            foreach (string key in toggles) Entry<bool>(key).Value = false;
            Invoke(effects, "Update", true, actualVolume);
            Need(actualColour.saturation.value == oldActualSaturation && actualColour.saturation.overrideState == oldActualSaturationOverride,
                "real native NVG saturation restored before comparison capture");
            Capture("nvg-restored.png");

            if (SystemInfo.supportsAsyncGPUReadback)
            {
                Entry<bool>("NVG auto gain").Value = true; Invoke(effects, "Update", true, actualVolume);
                int completed = (int)Property(gain, "SamplesCompleted"), requested = (int)Property(gain, "SamplesRequested");
                gain.GetField("nextSample", F).SetValue(null, 0f);
                Capture("nvg-auto-gain-sample.png");
                Need((int)Property(gain, "SamplesRequested") > requested, "native camera render issues actual bounded async gain sample");
                await Until(() => (int)Property(gain, "SamplesCompleted") > completed, 8, "NVG GPU brightness sample completes");
                float measured = (float)Property(gain, "LastMeasured");
                Need(measured > .001f && measured <= 1f, "NVG async sample reads nonzero rendered luminance: " + measured);
                // Let the ordinary game camera stack render. No capture request
                // is issued during this wait: both render paths must feed the meter.
                int ordinaryStart = (int)Property(gain, "SamplesCompleted");
                gain.GetField("nextSample", F).SetValue(null, 0f);
                int beginCount = 0, endCount = 0, viewBeginCount = 0, firstFrame = Time.frameCount;
                var seenCameras = new HashSet<string>();
                void OnCameraBegin(ScriptableRenderContext context, Camera current)
                {
                    beginCount++;
                    if ((bool)Invoke(gain, "IsViewCamera", current)) viewBeginCount++;
                    seenCameras.Add(current.name + "(" + current.GetInstanceID() + ")");
                }
                void OnCameraEnd(ScriptableRenderContext context, Camera current) { endCount++; }
                void ReportOrdinary(string stage)
                {
                    Note("NVG ordinary " + stage + ": frames=" + (Time.frameCount - firstFrame) +
                        " begin=" + beginCount + " end=" + endCount + " viewBegin=" + viewBeginCount +
                        " selected=" + actualSelected.GetValue(actual) + " nativeActive=" + actualActive.GetValue(actual) +
                        " gainEnabled=" + Property(gain, "Enabled") + " subscribed=" + Read(gain, "subscribed") +
                        " failed=" + Read(gain, "samplingFailed") + " pending=" + Read(gain, "pending") +
                        " requested=" + Property(gain, "SamplesRequested") + " completed=" + Property(gain, "SamplesCompleted") +
                        " measured=" + Property(gain, "LastMeasured") + " camera=" + camera.name +
                        " enabled=" + camera.enabled + " active=" + camera.gameObject.activeInHierarchy +
                        " main=" + (camera == SceneSingleton<CameraStateManager>.i.mainCamera) +
                        " target=" + (camera.targetTexture == null ? "backbuffer" : camera.targetTexture.name) +
                        " focus=" + Application.isFocused + " background=" + Application.runInBackground +
                        " renderInterval=" + OnDemandRendering.renderFrameInterval +
                        " seen=" + string.Join(",", seenCameras));
                }
                RenderPipelineManager.beginCameraRendering += OnCameraBegin;
                RenderPipelineManager.endCameraRendering += OnCameraEnd;
                RenderTexture ordinaryTarget = null, previousTarget = camera.targetTexture;
                bool ordinaryCompleted = false;
                try
                {
                    ReportOrdinary("start");
                    float deadline = Time.realtimeSinceStartup + 8f;
                    while ((int)Property(gain, "SamplesCompleted") <= ordinaryStart && Time.realtimeSinceStartup < deadline)
                        await UniTask.Yield();
                    ReportOrdinary("backbuffer");
                    if ((int)Property(gain, "SamplesCompleted") <= ordinaryStart && beginCount == 0 && Time.frameCount > firstFrame)
                    {
                        // A hidden Windows player may run Update without rendering
                        // its swap chain. Observe that explicitly, then bind a real
                        // destination; the player's normal render loop still owns
                        // the camera (no SubmitRenderRequest/Camera.Render call).
                        Note("OPEN hidden player produced no ordinary camera renders; hardware backbuffer acceptance remains separate");
                        ordinaryTarget = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32)
                        { name = "NVG ordinary player-loop fixture" };
                        ordinaryTarget.Create(); camera.targetTexture = ordinaryTarget;
                        gain.GetField("nextSample", F).SetValue(null, 0f);
                        deadline = Time.realtimeSinceStartup + 8f;
                        while ((int)Property(gain, "SamplesCompleted") <= ordinaryStart && Time.realtimeSinceStartup < deadline)
                            await UniTask.Yield();
                    }
                    ordinaryCompleted = (int)Property(gain, "SamplesCompleted") > ordinaryStart;
                    if (!ordinaryCompleted && beginCount == 0 && Time.frameCount > firstFrame)
                        Note("OPEN ordinary player-loop NVG metering unverified: hidden window suppresses all camera rendering, including target-texture camera; no product failure observed");
                    else
                    {
                        Need(ordinaryCompleted, "ordinary game camera stack produces an NVG GPU sample without a capture request");
                        Need(viewBeginCount > 0, "ordinary sample traverses actual player camera rendering callbacks");
                    }
                }
                finally
                {
                    ReportOrdinary("finish");
                    RenderPipelineManager.beginCameraRendering -= OnCameraBegin;
                    RenderPipelineManager.endCameraRendering -= OnCameraEnd;
                    camera.targetTexture = previousTarget;
                    if (ordinaryTarget != null) { ordinaryTarget.Release(); Destroy(ordinaryTarget); }
                }
                float ordinaryMeasured = (float)Property(gain, "LastMeasured");
                if (ordinaryCompleted)
                    Need(ordinaryMeasured > .001f && ordinaryMeasured <= 1f,
                        "ordinary camera NVG readback contains rendered luminance: " + ordinaryMeasured);
                gain.GetField("nextSample", F).SetValue(null, 0f); Capture("nvg-auto-gain-pending-disable.png");
                Entry<bool>("NVG auto gain").Value = false; Invoke(effects, "Update", true, actualVolume);
                Need(!(bool)Read(gain, "subscribed") && !(bool)Read(gain, "owned"), "disable unsubscribes and restores gain during pending readback");
                await Until(() => !(bool)Read(gain, "pending"), 8, "disabled gain callback retires pending GPU work");
                Need((RenderTexture)Read(gain, "target") == null, "pending callback releases retired render texture without reactivation");
            }
            else
            {
                Entry<bool>("NVG auto gain").Value = true; Invoke(effects, "Update", true, actualVolume);
                Need(!(bool)Read(gain, "subscribed"), "unsupported async readback leaves native gain untouched");
                Note("OPEN GPU readback unavailable on this graphics device");
            }

            Entry<bool>("Phosphor tint").Value = true; Entry<bool>("NVG grain").Value = true;
            Invoke(effects, "Update", true, volume); master.Value = false; Invoke(effects, "Tick");
            Need(!nativeMixer.active && nativeMixer.redOutRedIn.value == 17f && !nativeGrain.active && Read(overlay, "root") == null &&
                Read(effects, "noise") == null && nativeColour.saturation.value == -80f && nativeColour.saturation.overrideState,
                "pack master off restores profile and removes owned NVG resources");
        }
        finally
        {
            Invoke(effects, "Clear");
            foreach (var pair in saved) pair.Key.BoxedValue = pair.Value;
            master.Value = oldMaster;
            actualSelected.SetValue(actual, oldSelected); actualActive.SetValue(actual, oldActive);
            actualVolume.enabled = oldVolumeEnabled; LevelInfo.i.PostProcessing.enabled = oldLevelPost; cameraData.renderPostProcessing = oldPost;
            actualColour.postExposure.value = oldActualGain; actualBloom.threshold.value = oldActualThreshold;
            actualColour.saturation.value = oldActualSaturation; actualColour.saturation.overrideState = oldActualSaturationOverride;
            AccessTools.Field(typeof(NightVision), "gainLastUpdated").SetValue(actual, oldGainUpdated);
            Destroy(host); Destroy(profile); if (addedProfile != null) Destroy(addedProfile);
        }
        await UniTask.Yield();
    }
}
