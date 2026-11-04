using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Modules.Weather.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    internal enum CloudMode { Vanilla, Enhanced, Volumetric }

    // Optional runtime integration. NOAtmoFX is neither bundled nor referenced at build time.
    internal sealed class CloudModeCoordinator : MonoBehaviour
    {
        private const string Id="paul.no.clouds", TestedHash="2F0F06FF3723E73A1C8155EFBED30FCF24373A79EB1FC1DC78E45D9CC832407B";
        internal static CloudModeCoordinator Live;
        private WeatherSettings settings;private ConfigEntry<CloudMode> mode;private ConfigFile addon;private BepInEx.BaseUnityPlugin addonInstance;private ManualLogSource log;
        private Harmony guards;private bool writing,releasing,dirty,oldAutoSave,ready;private string reason="NOATMOFX NOT INSTALLED";
        private static object sharedBundle;private static Material sharedMaterial;
        private readonly Dictionary<ConfigEntryBase,object> original=new Dictionary<ConfigEntryBase,object>();
        private readonly Dictionary<ConfigEntryBase,object> lastWritten=new Dictionary<ConfigEntryBase,object>();
        internal CloudMode Mode=>mode.Value;
        internal bool AddonReady=>ready && addonInstance!=null && addonInstance.isActiveAndEnabled;
        internal string Status=>AddonReady?"NOATMOFX 1.0.0 READY":(ready?"NOATMOFX STOPPED":reason);

        internal void Configure(WeatherSettings weather,ConfigFile ours,ManualLogSource logger)
        {
            settings=weather;log=logger;Live=this;
            mode=ours.Bind("Weather display","Cloud mode",settings.Enabled.Value?CloudMode.Enhanced:CloudMode.Vanilla,
                "Vanilla, Boscali enhanced weather, or optional NOAtmoFX volumetric clouds.");
            TryAttach();
            if(mode.Value==CloudMode.Volumetric && !ready)mode.Value=CloudMode.Enhanced;
            ApplyMode();
        }
        private void TryAttach()
        {
            if(!Chainloader.PluginInfos.TryGetValue(Id,out var info) || info.Instance==null)return;
            try
            {
                if(info.Metadata.Version.ToString()!="1.0.0" || !File.Exists(info.Location)){reason="NOATMOFX VERSION NOT VERIFIED";return;}
                using(var sha=SHA256.Create())using(var stream=File.OpenRead(info.Location))
                    if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")!=TestedHash){reason="NOATMOFX BUILD NOT VERIFIED";return;}
                var plugin=info.Instance;var asm=plugin.GetType().Assembly;
                var material=AccessTools.Field(plugin.GetType(),"_mat")?.GetValue(plugin) as Material;
                if(material==null || material.shader==null || !material.shader.isSupported){reason="NOATMOFX CLOUD BUNDLE UNAVAILABLE";return;}
                // Recent DOORMAN startup builds already protect BepInEx's host.
                // Only rehost when that protection is absent.
                if((plugin.gameObject.hideFlags & HideFlags.HideAndDontSave)!=HideFlags.HideAndDontSave)
                    plugin=Rehost(info,plugin,material);
                var cfg=plugin.Config;
                foreach(var def in new[]{new ConfigDefinition("General","Enabled"),new ConfigDefinition("General","FollowGameWeather"),new ConfigDefinition("General","RenderOrder"),new ConfigDefinition("Glass","WetGlass"),new ConfigDefinition("Atmosphere","NearRain"),new ConfigDefinition("Atmosphere","Turbulence"),new ConfigDefinition("Atmosphere","Fog"),new ConfigDefinition("Weather","ConditionsOverride"),new ConfigDefinition("Weather","TimeOfDayOverride"),new ConfigDefinition("Lighting","KeepSunBelowClouds"),new ConfigDefinition("Nuclear","UnlockNukes"),new ConfigDefinition("Nuclear","TestBlastKey")})
                    if(!cfg.ContainsKey(def)){reason="NOATMOFX CONFIG CONTRACT CHANGED";return;}
                var atmosphere=asm.GetType("NoAtmoFX.NOCAtmos",false);var nukes=asm.GetType("NoAtmoFX.NOCNukes",false);
                var thunder=AccessTools.Method(atmosphere,"Thunder",new[]{typeof(Vector3),typeof(Vector3),typeof(float),typeof(bool)});
                var physics=AccessTools.Method(atmosphere,"Fixed",new[]{typeof(float),typeof(float),typeof(float),typeof(float)});
                var allow=AccessTools.Method(nukes,"PreAllowed");
                var remove=AccessTools.Method(nukes,"PreRemove");
                var save=AccessTools.Method(typeof(ConfigFile),"Save",Type.EmptyTypes);
                if(thunder==null || physics==null || allow==null || remove==null || save==null){reason="NOATMOFX SAFETY CONTRACT CHANGED";return;}
                guards=new Harmony("aaron.boscali.noatmofx.bridge");
                guards.Patch(thunder,prefix:new HarmonyMethod(typeof(CloudModeCoordinator),nameof(SuppressAddonEffect)));
                guards.Patch(physics,prefix:new HarmonyMethod(typeof(CloudModeCoordinator),nameof(SuppressAddonEffect)));
                guards.Patch(allow,prefix:new HarmonyMethod(typeof(CloudModeCoordinator),nameof(AllowNativeRules)));
                guards.Patch(remove,prefix:new HarmonyMethod(typeof(CloudModeCoordinator),nameof(SuppressAddonEffect)));
                guards.Patch(save,prefix:new HarmonyMethod(typeof(CloudModeCoordinator),nameof(GuardSave)));
                addon=cfg;addonInstance=plugin;oldAutoSave=cfg.SaveOnConfigSet;cfg.SaveOnConfigSet=false;cfg.SettingChanged+=OnAddonChanged;
                EffectsSettingsCache.Register(Id,cfg);ready=true;reason="";
                log.LogInfo("Optional NOAtmoFX 1.0.0 verified; native weather drives its clouds. Add-on delayed thunder and forces are suppressed.");
            }
            catch(Exception ex){guards?.UnpatchSelf();guards=null;reason="NOATMOFX INTEGRATION UNAVAILABLE";log.LogWarning("NOAtmoFX integration unavailable: "+ex.Message);}
        }
        private static bool SuppressAddonEffect()=>false;
        private BepInEx.BaseUnityPlugin Rehost(PluginInfo info,BepInEx.BaseUnityPlugin previous,Material material)
        {
            var type=previous.GetType();var load=AccessTools.Method(type,"LoadBundle",Type.EmptyTypes);
            var bundleField=AccessTools.Field(type,"_ab");
            if(load==null || bundleField==null)throw new InvalidOperationException("NOAtmoFX host contract changed");
            sharedBundle=bundleField.GetValue(previous);sharedMaterial=material;
            if(sharedBundle==null)throw new InvalidOperationException("NOAtmoFX bundle unavailable");
            var transfer=new Harmony("aaron.boscali.noatmofx.rehost");
            transfer.Patch(load,prefix:new HarmonyMethod(typeof(CloudModeCoordinator),nameof(ReuseBundle)));
            try
            {
                // Its startup component shares the transient BepInEx manager. Move
                // its work to our persistent runtime, reusing its loaded bundle.
                new Harmony("paul.no.clouds.nukes").UnpatchSelf();
                DestroyImmediate(previous);
                var next=(BepInEx.BaseUnityPlugin)gameObject.AddComponent(type);
                if(next==null || AccessTools.Field(type,"_mat")?.GetValue(next)==null)throw new InvalidOperationException("NOAtmoFX rehost failed");
                typeof(PluginInfo).GetProperty("Instance",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SetValue(info,next,null);
                log.LogInfo("NOAtmoFX renderer attached to persistent client runtime.");
                return next;
            }
            finally{transfer.UnpatchSelf();sharedBundle=null;sharedMaterial=null;}
        }
        private static bool ReuseBundle(object __instance,ref bool __result)
        {
            if(sharedBundle==null || sharedMaterial==null)return true;
            var type=__instance.GetType();AccessTools.Field(type,"_ab").SetValue(__instance,sharedBundle);AccessTools.Field(type,"_mat").SetValue(__instance,sharedMaterial);
            __result=true;return false;
        }
        private static bool AllowNativeRules(ref bool __result){__result=true;return false;}
        private static bool GuardSave(ConfigFile __instance)=>Live==null || Live.addon!=__instance || Live.releasing;
        private void OnAddonChanged(object sender,SettingChangedEventArgs e)
        {
            if(writing)return;
            dirty=true;
            if(original.ContainsKey(e.ChangedSetting))original[e.ChangedSetting]=e.ChangedSetting.BoxedValue;
        }
        private ConfigEntryBase Entry(string section,string key)=>addon[new ConfigDefinition(section,key)];
        private void Own(string section,string key,object value)
        {
            var entry=Entry(section,key);if(!original.ContainsKey(entry))original[entry]=entry.BoxedValue;
            if(!Equals(entry.BoxedValue,value)){writing=true;try{entry.BoxedValue=value;}finally{writing=false;}}
            lastWritten[entry]=value;
        }
        private void ReleaseOwned()
        {
            writing=true;
            try{foreach(var pair in original)if(lastWritten.TryGetValue(pair.Key,out var own) && Equals(pair.Key.BoxedValue,own))pair.Key.BoxedValue=pair.Value;}
            finally{writing=false;original.Clear();lastWritten.Clear();}
        }
        internal bool Select(CloudMode requested)
        {
            if(requested==CloudMode.Volumetric && !AddonReady)return false;
            if(mode.Value!=requested)mode.Value=requested;
            ApplyMode();return true;
        }
        private void ApplyMode()
        {
            if(settings==null || mode==null)return;
            if(settings.Enabled.Value!=(mode.Value!=CloudMode.Vanilla))settings.Enabled.Value=mode.Value!=CloudMode.Vanilla;
            if(!ready)return;
            // Release each transition before acquiring the new state, preserving user values.
            ReleaseOwned();
            Own("General","Enabled",mode.Value==CloudMode.Volumetric);
            Own("Glass","WetGlass",false);Own("Atmosphere","NearRain",2);Own("Atmosphere","Turbulence",0f);
            Own("Nuclear","UnlockNukes",false);Own("Nuclear","TestBlastKey",new KeyboardShortcut(KeyCode.None));
            if(mode.Value==CloudMode.Volumetric)
            {
                Own("General","FollowGameWeather",true);Own("General","RenderOrder",1);
                Own("Weather","ConditionsOverride",-1f);Own("Weather","TimeOfDayOverride",-1f);
                Own("Atmosphere","Fog",0f);Own("Lighting","KeepSunBelowClouds",false);
            }
        }
        private void LateUpdate()
        {
            if(mode==null)return;
            if(ready && !AddonReady && mode.Value==CloudMode.Volumetric){mode.Value=CloudMode.Enhanced;ApplyMode();}
            // Existing master toggle remains usable: turning it off returns to vanilla.
            if(settings!=null && !settings.Enabled.Value && mode.Value!=CloudMode.Vanilla){mode.Value=CloudMode.Vanilla;ApplyMode();}
            else if(settings!=null && settings.Enabled.Value && mode.Value==CloudMode.Vanilla){mode.Value=CloudMode.Enhanced;ApplyMode();}
            if(!ready)return;
            // If F10 changes NOAtmoFX, keep the selected renderer authoritative.
            foreach(var pair in lastWritten)
                if(!Equals(pair.Key.BoxedValue,pair.Value))
                {writing=true;try{pair.Key.BoxedValue=pair.Value;}finally{writing=false;}}
            // Persist the user's own cloud settings without writing our temporary
            // compatibility overrides into the add-on's independent config file.
            if(dirty)
            {
                ReleaseOwned();releasing=true;
                try{addon.Save();dirty=false;}
                catch(Exception ex){log.LogWarning("NOAtmoFX settings save failed: "+ex.Message);}
                finally{releasing=false;ApplyMode();}
            }
        }
        private void OnDestroy()
        {
            if(addon!=null)
            {
                addon.SettingChanged-=OnAddonChanged;ReleaseOwned();releasing=true;
                try{if(dirty)addon.Save();}catch(Exception ex){log?.LogWarning("NOAtmoFX settings save failed: "+ex.Message);}
                addon.SaveOnConfigSet=oldAutoSave;
            }
            guards?.UnpatchSelf();if(Live==this)Live=null;
        }
    }
}
