using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace ClientEffectsControlPanel
{
    // Read by the weather shortcut to avoid competing cursor owners.
    public static class ControlUiBridge
    {
        public static bool IsOpen { get; internal set; }
        internal static Panel Active;
        public static ConfigFile GetConfig(string id) => Active != null ? Active.GetConfig(id) : null;
    }
    // Controls component-owned settings and opens the weather module's authority-checked console.
    [BepInPlugin("aaron.boscali.control-unified", "Unified Client Effects Controls", "0.7.0")]
    [BepInIncompatibility("aaron.clienteffects.controlpanel")]
    [BepInDependency("aaron.boscali.unified", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("aaron.boscali.krop-unified", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("aaron.boscali.betternight", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("aaron.boscali.weather-unified", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("aaron.boscali.missile-unified", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("aaron.boscali.cockpit-audio", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("aaron.boscali.cockpitfeel", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("aaron.boscali.airfield-lighting", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        static GameObject host;
        void Awake()
        {
            var shortcut = Config.Bind("Controls", "Open effects panel", new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl), "Open and close the local effects panel. Close before flying.");
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (host != null) return;
            host = new GameObject("Client Effects Control Panel runtime") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            host.AddComponent<Panel>().Initialize(shortcut);
            Logger.LogInfo("Client effects control panel loaded. Configured hotkey: " + shortcut.Value + "; Ctrl+F8 also works with either Ctrl key.");
        }
    }
    internal sealed class Panel : MonoBehaviour
    {
        const string Boscali = "aaron.boscali.unified";
        const string Krop = "aaron.boscali.krop-unified";
        const string Night = "aaron.boscali.betternight";
        const string Weather = "aaron.boscali.weather-unified";
        const string Boom = "aaron.boscali.missile-unified";
        const string CockpitAudio = "aaron.boscali.cockpit-audio";
        const string CockpitFeel = "aaron.boscali.cockpitfeel";
        const string Airfield = "aaron.boscali.airfield-lighting";
        ConfigEntry<KeyboardShortcut> shortcut;
        Rect window = new Rect(35, 45, 550, 640);
        Vector2 scroll;
        bool open, cursorWasVisible;
        CursorLockMode previousCursor;
        int tab;
        static readonly string[] Tabs = { "Weather", "Ocean", "Cockpit", "Trails", "Lighting", "Sound", "Battlefield" };
        static readonly string[] CandidateIds = { Boscali, Krop, Night, Weather, Boom, CockpitAudio, CockpitFeel, Airfield };
        readonly Dictionary<string, ConfigFile> configs = new Dictionary<string, ConfigFile>();
        Assembly weatherAssembly;
        string weatherStatus;

        internal void Initialize(ConfigEntry<KeyboardShortcut> key) { shortcut = key; ControlUiBridge.Active=this; CaptureAvailable(); }
        internal ConfigFile GetConfig(string id) { configs.TryGetValue(id,out var value); return value; }
        void Update()
        {
            if (configs.Count < CandidateIds.Length) CaptureAvailable();
            // Keep the user's configured shortcut, while making the documented fallback
            // available with either control key even if an older config changed the binding.
            bool fallback = Input.GetKeyDown(KeyCode.F8) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            if (fallback || (shortcut != null && shortcut.Value.IsDown()))
            {
                // MET OFFICE owns the cursor until it is closed. Do not create a
                // second window owner that would restore the cursor out of order.
                if (open || !MetOfficeOpen()) SetOpen(!open);
            }
            if (open && Application.isFocused) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
        void OnDestroy() { SetOpen(false); if(ControlUiBridge.Active==this)ControlUiBridge.Active=null; }
        void SetOpen(bool value)
        {
            if (value == open) return;
            if (value) { cursorWasVisible = Cursor.visible; previousCursor = Cursor.lockState; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else { Cursor.lockState = previousCursor; Cursor.visible = cursorWasVisible; }
            open = value;
            ControlUiBridge.IsOpen = value;
        }
        void OnGUI()
        {
            if (!open) return;
            window.width = Mathf.Min(550f, Mathf.Max(320f, Screen.width - 10f));
            window.height = Mathf.Min(640f, Mathf.Max(260f, Screen.height - 10f));
            window.x = Mathf.Clamp(window.x, 0, Mathf.Max(0, Screen.width - window.width));
            window.y = Mathf.Clamp(window.y, 0, Mathf.Max(0, Screen.height - window.height));
            window = GUILayout.Window(31984173, window, DrawWindow, "CLIENT EFFECTS — EXPERIMENTAL");
        }
        void DrawWindow(int id)
        {
            GUILayout.Label("Local presentation settings. Close panel before flying; game controls are not paused.");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Tabs.Length; i++) if (GUILayout.Toggle(tab == i, Tabs[i], GUI.skin.button)) tab = i;
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll, false, true);
            switch (tab)
            {
                case 0:
                    if (configs.ContainsKey(Weather))
                    {
                        GUILayout.Label("Boscali weather uses the game's native clouds. MET OFFICE: O or Ctrl+O (also your configured weather shortcut). Single-player only.");
                        if (GUILayout.Button("Open MET OFFICE")) OpenMetOffice();
                        if (!string.IsNullOrEmpty(weatherStatus)) GUILayout.Label(weatherStatus);
                        Toggle(Weather, "Weather", "ConsoleKeyOFallback", "O / Ctrl+O weather shortcut (disable if O conflicts)");
                        Toggle(Weather, "Weather", "Enabled", "Boscali weather system");
                        if (!WeatherEnabled()) GUILayout.Label("Weather is off: rain, canopy rain, wet terrain, haze and MET OFFICE are inactive. Ocean spray has its own switch.");
                        Toggle(Weather, "Weather", "RainVisualsEnabled", "Rain visuals");
                        Slider(Weather, "Weather", "RainDensity", "Rain density", 0.25f, 2f);
                        Toggle(Weather, "Weather", "RainAudioEnabled", "Rain sound");
                        Toggle(Weather, "Weather", "CanopyRainEnabled", "Canopy rain");
                        Toggle(Weather, "Weather", "CanopyShaderEnabled", "Canopy runoff shader");
                        Toggle(Weather, "Weather", "TerrainRainEnabled", "Wet terrain");
                        Toggle(Weather, "Weather", "RainAtmosphereEnabled", "Rain haze and atmosphere");
                        Toggle(Weather, "Weather", "SunShaftsEnabled", "Sun shafts");
                        Toggle(Weather, "Weather", "SkyPhenomenaEnabled", "Sky phenomena");
                        Toggle(Weather, "Weather", "DarkNightsEnabled", "Boscali dark nights");
                        Toggle(Weather, "Weather", "LightningEnabled", "Boscali storm lightning");
                        Toggle(Weather, "Weather", "ReducedFlashes", "Reduce Boscali flashes (native flashes unchanged)");
                        Toggle(Weather, "Weather", "CloudPassageShakeEnabled", "Cloud-passage camera shake");
                    }
                    else
                    {
                        GUILayout.Label("Boscali weather system unavailable. Weather controls need its component to be loaded.");
                    }
                    break;
                case 1:
                    Toggle(Krop, "08 Visual refinements", "KROP water wakes", "KROP fast-pass spray (replaces Boscali)");
                    GUILayout.Label("KROP needs Air wake effects: fixed-wing only, normally over 400 km/h and below 30 m. Keep it off for Boscali's rotorcraft and slower-flight spray/wash.");
                    if (configs.ContainsKey(Weather))
                    {
                        Toggle(Weather, "Weather", "OceanWavesEnabled", "Ocean waves");
                        Toggle(Weather, "Weather", "SeaSprayEnabled", "Aircraft sea spray and foam wakes");
                        GUILayout.Label("Sea spray and wakes work independently of Boscali weather and ocean waves. Spray appears behind fast aircraft flying low over water.");
                        if (!WeatherEnabled()) GUILayout.Label("Ocean wave replacement needs Boscali weather enabled in an offline mission. Sea spray can use the native ocean.");
                    }
                    else
                    {
                        GUILayout.Label("Boscali weather system unavailable. Ocean effects need its component to be loaded.");
                    }
                    break;
                case 2:
                    GUILayout.Label("Optional NVG effects use the KROP master and your normal NVG key. All start off.");
                    Toggle(Krop, "09 Night vision", "Allow game auto NVG on camera change", "Allow game auto-NVG on camera change");
                    Toggle(Krop, "09 Night vision", "Phosphor tint", "NVG phosphor tint");
                    IntChoice(Krop, "09 Night vision", "Phosphor colour", "NVG phosphor colour", new[] { "Green", "White", "Amber" });
                    Toggle(Krop, "09 Night vision", "NVG bloom", "NVG bloom");
                    Slider(Krop, "09 Night vision", "NVG bloom strength", "NVG bloom strength", 0f, 80f);
                    Toggle(Krop, "09 Night vision", "NVG grain", "NVG grain and static");
                    Slider(Krop, "09 Night vision", "NVG grain strength", "NVG grain strength", 0f, 1f);
                    Toggle(Krop, "09 Night vision", "NVG sparkles", "NVG bright speckles");
                    IntSlider(Krop, "09 Night vision", "NVG speck count", "NVG speckle count", 0, 100);
                    Toggle(Krop, "09 Night vision", "Nuclear NVG speckles", "Extra NVG speckles after nuclear flashes");
                    Toggle(Krop, "09 Night vision", "NVG auto gain", "NVG automatic brightness adjustment");
                    Toggle(Krop, "09 Night vision", "NVG transition", "NVG raise and lower animation");
                    GUILayout.Space(8);
                    Toggle(Boscali, "Immersion", "PilotBodyEnabled", "First-person pilot body");
                    Toggle(Boscali, "Immersion", "PilotControlMotionEnabled", "Pilot control and G-load motion");
                    Toggle(Boscali, "Immersion", "KneeboardEnabled", "Pilot kneeboard (requires body)");
                    Toggle(CockpitFeel, "Cockpit Feel", "Head inertia", "Head inertia");
                    Slider(CockpitFeel, "Cockpit Feel", "Head inertia strength", "Head inertia strength", 0f, 2f);
                    Toggle(CockpitFeel, "Cockpit Feel", "Cockpit shake", "Cockpit vibration and shake");
                    Slider(CockpitFeel, "Cockpit Feel", "Cockpit shake strength", "Cockpit shake strength", 0f, 2f);
                    Toggle(CockpitFeel, "Cockpit Feel", "Mach buffet", "Mach buffet");
                    Toggle(CockpitFeel, "Cockpit Feel", "Comfort motion", "Comfort motion");
                    Toggle(CockpitFeel, "Cockpit Feel", "Instrument glow", "Instrument glow");
                    Toggle(CockpitFeel, "Cockpit Feel", "Cockpit night lighting", "Cockpit night lighting");
                    Toggle(CockpitFeel, "Cockpit Feel", "Cockpit damage shading", "Cockpit damage shading");
                    Toggle(Boscali, "Immersion", "HelmetVisor", "Helmet visor reflections");
                    Slider(Boscali, "Immersion", "HelmetReflection", "Visor reflection strength", 0f, 1f);
                    Toggle(Boscali, "Immersion", "SunGlare", "Sun glare");
                    Toggle(Boscali, "Audio", "Cockpit voice warnings", "Cockpit voice warnings (off by default)");
                    Toggle(CockpitAudio, "Cockpit Audio", "PilotBreathing", "Pilot breathing and G-suit sounds");
                    Toggle(CockpitAudio, "Cockpit Audio", "AirframeCreaks", "Airframe creaks and stress sounds");
                    Toggle(CockpitAudio, "Cockpit Audio", "CanopyWind", "Canopy wind rush");
                    Toggle(CockpitAudio, "Cockpit Audio", "ControlFoley", "Gear and canopy cockpit cues");
                    Toggle(CockpitAudio, "Cockpit Audio", "SoftRadarTones", "Optional softer radar tones");
                    break;
                case 3:
                    Toggle(Krop, "00 General", "Enable pack", "KROP client effects master");
                    Toggle(Krop, "05 Contrails", "Aircraft contrails", "Aircraft contrails");
                    Toggle(Krop, "05 Contrails", "Missile contrails", "Missile contrails");
                    Toggle(Krop, "04 Low-flight wake", "Air wake effects", "Low-flight air wakes");
                    Toggle(Krop, "04 Low-flight wake", "Land dust", "Land dust");
                    GUILayout.Label("Aircraft trails and dust use KROP when that component is present.");
                    break;
                case 4:
                    Toggle(Night, "1. General", "Enabled", "Better Night master");
                    Toggle(Night, "2. Night", "Enabled", "Night ambient lighting");
                    Slider(Night, "2. Night", "Ambient Boost", "Night ambient strength", 0f, 5f);
                    Toggle(Night, "3. Lights", "City Windows", "Lit building windows");
                    Slider(Night, "3. Lights", "City Intensity", "Window brightness", 0f, 30f);
                    Toggle(Night, "3. Lights", "Vehicle Lights", "Vehicle lights");
                    Toggle(Night, "3. Lights", "Other Lights", "Other lights and beacons");
                    Toggle(Night, "4. Street Lights", "Enabled", "Street lights");
                    Toggle(Night, "4. Street Lights", "Always On (ignore day/night)", "Street lights in daylight");
                    Toggle(Night, "4. Street Lights", "Poles", "Street-light poles");
                    EnumChoice(Night, "4. Street Lights", "Light Sources", "Street-light appearance");
                    Toggle(Boscali, "Lighting", "Compact street lighting", "Compact street lighting");
                    Slider(Boscali, "Lighting", "Street pool radius", "Ground light radius (m)", 15f, 60f);
                    Slider(Boscali, "Lighting", "Street pool brightness", "Ground light strength", 0f, 2f);
                    Toggle(Boscali, "Lighting", "Reduce street glow at altitude", "Reduce street glow at altitude");
                    Toggle(Boscali, "Lighting", "Reduce street glow above clouds", "Reduce street glow above clouds");
                    GUILayout.Space(8);
                    GUILayout.Label("AIRFIELD AND INDUSTRIAL LIGHTING — PROTOTYPE");
                    Toggle(Airfield, "Airfield lighting", "Enabled", "Airfield and site lighting");
                    Toggle(Airfield, "Airfield lighting", "Runways", "Runway lights");
                    Toggle(Airfield, "Airfield lighting", "Approach strobes", "Sequenced approach strobes");
                    Toggle(Airfield, "Airfield lighting", "Legacy flashing runway ends", "Flash runway ends (nonstandard)");
                    Toggle(Airfield, "Airfield lighting", "Taxiways", "Taxiway lights");
                    Toggle(Airfield, "Airfield lighting", "Hangars", "Hangar illumination");
                    Toggle(Airfield, "Airfield lighting", "Helipads", "Helipad lights");
                    Toggle(Airfield, "Airfield lighting", "Industrial and ports", "Industrial and port lights");
                    GUILayout.Label("Compact lighting limits lamp size and light spread. Radius and strength apply immediately; pole layout changes may need a mission reload.");
                    break;
                case 5:
                    GUILayout.Label("Optional sound travel time uses the KROP master. UI and cockpit warnings are excluded.");
                    Toggle(Krop, "09 Sound propagation", "World sound delay", "World sound travel time");
                    Toggle(Krop, "09 Sound propagation", "Engine sound delay", "Engine sound travel time");
                    Toggle(Krop, "09 Sound propagation", "Gun loop fade", "Gun burst fade (requires world delay)");
                    Slider(Krop, "09 Sound propagation", "Maximum delay seconds", "Maximum sound delay (seconds)", 1f, 30f);
                    IntSlider(Krop, "09 Sound propagation", "Maximum delayed sounds", "Maximum delayed sounds", 16, 512);
                    IntSlider(Krop, "09 Sound propagation", "Maximum engine sources", "Maximum delayed engines", 4, 64);
                    GUILayout.Space(8);
                    Toggle(CockpitAudio, "Cockpit Audio", "PilotBreathing", "Pilot breathing and G-suit sounds");
                    Toggle(CockpitAudio, "Cockpit Audio", "AirframeCreaks", "Airframe creaks and stress sounds");
                    Toggle(CockpitAudio, "Cockpit Audio", "CanopyWind", "Canopy wind rush");
                    Toggle(CockpitAudio, "Cockpit Audio", "ControlFoley", "Gear and canopy cockpit cues");
                    Toggle(CockpitAudio, "Cockpit Audio", "SoftRadarTones", "Optional softer radar tones");
                    Toggle(Krop, "00 General", "Enable pack", "KROP client effects master");
                    Toggle(Krop, "01 Audio filters", "Atmospheric muffling", "Distant sound muffling");
                    Toggle(Krop, "01 Audio filters", "First-person cockpit muffling", "Cockpit muffling");
                    Toggle(Krop, "01 Audio filters", "Reduced mechanical muffling", "Reduced mechanical muffling");
                    Toggle(Krop, "01 Audio filters", "Ejection filter release", "Fade cockpit filter after ejection");
                    Toggle(Krop, "01 Audio filters", "Own-aircraft impact filtering", "Own-aircraft impact filtering");
                    Toggle(Krop, "01 Audio filters", "Helicopter turbine treatment", "Helicopter turbine treatment");
                    Toggle(Krop, "01 Audio filters", "Canopy-aware muffling", "Muffling follows canopy opening and loss");
                    Toggle(Krop, "01 Audio filters", "Open-cabin muffling", "Open-cabin muffling for Ibis door guns");
                    Toggle(Krop, "02 Bullet near misses", "Bullet cracks", "Bullet near-miss cracks");
                    Toggle(Krop, "02 Bullet near misses", "Large-round camera shake", "Large-round camera shake");
                    Toggle(Krop, "02 Bullet near misses", "KROP crack shaping", "KROP bullet crack sound shaping");
                    Toggle(Krop, "03 Aircraft sonic booms", "Replacement aircraft booms", "Aircraft sonic booms");
                    Toggle(Krop, "03 Aircraft sonic booms", "Retrigger suppression", "Suppress repeated aircraft booms");
                    Toggle(Krop, "03 Aircraft sonic booms", "Avoid repeated samples", "Avoid repeating aircraft boom recordings");
                    Toggle(Boscali, "Audio", "Cockpit voice warnings", "Cockpit voice warnings");
                    Toggle(Boom, "Audio", "Missile sonic boom", "Experimental missile sonic booms");
                    Toggle(Boom, "Audio", "KROP missile recordings", "KROP missile recordings (requires missile booms)");
                    GUILayout.Label("Additional sound filtering uses the cockpit / distant muffling switches above. Each source group is optional.");
                    Toggle(Krop, "08 Additional audio", "Turbine startup", "Filter turbine startup sounds");
                    Toggle(Krop, "08 Additional audio", "Engine fires", "Filter engine fire sounds");
                    Toggle(Krop, "08 Additional audio", "Split airbrakes", "Filter split airbrakes");
                    Toggle(Krop, "08 Additional audio", "Airframe scrapes", "Filter airframe scraping");
                    Toggle(Krop, "08 Additional audio", "Ground and ship launchers", "Filter ground and ship launchers");
                    Toggle(Krop, "08 Additional audio", "VLS boosters", "Filter VLS booster sounds");
                    Toggle(Krop, "08 Additional audio", "Lasers", "Filter laser sounds");
                    Toggle(Krop, "08 Additional audio", "Ship engines", "Filter ship engines");
                    Toggle(Krop, "08 Additional audio", "World ambience", "Filter world ambience");
                    Toggle(Krop, "08 Additional audio", "Escape capsules", "Filter escape capsules");
                    Toggle(Krop, "08 Additional audio", "Lift intake doors", "Filter lift intake doors");
                    Toggle(Krop, "08 Additional audio", "Missile startup", "Filter missile startup sounds");
                    Toggle(Krop, "08 Additional audio", "Missile motors and flight", "Filter missile motors and fly-bys");
                    Toggle(Krop, "08 Additional audio", "Pylon releases", "Filter weapon release sounds");
                    Toggle(Krop, "08 Additional audio", "Warhead effects", "Filter warhead explosion sounds");
                    Toggle(Krop, "08 Additional audio", "Canopy jettison", "Filter canopy jettison sounds");
                    Toggle(Krop, "08 Additional audio", "Ejection seats", "Filter ejection-seat sounds");
                    Toggle(Krop, "08 Additional audio", "Countermeasure ejection", "Filter flare and chaff release sounds");
                    Toggle(Krop, "08 Additional audio", "Fuel ignition", "Filter fuel ignition sounds");
                    break;
                case 6:
                    Toggle(Boscali, "Impact marks", "Enabled", "Impact scorch decals");
                    Toggle(Krop, "00 General", "Enable pack", "KROP client effects master");
                    Toggle(Krop, "07 Cosmetic lifeboats", "Lifeboats", "Lifeboats from sinking ships");
                    Toggle(Krop, "07 Cosmetic lifeboats", "Gentle bobbing", "Lifeboat bobbing");
                    Toggle(Krop, "07 Cosmetic lifeboats", "Blinking beacons", "Lifeboat beacons");
                    Toggle(Krop, "08 Visual refinements", "Delayed lifeboat beacons", "Stagger initial lifeboat beacon activation");
                    Toggle(Krop, "08 Visual refinements", "Varied fuel explosions", "Varied fuel explosions (visual only)");
                    Toggle(Krop, "08 Visual refinements", "Fuel explosion audio", "Extra fuel explosion sound (requires varied explosions)");
                    Toggle(Krop, "08 Visual refinements", "Extended burning embers", "Longer-lived moving embers (requires varied explosions)");
                    Slider(Krop, "08 Visual refinements", "Fuel explosion chance", "Extra fuel explosion chance", 0f, 1f);
                    Slider(Krop, "08 Visual refinements", "Fuel effect lifetime seconds", "Fuel effect lifetime (seconds)", 3f, 45f);
                    Slider(Krop, "08 Visual refinements", "Fuel effect maximum distance m", "Fuel effect range (m)", 250f, 12000f);
                    GUILayout.Label("Lifeboats are local visual decorations; they do not simulate rescue or crew.");
                    break;
            }
            foreach(var item in UnifiedEffects.ControlsCatalog.AdditionalEffectControls.Items)
            {
                if(item.Category!=tab)continue;
                if(item.Kind=="Toggle")Toggle(item.Plugin,item.Section,item.Key,item.Label);
                else if(item.Kind=="Slider")Slider(item.Plugin,item.Section,item.Key,item.Label,item.Min,item.Max);
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close")) SetOpen(false);
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }
        void Toggle(string id, string section, string key, string label)
        {
            ConfigEntry<bool> entry;
            if (!Find(id, section, key, out entry)) { GUILayout.Label(label + " — component or setting unavailable"); return; }
            bool value = GUILayout.Toggle(entry.Value, label);
            if (value != entry.Value) entry.Value = value;
        }
        void Slider(string id, string section, string key, string label, float min, float max)
        {
            ConfigEntry<float> entry;
            if (!Find(id, section, key, out entry)) { GUILayout.Label(label + " — component or setting unavailable"); return; }
            GUILayout.Label(label + ": " + entry.Value.ToString("0.00"));
            float value = GUILayout.HorizontalSlider(entry.Value, min, max);
            if (Math.Abs(value - entry.Value) > 0.001f) entry.Value = value;
        }
        void EnumChoice(string id, string section, string key, string label)
        {
            ConfigFile config;
            if (!configs.TryGetValue(id, out config)) { GUILayout.Label(label + " — component unavailable"); return; }
            var definition = new ConfigDefinition(section, key);
            if (!config.ContainsKey(definition)) { GUILayout.Label(label + " — setting unavailable"); return; }
            var entry = config[definition];
            if (entry.SettingType == null || !entry.SettingType.IsEnum) { GUILayout.Label(label + " — unsupported setting"); return; }
            GUILayout.Label(label);
            GUILayout.BeginHorizontal();
            foreach (object value in Enum.GetValues(entry.SettingType))
                if (GUILayout.Toggle(Equals(entry.BoxedValue, value), value.ToString(), GUI.skin.button) && !Equals(entry.BoxedValue, value)) entry.BoxedValue = value;
            GUILayout.EndHorizontal();
        }
        void IntSlider(string id, string section, string key, string label, int min, int max)
        {
            ConfigEntry<int> entry;
            if (!Find(id, section, key, out entry)) { GUILayout.Label(label + " - component or setting unavailable"); return; }
            GUILayout.Label(label + ": " + entry.Value);
            int value = Mathf.Clamp(Mathf.RoundToInt(GUILayout.HorizontalSlider(entry.Value, min, max)), min, max);
            if (value != entry.Value) entry.Value = value;
        }
        void IntChoice(string id, string section, string key, string label, string[] choices)
        {
            ConfigEntry<int> entry;
            if (!Find(id, section, key, out entry)) { GUILayout.Label(label + " - component or setting unavailable"); return; }
            GUILayout.Label(label);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < choices.Length; i++)
                if (GUILayout.Toggle(entry.Value == i, choices[i], GUI.skin.button) && entry.Value != i) entry.Value = i;
            GUILayout.EndHorizontal();
        }
        void CaptureAvailable()
        {
            foreach (string id in CandidateIds)
            {
                if (configs.ContainsKey(id)) continue;
                PluginInfo info;
                if (Chainloader.PluginInfos.TryGetValue(id, out info) && info.Instance != null)
                {
                    configs[id] = info.Instance.Config;
                    if (id == Weather) weatherAssembly = info.Instance.GetType().Assembly;
                }
            }
        }
        void OpenMetOffice()
        {
            try
            {
                if (!WeatherEnabled())
                {
                    weatherStatus = "Enable Boscali weather system to use MET OFFICE. Ocean spray does not require it.";
                    return;
                }
                Type bridge = weatherAssembly?.GetType("BoscaliUnifiedWeather.WeatherUiBridge");
                MethodInfo method = bridge?.GetMethod("OpenMetOffice", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (method == null) { weatherStatus = "MET OFFICE is unavailable in this weather build."; return; }
                // Close the effects panel first so its cursor restoration cannot undo MET OFFICE input capture.
                SetOpen(false);
                if (!(method.Invoke(null, null) is bool opened) || !opened)
                {
                    weatherStatus = "MET OFFICE needs a loaded offline mission. If weather was just enabled, allow it a moment to initialise.";
                    SetOpen(true);
                    return;
                }
                weatherStatus = null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not open MET OFFICE: " + e);
                weatherStatus = "Could not open MET OFFICE; see the log for details.";
                SetOpen(true);
            }
        }
        bool MetOfficeOpen()
        {
            Type bridge = weatherAssembly?.GetType("BoscaliUnifiedWeather.WeatherUiBridge");
            PropertyInfo property = bridge?.GetProperty("IsMetOfficeOpen", BindingFlags.Public | BindingFlags.Static);
            return property != null && property.GetValue(null, null) is bool value && value;
        }
        bool WeatherEnabled()
        {
            ConfigEntry<bool> entry;
            return Find(Weather, "Weather", "Enabled", out entry) && entry.Value;
        }
        bool Find<T>(string id, string section, string key, out ConfigEntry<T> entry)
        {
            entry = null;
            ConfigFile config;
            return configs.TryGetValue(id, out config) && config.TryGetEntry(section, key, out entry);
        }
    }
}
