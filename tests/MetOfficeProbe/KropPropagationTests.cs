using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

public sealed partial class MetDriver
{
    private void TestKropPropagation()
    {
        var plugin = ModuleInfos()["aaron.boscali.krop-unified"].Instance;
        var asm = plugin.GetType().Assembly;
        var type = asm.GetType("KropClientEffects.AudioPropagation",true);
        var hooks = asm.GetType("KropClientEffects.AudioPropagationHooks",true);
        var clear = type.GetMethod("Clear",F); var tick = type.GetMethod("Tick",F);
        var play = type.GetMethod("TryPlay",F); var observe = type.GetMethod("ObserveLoop",F);
        var travel = type.GetMethod("TravelTime",F);
        var voices = (IList)type.GetField("Voices",F).GetValue(null);
        var loops = (IDictionary)type.GetField("Loops",F).GetValue(null);
        var saved = new Dictionary<ConfigEntryBase,object>();
        ConfigEntry<T> Entry<T>(string section,string key,T value)
        {
            var entry = plugin.Config.Bind(section,key,value);
            if (!saved.ContainsKey(entry)) saved.Add(entry,entry.BoxedValue);
            return entry;
        }
        string sectionName = "09 Sound propagation";
        var world = Entry(sectionName,"World sound delay",false);
        var engine = Entry(sectionName,"Engine sound delay",false);
        var fade = Entry(sectionName,"Gun loop fade",false);
        var cap = Entry(sectionName,"Maximum delayed sounds",128);
        var maximum = Entry(sectionName,"Maximum delay seconds",10f);
        var engineCap = Entry(sectionName,"Maximum engine sources",32);
        var objects = new List<GameObject>();
        AudioClip clip = AudioClip.Create("Silent propagation fixture",44100,1,44100,false);
        AudioClip other = AudioClip.Create("Alternate silent propagation fixture",44100,1,44100,false);
        float timeScale = Time.timeScale;
        AudioSource Make(string name)
        {
            var obj = new GameObject("Propagation test " + name); objects.Add(obj);
            obj.transform.SetParent(Datum.origin,false);
            obj.transform.position = SceneSingleton<CameraStateManager>.i.transform.position + Vector3.right*100f;
            var source = obj.AddComponent<AudioSource>(); source.playOnAwake=false;
            source.clip=clip; source.spatialBlend=1f; source.volume=0f;
            source.minDistance=3f; source.maxDistance=10000f; source.pitch=1f;
            if (SoundManager.i != null) source.outputAudioMixerGroup=SoundManager.i.EffectsMixer;
            return source;
        }
        bool Queue(AudioSource source) => (bool)play.Invoke(null,new object[]{source,clip,1f});
        object Field(object item,string name) => item.GetType().GetField(name,F).GetValue(item);
        void Set(object item,string name,object value) => item.GetType().GetField(name,F).SetValue(item,value);
        try
        {
            Need(Equals(world.DefaultValue,false) && Equals(engine.DefaultValue,false) && Equals(fade.DefaultValue,false),"all propagation and burst-fade defaults are off");
            Entry("00 General","Enable pack",true).Value=true;
            world.Value=false; engine.Value=false; clear.Invoke(null,null);
            Need((float)travel.Invoke(null,new object[]{340f,340f,10f})==1f,"sound-travel time uses distance divided by speed");
            Need((float)travel.Invoke(null,new object[]{6800f,340f,10f})<0f,"over-budget delays are rejected rather than clamped");
            Need((float)travel.Invoke(null,new object[]{1f,340f,10f})<0f,"nearby audio stays immediate");
            Need((float)travel.Invoke(null,new object[]{float.NaN,340f,10f})<0f,"invalid delay input is rejected");
            var source=Make("event");
            Need(!Queue(source)&&voices.Count==0,"disabled event propagation creates no voice");
            world.Value=true; maximum.Value=10f; cap.Value=16; tick.Invoke(null,null);
            source.spatialBlend=0f; Need(!Queue(source),"2D/UI audio is never delayed"); source.spatialBlend=1f;
            source.ignoreListenerPause=true; Need(!Queue(source),"listener-pause-independent audio is never delayed"); source.ignoreListenerPause=false;
            source.mute=true; Need(!Queue(source),"already-muted audio is not resurrected"); source.mute=false;
            var listener=source.gameObject.AddComponent<AudioListener>(); listener.enabled=false;
            Need(!Queue(source),"AudioListener hosts are excluded from propagation"); UnityEngine.Object.DestroyImmediate(listener);
            if (SoundManager.i != null && SoundManager.i.MissileAlertMixer != null)
            {
                source.outputAudioMixerGroup=SoundManager.i.MissileAlertMixer;
                Need(!Queue(source),"missile warning mixer is excluded from propagation");
                source.outputAudioMixerGroup=SoundManager.i.EffectsMixer;
            }
            var warning=Make("explicit warning outside alert mixer");
            asm.GetType("KropClientEffects.AudioFilters",true).GetMethod("ProtectWarning",F).Invoke(null,new object[]{warning});
            Need(warning.bypassEffects,"explicit warning fixture is protected independently of mixer routing");
            Need(!Queue(warning),"explicitly protected warning stays immediate even outside alert mixer");
            Need(Queue(source),"supported spatial world sound queues locally");
            object voice=voices[0]; var clone=(AudioSource)Field(voice,"Audio");
            var position=(GlobalPosition)Field(voice,"Position");
            Need(clone.clip==clip && !clone.isPlaying,"queued sound captures clip without starting early");
            Need(clone.transform.parent==Datum.origin,"queued world source uses floating-origin anchor");
            source.clip=other; source.pitch=2f; source.transform.position+=Vector3.forward*50f;
            Need(clone.clip==clip && clone.pitch==1f && FastMath.Distance(position,(GlobalPosition)Field(voice,"Position"))<0.01f,
                "pooled source reuse cannot change queued clip, pitch or emission position");
            for (int i=1;i<16;i++) Need(Queue(source),"bounded queue admits available voice "+i);
            Need(!Queue(source)&&voices.Count==16,"full queue falls back to native without allocating a seventeenth voice");
            clear.Invoke(null,null); tick.Invoke(null,null); source.clip=clip; source.pitch=1f;
            Need(Queue(source),"arrival fixture queued"); voice=voices[0]; clone=(AudioSource)Field(voice,"Audio");
            Set(voice,"Emitted",Time.time-1f);
            Time.timeScale=0f; tick.Invoke(null,null);
            Need((float)Field(voice,"Started")<0f,"paused game does not release pending audio");
            Time.timeScale=timeScale; tick.Invoke(null,null);
            Need((float)Field(voice,"Started")>=0f && clone.isPlaying,"arrival releases captured audio after travel time");
            world.Value=false; tick.Invoke(null,null);
            Need(voices.Count==0,"turning off event propagation clears all owned voices");

            world.Value=true; tick.Invoke(null,null); source.Stop(); source.clip=clip;
            type.GetMethod("Play",F).Invoke(null,new object[]{source});
            Need(source.mute && source.isPlaying,"event wrapper preserves native isPlaying while muting only owned output");
            Need(voices.Count==1,"native clock preservation does not duplicate delayed event");
            source.clip=other; tick.Invoke(null,null);
            Need(!source.mute,"pooled event source clip change restores native mute");
            Need(((AudioSource)Field(voices[0],"Audio")).clip==clip,"pooled event reuse retains queued clip snapshot");
            clear.Invoke(null,null); source.clip=clip; source.Stop(); tick.Invoke(null,null);
            type.GetMethod("Play",F).Invoke(null,new object[]{source}); engine.Value=true;
            Time.timeScale=0f; world.Value=false; tick.Invoke(null,null);
            Need(voices.Count==0&&!source.mute,"event disable while paused clears voices and restores native output with engines still enabled");
            Time.timeScale=timeScale;

            engine.Value=true; engineCap.Value=4; tick.Invoke(null,null);
            source.loop=true; source.Play();
            Need(source.isPlaying,"native loop playback fixture started");
            observe.Invoke(null,new object[]{source,true,false});
            Need(loops.Count==1 && source.mute && source.isPlaying,"engine observer mutes output but preserves native playing state");
            Need(source.clip==clip && source.pitch==1f && source.volume==0f,"engine observer preserves native clip/pitch/volume");
            for (int i=0;i<4;i++)
            {
                var extra=Make("engine cap "+i); extra.loop=true; extra.Play();
                observe.Invoke(null,new object[]{extra,true,false});
                Need(i<3 ? extra.mute : !extra.mute,"engine cap preserves native output when full "+i);
            }
            Need(loops.Count==4,"engine history allocation remains bounded");
            source.clip=other; tick.Invoke(null,null);
            Need(!loops.Contains(source)&&!source.mute,"pooled engine clip change releases ownership and restores native mute");
            world.Value=true; Time.timeScale=0f; engine.Value=false; tick.Invoke(null,null);
            Need(loops.Count==0,"engine disable while paused releases histories with world delay still enabled");
            foreach (var obj in objects) { var audio=obj.GetComponent<AudioSource>(); Need(!audio.mute,"disable restores owned native loop mute "+obj.name); }
            Time.timeScale=timeScale;

            world.Value=true; tick.Invoke(null,null); source.loop=false; source.clip=clip;
            Need(Queue(source),"camera reset fixture queued");
            // Change only the recorded previous camera state. No player camera is moved.
            type.GetField("lastCameraState",F).SetValue(null,new object()); tick.Invoke(null,null);
            Need(voices.Count==0&&loops.Count==0,"camera-state change discards stale acoustic history");
            Need(Queue(source),"master-disable fixture queued");
            Entry("00 General","Enable pack",true).Value=false; tick.Invoke(null,null);
            Need(voices.Count==0&&loops.Count==0,"master disable clears propagation resources");
            var explosion=AccessTools.Method(typeof(ExplosionAudioManager.ManagedExplosion),"Play");
            var patch=Harmony.GetPatchInfo(explosion); bool hasPropagation=false;
            if (patch!=null) foreach(var p in patch.Transpilers) if(p.PatchMethod.DeclaringType==hooks) hasPropagation=true;
            Need(!hasPropagation,"native explosion propagation is not delayed a second time");
        }
        finally
        {
            Time.timeScale=timeScale; clear.Invoke(null,null);
            foreach(var pair in saved) pair.Key.BoxedValue=pair.Value;
            foreach(var obj in objects) if(obj!=null) UnityEngine.Object.Destroy(obj);
            UnityEngine.Object.Destroy(clip); UnityEngine.Object.Destroy(other);
        }
    }
}
