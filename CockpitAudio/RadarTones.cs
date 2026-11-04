using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliCockpitAudio
{
    // Capture native references; replace only the argument of the current playback call.
    // No AudioSource, cached alarm clip, mixer or other component is modified.
    internal static class RadarTones
    {
        private static readonly HashSet<AudioClip> newClips = new HashSet<AudioClip>();
        private static readonly HashSet<AudioClip> sweepClips = new HashSet<AudioClip>();
        internal static void Capture(AudioClip ___radarWarningNew, AudioClip ___radarWarningExisting)
        {
            if (___radarWarningNew != null) newClips.Add(___radarWarningNew);
            if (___radarWarningExisting != null) sweepClips.Add(___radarWarningExisting);
        }
        internal static void Replace(ref AudioClip audioClip)
        {
            if (audioClip == null || CockpitAudioRuntime.Live == null || !CockpitAudioRuntime.Live.SoftRadarActive) return;
            if (newClips.Contains(audioClip)) audioClip = SfxClips.Get("radar-new") ?? audioClip;
            else if (sweepClips.Contains(audioClip)) audioClip = SfxClips.Get("radar-sweep") ?? audioClip;
        }
        internal static void Clear() { newClips.Clear(); sweepClips.Clear(); }
    }
}
