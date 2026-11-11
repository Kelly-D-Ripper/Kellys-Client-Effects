using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Modules.Weather.Runtime;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    internal enum CloudMode { Vanilla, Enhanced, Volumetric }

    // Optional add-on stays a separate download. Weather writes use its public API;
    // the guarded 1.4 timer adapter and read-only lease check are explicit exceptions.
    internal sealed class CloudModeCoordinator : MonoBehaviour
    {
        private const string Id="paul.no.clouds";
        internal static CloudModeCoordinator Live;
        private WeatherSettings settings;
        private ConfigEntry<CloudMode> mode;
        private BaseUnityPlugin addon;
        private ManualLogSource log;
        private Type api;
        private MethodInfo setEnabled,setFeature,getFeature,setPreset;
        private PropertyInfo isEnabled,currentPreset,apiReady,weatherSource,followingHost;
        private FieldInfo presets;
        private MethodInfo setWeather, clearWeather, setLightning;
        private FieldInfo weatherInput;
        private ConfigEntry<float> conditionsOverride;
        private PropertyInfo cloudConditions;
        private object lastWeatherInput;
        private LevelInfo weatherLevel;
        private int selectedWeather=-1;
        private float releaseBlendUntil,nextWeatherCheck,weatherCommandAt;
        private bool fastWeatherActive;
        private string weatherError="";
        private bool ready,initialEnabled,lastWritten,hasWrite,writing;
        private string reason="NOATMOFX NOT INSTALLED",version="";
        private static readonly string[] EmptyPresets=Array.Empty<string>();

        internal CloudMode Mode => mode != null ? mode.Value : CloudMode.Vanilla;
        internal int SelectedWeatherIndex => OwnsWeatherInput ? selectedWeather : -1;
        internal float TransitionSeconds => UnifiedEffects.NoAtmoWeatherTransition.Seconds;
        internal bool FastWeatherTransitionActive => fastWeatherActive && Mode==CloudMode.Volumetric &&
            GameManager.gameState==GameState.SinglePlayer && !MenuWeatherOverride;
        private bool MenuWeatherOverride => conditionsOverride!=null && conditionsOverride.Value>=0f;
        private bool OwnsWeatherInput
        {
            get {try{return lastWeatherInput!=null && weatherInput!=null && lastWeatherInput.Equals(weatherInput.GetValue(null));}catch{return false;}}
        }
        internal bool CanSelectWeather => Mode==CloudMode.Volumetric && AddonReady && setWeather!=null &&
            clearWeather!=null && setLightning!=null && weatherInput!=null && !MenuWeatherOverride && !HostOwnsWeather &&
            WeatherManager.Live!=null && WeatherManager.Live.CanCommand && WeatherManager.Live.IsCaptured &&
            (Read<string>(weatherSource,"")!="api" || OwnsWeatherInput);
        internal string WeatherStatus
        {
            get
            {
                if(MenuWeatherOverride)return "NOATMOFX F11 WEATHER OVERRIDE ACTIVE";
                if(HostOwnsWeather || GameManager.gameState==GameState.Multiplayer)return "MULTIPLAYER WEATHER IS HOST CONTROLLED";
                if(weatherInput==null)return "CLOUD WEATHER API VERSION NOT VERIFIED";
                if(Read<string>(weatherSource,"")=="api" && !OwnsWeatherInput)return "ANOTHER MOD IS CONTROLLING CLOUD WEATHER";
                if(!string.IsNullOrEmpty(weatherError))return weatherError;
                if(SelectedWeatherIndex<0)return "FOLLOWING MISSION / BOSCALI WEATHER";
                var p=VolumetricWeatherProfiles.Get(selectedWeather);
                float actual=Read<float>(cloudConditions,p.Conditions);
                return (Mathf.Abs(actual-p.Conditions)>.02f || Time.time-weatherCommandAt<TransitionSeconds ? "CHANGING TO " : "SELECTED: ")+p.Name(BoscaliUnifiedWeather.WeatherPlugin.SimpleNames?.Value ?? true).ToUpperInvariant();
            }
        }
        internal string TransitionLabel => UnifiedEffects.NoAtmoWeatherTransition.Available && UnifiedEffects.NoAtmoWeatherTransition.Enabled
            ? "TRANSITION: ABOUT "+TransitionSeconds.ToString("0")+" SECONDS"
            : "NATIVE CLOUD TRANSITION (SEE CLOUD SETTINGS)";
        internal void CycleTransition()
        {
            var entry=UnifiedEffects.NoAtmoWeatherTransition.Entry;if(entry==null)return;
            float now=entry.Value;entry.Value=now<5.1f?15f:now<15.1f?60f:now<60.1f?600f:5f;
        }
        internal bool SelectWeather(int index)
        {
            if(!CanSelectWeather || !VolumetricWeatherProfiles.TryGet(index,out var p))return false;
            try
            {
                // Pin our rain/atmosphere to its matching sky. The add-on receives
                // independent, uncapped cloud targets, never native fog conditions.
                if(!WeatherManager.Live.PinSky(p.BoscaliSkyIndex))return false;
                setWeather.Invoke(null,new object[]{p.Conditions,p.CloudBaseMetres,p.HumidityBias,p.InstabilityBias,p.CoverageScale,p.Front});
                setLightning.Invoke(null,new object[]{p.LightningScale});
                lastWeatherInput=weatherInput.GetValue(null);weatherLevel=LevelInfo.i;
                selectedWeather=index;fastWeatherActive=true;weatherCommandAt=Time.time;weatherError="";return true;
            }
            catch(Exception ex){weatherError="CLOUD WEATHER COMMAND FAILED";log.LogWarning(weatherError+": "+ex.GetBaseException().Message);ResetWeather();return false;}
        }
        internal bool ResumeWeather()
        {
            if(!CanSelectWeather)return false;
            ResetWeather();releaseBlendUntil=Time.time+TransitionSeconds*2f;fastWeatherActive=true;
            return WeatherManager.Live.ApplyScenario(WeatherScenario.ResetAll);
        }
        internal void ResetWeather()
        {
            // Never clear another mod's API input. The boxed public-API input
            // value is read only and used to recognize our last complete write.
            if(OwnsWeatherInput)try{clearWeather.Invoke(null,null);}catch(Exception ex){log?.LogWarning("NOAtmoFX weather release failed: "+ex.GetBaseException().Message);}
            lastWeatherInput=null;weatherLevel=null;selectedWeather=-1;releaseBlendUntil=0f;fastWeatherActive=false;
        }
        internal bool AddonReady => ready && addon != null && addon.isActiveAndEnabled && api != null && Read<bool>(apiReady,false);
        internal string Status
        {
            get {if(!AddonReady)return ready ? "NOATMOFX STOPPED" : reason;string source=Read<string>(weatherSource,"");return "NOATMOFX "+version+(string.IsNullOrEmpty(source) || source=="none" ? " READY" : " / "+source.ToUpperInvariant());}
        }
        internal bool SessionOnly => GameManager.gameState==GameState.Multiplayer || Read<bool>(followingHost,false);
        internal bool HostOwnsWeather => AddonReady && Read<bool>(followingHost,false) &&
            (!addon.Config.TryGetEntry<bool>(new ConfigDefinition("Multiplayer","FollowHostSettings"),out var follow) || follow.Value);
        internal bool CanEdit(string section,string feature)
        {
            if(!AddonReady)return false;
            if(!HostOwnsWeather)return true;
            return section!="Weather" && section!="Layer" && section!="Shape" && section!="Scale" && section!="Variation" && section!="Wind" && section!="Storm" && section!="Nuclear";
        }
        internal string CurrentPreset => AddonReady ? Read<string>(currentPreset,"Custom") : "UNAVAILABLE";
        internal string[] Presets
        {
            get { if(!AddonReady)return EmptyPresets;try{return presets.GetValue(null) as string[] ?? EmptyPresets;}catch{return EmptyPresets;} }
        }
        internal void Configure(WeatherSettings weather,ConfigFile ours,ManualLogSource logger)
        {
            settings=weather;log=logger;Live=this;
            mode=ours.Bind("Weather display","Cloud mode",settings.Enabled.Value?CloudMode.Enhanced:CloudMode.Vanilla,
                "Vanilla, Boscali enhanced weather, or optional NOAtmoFX volumetric clouds.");
            TryAttach();
            UnifiedEffects.NoAtmoWeatherTransition.Configure(this,ours,logger);
            if(mode.Value==CloudMode.Volumetric && !AddonReady)mode.Value=CloudMode.Enhanced;
            ApplyMode();
        }
        private void TryAttach()
        {
            if(!Chainloader.PluginInfos.TryGetValue(Id,out var info) || info.Instance==null)return;
            try
            {
                version=info.Metadata.Version.ToString();
                if(info.Metadata.Version < new Version(1,4,0)){reason="NOATMOFX 1.4.0 OR NEWER REQUIRED";return;}
                var candidate=info.Instance.GetType().Assembly.GetType("NoAtmoFX.NoAtmoFXApi",false);
                var apiVersion=candidate?.GetField("ApiVersion",BindingFlags.Public|BindingFlags.Static);
                if(candidate==null || apiVersion==null || Convert.ToInt32(apiVersion.GetRawConstantValue())!=1)
                {reason="NOATMOFX API UNAVAILABLE";return;}
                const BindingFlags flags=BindingFlags.Public|BindingFlags.Static;
                MethodInfo Method(string name,params Type[] types)=>candidate.GetMethod(name,flags,null,types,null);
                var enable=Method("SetEnabled",typeof(bool),typeof(bool));
                var feature=Method("SetFeature",typeof(string),typeof(bool),typeof(bool));
                var featureGet=Method("GetFeature",typeof(string));
                var preset=Method("SetPreset",typeof(string),typeof(bool));
                var enabled=candidate.GetProperty("IsEnabled",flags);
                var readyProperty=candidate.GetProperty("Ready",flags);
                var current=candidate.GetProperty("CurrentPreset",flags);
                var names=candidate.GetField("Presets",flags);
                if(enable==null || feature==null || feature.ReturnType!=typeof(bool) || featureGet==null || featureGet.ReturnType!=typeof(bool) || preset==null || preset.ReturnType!=typeof(bool) || enabled==null || enabled.PropertyType!=typeof(bool) || readyProperty==null || readyProperty.PropertyType!=typeof(bool) || current==null || current.PropertyType!=typeof(string) || names==null || names.FieldType!=typeof(string[]))
                {reason="NOATMOFX API CONTRACT CHANGED";return;}
                if(!Read<bool>(readyProperty,false)){reason="NOATMOFX CLOUD BUNDLE UNAVAILABLE";return;}
                addon=info.Instance;api=candidate;setEnabled=enable;setFeature=feature;getFeature=featureGet;setPreset=preset;isEnabled=enabled;apiReady=readyProperty;currentPreset=current;weatherSource=candidate.GetProperty("WeatherSource",flags);followingHost=candidate.GetProperty("FollowingHost",flags);presets=names;
                setWeather=Method("SetWeather",typeof(float),typeof(float),typeof(float),typeof(float),typeof(float),typeof(int));
                clearWeather=Method("ClearWeather");setLightning=Method("SetLightningScale",typeof(float));
                weatherInput=candidate.GetField("In",BindingFlags.Static|BindingFlags.NonPublic);
                cloudConditions=candidate.GetProperty("Conditions",flags);
                addon.Config.TryGetEntry<float>(new ConfigDefinition("Weather","ConditionsOverride"),out conditionsOverride);
                // The lease reader is deliberately limited to the reviewed 1.4 contract.
                if(info.Metadata.Version!=new Version(1,4,0) || weatherInput==null || !weatherInput.FieldType.IsValueType)weatherInput=null;
                initialEnabled=Read<bool>(isEnabled,false);ready=true;reason="";
                EffectsSettingsCache.Register(Id,addon.Config);
                UnifiedEffects.NoAtmoConfigurationDrawers.Attach(this,addon.Config,log);
                log.LogInfo("Optional NOAtmoFX "+version+" public API connected; add-on owns its weather and settings.");
            }
            catch(Exception ex){ready=false;api=null;reason="NOATMOFX API UNAVAILABLE";log.LogWarning("NOAtmoFX integration unavailable: "+ex.GetBaseException().Message);}
        }
        private T Read<T>(PropertyInfo property,T fallback)
        {try{return property!=null && property.GetValue(null,null) is T value?value:fallback;}catch{return fallback;}}
        private bool WriteEnabled(bool value,bool persist=false)
        {
            if(!AddonReady)return false;
            try
            {
                writing=true;setEnabled.Invoke(null,new object[]{value,persist});
                if(Read<bool>(isEnabled,!value)!=value){reason="NOATMOFX ENABLE CHANGE FAILED";return false;}
                hasWrite=!persist;lastWritten=value;return true;
            }
            catch(Exception ex){log.LogWarning("NOAtmoFX enable failed: "+ex.GetBaseException().Message);return false;}
            finally{writing=false;}
        }
        internal bool Feature(string name)
        {if(!AddonReady)return false;try{return (bool)getFeature.Invoke(null,new object[]{name});}catch{return false;}}
        private void ReleaseStartupOwnership(){initialEnabled=Read<bool>(isEnabled,false);hasWrite=false;}
        internal bool ToggleFeature(string name)
        {
            if(!CanEdit("",name))return false;
            try{bool changed=(bool)setFeature.Invoke(null,new object[]{name,!Feature(name),!SessionOnly});if(changed)ReleaseStartupOwnership();return changed;}
            catch(Exception ex){log.LogWarning("NOAtmoFX feature failed: "+ex.GetBaseException().Message);return false;}
        }
        internal bool Preset(string name)
        {
            if(!AddonReady || string.IsNullOrEmpty(name))return false;
            try{bool changed=(bool)setPreset.Invoke(null,new object[]{name,!SessionOnly});if(changed)ReleaseStartupOwnership();return changed;}
            catch(Exception ex){log.LogWarning("NOAtmoFX preset failed: "+ex.GetBaseException().Message);return false;}
        }
        internal bool Select(CloudMode requested)
        {
            if(requested==CloudMode.Volumetric && !AddonReady)return false;
            CloudMode prior=Mode;
            if(AddonReady && !WriteEnabled(requested==CloudMode.Volumetric,!SessionOnly))return false;
            if(AddonReady)ReleaseStartupOwnership();
            if(requested!=CloudMode.Volumetric)ResetWeather();
            mode.Value=requested;
            if(settings.Enabled.Value!=(requested!=CloudMode.Vanilla))settings.Enabled.Value=requested!=CloudMode.Vanilla;
            return Mode==requested || prior==requested;
        }
        private void ApplyMode()
        {
            if(settings==null || mode==null)return;
            if(settings.Enabled.Value!=(mode.Value!=CloudMode.Vanilla))settings.Enabled.Value=mode.Value!=CloudMode.Vanilla;
            if(AddonReady)WriteEnabled(mode.Value==CloudMode.Volumetric);
        }
        private void LateUpdate()
        {
            if(mode==null || settings==null || writing)return;
            if(Time.unscaledTime>=nextWeatherCheck)
            {
                nextWeatherCheck=Time.unscaledTime+.25f;
                if(lastWeatherInput!=null && (Mode!=CloudMode.Volumetric || GameManager.gameState!=GameState.SinglePlayer ||
                    weatherLevel!=LevelInfo.i || !OwnsWeatherInput || WeatherManager.Live==null || !WeatherManager.Live.IsCaptured ||
                    WeatherManager.Live.LiveKey.SkyPin!=VolumetricWeatherProfiles.Get(selectedWeather).BoscaliSkyIndex))ResetWeather();
                fastWeatherActive=lastWeatherInput!=null || Time.time<releaseBlendUntil;
            }
            if(ready && !AddonReady){ready=false;reason="NOATMOFX STOPPED - CHECK PACK STARTUP FILE";if(mode.Value==CloudMode.Volumetric){mode.Value=CloudMode.Enhanced;settings.Enabled.Value=true;}return;}
            // Keep the effects master and add-on's own F10 switch in agreement without
            // overriding other feature settings or saving temporary mode changes.
            if(!settings.Enabled.Value && mode.Value!=CloudMode.Vanilla){Select(CloudMode.Vanilla);return;}
            if(settings.Enabled.Value && mode.Value==CloudMode.Vanilla){Select(CloudMode.Enhanced);return;}
            if(AddonReady)
            {
                bool actual=Read<bool>(isEnabled,false), expected=mode.Value==CloudMode.Volumetric;
                if(actual!=expected){initialEnabled=actual;hasWrite=false;mode.Value=actual?CloudMode.Volumetric:(settings.Enabled.Value?CloudMode.Enhanced:CloudMode.Vanilla);if(actual && !settings.Enabled.Value)settings.Enabled.Value=true;}
            }
        }
        private void OnDestroy()
        {
            ResetWeather();UnifiedEffects.NoAtmoWeatherTransition.Release(this);
            UnifiedEffects.NoAtmoConfigurationDrawers.Detach(this);
            if(AddonReady && hasWrite && Read<bool>(isEnabled,!lastWritten)==lastWritten && lastWritten!=initialEnabled)WriteEnabled(initialEnabled);
            if(Live==this)Live=null;
        }
    }
}
