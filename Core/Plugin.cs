using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliClientExperimental
{
    [BepInPlugin(Id, "Unified Client Effects", "0.6.6")]
    [BepInIncompatibility("com.marci.boscalisummer")]
    [BepInIncompatibility("aaron.boscali.client.experimental")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "aaron.boscali.unified";
        internal static GameObject EffectsHost { get; private set; }

        private void Awake()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || GameManager.IsHeadless)
            {
                Logger.LogInfo("Experimental visual effects disabled in headless/batch mode.");
                return;
            }
            EffectsHost = new GameObject("Boscali Unified runtime") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(EffectsHost);
            var harmony = new Harmony(Id);
            EffectsHost.AddComponent<RuntimeLifetime>().Initialize(harmony);
            Install("immersion", () => Immersion.ImmersionFeature.Install(this, harmony));
            Install("cockpit pilot body", () => EffectsHost.AddComponent<BoscaliSummer.Modules.Immersion.Runtime.CockpitPilot>().Configure(Config, Logger));
            Install("street glow cloud fade", () => Immersion.StreetGlowCloudFade.Install(this));
            // Weather is owned exclusively by the separate full Boscali weather component.
            // Do not start the older rain/ocean renderers even if their old settings remain on.
            Install("cockpit voice warnings", () => Weather.CockpitVoiceWarnings.Install(this));
            Install("impact marks", () => Scorch.ScorchFeature.Install(this, harmony));
            Logger.LogInfo("Unified Client Effects initialized. Presentation features are experimental.");
        }

        private void Install(string name, Action install)
        {
            try { install(); }
            catch (Exception e) { Logger.LogError("Could not initialize " + name + ": " + e); }
        }

    }

    internal sealed class RuntimeLifetime : MonoBehaviour
    {
        private Harmony harmony;
        internal void Initialize(Harmony instance) => harmony = instance;
        private void OnDestroy() => harmony?.UnpatchSelf();
    }
}
