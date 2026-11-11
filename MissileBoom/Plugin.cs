using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Boscali.ExperimentalMissileBoom
{
    // Independent local trigger, with optional recordings from KROP 1.1.2.
    // This never registers a missile with game simulation or delays native audio.
    [BepInPlugin("aaron.boscali.missile-unified", "Unified Experimental Missile Boom", "0.1.1")]
    [BepInIncompatibility("aaron.boscali.missileboom")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            var setting = Config.Bind("Audio", "Missile sonic boom", false,
                "Local missile boom using a bounded approximate Mach-cone detector. No sound travel delay or changes to missile simulation.");
            var recordings = Config.Bind("Audio", "KROP missile recordings", false,
                "Use KROP's three missile-boom recordings without consecutive repeats. Requires Missile sonic boom; native audio is the fallback.");
            if (Application.isBatchMode || GameManager.IsHeadless || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var host = new GameObject("Unified missile boom runtime") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            host.AddComponent<MissileBoomRuntime>().Initialize(setting, recordings, Logger);
            Logger.LogInfo("Unified missile boom experiment loaded.");
        }
    }
    internal sealed class MissileBoomRuntime : MonoBehaviour
    {
        private const int MaxTracked = 64;
        private const float ScanPeriod = 0.5f;
        private const float SamplePeriod = 0.05f;
        private const float MaxDistance = 1200f;
        private const int MaxPlaying = 8;
        private readonly Dictionary<Missile, State> tracked = new Dictionary<Missile, State>();
        private readonly List<Missile> remove = new List<Missile>();
        private readonly List<GameObject> audioObjects = new List<GameObject>();
        private ConfigEntry<bool> enabledSetting, recordingsSetting;
        private readonly MissileBoomClips recordings = new MissileBoomClips();
        private BepInEx.Logging.ManualLogSource log;
        private float nextScan, nextSample, lastSound = -100f;
        private bool suppressed, usedRecordings;
        private sealed class State { internal bool InCone; internal bool Heard; }

        internal void Initialize(ConfigEntry<bool> setting, ConfigEntry<bool> customRecordings, BepInEx.Logging.ManualLogSource logger)
        {
            enabledSetting = setting; recordingsSetting = customRecordings; log = logger;
            SceneManager.activeSceneChanged += SceneChanged;
        }
        private void SceneChanged(Scene previous, Scene current) { Clear(); }

        private void Update()
        {
            try { Tick(); }
            catch (Exception error)
            {
                Clear();
                enabled = false;
                log.LogError("Missile boom experiment disabled after a local error: " + error);
            }
        }

        private void Tick()
        {
            // Full KROP has its own missile boom. Check after all plugins have loaded as well as at start.
            bool originalKrop = Chainloader.PluginInfos.ContainsKey("pavehog727.qolrealismfixes");
            if (!enabledSetting.Value || originalKrop)
            {
                if (originalKrop && !suppressed) { log.LogInfo("Missile boom suppressed because original KROP is loaded."); suppressed = true; }
                Clear(); return;
            }
            suppressed = false;
            if (usedRecordings != recordingsSetting.Value)
            {
                StopSounds();
                recordings.Clear();
                usedRecordings = recordingsSetting.Value;
            }
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (camera == null) { Clear(); return; }
            Vector3 listener = camera.mainCamera != null ? camera.mainCamera.transform.position : camera.transform.position;
            float now = Time.unscaledTime;
            if (now >= nextScan)
            {
                nextScan = now + ScanPeriod;
                // Destroyed Unity objects otherwise remain as managed references for the
                // rest of a mission, even though each sound has already finished playing.
                audioObjects.RemoveAll(obj => obj == null);
                remove.Clear();
                foreach (var item in tracked)
                {
                    Missile missile = item.Key;
                    if (missile == null || missile.rb == null ||
                        Vector3.SqrMagnitude(missile.transform.position - listener) > MaxDistance * MaxDistance)
                        remove.Add(missile);
                }
                foreach (Missile missile in remove) tracked.Remove(missile);
                audioObjects.RemoveAll(obj => obj == null);
                if (tracked.Count < MaxTracked)
                {
                    // Periodic discovery avoids detouring missile or sonic-boom game methods.
                    foreach (Missile missile in UnityEngine.Object.FindObjectsOfType<Missile>())
                    {
                        if (tracked.Count >= MaxTracked) break;
                        if (missile != null && missile.rb != null && missile.rb.velocity.sqrMagnitude > 40000f && Vector3.SqrMagnitude(missile.transform.position - listener) < MaxDistance * MaxDistance && !tracked.ContainsKey(missile))
                            tracked.Add(missile, new State());
                    }
                }
            }
            if (now < nextSample) return;
            nextSample = now + SamplePeriod;
            foreach (var item in tracked)
            {
                Missile missile = item.Key;
                if (missile == null || missile.rb == null || item.Value.Heard) continue;
                Vector3 offset = missile.transform.position - listener;
                float distance = offset.magnitude;
                float speed = missile.rb.velocity.magnitude;
                float soundSpeed = LevelInfo.GetSpeedOfSound(missile.transform.position.y - Datum.LocalSeaY);
                if (soundSpeed <= 0f || speed <= soundSpeed || distance > MaxDistance || distance < 1f)
                { item.Value.InCone = false; continue; }
                // A listener behind the projectile and inside its approximate Mach cone.
                float facing = Vector3.Dot(offset / distance, missile.rb.velocity / speed);
                float mach = speed / soundSpeed;
                bool inside = facing > Mathf.Sqrt(1f - 1f / (mach * mach));
                if (inside && !item.Value.InCone && now - lastSound >= 0.25f)
                {
                    if (Play(listener, missile.transform.position, distance))
                    {
                        item.Value.Heard = true;
                        lastSound = now;
                    }
                }
                item.Value.InCone = inside && item.Value.Heard;
            }
        }

        private bool Play(Vector3 listener, Vector3 missilePosition, float distance)
        {
            if (SoundManager.i == null) return false;
            audioObjects.RemoveAll(obj => obj == null);
            if (audioObjects.Count >= MaxPlaying) return false;
            AudioClip clip = recordingsSetting.Value ? recordings.Next(log) : null;
            bool custom = clip != null;
            if (clip == null) clip = GameAssets.i != null ? GameAssets.i.sonicBoom : null;
            if (clip == null) return false;
            var obj = new GameObject("Experimental missile sonic boom");
            audioObjects.Add(obj);
            if (Datum.origin != null) obj.transform.SetParent(Datum.origin, false);
            // Keep the sound source close to the listener-facing shock front, not a moving missile.
            obj.transform.position = Vector3.Lerp(listener, missilePosition, Mathf.Min(1f, 250f / distance));
            AudioSource source = obj.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = SoundManager.i.HeavyEffectsMixer;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.minDistance = 80f;
            source.maxDistance = MaxDistance;
            source.volume = 0.35f;
            source.pitch = custom ? 1f : 1.25f;
            source.clip = clip;
            source.Play();
            Destroy(obj, source.clip.length / source.pitch + 0.25f);
            return true;
        }

        private void Clear()
        {
            tracked.Clear();
            remove.Clear();
            StopSounds();
            recordings.Clear();
            nextScan = nextSample = 0f;
            lastSound = -100f;
        }

        private void StopSounds()
        {
            foreach (GameObject obj in audioObjects) if (obj != null)
            {
                var source = obj.GetComponent<AudioSource>();
                if (source != null) source.Stop();
                Destroy(obj);
            }
            audioObjects.Clear();
        }

        private void OnDestroy() { SceneManager.activeSceneChanged -= SceneChanged; Clear(); }
    }
}


