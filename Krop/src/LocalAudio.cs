using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace KropClientEffects
{
    internal static class LocalAudio
    {
        internal static void Play(AudioSource template,AudioClip clip,Vector3 position,float volume,float? minDistance=null,float? maxDistance=null,float? pitch=null,bool shapeCrack=false,bool propagate=false)
        {
            if (clip==null) return;
            GameObject obj=VisualObjects.Create("Audio","audio",64);
            if (obj==null) return;
            obj.transform.SetParent(Datum.origin,false); obj.transform.position=position;
            AudioSource source=obj.AddComponent<AudioSource>(); source.playOnAwake=false;
            source.outputAudioMixerGroup=template!=null?template.outputAudioMixerGroup:SoundManager.i!=null?SoundManager.i.EffectsMixer:null;
            source.volume=volume; source.pitch=pitch??(template!=null?template.pitch:1f);
            source.spatialBlend=1f; source.dopplerLevel=0f;
            source.rolloffMode=template!=null?template.rolloffMode:AudioRolloffMode.Logarithmic;
            source.minDistance=minDistance??(template!=null?template.minDistance:3f);
            source.maxDistance=maxDistance??(template!=null?template.maxDistance:150f);
            source.spread=template!=null?template.spread:0f;
            if (shapeCrack)
            {
                var highpass = obj.AddComponent<AudioHighPassFilter>();
                highpass.cutoffFrequency = 4000f; highpass.highpassResonanceQ = 1f;
                obj.AddComponent<CrackTransient>().Initialize(source, highpass, clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch)));
            }
            source.clip=clip;
            AudioFilters.Register(source);
            if (propagate && AudioPropagation.TryPlay(source,clip)) { UnityEngine.Object.Destroy(obj); return; }
            source.Play();
            UnityEngine.Object.Destroy(obj,clip.length/Mathf.Max(0.01f,Mathf.Abs(source.pitch))+0.25f);
        }
    }
    [HarmonyPatch(typeof(BulletSim.Bullet),"TrajectoryTrace")]
    internal static class BulletCracks
    {
        private static ConditionalWeakTable<BulletSim.Bullet,object> Heard=new ConditionalWeakTable<BulletSim.Bullet,object>();
        private static float last=float.NegativeInfinity;
        private static void Prefix(BulletSim.Bullet __instance,out GlobalPosition __state) { __state=__instance.position; }
        private static void Postfix(BulletSim.Bullet __instance,GlobalPosition __state,Transform muzzle,WeaponInfo info,Unit owner,bool hasCamera,GlobalPosition cameraPos)
        {
            if (!Settings.Active || (!BulletConfig.Enabled.Value && !BulletConfig.Shake.Value) || muzzle!=null || !hasCamera || Heard.TryGetValue(__instance,out _)) return;
            float speedOfSound=LevelInfo.GetSpeedOfSound(__instance.position.y);
            if (__instance.velocity.magnitude<=speedOfSound) return;
            if (owner!=null && GameManager.IsLocalAircraft(owner) && AudioFilters.InCockpit()) return;
            Vector3 segment=__instance.position-__state;
            float t=segment.sqrMagnitude>0.0001f?Mathf.Clamp01(Vector3.Dot(cameraPos-__state,segment)/segment.sqrMagnitude):0f;
            GlobalPosition closest=__state+segment*t; float distance=FastMath.Distance(closest,cameraPos);
            if (distance>BulletConfig.Radius.Value || Time.unscaledTime-last<1f/BulletConfig.MaxRate.Value) return;
            last=Time.unscaledTime; Heard.Add(__instance,new object());
            float pitch=EffectMath.CaliberPitch(info!=null?info.massPerRound:0f,BulletConfig.SmallPitch.Value,BulletConfig.LargePitch.Value);
            float massFraction=info!=null?Mathf.Clamp01((Mathf.Log(Mathf.Max(0.04f,info.massPerRound))-Mathf.Log(0.04f))/(Mathf.Log(50f)-Mathf.Log(0.04f))):0.5f;
            float volume=Mathf.Lerp(BulletConfig.SmallVolume.Value,BulletConfig.LargeVolume.Value,massFraction);
            if(BulletConfig.Enabled.Value)
                LocalAudio.Play(null,BulletCrackAssets.Clip,closest.ToLocalPosition(),volume,pitch:pitch+EffectRandom.Range(-0.15f,0.15f),shapeCrack:AudioRefinements.CrackShaping.Value);
            if (BulletConfig.Shake.Value && pitch<BulletConfig.ShakePitchThreshold.Value)
            {
                var camera=SceneSingleton<CameraStateManager>.i;
                if (camera!=null && AudioFilters.InCockpit()) camera.ShakeCamera(BulletConfig.ShakeStrength.Value*Mathf.Clamp01(1f-distance/BulletConfig.Radius.Value),0f);
            }
        }
        internal static void Clear() { Heard=new ConditionalWeakTable<BulletSim.Bullet,object>(); last=float.NegativeInfinity; }
    }
    internal static class AircraftBooms
    {
        private sealed class State { internal Unit Unit; internal float Last=float.NegativeInfinity; }
        private static ConditionalWeakTable<AudioSource,State> Sources=new ConditionalWeakTable<AudioSource,State>();
        private static AudioClip[] near,far;
        private static int lastNear = -1, lastFar = -1;
        private static readonly MethodInfo OriginalPlay=AccessTools.Method(typeof(AudioSource),"PlayOneShot",new[]{typeof(AudioClip),typeof(float)});
        internal static void Apply(Harmony harmony)
        {
            try
            {
                Type type=AccessTools.Inner(typeof(SonicBoomManager),"ManagedSonicBoom");
                harmony.Patch(AccessTools.Constructor(type,new[]{typeof(Unit)}),postfix:new HarmonyMethod(typeof(AircraftBooms),nameof(ConstructorPostfix)));
                harmony.Patch(AccessTools.Method(type,"Manage"),prefix:new HarmonyMethod(typeof(AircraftBooms),nameof(ManagePrefix)),transpiler:new HarmonyMethod(typeof(AircraftBooms),nameof(Transpiler)));
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Aircraft boom hooks unavailable: "+ex.Message); }
        }
        private static void ConstructorPostfix(Unit supersonicUnit,AudioSource ___source)
        {
            if (___source!=null && supersonicUnit is Aircraft) { Sources.Remove(___source); Sources.Add(___source,new State{Unit=supersonicUnit}); }
        }
        private static void ManagePrefix(Unit ___supersonicUnit, AudioSource ___source)
        {
            if (Settings.Active && ___source != null && ___supersonicUnit is Aircraft && !Sources.TryGetValue(___source, out _))
                Sources.Add(___source, new State { Unit = ___supersonicUnit });
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list=new List<CodeInstruction>(instructions);
            if (!list.Exists(i=>i.Calls(OriginalPlay))) throw new InvalidOperationException("Native aircraft boom PlayOneShot call was not found.");
            return Transpilers.MethodReplacer(list,OriginalPlay,AccessTools.Method(typeof(AircraftBooms),nameof(Play)));
        }
        private static void Play(AudioSource source,AudioClip clip,float volumeScale)
        {
            if (!Settings.Active || !Sources.TryGetValue(source,out State state) || state.Unit==null)
            { source.PlayOneShot(clip,volumeScale); return; }
            if (BoomConfig.Suppress.Value && Time.unscaledTime-state.Last<BoomConfig.Cooldown.Value) return;
            state.Last=Time.unscaledTime;
            AudioFilters.Register(source);
            if (!BoomConfig.Replacement.Value) { AudioPropagation.PlayOneShotVolume(source,clip,volumeScale); return; }
            EnsureClips();
            var manager=SceneSingleton<CameraStateManager>.i;
            float distance=manager!=null?Vector3.Distance(source.transform.position,manager.transform.position):0f;
            bool distant=distance>=BoomConfig.FarDistance.Value;
            var pool=distant?far:near;
            int previous = distant ? lastFar : lastNear;
            bool avoid = AudioRefinements.AvoidRepeatedBooms.Value;
            int range = avoid && previous >= 0 && previous < pool.Length && pool.Length > 1 ? pool.Length - 1 : pool.Length;
            int index = EffectMath.NonRepeatingIndex(EffectRandom.Range(0,range), previous, pool.Length, avoid);
            if (distant) lastFar = index; else lastNear = index;
            AudioClip replacement=pool[index]??clip;
            LocalAudio.Play(source,replacement,source.transform.position,source.volume*volumeScale*BoomConfig.Volume.Value,
                distant?(float?)BoomConfig.FarDistance.Value:null,
                distant?(float?)Mathf.Max(BoomConfig.FarDistance.Value+1f,BoomConfig.FarMaxDistance.Value):null,propagate:true);
        }
        private static void EnsureClips()
        {
            if (near!=null) return;
            near=new AudioClip[3]; far=new AudioClip[3];
            for(int i=0;i<3;i++) { near[i]=Load("SonicBoom"+(i+1)); far[i]=Load("SonicBoomFar"+(i+1)); }
        }
        private static AudioClip Load(string name)
        {
            using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("KropClientEffects.SonicBooms."+name+".wav"))
            using(var data=new MemoryStream())
            {
                if(stream==null) { Plugin.Log.LogWarning("Missing aircraft boom sample "+name); return null; }
                stream.CopyTo(data); return WavUtility.ToAudioClip(data.ToArray(),name);
            }
        }
        internal static void Clear() { Sources=new ConditionalWeakTable<AudioSource,State>(); lastNear = lastFar = -1; }
    }
    internal sealed class CrackTransient : MonoBehaviour
    {
        private AudioSource source;
        private AudioHighPassFilter highpass;
        private float halfEnd;
        internal void Initialize(AudioSource value, AudioHighPassFilter filter, float duration)
        { source = value; highpass = filter; halfEnd = Time.unscaledTime + duration * 0.5f; }
        private void Update()
        {
            if (!AudioRefinements.CrackShaping.Value)
            {
                if (highpass != null) { highpass.enabled = false; Destroy(highpass); }
                Destroy(this); return;
            }
            if (Time.unscaledTime >= halfEnd)
            { if (source != null) source.Stop(); Destroy(gameObject); }
        }
    }
}

