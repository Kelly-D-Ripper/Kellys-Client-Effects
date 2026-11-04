using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace BoscaliClientExperimental.Weather
{
    // Client ownship warning speech only. No sound propagation patches, delayed
    // playback, or modifications to native flight/audio behaviours.
    internal sealed class CockpitVoiceWarnings : MonoBehaviour
    {
        private ConfigEntry<bool> enabledSetting;
        private readonly WarningMath rules = new WarningMath();
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private AudioSource source;
        private Aircraft bound;
        private Warning playing;

        internal static void Install(BaseUnityPlugin plugin)
        {
            var controller = Plugin.EffectsHost.AddComponent<CockpitVoiceWarnings>();
            controller.enabledSetting = plugin.Config.Bind("Audio", "Cockpit voice warnings", false,
                "Ownship PULL UP, TERRAIN, OVER G and BINGO. Client-only, plays immediately; generated voice assets require provenance review.");
        }
        private void Update()
        {
            if (enabledSetting == null || !enabledSetting.Value)
            {
                StopWarning();
                bound = null; return;
            }
            GameManager.GetLocalAircraft(out Aircraft aircraft);
            if (aircraft == null || aircraft.disabled || aircraft.rb == null)
            {
                StopWarning();
                bound = null; return;
            }
            if (bound != aircraft) { StopWarning(); bound = aircraft; rules.Reset(); }
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.loop = false; source.playOnAwake = false;
                source.spatialBlend = 0f; source.bypassEffects = true;
            }
            var mixer = SoundManager.i != null ? SoundManager.i.InterfaceMixer : null;
            if (mixer == null) return;
            source.outputAudioMixerGroup = mixer;
            Pilot pilot = aircraft.pilots != null && aircraft.pilots.Length > 0 ? aircraft.pilots[0] : null;
            var input = new WarningInput { RadarAltM = aircraft.radarAlt, ClimbMps = aircraft.rb.velocity.y,
                G = pilot != null ? pilot.gForce : 1f, Fuel01 = aircraft.GetFuelLevel(),
                GearDown = aircraft.gearDeployed, Airborne = aircraft.radarAlt > 2f };
            Warning warning = rules.Step(input, Time.time);
            if (warning == Warning.None) return;
            if (source.isPlaying)
            {
                if (warning <= playing) return;
                source.Stop(); // an urgent terrain call can interrupt a lower-priority voice
            }
            playing = warning;
            string name = warning == Warning.PullUp ? "vo-pull-up" : warning == Warning.Terrain ? "vo-terrain" :
                warning == Warning.OverG ? "vo-over-g" : "vo-bingo";
            AudioClip clip = GetClip(name);
            if (clip != null) { source.clip = clip; source.Play(); }
        }
        private void StopWarning()
        {
            if (source != null && source.isPlaying) source.Stop();
            playing = Warning.None;
        }
        private AudioClip GetClip(string name)
        {
            if (clips.TryGetValue(name, out AudioClip clip)) return clip;
            try
            {
                using (Stream stream = typeof(CockpitVoiceWarnings).Assembly.GetManifestResourceStream("BoscaliClientExperimental.Voice." + name + ".wav"))
                {
                    if (stream == null || stream.Length > 1024 * 1024) { clips[name] = null; return null; }
                    using (var data = new MemoryStream())
                    {
                        stream.CopyTo(data);
                        float[] samples = PcmWav.Decode(data.ToArray(), 22050);
                        if (samples != null && samples.Length > 0)
                        {
                            clip = AudioClip.Create("Boscali." + name, samples.Length, 1, 22050, false);
                            clip.SetData(samples, 0);
                        }
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Boscali experiment] Warning audio unavailable: " + e.Message); }
            clips[name] = clip;
            return clip;
        }
        private void OnDestroy()
        {
            if (source != null) Destroy(source);
            foreach (AudioClip clip in clips.Values) if (clip != null) Destroy(clip);
            clips.Clear();
        }
    }
}
