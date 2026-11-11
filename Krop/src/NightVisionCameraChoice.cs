using BepInEx.Configuration;
using HarmonyLib;

namespace KropClientEffects
{
    // The native camera-switch callback can select NVG again after a manual N-key
    // deselection. Suppress only that callback, not normal toggles or gain updates.
    internal static class NightVisionCameraChoice
    {
        internal static ConfigEntry<bool> AllowGameAutomatic;
        internal static void Initialize()
        {
            AllowGameAutomatic = Settings.Toggle("09 Night vision", "Allow game auto NVG on camera change", false,
                "Allow the game's automatic NVG selection when switching camera at night. Off preserves your manual NVG choice. On also requires the game's Camera Auto NVG setting. Does not change that saved game setting.");
        }

        internal static bool AllowNativeCallback() => !Settings.Active || AllowGameAutomatic.Value;
    }

    [HarmonyPatch(typeof(NightVision), "NightVis_OnSwitchCam")]
    internal static class NightVisionCameraChoicePatch
    {
        private static bool Prefix() => NightVisionCameraChoice.AllowNativeCallback();
    }
}
