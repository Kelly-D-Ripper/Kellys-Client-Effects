using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;
using HarmonyLib;
using System;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Modules.Weather.Networking;
using BoscaliSummer.Modules.Weather.Runtime;
using BoscaliSummer.Modules.Weather.Visuals;
using BoscaliSummer.Modules.Weather.Presentation;

namespace BoscaliUnifiedWeather
{
    [BepInPlugin("aaron.boscali.weather-unified", "Unified Boscali Weather", "0.8.1")]
    [BepInIncompatibility("com.marci.boscalisummer")]
    [BepInIncompatibility("aaron.boscali.clouds-unified")]
    [BepInDependency("paul.no.clouds", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class WeatherPlugin : BaseUnityPlugin
    {
        internal static BepInEx.Configuration.ConfigEntry<bool> SimpleNames;
        private void Awake()
        {
            BoscaliSummer.Plugin.Logger = Logger;
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || GameManager.IsHeadless) return;
            GameAccess.Initialise();
            NOAvionics.AvStyleHost.Configure(Paths.ConfigPath, Logger.LogInfo, Logger.LogWarning);
            NOAvionics.AvBundle.Load(Logger.LogInfo);
            var settings = new WeatherSettings(Config);
            SimpleNames = Config.Bind("MET OFFICE", "Simple weather names", true, "Use plain-English weather preset names in the weather console.");
            var root = new GameObject("Boscali Unified Weather runtime") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(root);
            root.AddComponent<CloudModeCoordinator>().Configure(settings,Config,Logger);
            EffectsSettingsCache.Register("aaron.boscali.weather-unified",Config);
            root.AddComponent<EffectsSettingsCache>();
            var guard = root.AddComponent<TransitionGuard>();
            if (!guard.Install(Logger))
            {
                Logger.LogError("Weather disabled: could not install solo transition cleanup.");
                Destroy(root);
                return;
            }
            var network = root.AddComponent<WeatherNet>();
            var manager = root.AddComponent<WeatherManager>();
            manager.Configure(settings, network, Logger);
            var ocean = root.AddComponent<OceanDressing>();
            ocean.Configure(settings, manager, Logger);
            var metMfd = root.AddComponent<MetOfficeMfdLauncher>();
            metMfd.Configure(settings, manager, Logger);
            guard.Configure(manager, ocean);
            var lifecycle = root.AddComponent<SceneLifecycle>();
            lifecycle.Register("weather", manager, 64);
            lifecycle.Register("ocean", ocean, 66);
            lifecycle.Register("met mfd", metMfd, 68);
            Logger.LogInfo("Boscali weather initialized; multiplayer synchronization and multiplayer weather are disabled.");
        }
    }
    internal sealed class TransitionGuard : MonoBehaviour
    {
        private static WeatherManager manager;
        private static OceanDressing ocean;
        private static BepInEx.Logging.ManualLogSource log;
        private Harmony harmony;
        internal bool Install(BepInEx.Logging.ManualLogSource logger)
        {
            log = logger;
            try
            {
                var setter = AccessTools.PropertySetter(typeof(GameManager), "gameState");
                if (setter == null) return false;
                harmony = new Harmony("aaron.boscali.weather-unified.transition");
                harmony.Patch(setter, prefix: new HarmonyMethod(typeof(TransitionGuard), nameof(BeforeGameStateChange)));
                return true;
            }
            catch (Exception e) { log?.LogError("Weather transition guard failed: " + e); return false; }
        }
        internal void Configure(WeatherManager owner, OceanDressing sea) { manager = owner; ocean = sea; }
        private static void BeforeGameStateChange(GameState value)
        {
            if (GameManager.gameState != GameState.SinglePlayer || value == GameState.SinglePlayer) return;
            try { manager?.ResetForScene(); } catch (Exception e) { log?.LogError("Weather transition cleanup: " + e); }
            try { ocean?.ResetForScene(); } catch (Exception e) { log?.LogError("Ocean transition cleanup: " + e); }
        }
        private void OnDestroy() { harmony?.UnpatchSelf(); manager = null; ocean = null; }
    }

    public static class WeatherUiBridge
    {
        public static bool IsMetOfficeOpen => WeatherManager.Live != null && WeatherManager.Live.IsConsoleOpen;
        public static bool OpenMetOffice()
        {
            var manager = WeatherManager.Live;
            if (GameManager.gameState != GameState.SinglePlayer || manager == null || !manager.IsEnabled || !manager.IsCaptured || LevelInfo.i == null) return false;
            manager.OpenConsole();
            return true;
        }
    }
}
namespace BoscaliSummer
{
    internal static class Plugin
    {
        internal static BepInEx.Logging.ManualLogSource Logger;
        internal const string PluginGuid = "aaron.boscali.weather-unified";
    }
}
