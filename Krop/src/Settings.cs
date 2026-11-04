using BepInEx.Configuration;
using UnityEngine;

namespace KropClientEffects
{
    internal static class Settings
    {
        internal static ConfigFile File;
        internal static ConfigEntry<bool> Master, Logging;
        internal static bool Active => Plugin.Running && Master.Value;
        internal static ConfigEntry<bool> Toggle(string section, string name, bool value, string description)
            => File.Bind(section, name, value, description);
        internal static ConfigEntry<float> Number(string section, string name, float value, float min, float max, string description)
            => File.Bind(section, name, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
        internal static ConfigEntry<int> Count(string section, string name, int value, int min, int max, string description)
            => File.Bind(section, name, value, new ConfigDescription(description, new AcceptableValueRange<int>(min, max)));
        internal static void Initialize(ConfigFile file)
        {
            File = file;
            Master = Toggle("00 General", "Enable pack", true, "Enable local audio and visual effects. Turning off restores tracked audio and removes local effects.");
            Logging = Toggle("00 General", "Diagnostic logging", false, "Log patch startup and unsupported audio hosts. Does not change multiplayer compatibility.");
            AudioConfig.Initialize(); BulletConfig.Initialize(); BoomConfig.Initialize();
            AirWakeConfig.Initialize(); ContrailConfig.Initialize(); LifeboatConfig.Initialize();
        }
    }
    internal static class VerboseLoggingConfig { internal static ConfigEntry<bool> Enabled => Settings.Logging; }
    internal static class AudioConfig
    {
        internal static ConfigEntry<bool> Atmosphere, Cockpit, Mechanical, Ejection, OwnHit, Helicopter;
        internal static ConfigEntry<float> AtmosphereStrength, MinimumCutoff, CockpitCutoff, MechanicalStrength, EjectionSeconds, OwnHitMeters, HelicopterStrength;
        internal static ConfigEntry<int> MaxHosts;
        internal static void Initialize()
        {
            const string s = "01 Audio filters";
            Atmosphere = Settings.Toggle(s,"Atmospheric muffling",true,"Distance-dependent filtering of registered world audio; no audio travel-delay replacement.");
            AtmosphereStrength = Settings.Number(s,"Atmospheric strength",1f,0f,4f,"Atmospheric absorption strength; zero disables distance attenuation of high frequencies.");
            MinimumCutoff = Settings.Number(s,"Minimum distance cutoff Hz",200f,50f,22000f,"Lowest permitted atmospheric low-pass cutoff.");
            Cockpit = Settings.Toggle(s,"First-person cockpit muffling",true,"Muffle supported world and mechanical sounds in the local cockpit. UI/RWR/voice sources are not selected.");
            CockpitCutoff = Settings.Number(s,"Cockpit cutoff Hz",456.338f,100f,22000f,"Low-pass cutoff while seated in the cockpit.");
            Mechanical = Settings.Toggle(s,"Reduced mechanical muffling",true,"Weaker cockpit filtering for bay doors, swing wings and gear mechanisms.");
            MechanicalStrength = Settings.Number(s,"Mechanical muffle fraction",0.75f,0f,1f,"Fraction of normal cockpit muffling on those mechanisms.");
            Ejection = Settings.Toggle(s,"Ejection filter release",true,"Fade cockpit muffling away on local ejection; reset when entering a cockpit.");
            EjectionSeconds = Settings.Number(s,"Ejection release seconds",0.3f,0f,3f,"Duration of cockpit filter release.");
            OwnHit = Settings.Toggle(s,"Own-aircraft impact filtering",true,"Scale impact muffling by distance from the local cockpit to the struck part.");
            OwnHitMeters = Settings.Number(s,"Own-hit full-muffle distance m",12f,1f,100f,"Distance from the cockpit at which full impact muffling applies.");
            Helicopter = Settings.Toggle(s,"Helicopter turbine treatment",true,"Weaker turbine filtering while seated in Ibis, Chicane or Aryx Knockout.");
            HelicopterStrength = Settings.Number(s,"Helicopter turbine muffle fraction",0.5f,0f,1f,"Fraction of normal cockpit muffling for helicopter turbines.");
            MaxHosts = Settings.Count(s,"Maximum filtered audio hosts",256,32,1024,"Bound on simultaneously tracked GameObjects; other sounds keep their original filters.");
        }
    }
    internal static class BulletConfig
    {
        internal static ConfigEntry<bool> Enabled, Shake;
        internal static ConfigEntry<float> Radius, SmallVolume, LargeVolume, SmallPitch, LargePitch, MaxRate, ShakeStrength, ShakePitchThreshold;
        internal static void Initialize()
        {
            const string s="02 Bullet near misses";
            Enabled=Settings.Toggle(s,"Bullet cracks",true,"Local passing-round sound for supersonic bullets; never changes bullet trajectory, fuse or damage.");
            Radius=Settings.Number(s,"Detection radius m",50f,1f,150f,"Maximum camera-to-bullet-path distance.");
            SmallVolume=Settings.Number(s,"Small-round volume",2f,0f,30f,"Volume at the small-arms end of the projectile-mass curve.");
            LargeVolume=Settings.Number(s,"Large-round volume",4f,0f,40f,"Volume at the largest-round end of the curve.");
            SmallPitch=Settings.Number(s,"Small-round pitch",1.5f,0.25f,3f,"Pitch for rounds at or below 0.04 kg.");
            LargePitch=Settings.Number(s,"Large-round pitch",0.5f,0.25f,3f,"Pitch for rounds at or above 50 kg; intermediate rounds interpolate logarithmically.");
            MaxRate=Settings.Number(s,"Maximum cracks per second",12.5f,1f,30f,"Global sound rate cap for dense gunfire.");
            Shake=Settings.Toggle(s,"Large-round camera shake",true,"Camera effect only; no aircraft force or physical impulse.");
            ShakeStrength=Settings.Number(s,"Camera shake strength",0.5f,0f,1f,"Maximum shake amplitude, reduced with passing distance.");
            ShakePitchThreshold=Settings.Number(s,"Shake pitch threshold",0.75f,0.25f,3f,"Rounds with a mass-derived pitch below this value can shake the camera.");
        }
    }
    internal static class BoomConfig
    {
        internal static ConfigEntry<bool> Replacement, Suppress;
        internal static ConfigEntry<float> Cooldown, FarDistance, FarMaxDistance, Volume;
        internal static void Initialize()
        {
            const string s="03 Aircraft sonic booms";
            Replacement=Settings.Toggle(s,"Replacement aircraft booms",true,"Replace aircraft boom samples with separate near/far three-clip pools; no missile registration.");
            Suppress=Settings.Toggle(s,"Retrigger suppression",true,"Suppress repeated boom audio from the same source within the cooldown.");
            Cooldown=Settings.Number(s,"Retrigger cooldown seconds",1.5f,0f,10f,"Audio-only cooldown; independent of sample replacement.");
            FarDistance=Settings.Number(s,"Far sample threshold m",2500f,100f,15000f,"Distance where far samples and rolloff begin.");
            FarMaxDistance=Settings.Number(s,"Far maximum audible distance m",5000f,200f,20000f,"Far-clip rolloff endpoint, clamped above the far threshold at playback.");
            Volume=Settings.Number(s,"Boom volume multiplier",1f,0f,3f,"Volume multiplier applied to aircraft boom audio.");
        }
    }
    internal static class AirWakeConfig
    {
        internal static ConfigEntry<bool> Enabled, Dust;
        internal static ConfigEntry<float> Speed, Altitude, Lifetime, Spacing, Inherit, Lateral, Settle, Spin;
        internal static ConfigEntry<int> MaxEmitters;
        internal static float LifetimeSeconds=>Lifetime.Value;
        internal static float InheritedSpeedPercent=>Inherit.Value;
        internal static float LateralSpeed=>Lateral.Value;
        internal static float SettleTimeSeconds=>Settle.Value;
        internal static float SpinSpeedDegrees=>Spin.Value;
        internal const float LateralRandomness=0.6f, WeightScaling=0.5f, NormalDirection=0f, TintStrength=1f, NormalStrength=1f, SelfIllumination=0f;
        internal const int CenterFillParticles=2;
        internal static readonly Color LandColor=new Color32(0xE0,0xC5,0x8F,0xFF);
        internal static void Initialize()
        {
            const string s="04 Low-flight wake";
            Enabled=Settings.Toggle(s,"Air wake effects",true,"Local fixed-wing land dust, with no surface, aircraft or physics changes.");
            Dust=Settings.Toggle(s,"Land dust",true,"Emit dust over unpaved terrain. Water spray is handled separately by the ocean effect.");
            Speed=Settings.Number(s,"Minimum speed kmh",400f,50f,1200f,"Minimum aircraft speed for wake emission.");
            Altitude=Settings.Number(s,"Maximum height m",30f,1f,100f,"Maximum height above the surface.");
            Lifetime=Settings.Number(s,"Particle lifetime seconds",10.39437f,1f,30f,"Lifetime of each wake puff.");
            Spacing=Settings.Number(s,"Burst spacing m",10f,5f,100f,"Ground-track distance between wake bursts.");
            Inherit=Settings.Number(s,"Inherited speed percent",67.32394f,0f,100f,"Initial forward movement of wake particles.");
            Lateral=Settings.Number(s,"Lateral throw speed mps",10.79812f,0f,30f,"Outward particle speed, visually scaled by aircraft mass.");
            Settle=Settings.Number(s,"Settle time seconds",0.8f,0.05f,5f,"Time constant for particle deceleration.");
            Spin=Settings.Number(s,"Particle spin degrees per second",70f,0f,180f,"Opposing spins on the left and right wake.");
            MaxEmitters=Settings.Count(s,"Maximum wake aircraft",24,1,64,"Bound on aircraft with local wake systems; two systems per aircraft.");
        }
    }
    internal static class ContrailConfig
    {
        internal static ConfigEntry<bool> Enabled, MissileContrailsEnabled;
        internal static ConfigEntry<float> MinAltitude, MaxAltitude, Lifetime, Widen, StartWidth, EndWidth, Spacing, WindSettle;
        internal static ConfigEntry<int> MaxEmitters;
        internal static float StartThickness=>StartWidth.Value;
        internal static float EndThickness=>EndWidth.Value;
        internal const float NormalStrength=1f, SelfIllumination=0.6032367f;
        internal const bool SwapNormalAxes=false;
        internal static void Initialize()
        {
            const string s="05 Contrails";
            Enabled=Settings.Toggle(s,"Aircraft contrails",true,"Local trails from supported running aircraft engines.");
            MissileContrailsEnabled=Settings.Toggle(s,"Missile contrails",true,"Local trails while missile motors burn; independent of aircraft toggle.");
            MinAltitude=Settings.Number(s,"Minimum altitude ASL m",5000f,0f,20000f,"Minimum altitude above sea level.");
            MaxAltitude=Settings.Number(s,"Maximum altitude ASL m",12000f,1f,30000f,"Maximum altitude; runtime enforces an ordered band.");
            Lifetime=Settings.Number(s,"Trail lifetime seconds",180f,10f,300f,"Persistence of emitted trail particles.");
            Widen=Settings.Number(s,"Widen duration seconds",10f,1f,60f,"Seconds for a trail to reach full width, bounded by its lifetime.");
            StartWidth=Settings.Number(s,"Starting width m",1.553991f,0.1f,10f,"Width when emitted.");
            EndWidth=Settings.Number(s,"Final width m",40f,1f,80f,"Width after widening.");
            Spacing=Settings.Number(s,"Particle spacing m",50f,20f,200f,"Spacing at high speed; halved below 600 km/h.");
            WindSettle=Settings.Number(s,"Wind response seconds",0.3f,0.05f,5f,"Time constant for visual particles to follow mission wind.");
            MaxEmitters=Settings.Count(s,"Maximum trail emitters",48,1,128,"Shared bound for aircraft engines and missiles; 1000 particles per emitter.");
        }
    }
    internal static class LifeboatConfig
    {
        internal static ConfigEntry<bool> Enabled, Bob, Beacon;
        internal static ConfigEntry<float> LifetimeMinutes, Delay;
        internal static ConfigEntry<int> MaxLifeboatsPerShip, GlobalLimit, CrewPerBoat;
        internal static void Initialize()
        {
            const string s="07 Cosmetic lifeboats";
            Enabled=Settings.Toggle(s,"Lifeboats",true,"Local mesh-only decorations after a ship sinks; no collision, rescue, unit registration or networking.");
            LifetimeMinutes=Settings.Number(s,"Lifetime minutes",15f,1f,60f,"Local decoration lifetime.");
            MaxLifeboatsPerShip=Settings.Count(s,"Maximum boats per ship",15,1,30,"Cap on each sinking ship's decorations.");
            GlobalLimit=Settings.Count(s,"Maximum boats in scene",100,1,300,"Global cap including scheduled spawns.");
            CrewPerBoat=Settings.Count(s,"Crew represented per boat",15,1,100,"Visual boat count derived from ship definition manpower; no crew simulation.");
            Delay=Settings.Number(s,"Maximum spawn delay seconds",8f,0f,30f,"Random per-boat delay.");
            Bob=Settings.Toggle(s,"Gentle bobbing",true,"Cosmetic vertical motion.");
            Beacon=Settings.Toggle(s,"Blinking beacons",true,"Local emission on the boat's exterior-light material.");
        }
    }
}

