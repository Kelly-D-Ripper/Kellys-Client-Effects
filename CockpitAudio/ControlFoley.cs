using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliCockpitAudio
{
    // Upstream cockpit-only gear and canopy cues; no delayed playback scheduler.
    internal sealed class ControlFoley
    {
        private AudioSource source;
        private Aircraft bound;
        private LandingGear.GearState gear;
        private float canopyAt = -1f;
        internal ControlFoley(GameObject host)
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0f; source.bypassEffects = true;
        }
        internal void Tick(Aircraft aircraft)
        {
            if (aircraft == null || source == null) return;
            float now = Time.time;
            if (aircraft != bound)
            {
                bound = aircraft; gear = aircraft.gearState;
                canopyAt = aircraft.radarAlt < 3f && aircraft.speed < 5f ? now + 1.2f : -1f;
            }
            if (canopyAt > 0f && aircraft.speed > 15f) canopyAt = -1f;
            if (canopyAt > 0f && now >= canopyAt) { canopyAt = -1f; Play("canopy-close", .7f); }
            if (aircraft.gearState != gear)
            {
                gear = aircraft.gearState;
                if (gear == LandingGear.GearState.Extending || gear == LandingGear.GearState.Retracting) Play("gear-lever", .8f);
            }
        }
        private void Play(string name, float volume)
        {
            var mixer = SoundManager.i != null ? SoundManager.i.InterfaceMixer : null;
            var clip = SfxClips.Get(name);
            if (mixer == null || clip == null || !FxVoiceBus.TryOneShot(clip.length)) return;
            source.outputAudioMixerGroup = mixer; source.PlayOneShot(clip, volume);
        }
        internal void Disable(Aircraft aircraft)
        {
            bound = aircraft; if (aircraft != null) gear = aircraft.gearState;
            canopyAt = -1f; if (source != null) source.Stop();
        }
        internal void Reset() { bound = null; canopyAt = -1f; if (source != null) source.Stop(); }
        internal void Release() { Reset(); if (source != null) Object.Destroy(source); source = null; }
    }
}
