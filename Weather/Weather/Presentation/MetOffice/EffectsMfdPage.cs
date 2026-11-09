using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    // Reuses the same BepInEx settings as the original Ctrl+F8 panel.
    internal sealed class EffectsMfdPage
    {
        internal sealed class Item
        {
            internal readonly int category; internal readonly string kind,id,section,key,label; internal readonly float min,max;
            internal readonly ConfigDefinition definition;
            internal Item(int c,string k,string i,string s,string n,string l,float low=0,float high=0){category=c;kind=k;id=i;section=s;key=n;label=l;min=low;max=high;definition=new ConfigDefinition(s,n);}
        }
        private const string Boscali="aaron.boscali.unified", Krop="aaron.boscali.krop-unified", Night="aaron.boscali.betternight", Weather="aaron.boscali.weather-unified", Boom="aaron.boscali.missile-unified", CockpitAudio="aaron.boscali.cockpit-audio", CockpitFeel="aaron.boscali.cockpitfeel", Airfield="aaron.boscali.airfield-lighting", NoAtmo="paul.no.clouds";
        private static readonly string[] Names={"WEATHER","OCEAN","COCKPIT","TRAILS","LIGHTS","SOUND","BATTLEFIELD"};
        internal static readonly Item[] Items={
            new Item(0, "Toggle", Weather, "Weather", "ConsoleKeyOFallback", "O / Ctrl+O weather shortcut (disable if O conflicts)"),
            new Item(0, "Toggle", Weather, "Weather", "Enabled", "Boscali weather system"),
            new Item(0, "Toggle", Weather, "Weather", "RainVisualsEnabled", "Rain visuals"),
            new Item(0, "Slider", Weather, "Weather", "RainDensity", "Rain density", 0.25f, 2f),
            new Item(0, "Toggle", Weather, "Weather", "RainAudioEnabled", "Rain sound"),
            new Item(0, "Toggle", Weather, "Weather", "CanopyRainEnabled", "Canopy rain"),
            new Item(0, "Toggle", Weather, "Weather", "CanopyShaderEnabled", "Canopy runoff shader"),
            new Item(0, "Toggle", Weather, "Weather", "TerrainRainEnabled", "Wet terrain"),
            new Item(0, "Toggle", Weather, "Weather", "RainAtmosphereEnabled", "Rain haze and atmosphere"),
            new Item(0, "Toggle", Weather, "Weather", "SunShaftsEnabled", "Sun shafts"),
            new Item(0, "Toggle", Weather, "Weather", "SkyPhenomenaEnabled", "Sky phenomena"),
            new Item(0, "Toggle", Weather, "Weather", "DarkNightsEnabled", "Boscali dark nights"),
            new Item(0, "Toggle", Weather, "Weather", "LightningEnabled", "Boscali storm lightning"),
            new Item(0, "Toggle", Weather, "Weather", "ReducedFlashes", "Reduce Boscali flashes (native flashes unchanged)"),
            new Item(0, "Toggle", Weather, "Weather", "CloudPassageShakeEnabled", "Cloud-passage camera shake"),
            new Item(1, "Toggle", Weather, "Weather", "OceanWavesEnabled", "Ocean waves"),
            new Item(1, "Toggle", Weather, "Weather", "SeaSprayEnabled", "Aircraft sea spray and foam wakes"),
            new Item(2, "Toggle", Boscali, "Immersion", "PilotBodyEnabled", "First-person pilot body"),
            new Item(2, "Toggle", Boscali, "Immersion", "PilotControlMotionEnabled", "Pilot control and G-load motion"),
            new Item(2, "Toggle", Boscali, "Immersion", "KneeboardEnabled", "Pilot kneeboard (requires body)"),
            new Item(2, "Toggle", CockpitFeel, "Cockpit Feel", "Head inertia", "Head inertia"),
            new Item(2, "Slider", CockpitFeel, "Cockpit Feel", "Head inertia strength", "Head inertia strength", 0f, 2f),
            new Item(2, "Toggle", CockpitFeel, "Cockpit Feel", "Cockpit shake", "Cockpit vibration and shake"),
            new Item(2, "Slider", CockpitFeel, "Cockpit Feel", "Cockpit shake strength", "Cockpit shake strength", 0f, 2f),
            new Item(2, "Toggle", CockpitFeel, "Cockpit Feel", "Mach buffet", "Mach buffet"),
            new Item(2, "Toggle", CockpitFeel, "Cockpit Feel", "Comfort motion", "Comfort motion"),
            new Item(2, "Toggle", CockpitFeel, "Cockpit Feel", "Instrument glow", "Instrument glow"),
            new Item(2, "Toggle", CockpitFeel, "Cockpit Feel", "Cockpit night lighting", "Cockpit night lighting"),
            new Item(2, "Toggle", CockpitFeel, "Cockpit Feel", "Cockpit damage shading", "Cockpit damage shading"),
            new Item(2, "Toggle", Boscali, "Immersion", "HelmetVisor", "Helmet visor reflections"),
            new Item(2, "Slider", Boscali, "Immersion", "HelmetReflection", "Visor reflection strength", 0f, 1f),
            new Item(2, "Toggle", Boscali, "Immersion", "SunGlare", "Sun glare"),
            new Item(2, "Toggle", Boscali, "Audio", "Cockpit voice warnings", "Cockpit voice warnings (off by default)"),
            new Item(2, "Toggle", CockpitAudio, "Cockpit Audio", "PilotBreathing", "Pilot breathing and G-suit sounds"),
            new Item(2, "Toggle", CockpitAudio, "Cockpit Audio", "AirframeCreaks", "Airframe creaks and stress sounds"),
            new Item(2, "Toggle", CockpitAudio, "Cockpit Audio", "CanopyWind", "Canopy wind rush"),
            new Item(2, "Toggle", CockpitAudio, "Cockpit Audio", "ControlFoley", "Gear and canopy cockpit cues"),
            new Item(2, "Toggle", CockpitAudio, "Cockpit Audio", "SoftRadarTones", "Optional softer radar tones"),
            new Item(3, "Toggle", Krop, "00 General", "Enable pack", "KROP client effects master"),
            new Item(3, "Toggle", Krop, "05 Contrails", "Aircraft contrails", "Aircraft contrails"),
            new Item(3, "Toggle", Krop, "05 Contrails", "Missile contrails", "Missile contrails"),
            new Item(3, "Toggle", Krop, "04 Low-flight wake", "Air wake effects", "Low-flight air wakes"),
            new Item(3, "Toggle", Krop, "04 Low-flight wake", "Land dust", "Land dust"),
            new Item(4, "Toggle", Night, "1. General", "Enabled", "Better Night master"),
            new Item(4, "Toggle", Night, "2. Night", "Enabled", "Night ambient lighting"),
            new Item(4, "Slider", Night, "2. Night", "Ambient Boost", "Night ambient strength", 0f, 5f),
            new Item(4, "Toggle", Night, "3. Lights", "City Windows", "Lit building windows"),
            new Item(4, "Slider", Night, "3. Lights", "City Intensity", "Window brightness", 0f, 30f),
            new Item(4, "Toggle", Night, "3. Lights", "Vehicle Lights", "Vehicle lights"),
            new Item(4, "Toggle", Night, "3. Lights", "Other Lights", "Other lights and beacons"),
            new Item(4, "Toggle", Night, "4. Street Lights", "Enabled", "Street lights"),
            new Item(4, "Toggle", Night, "4. Street Lights", "Always On (ignore day/night)", "Street lights in daylight"),
            new Item(4, "Toggle", Night, "4. Street Lights", "Poles", "Street-light poles"),
            new Item(4, "EnumChoice", Night, "4. Street Lights", "Light Sources", "Street-light appearance"),
            new Item(4, "Toggle", Boscali, "Lighting", "Compact street lighting", "Compact street lighting"),
            new Item(4, "Slider", Boscali, "Lighting", "Street pool radius", "Ground light radius (m)", 15f, 60f),
            new Item(4, "Slider", Boscali, "Lighting", "Street pool brightness", "Ground light strength", 0f, 2f),
            new Item(4, "Toggle", Boscali, "Lighting", "Reduce street glow at altitude", "Reduce street glow at altitude"),
            new Item(4, "Toggle", Boscali, "Lighting", "Reduce street glow above clouds", "Reduce street glow above clouds"),
            new Item(4, "Toggle", Airfield, "Airfield lighting", "Enabled", "Airfield and site lighting"),
            new Item(4, "Toggle", Airfield, "Airfield lighting", "Runways", "Runway lights"),
            new Item(4, "Toggle", Airfield, "Airfield lighting", "Approach strobes", "Sequenced approach strobes"),
            new Item(4, "Toggle", Airfield, "Airfield lighting", "Legacy flashing runway ends", "Flash runway ends (nonstandard)"),
            new Item(4, "Toggle", Airfield, "Airfield lighting", "Taxiways", "Taxiway lights"),
            new Item(4, "Toggle", Airfield, "Airfield lighting", "Hangars", "Hangar illumination"),
            new Item(4, "Toggle", Airfield, "Airfield lighting", "Helipads", "Helipad lights"),
            new Item(4, "Toggle", Airfield, "Airfield lighting", "Industrial and ports", "Industrial and port lights"),
            new Item(5, "Toggle", CockpitAudio, "Cockpit Audio", "PilotBreathing", "Pilot breathing and G-suit sounds"),
            new Item(5, "Toggle", CockpitAudio, "Cockpit Audio", "AirframeCreaks", "Airframe creaks and stress sounds"),
            new Item(5, "Toggle", CockpitAudio, "Cockpit Audio", "CanopyWind", "Canopy wind rush"),
            new Item(5, "Toggle", CockpitAudio, "Cockpit Audio", "ControlFoley", "Gear and canopy cockpit cues"),
            new Item(5, "Toggle", CockpitAudio, "Cockpit Audio", "SoftRadarTones", "Optional softer radar tones"),
            new Item(5, "Toggle", Krop, "00 General", "Enable pack", "KROP client effects master"),
            new Item(5, "Toggle", Krop, "01 Audio filters", "Atmospheric muffling", "Distant sound muffling"),
            new Item(5, "Toggle", Krop, "01 Audio filters", "First-person cockpit muffling", "Cockpit muffling"),
            new Item(5, "Toggle", Krop, "01 Audio filters", "Reduced mechanical muffling", "Reduced mechanical muffling"),
            new Item(5, "Toggle", Krop, "01 Audio filters", "Ejection filter release", "Fade cockpit filter after ejection"),
            new Item(5, "Toggle", Krop, "01 Audio filters", "Own-aircraft impact filtering", "Own-aircraft impact filtering"),
            new Item(5, "Toggle", Krop, "01 Audio filters", "Helicopter turbine treatment", "Helicopter turbine treatment"),
            new Item(5, "Toggle", Krop, "02 Bullet near misses", "Bullet cracks", "Bullet near-miss cracks"),
            new Item(5, "Toggle", Krop, "02 Bullet near misses", "Large-round camera shake", "Large-round camera shake"),
            new Item(5, "Toggle", Krop, "03 Aircraft sonic booms", "Replacement aircraft booms", "Aircraft sonic booms"),
            new Item(5, "Toggle", Krop, "03 Aircraft sonic booms", "Retrigger suppression", "Suppress repeated aircraft booms"),
            new Item(5, "Toggle", Boscali, "Audio", "Cockpit voice warnings", "Cockpit voice warnings"),
            new Item(5, "Toggle", Boom, "Audio", "Missile sonic boom", "Experimental missile sonic booms (mission restart)"),
            new Item(6, "Toggle", Boscali, "Impact marks", "Enabled", "Impact scorch decals"),
            new Item(6, "Toggle", Krop, "00 General", "Enable pack", "KROP client effects master"),
            new Item(6, "Toggle", Krop, "07 Cosmetic lifeboats", "Lifeboats", "Lifeboats from sinking ships"),
            new Item(6, "Toggle", Krop, "07 Cosmetic lifeboats", "Gentle bobbing", "Lifeboat bobbing"),
            new Item(6, "Toggle", Krop, "07 Cosmetic lifeboats", "Blinking beacons", "Lifeboat beacons"),
            new Item(7, "Preset", NoAtmo, "Quality", "Preset", "Quality preset"),
            new Item(7, "Feature", NoAtmo, "", "Clouds", "Volumetric clouds"),
            new Item(7, "Feature", NoAtmo, "", "Shadows", "Cloud shadows"),
            new Item(7, "Feature", NoAtmo, "", "RainShafts", "Distant rain curtains"),
            new Item(7, "Feature", NoAtmo, "", "NearRain", "Rain near camera"),
            new Item(7, "Feature", NoAtmo, "", "Lightning", "NOAtmoFX lightning"),
            new Item(7, "Feature", NoAtmo, "", "Turbulence", "NOAtmoFX turbulence"),
            new Item(7, "Feature", NoAtmo, "", "GodRays", "Sun rays"),
            new Item(7, "Feature", NoAtmo, "", "LightShafts", "Light shafts"),
            new Item(7, "Feature", NoAtmo, "", "Fog", "NOAtmoFX fog"),
            new Item(7, "Feature", NoAtmo, "", "WetGlass", "NOAtmoFX wet glass"),
            new Item(7, "Feature", NoAtmo, "", "NuclearClouds", "Nuclear cloud effects"),
            new Item(7, "Feature", NoAtmo, "", "KeepSun", "Keep sun below clouds"),
            new Item(7, "Feature", NoAtmo, "", "Rainbow", "Rainbow"),
            new Item(7, "Feature", NoAtmo, "", "Glory", "Glory"),
            new Item(7, "Feature", NoAtmo, "", "ValleyFog", "Valley fog"),
            new Item(7, "Feature", NoAtmo, "", "MountainCloud", "Mountain clouds"),
            new Item(7, "Toggle", NoAtmo, "Integration", "FollowBoscaliWeather", "Follow Boscali weather"),
            new Item(7, "Toggle", NoAtmo, "Integration", "YieldToBoscali", "Yield rain/fog to Boscali"),
            new Item(7, "Slider", NoAtmo, "Weather", "ChangeTime", "Cloud transition minutes", 1f, 30f),
            new Item(7, "Toggle", NoAtmo, "Weather", "OwnCirrus", "NOAtmoFX cirrus"),
            new Item(7, "Slider", NoAtmo, "Weather", "Cirrus", "Cirrus amount", 0f, 2f),
            new Item(7, "Slider", NoAtmo, "Weather", "MidLevel", "Mid-level clouds", 0f, 2f),
            new Item(7, "Slider", NoAtmo, "Layer", "OvercastDeck", "Overcast layer", 0f, 1f),
            new Item(7, "Slider", NoAtmo, "Shape", "CoverageScale", "Cumulus coverage", 0f, 2f),
            new Item(7, "Slider", NoAtmo, "Shape", "Density", "Cloud density", .002f, .2f),
            new Item(7, "Slider", NoAtmo, "Shape", "Dome", "Cumulus shape", 0f, 1.5f),
            new Item(7, "Slider", NoAtmo, "Shape", "Billow", "Billowing detail", 0f, 1.5f),
            new Item(7, "Slider", NoAtmo, "Shape", "Blur", "Cloud blur", 0f, 4f),
            new Item(7, "Slider", NoAtmo, "Weather", "HumidityBias", "Humidity", -.5f, .5f),
            new Item(7, "Slider", NoAtmo, "Weather", "InstabilityBias", "Storm instability", -.5f, .5f),
            new Item(7, "Front", NoAtmo, "Weather", "Front", "Weather front", 0f, 3f),
            new Item(7, "Slider", NoAtmo, "Weather", "FrontDistanceKm", "Front distance km", -300f, 300f),
            new Item(7, "Slider", NoAtmo, "Weather", "FrontWidthKm", "Front width km", 3f, 60f),
            new Item(7, "Slider", NoAtmo, "Storm", "CbTop", "Storm cloud top m", 5000f, 16000f),
            new Item(7, "Slider", NoAtmo, "Storm", "Anvil", "Anvil width", 0f, .8f),
            new Item(7, "Slider", NoAtmo, "Storm", "Supercells", "Supercell chance", 0f, 1f),
            new Item(7, "Slider", NoAtmo, "Lighting", "WetDarkening", "Rain cloud darkening", 0f, 2f),
            new Item(7, "Slider", NoAtmo, "Atmosphere", "Lightning", "NOAtmoFX lightning", 0f, 3f),
            new Item(7, "Slider", NoAtmo, "Atmosphere", "GodRays", "Sun rays", 0f, 1.5f),
            new Item(7, "Slider", NoAtmo, "Atmosphere", "LightShafts", "Light shafts", 0f, 2f),
            new Item(7, "Downscale", NoAtmo, "Quality", "Downscale", "Render downscale", 1f, 4f),
        };
        private readonly List<Item> visible=new List<Item>();
        private readonly Button[] categories=new Button[7], rows=new Button[9], minus=new Button[9], plus=new Button[9];
        private readonly Func<RectTransform,string,float,float,float,float,Action,Button> button;
        private readonly Action<Button,string,bool,bool> label;
        private TMP_Text pageText, hint;
        private Button previous,next;
        private int category,page;
        private readonly bool cloudOnly;
        private const int PageSize=9;

        internal EffectsMfdPage(RectTransform root,Func<RectTransform,string,float,float,float,float,Action,Button> make,Action<Button,string,bool,bool> set,Action<TMP_Text> style,bool cloudOnly=false,float availableHeight=560f)
        {
            button=make;label=set;this.cloudOnly=cloudOnly;if(cloudOnly)category=7;
            if(!cloudOnly)for(int i=0;i<categories.Length;i++){int ix=i;categories[i]=button(root,Names[i],16+(i%4)*112,6+(i/4)*34,108,30,()=>{category=ix;page=0;Refresh();});}
            float yBase=cloudOnly?36f:112f,footerY=Mathf.Max(yBase+9*20f,availableHeight-40f);
            float rowStep=(footerY-yBase-10f)/PageSize;float rowHeight=Mathf.Max(15f,rowStep-5f);
            hint=AvText.Make(root,"Effects hint",AvTextRole.Micro,cloudOnly?"NOATMOFX CLOUDS  /  ATMOSPHERE":"SELECT A CATEGORY  /  LOCAL CLIENT SETTINGS",TextAlignmentOptions.MidlineLeft);AvLay.Place(hint,16,cloudOnly?6f:80f,448,22);
            style(hint);
            for(int i=0;i<PageSize;i++)
            {
                int ix=i;float y=yBase+i*rowStep;
                rows[i]=button(root,"",16,y,350,rowHeight,()=>Change(ix,0));
                minus[i]=button(root,"-",370,y,45,rowHeight,()=>Change(ix,-1));
                plus[i]=button(root,"+",419,y,45,rowHeight,()=>Change(ix,1));
            }
            previous=button(root,"< PREV",16,footerY,115,32,()=>{if(page>0)page--;Refresh();});
            next=button(root,"NEXT >",349,footerY,115,32,()=>{if((page+1)*PageSize<Count())page++;Refresh();});
            pageText=AvText.Make(root,"Effects page",AvTextRole.Head,"",TextAlignmentOptions.Center);AvLay.Place(pageText,134,footerY,210,32);
            style(pageText);
            Refresh();
        }
        internal void Reflow(float availableHeight)
        {
            float yBase=cloudOnly?36f:112f,footerY=Mathf.Max(yBase+9*20f,availableHeight-40f);
            float rowStep=(footerY-yBase-10f)/PageSize,rowHeight=Mathf.Max(15f,rowStep-5f);
            for(int i=0;i<PageSize;i++)
            {
                float y=yBase+i*rowStep;var r=(RectTransform)rows[i].transform;
                AvLay.Place(r,16,y,r.sizeDelta.x,rowHeight);
                AvLay.Place((RectTransform)minus[i].transform,370,y,45,rowHeight);
                AvLay.Place((RectTransform)plus[i].transform,419,y,45,rowHeight);
            }
            AvLay.Place((RectTransform)previous.transform,16,footerY,115,32);
            AvLay.Place((RectTransform)next.transform,349,footerY,115,32);
            AvLay.Place(pageText,134,footerY,210,32);
        }
        private int Count(){int n=0;foreach(var x in Items)if(x.category==category)n++;return n;}
        private static ConfigEntryBase Find(Item item)
        {
            var cfg=EffectsSettingsCache.Get(item.id);if(cfg==null)return null;
            return cfg.ContainsKey(item.definition)?cfg[item.definition]:null;
        }
        internal void Refresh()
        {
            if(cloudOnly)hint.text=CloudModeCoordinator.Live?.HostOwnsWeather==true?"HOST WEATHER / LOCAL CHANGES THIS SESSION":CloudModeCoordinator.Live?.SessionOnly==true?"MULTIPLAYER / LOCAL CHANGES THIS SESSION":"NOATMOFX CLOUDS / ATMOSPHERE";
            visible.Clear();foreach(var x in Items)if(x.category==category)visible.Add(x);
            int pages=Math.Max(1,(visible.Count+PageSize-1)/PageSize);page=Mathf.Clamp(page,0,pages-1);
            for(int i=0;i<categories.Length;i++) label(categories[i],Names[i],i==category,true);
            for(int i=0;i<PageSize;i++)
            {
                int ix=page*PageSize+i;bool exists=ix<visible.Count;
                if(rows[i].gameObject.activeSelf!=exists)rows[i].gameObject.SetActive(exists);
                if(!exists){if(minus[i].gameObject.activeSelf)minus[i].gameObject.SetActive(false);if(plus[i].gameObject.activeSelf)plus[i].gameObject.SetActive(false);continue;}
                Item item=visible[ix];ConfigEntryBase entry=Find(item);var api=CloudModeCoordinator.Live;
                bool feature=item.kind=="Feature",preset=item.kind=="Preset";
                bool usable=feature || preset ? api!=null && api.AddonReady : entry!=null;
                bool hostLocked=item.category==7 && api!=null && api.HostOwnsWeather && !api.CanEdit(item.section,item.key);
                if(item.category==7)usable=usable && api!=null && api.CanEdit(item.section,item.key);
                string value=hostLocked?"HOST CONTROLLED":"UNAVAILABLE";
                if(usable && feature)value=api.Feature(item.key)?"[ X ]":"[   ]";
                else if(usable && preset)value=api.CurrentPreset.ToUpperInvariant();
                else if(usable && item.kind=="Toggle" && entry.BoxedValue is bool b)value=b?"[ X ]":"[   ]";
                else if(usable && item.kind=="Slider" && entry.BoxedValue is float f)value=f.ToString("0.00");
                else if(usable && item.kind=="Front" && entry.BoxedValue is int front)value=front>=0 && front<=3?new[]{"NONE","COLD","WARM","AUTO"}[front]:"--";
                else if(usable && item.kind=="Downscale" && entry.BoxedValue is int down)value=down.ToString()+"X";
                else if(usable && item.kind=="EnumChoice")value=entry.BoxedValue?.ToString()??"--";
                bool selected=usable && (feature ? api.Feature(item.key) : item.kind=="Toggle" && entry.BoxedValue is bool v && v);
                label(rows[i],value+"   "+item.label,selected,usable);
                bool adjustable=item.kind!="Toggle" && !feature;
                var rowRect=(RectTransform)rows[i].transform;float rowWidth=adjustable?350f:448f;
                if(!Mathf.Approximately(rowRect.sizeDelta.x,rowWidth))rowRect.sizeDelta=new Vector2(rowWidth,rowRect.sizeDelta.y);
                var rowLabel=rows[i].GetComponentInChildren<TMP_Text>(true);if(rowLabel!=null){rowLabel.alignment=TextAlignmentOptions.MidlineLeft;rowLabel.margin=new Vector4(8f,0f,8f,0f);}
                if(minus[i].gameObject.activeSelf!=adjustable)minus[i].gameObject.SetActive(adjustable);
                if(plus[i].gameObject.activeSelf!=adjustable)plus[i].gameObject.SetActive(adjustable);
                if(adjustable){label(minus[i],"-",false,usable);label(plus[i],"+",false,usable);}
            }
            label(previous,"< PREV",false,page>0);label(next,"NEXT >",false,page+1<pages);
            pageText.text=(page+1)+" / "+pages;
        }
        private void Change(int row,int direction)
        {
            int ix=page*PageSize+row;if(ix>=visible.Count)return;Item item=visible[ix];var api=CloudModeCoordinator.Live;
            if(item.category==7 && (api==null || !api.CanEdit(item.section,item.key)))return;
            if(item.kind=="Feature"){api?.ToggleFeature(item.key);Refresh();return;}
            if(item.kind=="Preset")
            {
                if(api!=null && api.AddonReady && direction!=0){var values=api.Presets;int current=Array.IndexOf(values,api.CurrentPreset);if(values.Length>0){int next=current<0?(direction>0?0:values.Length-1):(current+direction+values.Length)%values.Length;api.Preset(values[next]);}}
                Refresh();return;
            }
            var entry=Find(item);if(entry==null)return;
            var config=item.category==7?EffectsSettingsCache.Get(NoAtmo):null;
            bool temporary=config!=null && api.SessionOnly,save= config!=null && config.SaveOnConfigSet;
            if(temporary)config.SaveOnConfigSet=false;
            try {
            if(item.kind=="Toggle" && entry.BoxedValue is bool b)entry.BoxedValue=!b;
            else if(item.kind=="Slider" && direction!=0 && entry.BoxedValue is float f)entry.BoxedValue=Mathf.Clamp(f+direction*(item.max-item.min)/20f,item.min,item.max);
            else if(item.kind=="Front" && direction!=0 && entry.BoxedValue is int front)entry.BoxedValue=(front+direction+4)%4;
            else if(item.kind=="Downscale" && direction!=0 && entry.BoxedValue is int down){int[] values={1,2,3,4};int current=Array.IndexOf(values,down);entry.BoxedValue=values[(Mathf.Max(0,current)+direction+values.Length)%values.Length];}
            else if(item.kind=="EnumChoice" && direction!=0 && entry.SettingType.IsEnum)
            {var values=Enum.GetValues(entry.SettingType);int index=Array.IndexOf(values,entry.BoxedValue);entry.BoxedValue=values.GetValue((index+direction+values.Length)%values.Length);}
            } finally {if(temporary)config.SaveOnConfigSet=save;}
            Refresh();
        }
    }
}
