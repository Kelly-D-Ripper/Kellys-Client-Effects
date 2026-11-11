using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace KropClientEffects
{
    [BepInPlugin(Guid, "Unified Client Effects: KROP", "0.3.0")]
    [BepInIncompatibility("pavehog727.qolrealismfixes")]
    [BepInIncompatibility("aaron.krop.client-effects")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "aaron.boscali.krop-unified";
        internal static bool Running;
        internal static ManualLogSource Log;
        private void Awake()
        {
            Log = Logger;
            Settings.Initialize(Config);
            VisualRefinementSettings.Initialize();
            NightVisionCameraChoice.Initialize();
            NightVisionEffects.Initialize();
            AudioPropagation.Initialize();
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || GameManager.IsHeadless)
            {
                Logger.LogInfo("Client effects inactive on headless/batch process.");
                return;
            }
            var host = new GameObject("Unified KROP Client Effects runtime") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            var harmony = new Harmony(Guid);
            host.AddComponent<RuntimeDriver>().Initialize(harmony);
            Running = true;
            // Patch modules independently: an unsupported hook leaves that effect unavailable,
            // rather than aborting unrelated client modules or touching join/authentication paths.
            foreach (Type type in typeof(Plugin).Assembly.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length != 0))
            {
                try { harmony.CreateClassProcessor(type).Patch(); }
                catch (Exception ex) { Logger.LogWarning("Skipped effect hook " + type.Name + ": " + ex.Message); }
            }
            AudioHooks.Apply(harmony);
            AircraftBooms.Apply(harmony);
            AudioPropagationHooks.Apply(harmony);
            Patches.MissileContrailPatch.ApplyManualPatch(harmony);
            Logger.LogInfo("Unified KROP client effects 0.3.0 candidate: optional presentation-only hooks loaded.");
        }
        internal static void ClearScene()
        {
            AudioFilters.Clear(); VisualObjects.Clear(); LocalLifeboats.Clear();
            BulletCracks.Clear(); AircraftBooms.Clear();
            Patches.ContrailEffectPatch.ClearAll(); Patches.AirWakeEffectPatch.ClearAll();
            VisualRefinements.Clear();
            NightVisionEffects.Clear();
            AudioPropagation.Clear();
        }
    }
    // The game's scene transitions can dispose objects owned by the loader manager.
    // Keep the tick loop and Harmony lifetime on a separate protected root instead.
    internal sealed class RuntimeDriver : MonoBehaviour
    {
        private Harmony harmony;
        private bool cleared;
        internal void Initialize(Harmony value) { harmony = value; }
        private void Update()
        {
            if (!Plugin.Running) return;
            if (!Settings.Master.Value)
            {
                if (!cleared) Plugin.ClearScene();
                cleared = true;
                return;
            }
            cleared = false;
            LocalLifeboats.Tick();
            VisualObjects.Tick();
            VisualRefinements.Tick();
            NightVisionEffects.Tick();
            AudioPropagation.Tick();
        }
        private void LateUpdate() { if (Settings.Active) AudioFilters.Tick(); }
        private void OnDestroy()
        {
            Plugin.Running = false;
            Plugin.ClearScene();
            harmony?.UnpatchSelf();
        }
    }
    // Minimal compatibility surface for the imported visual helpers; no KROP delay engine.
    internal static class SoundPropagation { internal static ManualLogSource Log => Plugin.Log; }
    [HarmonyPatch(typeof(LevelInfo), "OnDestroy")]
    internal static class SceneCleanupPatch { private static void Prefix() { Plugin.ClearScene(); } }
}

