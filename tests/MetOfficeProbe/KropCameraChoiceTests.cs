using System;
using BepInEx.Configuration;
using HarmonyLib;

public sealed partial class MetDriver
{
    private void TestKropCameraChoice()
    {
        var plugin = ModuleInfos()["aaron.boscali.krop-unified"].Instance;
        var config = plugin.Config;
        Need(config.TryGetEntry<bool>(new ConfigDefinition("09 Night vision", "Allow game auto NVG on camera change"), out var allow), "camera auto-NVG setting exists");
        Need(!(bool)allow.DefaultValue && !allow.Value, "camera auto-NVG defaults off");
        var masterType = plugin.GetType().Assembly.GetType("KropClientEffects.Settings", true);
        var master = (ConfigEntry<bool>)masterType.GetField("Master", F).GetValue(null);
        var callback = AccessTools.Method(typeof(NightVision), "NightVis_OnSwitchCam");
        var patches = Harmony.GetPatchInfo(callback);
        Need(patches != null && System.Linq.Enumerable.Any(patches.Prefixes, p => p.owner == "aaron.boscali.krop-unified"), "native camera callback has scoped local policy hook");
        var nv = NightVision.i;
        var level = LevelInfo.i;
        Need(nv != null && level != null, "camera policy tested against actual scene NVG and level");
        var selected = AccessTools.Field(typeof(NightVision), "nightVisSelected");
        var nativeSetting = AccessTools.Field(typeof(PlayerSettings), "cameraAutoNVG");
        bool oldSelected = (bool)selected.GetValue(nv), oldNative = (bool)nativeSetting.GetValue(null);
        bool oldAllow = allow.Value, oldMaster = master.Value;
        float oldTime = level.timeOfDay;
        try
        {
            master.Value = true; allow.Value = false;
            nativeSetting.SetValue(null, true); level.timeOfDay = 22f;
            selected.SetValue(nv, false); callback.Invoke(nv, null);
            Need(!(bool)selected.GetValue(nv), "night camera change does not select NVG by default");
            selected.SetValue(nv, true); callback.Invoke(nv, null);
            Need((bool)selected.GetValue(nv), "camera change preserves manually selected NVG");
            allow.Value = true; selected.SetValue(nv, false); callback.Invoke(nv, null);
            Need((bool)selected.GetValue(nv), "explicit opt-in restores native night camera selection");
            nativeSetting.SetValue(null, false); selected.SetValue(nv, false); callback.Invoke(nv, null);
            Need(!(bool)selected.GetValue(nv), "opt-in still respects native camera-auto-NVG preference");
            nativeSetting.SetValue(null, true); level.timeOfDay = 12f; callback.Invoke(nv, null);
            Need(!(bool)selected.GetValue(nv), "opt-in retains native daytime gate");
            level.timeOfDay = 22f; allow.Value = false; master.Value = false; callback.Invoke(nv, null);
            Need((bool)selected.GetValue(nv), "disabling KROP module restores native camera callback");
            Need((bool)nativeSetting.GetValue(null), "policy never changes native preference in callback");
        }
        finally
        {
            selected.SetValue(nv, oldSelected); nativeSetting.SetValue(null, oldNative); level.timeOfDay = oldTime;
            allow.Value = oldAllow; master.Value = oldMaster;
        }
    }
}
