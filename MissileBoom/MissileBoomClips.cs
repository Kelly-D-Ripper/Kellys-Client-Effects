using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace Boscali.ExperimentalMissileBoom
{
    // Recordings supplied by KROP 1.1.2. Decoder/selection are local presentation only.
    internal sealed class MissileBoomClips
    {
        private readonly System.Random random = new System.Random();
        private AudioClip[] clips;
        private bool attempted;
        private int previous = -1;

        internal AudioClip Next(ManualLogSource log)
        {
            if (!attempted)
            {
                attempted = true;
                var loaded = new List<AudioClip>(3);
                for (int i = 1; i <= 3; i++)
                {
                    string name = "MissileBoom" + i + ".wav";
                    try
                    {
                        using (Stream stream = typeof(MissileBoomClips).Assembly.GetManifestResourceStream("UnifiedEffects.MissileBoom." + name))
                        {
                            if (stream == null) throw new InvalidDataException("Missing embedded recording " + name);
                            using (var buffer = new MemoryStream())
                            {
                                stream.CopyTo(buffer);
                                if (!PcmWave.TryDecode16(buffer.ToArray(), out float[] samples, out int channels, out int rate))
                                    throw new InvalidDataException("Invalid PCM recording " + name);
                                AudioClip clip = AudioClip.Create("Unified KROP " + name, samples.Length / channels, channels, rate, false);
                                if (clip == null || !clip.SetData(samples, 0))
                                {
                                    if (clip != null) UnityEngine.Object.Destroy(clip);
                                    throw new InvalidDataException("Unable to load " + name);
                                }
                                loaded.Add(clip);
                            }
                        }
                    }
                    catch (Exception error) { log?.LogWarning("Missile recording unavailable; native fallback retained: " + error.Message); }
                }
                clips = loaded.ToArray();
            }
            if (clips == null || clips.Length == 0) return null;
            int count = clips.Length;
            int draw = random.Next(count > 1 && previous >= 0 ? count - 1 : count);
            previous = PcmWave.NonRepeatingIndex(count, previous, draw);
            return clips[previous];
        }

        internal void Clear()
        {
            if (clips != null) foreach (AudioClip clip in clips) if (clip != null) UnityEngine.Object.Destroy(clip);
            clips = null; previous = -1; attempted = false;
        }
    }
}
