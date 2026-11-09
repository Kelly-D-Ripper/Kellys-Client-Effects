using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using System;
using System.Reflection;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    // Some loaders destroy the originating plugin component during scene changes.
    // Retain the config object while it is live, as the existing control panel does.
    internal sealed class EffectsSettingsCache : MonoBehaviour
    {
        private static readonly Dictionary<string,ConfigFile> Configs=new Dictionary<string,ConfigFile>();
        private static readonly string[] Ids={"aaron.boscali.unified","aaron.boscali.krop-unified","aaron.boscali.betternight","aaron.boscali.weather-unified","aaron.boscali.missile-unified","aaron.boscali.cockpit-audio","aaron.boscali.cockpitfeel","aaron.boscali.airfield-lighting"};
        private float next;
        private static bool complete;
        private static MethodInfo bridge;
        internal static void Register(string id,ConfigFile config){if(config!=null)Configs[id]=config;}
        private void Update(){if(complete || Time.unscaledTime<next)return;next=Time.unscaledTime+1f;Capture();}
        internal static void Capture()
        {
            if(bridge==null)foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type=assembly.GetType("ClientEffectsControlPanel.ControlUiBridge",false);
                if(type!=null){bridge=type.GetMethod("GetConfig",BindingFlags.Public|BindingFlags.Static);break;}
            }
            foreach(string id in Ids)
            {
                if(Configs.ContainsKey(id))continue;
                if(UnifiedEffects.ModuleRegistry.GetPluginInfos().TryGetValue(id,out var info) && info.Instance!=null)Configs[id]=info.Instance.Config;
                else if(bridge!=null && bridge.Invoke(null,new object[]{id}) is ConfigFile config)Configs[id]=config;
            }
            complete=true;foreach(string id in Ids)if(!Configs.ContainsKey(id)){complete=false;break;}
        }
        internal static ConfigFile Get(string id){Configs.TryGetValue(id,out var value);return value;}
    }
}
