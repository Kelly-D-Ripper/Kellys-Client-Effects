using System;
using System.Threading;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Audio
{
    // Opt-in extreme preset only. Strikes play immediately or are dropped: no propagation queue.
    internal sealed class ThunderSoundscape
    {
        private const int Voices = 3;
        private const float MaxDistance = 25000f, MinimumInterval = 1.5f;
        private readonly AudioSource[] sources = new AudioSource[Voices];
        private readonly AudioLowPassFilter[] filters = new AudioLowPassFilter[Voices];
        private readonly float[] distances = new float[Voices];
        private readonly AudioClip[] clips = new AudioClip[2];
        private static float[][] baked;
        private static int baking;
        private GameObject root;
        private float nextStrike;
        private bool enabled;
        internal int Queued => 0;
        internal int Played { get; private set; }
        internal int Playing
        {
            get { int n = 0; foreach (var source in sources) if (source != null && source.isPlaying) n++; return n; }
        }

        internal void Tick(bool wantEnabled, bool cockpit)
        {
            enabled = wantEnabled;
            if (!enabled) { Clear(); return; }
            if (Volatile.Read(ref baked) == null && Interlocked.CompareExchange(ref baking, 1, 0) == 0)
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Volatile.Write(ref baked, new[] { Bake(451), Bake(783) }); }
                    catch { Volatile.Write(ref baked, Array.Empty<float[]>()); }
                    finally { Interlocked.Exchange(ref baking, 0); }
                });
            for (int i = 0; i < Voices; i++)
            {
                if (sources[i] == null || !sources[i].isPlaying) continue;
                sources[i].volume = Gain(distances[i], cockpit ? 0.55f : 0.7f);
                filters[i].cutoffFrequency = EnvironmentAudioMath.ThunderCutoff(distances[i], cockpit);
            }
        }

        internal void Play(float distance, uint seed, float pan, bool cockpit)
        {
            if (!enabled || Time.unscaledTime < nextStrike || float.IsNaN(distance) ||
                float.IsInfinity(distance) || distance < 0f || distance > MaxDistance) return;
            var mixer = SoundManager.i != null ? SoundManager.i.EffectsMixer : null;
            float[][] data = Volatile.Read(ref baked);
            if (mixer == null || data == null || data.Length != 2) return;
            EnsureSources(data);
            int free = -1;
            for (int i = 0; i < Voices && free < 0; i++) if (!sources[i].isPlaying) free = i;
            if (free < 0) return;
            var source = sources[free];
            source.outputAudioMixerGroup = mixer;
            source.clip = clips[(int)(seed & 1)];
            source.pitch = 0.9f + WeatherMath.Hash01(seed, 20) * 0.15f;
            source.volume = Gain(distance, cockpit ? 0.55f : 0.7f);
            source.panStereo = Mathf.Clamp(pan, -0.75f, 0.75f);
            distances[free] = distance;
            filters[free].cutoffFrequency = EnvironmentAudioMath.ThunderCutoff(distance, cockpit);
            source.Play(); Played++;
            nextStrike = Time.unscaledTime + MinimumInterval;
        }

        private static float Gain(float distance, float volume) =>
            Mathf.Clamp01(2500f / (2500f + Mathf.Max(0f, distance))) * volume;

        private void EnsureSources(float[][] data)
        {
            if (root != null) return;
            root = new GameObject("BoscaliExtremeThunder");
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i] = AudioClip.Create("BoscaliExtremeThunder" + i, data[i].Length, 1, 22050, false);
                clips[i].SetData(data[i], 0);
            }
            for (int i = 0; i < Voices; i++)
            {
                var voice = new GameObject("ThunderVoice" + i);
                voice.transform.SetParent(root.transform, false);
                sources[i] = voice.AddComponent<AudioSource>();
                sources[i].playOnAwake = false; sources[i].spatialBlend = 0f;
                sources[i].dopplerLevel = 0f; sources[i].reverbZoneMix = 0f;
                filters[i] = voice.AddComponent<AudioLowPassFilter>();
            }
        }

        // Boscali's procedural rumble synthesis, retained without its arrival scheduling.
        internal static float[] Bake(int seed)
        {
            var random = new System.Random(seed); var data = new float[22050 * 6];
            float low = 0f, mid = 0f, peak = 0f, movement = 0f;
            float restrike = 0.11f + (float)random.NextDouble() * 0.16f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / 22050f, white = (float)(random.NextDouble() * 2 - 1);
                low += 0.008f * (white - low); mid += 0.04f * (white - mid);
                float envelope = Math.Min(1f, t * 50f) * (float)Math.Exp(-t / 1.5f) * Math.Min(1f, (6f - t) * 2f);
                movement += 0.0001f * (white - movement);
                float roll = Math.Max(0.45f, Math.Min(1.2f, 0.8f + movement * 20f));
                float crack = (float)Math.Exp(-t * 48f) * Math.Min(1f, t * 600f);
                if (t >= restrike) crack += 0.5f * (float)Math.Exp(-(t - restrike) * 65f) * Math.Min(1f, (t - restrike) * 600f);
                data[i] = (low * 4f + mid * 0.5f) * envelope * roll + (white - mid) * crack * 0.16f;
                peak = Math.Max(peak, Math.Abs(data[i]));
            }
            if (peak > 0f) for (int i = 0; i < data.Length; i++) data[i] *= 0.72f / peak;
            return data;
        }

        internal void Clear()
        {
            enabled = false; nextStrike = 0f;
            foreach (var source in sources) if (source != null) source.Stop();
        }
        internal void Dispose()
        {
            Clear();
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            for (int i = 0; i < Voices; i++) { sources[i] = null; filters[i] = null; }
            for (int i = 0; i < clips.Length; i++) { if (clips[i] != null) UnityEngine.Object.Destroy(clips[i]); clips[i] = null; }
        }
    }
}
