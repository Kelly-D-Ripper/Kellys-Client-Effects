namespace UnifiedEffects.ControlsCatalog
{
    // Compiled into both menus. Keep granular effect identities in one place.
    internal sealed class EffectControl
    {
        internal readonly int Category;
        internal readonly string Kind, Plugin, Section, Key, Label;
        internal readonly float Min, Max;
        internal EffectControl(int category,string kind,string plugin,string section,string key,string label,float min=0f,float max=0f)
        { Category=category;Kind=kind;Plugin=plugin;Section=section;Key=key;Label=label;Min=min;Max=max; }
    }
    internal static class AdditionalEffectControls
    {
        private const string Weather="aaron.boscali.weather-unified",Feel="aaron.boscali.cockpitfeel",Audio="aaron.boscali.cockpit-audio",Core="aaron.boscali.unified",Atmo="paul.no.clouds";
        internal static readonly EffectControl[] Items={
            new EffectControl(7,"Toggle",Weather,"Weather display","Fast volumetric weather transitions","Quick volumetric weather transitions"),
            new EffectControl(7,"Slider",Weather,"Weather display","Volumetric transition seconds","Volumetric transition seconds",5f,1800f),
            new EffectControl(0,"Toggle",Weather,"Weather","SunDiscEnabled","Sun-disc colour and haze"),
            new EffectControl(0,"Toggle",Weather,"Weather","AuroraEnabled","Aurora (sky phenomena master)"),
            new EffectControl(0,"Toggle",Weather,"Weather","RedSpritesEnabled","Red sprites (sky phenomena master)"),
            new EffectControl(0,"Toggle",Weather,"Weather","BlueJetsEnabled","Blue jets (sky phenomena master)"),
            new EffectControl(0,"Toggle",Weather,"Weather","RainStreaksEnabled","Falling rain streaks (rain visuals master)"),
            new EffectControl(0,"Toggle",Weather,"Weather","CloudPassageMistEnabled","Cloud-passage mist (rain visuals master)"),
            new EffectControl(0,"Toggle",Weather,"Weather","ThunderAudioEnabled","Extreme-storm thunder (rain sound master)"),
            new EffectControl(0,"Toggle",Weather,"Weather","ExtremeStormCloudDeckEnabled","Extreme-storm thick cloud deck"),
            new EffectControl(0,"Toggle",Weather,"Weather","ExtremeStormGroundMistEnabled","Extreme-storm ground mist (rain haze master)"),
            new EffectControl(0,"Toggle",Weather,"Weather","ExtremeStormFogEnabled","Extreme-storm visibility fog (rain haze master)"),
            new EffectControl(1,"Toggle",Weather,"Weather","SeaSprayParticlesEnabled","Airborne ocean spray (spray master)"),
            new EffectControl(1,"Toggle",Weather,"Weather","SeaFoamWakeEnabled","Ocean foam wakes (spray master)"),
            new EffectControl(1,"Toggle",Weather,"Weather","SeaSurfaceWashEnabled","Ocean surface wash (spray + ocean waves)"),
            new EffectControl(2,"Toggle",Feel,"Cockpit Feel","Ground roll vibration","Ground-roll / takeoff vibration"),
            new EffectControl(2,"Slider",Feel,"Cockpit Feel","Ground roll vibration strength","Ground-roll vibration strength",0f,3f),
            new EffectControl(2,"Toggle",Feel,"Cockpit Feel","Gun recoil","Cockpit gun recoil"),
            new EffectControl(2,"Toggle",Feel,"Cockpit Feel","Touchdown jolts","Touchdown jolts"),
            new EffectControl(2,"Toggle",Feel,"Cockpit Feel","Landing gear jolts","Landing-gear lock jolts"),
            new EffectControl(2,"Toggle",Feel,"Cockpit Feel","Transonic vibration","Transonic vibration (Mach buffet master)"),
            new EffectControl(2,"Toggle",Feel,"Cockpit Feel","Sonic crossing jolt","Sonic-crossing jolt (Mach buffet master)"),
            new EffectControl(2,"Toggle",Audio,"Cockpit Audio","Breathing cycles","Breathing cycles (breathing master)"),
            new EffectControl(2,"Toggle",Audio,"Cockpit Audio","G strain","G-strain voice (breathing master)"),
            new EffectControl(2,"Toggle",Audio,"Cockpit Audio","G suit hiss","G-suit hiss (breathing master)"),
            new EffectControl(2,"Toggle",Audio,"Cockpit Audio","Heartbeat","Heartbeat (breathing master)"),
            new EffectControl(2,"Toggle",Audio,"Cockpit Audio","Recovery gasps","Recovery gasps (breathing master)"),
            new EffectControl(2,"Toggle",Audio,"Cockpit Audio","Structural creaks","Structural creaks (airframe master)"),
            new EffectControl(2,"Toggle",Audio,"Cockpit Audio","Harness strain","Harness strain (airframe master)"),
            new EffectControl(2,"Toggle",Audio,"Cockpit Audio","Impact foley","Impact sounds (airframe master)"),
            new EffectControl(2,"Toggle",Audio,"Cockpit Audio","Gear lever","Gear-lever sound (control foley master)"),
            new EffectControl(2,"Toggle",Audio,"Cockpit Audio","Canopy closing","Canopy-close sound (control foley master)"),
            new EffectControl(2,"Toggle",Core,"Audio","Pull up warning","Pull-up warning (voice master)"),
            new EffectControl(2,"Toggle",Core,"Audio","Terrain warning","Terrain warning (voice master)"),
            new EffectControl(2,"Toggle",Core,"Audio","Over G warning","Over-G warning (voice master)"),
            new EffectControl(2,"Toggle",Core,"Audio","Bingo warning","Bingo warning (voice master)"),
            new EffectControl(7,"Toggle",Atmo,"General","CloudsInReflections","Clouds reflected in water and surfaces"),
            new EffectControl(7,"Toggle",Atmo,"General","WaterLighting","NOAtmoFX ocean lighting"),
            new EffectControl(7,"Toggle",Atmo,"Weather","DiurnalCycle","Cloud growth through the day"),
        };
    }
}
