using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace KropClientEffects
{
    // Kelly's bounded local implementation of the KROP presentation feature. Never changes
    // the native playback clock, clip, volume, pitch or isPlaying state of a tracked loop.
    internal static class AudioPropagation
    {
        internal const string Section = "09 Sound propagation";
        internal static ConfigEntry<bool> WorldDelay, EngineDelay, GunFade;
        internal static ConfigEntry<float> MaximumDelay;
        internal static ConfigEntry<int> MaximumVoices, MaximumEngines;
        internal static void Initialize()
        {
            WorldDelay = Settings.Toggle(Section, "World sound delay", false, "Local speed-of-sound travel for supported gunfire, impacts, releases, ejections and aircraft booms. Native explosion propagation is retained, never doubled. Warnings and own-cockpit sources stay immediate.");
            EngineDelay = Settings.Toggle(Section, "Engine sound delay", false, "Bounded local history of supported engine/propeller/rotor audio. Delays position, pitch and volume without changing aircraft state. More CPU and memory work; off by default.");
            GunFade = Settings.Toggle(Section, "Gun loop fade", false, "With World sound delay, fade delayed gun loops over 0.05 seconds at the end of a burst.");
            MaximumDelay = Settings.Number(Section, "Maximum delay seconds", 10f, 1f, 30f, "Maximum history/travel time. Sources beyond this bound retain native audio rather than using a shortened fake delay.");
            MaximumVoices = Settings.Count(Section, "Maximum delayed sounds", 128, 16, 512, "Maximum pending and playing local event voices. When full, new events retain native playback.");
            MaximumEngines = Settings.Count(Section, "Maximum engine sources", 32, 4, 64, "Maximum tracked engine audio sources. Remaining sources retain native playback.");
        }

        private sealed class Voice
        {
            internal GameObject Root; internal AudioSource Audio; internal GlobalPosition Position;
            internal float Emitted, Started = -1f, Duration, Speed;
            internal AudioFilters.Registration Filter;
        }
        private sealed class NativeEvent
        {
            internal bool BaseMute; internal AudioClip SourceClip; internal float Until;
        }
        private struct Sample
        {
            internal float Time, Pitch, Volume; internal GlobalPosition Position; internal bool Playing;
        }
        private sealed class Loop
        {
            internal AudioSource Native, Audio; internal GameObject Root; internal AudioClip Clip;
            internal bool Engine, Gun, BaseMute, OwnedMute;
            internal readonly Sample[] History = new Sample[604]; // 30 seconds at 20 Hz + endpoints
            internal int Start, Count; internal float LastSample, LastAlive, LastPlaying, Cursor = float.NegativeInfinity;
            internal float FadeStarted = -1f, FadeVolume; internal GlobalPosition LastPosition;
            internal AudioFilters.Registration Filter;
            internal Sample Get(int i) => History[(Start + i) % History.Length];
            internal void Add(Sample sample)
            {
                if (Count == History.Length) { Start = (Start + 1) % History.Length; Count--; }
                History[(Start + Count++) % History.Length] = sample;
            }
        }
        private static readonly List<Voice> Voices = new List<Voice>();
        private static readonly Dictionary<AudioSource, Loop> Loops = new Dictionary<AudioSource, Loop>();
        private static readonly Dictionary<AudioSource, NativeEvent> NativeEvents = new Dictionary<AudioSource, NativeEvent>();
        private static readonly List<AudioSource> Remove = new List<AudioSource>();
        private static object lastCameraState;
        private static CameraStateManager lastManager;
        private static GlobalPosition lastListener;
        private static bool haveListener, paused;
        internal static int PendingCount => Voices.Count;
        internal static int TrackedLoopCount => Loops.Count;
        internal static bool EventActive => Settings.Active && WorldDelay != null && WorldDelay.Value;
        internal static bool EnginesActive => Settings.Active && EngineDelay != null && EngineDelay.Value;

        internal static float TravelTime(float distance, float speed, float maximum)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f ||
                float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f) return -1f;
            float result = distance / speed;
            return result >= 0.02f && result <= maximum ? result : -1f;
        }
        private static bool Eligible(AudioSource source, bool owned = false)
        {
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy ||
                source.spatialBlend < 0.95f || source.ignoreListenerPause || source.GetComponent<AudioListener>() != null ||
                source.GetComponent<PropagationOwned>() != null || AudioFilters.IsProtected(source) || (!owned && source.mute)) return false;
            if (SoundManager.i != null && source.outputAudioMixerGroup != null && source.outputAudioMixerGroup == SoundManager.i.MissileAlertMixer) return false;
            if (AudioFilters.InCockpit())
            {
                Unit unit = source.GetComponentInParent<Unit>();
                if (unit != null && GameManager.IsLocalAircraft(unit)) return false;
                // Native release/ejection sources are sometimes detached under Datum before
                // playback, losing their aircraft owner. Keep nearby cockpit feedback instant.
                var camera=SceneSingleton<CameraStateManager>.i;
                if(camera!=null && Vector3.SqrMagnitude(source.transform.position-camera.transform.position)<75f*75f) return false;
            }
            return true;
        }
        private static float Speed(GlobalPosition position)
            => Mathf.Clamp(LevelInfo.GetSpeedOfSound(position.y), 250f, 380f);
        private static AudioSource Clone(AudioSource template, string name, out GameObject root)
        {
            root = new GameObject("Unified delayed " + name) { hideFlags = HideFlags.HideAndDontSave };
            root.transform.SetParent(Datum.origin, false);
            root.AddComponent<PropagationOwned>();
            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.outputAudioMixerGroup = template.outputAudioMixerGroup;
            audio.volume = template.volume; audio.pitch = template.pitch; audio.priority = template.priority;
            audio.spatialBlend = template.spatialBlend; audio.spread = template.spread;
            audio.minDistance = template.minDistance; audio.maxDistance = template.maxDistance;
            audio.rolloffMode = template.rolloffMode; audio.reverbZoneMix = template.reverbZoneMix;
            audio.bypassEffects = template.bypassEffects; audio.bypassListenerEffects = template.bypassListenerEffects;
            audio.bypassReverbZones = template.bypassReverbZones;
            audio.dopplerLevel = 0f; // History playback already changes source position; avoid double Doppler.
            foreach (AudioSourceCurveType type in new[] { AudioSourceCurveType.CustomRolloff, AudioSourceCurveType.SpatialBlend, AudioSourceCurveType.Spread, AudioSourceCurveType.ReverbZoneMix })
            {
                var curve = template.GetCustomCurve(type);
                if (curve != null && curve.length != 0) audio.SetCustomCurve(type, curve);
            }
            return audio;
        }
        internal static bool TryPlay(AudioSource template, AudioClip clip, float volume = 1f)
        {
            if (!EventActive || clip == null || template == null || !Eligible(template,NativeEvents.ContainsKey(template)) || template.loop || Voices.Count >= MaximumVoices.Value) return false;
            var camera = SceneSingleton<CameraStateManager>.i;
            if (camera == null || Datum.origin == null) return false;
            GlobalPosition position = template.transform.position.ToGlobalPosition();
            float speed = Speed(position);
            if (TravelTime(Vector3.Distance(template.transform.position, camera.transform.position), speed, MaximumDelay.Value) < 0f) return false;
            // Preserve source parameters NOW: native pooled source may be reused before arrival.
            var source = Clone(template, "event", out GameObject root);
            source.clip = clip; source.loop = false; source.volume *= volume;
            root.transform.position = position.ToLocalPosition();
            Voices.Add(new Voice { Root = root, Audio = source, Position = position, Emitted = Time.time,
                Speed = speed, Duration = clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch)),
                Filter = AudioFilters.CaptureRegistration(template) });
            return true;
        }
        private static void HoldNativeEvent(AudioSource source,AudioClip clip)
        {
            if(!NativeEvents.TryGetValue(source,out NativeEvent state))
            { state=new NativeEvent { BaseMute=source.mute }; NativeEvents.Add(source,state); }
            state.SourceClip=source.clip;
            state.Until=Mathf.Max(state.Until,Time.time+clip.length/Mathf.Max(0.01f,Mathf.Abs(source.pitch))+0.1f);
            source.mute=true;
        }
        private static void RestoreNativeEvent(AudioSource source,NativeEvent state)
        { if(source!=null&&source.mute) source.mute=state.BaseMute; }
        private static void NativeFallback(AudioSource source)
        {
            // A cap/unsupported event must remain audible even on a source previously owned.
            // Drop that source's mute ownership; already captured delayed events remain independent.
            if(source!=null&&NativeEvents.TryGetValue(source,out NativeEvent state))
            { RestoreNativeEvent(source,state); NativeEvents.Remove(source); }
        }
        internal static void Play(AudioSource source)
        {
            if(TryPlay(source,source!=null?source.clip:null)) HoldNativeEvent(source,source.clip); else NativeFallback(source);
            source.Play(); // Keep native isPlaying gates, event timing and pooling behavior intact.
        }
        internal static void PlayOneShot(AudioSource source, AudioClip clip)
        {
            if(TryPlay(source,clip)) HoldNativeEvent(source,clip); else NativeFallback(source);
            source.PlayOneShot(clip);
        }
        internal static void PlayOneShotVolume(AudioSource source, AudioClip clip, float volume)
        {
            if(TryPlay(source,clip,volume)) HoldNativeEvent(source,clip); else NativeFallback(source);
            source.PlayOneShot(clip,volume);
        }

        // Called only from explicitly selected native audio-owning components; no scene-wide audio scan.
        internal static void ObserveLoop(AudioSource source, bool engine, bool gun = false)
        {
            if (!(engine ? EnginesActive : EventActive) || source == null || !source.loop || source.clip == null || !source.isPlaying) return;
            if (Loops.ContainsKey(source) || !Eligible(source)) return;
            var camera = SceneSingleton<CameraStateManager>.i;
            if (camera == null || Datum.origin == null) return;
            int count = 0; foreach (var item in Loops.Values) if (item.Engine == engine) count++;
            if (count >= (engine ? MaximumEngines.Value : 32)) return;
            GlobalPosition position = source.transform.position.ToGlobalPosition();
            if (TravelTime(Vector3.Distance(source.transform.position, camera.transform.position), Speed(position), MaximumDelay.Value) < 0f) return;
            AudioSource clone = Clone(source, engine ? "engine" : "gun loop", out GameObject root);
            clone.clip = source.clip; clone.loop = true;
            var loop = new Loop { Native = source, Audio = clone, Root = root, Clip = source.clip,
                Engine = engine, Gun = gun, BaseMute = source.mute, LastAlive = Time.time, LastPlaying = Time.time,
                LastSample = float.NegativeInfinity, LastPosition = position, Filter=AudioFilters.CaptureRegistration(source) };
            Loops.Add(source, loop);
            // Capture the first native sample before muting. Native playback remains running.
            SampleLoop(loop, Time.time);
            source.mute = true; loop.OwnedMute = true;
        }

        private static void SampleLoop(Loop loop, float now)
        {
            bool alive = loop.Native != null && loop.Native.enabled && loop.Native.gameObject.activeInHierarchy;
            if (alive)
            {
                loop.LastAlive = now; loop.LastPosition = loop.Native.transform.position.ToGlobalPosition();
                if (loop.Native.isPlaying) loop.LastPlaying = now;
            }
            if (now - loop.LastSample < 0.05f && loop.Count != 0) return;
            loop.LastSample = now;
            loop.Add(new Sample { Time = now, Position = loop.LastPosition,
                Pitch = alive ? loop.Native.pitch : 1f, Volume = alive ? loop.Native.volume : 0f,
                Playing = alive && loop.Native.isPlaying && loop.Native.clip == loop.Clip });
            while (loop.Count > 2 && now - loop.Get(1).Time > MaximumDelay.Value + 0.1f)
            { loop.Start = (loop.Start + 1) % loop.History.Length; loop.Count--; }
        }
        private static bool At(Loop loop, float time, out Sample sample)
        {
            sample = default;
            if (loop.Count == 0 || time < loop.Get(0).Time) return false;
            if (time >= loop.Get(loop.Count-1).Time) { sample=loop.Get(loop.Count-1); return true; }
            // Ring indices remain time-ordered. Binary search avoids walking up to thirty
            // seconds of history three times per source every rendered frame.
            int low=1, high=loop.Count-1;
            while(low<high)
            {
                int mid=(low+high)/2;
                if(loop.Get(mid).Time<time) low=mid+1; else high=mid;
            }
            Sample a=loop.Get(low-1), b=loop.Get(low);
            float t=Mathf.InverseLerp(a.Time,b.Time,time);
            sample=new Sample { Time=time, Position=a.Position+(b.Position-a.Position)*t,
                Pitch=Mathf.Lerp(a.Pitch,b.Pitch,t),Volume=Mathf.Lerp(a.Volume,b.Volume,t),Playing=a.Playing };
            return true;
        }
        internal static void Tick()
        {
            var camera = SceneSingleton<CameraStateManager>.i;
            if (!Settings.Active || camera == null || (!EventActive && !EnginesActive)) { Clear(); return; }
            GlobalPosition listener = camera.transform.position.ToGlobalPosition();
            // Camera/view changes discard old acoustic history instead of playing stale fly-bys.
            if (haveListener && (lastManager != camera || !ReferenceEquals(lastCameraState,camera.currentState) ||
                FastMath.Distance(lastListener,listener) > 2000f)) Clear();
            haveListener = true; lastManager = camera; lastCameraState = camera.currentState; lastListener = listener;
            // Feature switches release native ownership even while the game is paused.
            if(!EventActive)
            {
                foreach(var voice in Voices) ReleaseVoice(voice);
                Voices.Clear();
            }
            Remove.Clear();
            foreach(var pair in NativeEvents)
                if(!EventActive||pair.Key==null||pair.Key.clip!=pair.Value.SourceClip||Time.time>pair.Value.Until||!pair.Key.isPlaying)
                { RestoreNativeEvent(pair.Key,pair.Value); Remove.Add(pair.Key); }
            foreach(var source in Remove) NativeEvents.Remove(source);
            Remove.Clear();
            foreach(var pair in Loops) if(!(pair.Value.Engine?EnginesActive:EventActive))
            { Release(pair.Value); Remove.Add(pair.Key); }
            foreach(var source in Remove) Loops.Remove(source);
            bool pause = Time.timeScale == 0f || AudioListener.pause;
            if (pause != paused)
            {
                paused = pause;
                foreach (var voice in Voices) if (voice.Audio != null && voice.Started >= 0f) { if (pause) voice.Audio.Pause(); else voice.Audio.UnPause(); }
                foreach (var loop in Loops.Values) if (loop.Audio != null) { if (pause) loop.Audio.Pause(); else loop.Audio.UnPause(); }
            }
            if (pause) return;
            float now = Time.time;
            for (int i = Voices.Count - 1; i >= 0; i--)
            {
                Voice voice = Voices[i];
                bool remove = !EventActive || voice.Root == null || voice.Audio == null || voice.Audio.clip == null;
                if (!remove && voice.Started < 0f)
                {
                    if (now - voice.Emitted > MaximumDelay.Value + 0.1f) remove = true;
                    else if (FastMath.Distance(voice.Position,listener) <= voice.Speed * (now - voice.Emitted))
                    { voice.Root.transform.position = voice.Position.ToLocalPosition(); AudioFilters.ApplyRegistration(voice.Filter,voice.Audio); voice.Audio.Play(); voice.Started = now; }
                }
                if (!remove && voice.Started >= 0f && now - voice.Started > voice.Duration + 0.25f) remove = true;
                if (remove) { ReleaseVoice(voice); Voices.RemoveAt(i); }
            }
            Remove.Clear();
            foreach (var pair in Loops)
            {
                Loop loop = pair.Value;
                bool alive = loop.Native != null;
                if (!(loop.Engine ? EnginesActive : EventActive) || loop.Root == null || loop.Audio == null || loop.Clip == null || loop.Audio.clip == null ||
                    (alive && (!Eligible(loop.Native,true) || loop.Native.clip != loop.Clip ||
                        FastMath.Distance(loop.LastPosition,loop.Native.transform.position.ToGlobalPosition()) > 2000f)) ||
                    (!alive && now - loop.LastAlive > MaximumDelay.Value + 0.2f) ||
                    now - loop.LastPlaying > MaximumDelay.Value + 0.5f) { Remove.Add(pair.Key); continue; }
                SampleLoop(loop,now);
                float target = now - FastMath.Distance(loop.LastPosition,listener) / Speed(loop.LastPosition);
                if (now - target > MaximumDelay.Value) { Remove.Add(pair.Key); continue; }
                // Retarded emission time converges against the historical source position.
                for (int iteration = 0; iteration < 2; iteration++)
                    if (At(loop,target,out Sample historic)) target = now - FastMath.Distance(historic.Position,listener) / Speed(historic.Position);
                target = Mathf.Max(loop.Cursor,target);
                float doppler = float.IsNegativeInfinity(loop.Cursor) || Time.deltaTime <= 0f ? 1f :
                    Mathf.Clamp((target-loop.Cursor)/Time.deltaTime,0.5f,2f);
                loop.Cursor = target;
                if (At(loop,target,out Sample sample) && sample.Playing)
                {
                    loop.Root.transform.position = sample.Position.ToLocalPosition();
                    float amount = alive ? Mathf.Clamp01(loop.Native.dopplerLevel) : 0f;
                    loop.Audio.pitch = sample.Pitch * Mathf.Lerp(1f,doppler,amount); loop.Audio.volume = sample.Volume;
                    if (!loop.Audio.isPlaying) { AudioFilters.ApplyRegistration(loop.Filter,loop.Audio); loop.Audio.Play(); }
                    loop.FadeStarted = -1f;
                }
                else if (loop.Audio.isPlaying)
                {
                    if (loop.Gun && GunFade.Value)
                    {
                        if (loop.FadeStarted < 0f) { loop.FadeStarted = now; loop.FadeVolume = loop.Audio.volume; }
                        loop.Audio.volume = loop.FadeVolume * Mathf.Clamp01(1f - (now-loop.FadeStarted)/0.05f);
                        if (now-loop.FadeStarted >= 0.05f) loop.Audio.Stop();
                    }
                    else loop.Audio.Stop();
                }
                if (alive && loop.OwnedMute) loop.Native.mute = true;
            }
            foreach (var key in Remove) { Release(Loops[key]); Loops.Remove(key); }
        }
        private static void Release(Loop loop)
        {
            if (loop.Native != null && loop.OwnedMute && loop.Native.mute) loop.Native.mute = loop.BaseMute;
            if (loop.Audio != null) loop.Audio.Stop();
            if (loop.Root != null) UnityEngine.Object.Destroy(loop.Root);
        }
        private static void ReleaseVoice(Voice voice)
        { if(voice.Audio!=null) voice.Audio.Stop(); if(voice.Root!=null) UnityEngine.Object.Destroy(voice.Root); }
        internal static void Clear()
        {
            foreach (var voice in Voices) ReleaseVoice(voice);
            Voices.Clear(); foreach (var loop in Loops.Values) Release(loop);
            Loops.Clear(); foreach(var pair in NativeEvents) RestoreNativeEvent(pair.Key,pair.Value);
            NativeEvents.Clear(); Remove.Clear(); haveListener = false; lastManager = null; lastCameraState = null; paused = false;
        }
    }
    internal sealed class PropagationOwned : MonoBehaviour { }
}
