using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Modules.Weather.Presentation;
using HarmonyLib;
using UnityEngine;

namespace UnifiedEffects
{
    // NOAtmoFX 1.4's public setters supply targets, but its shortest supported
    // transition is one minute. This optional, narrowly matched compatibility
    // patch changes only the weather smoothing time constant while our own
    // single-player weather selection is active. It never changes the add-on's
    // saved settings, shader, private state or multiplayer weather clock.
    internal static class NoAtmoWeatherTransition
    {
        private const string PatchId = "aaron.boscali.weather-unified.noatmo-transition";
        private static CloudModeCoordinator owner;
        private static Harmony harmony;
        private static bool matched;
        private static string reason = "NOATMOFX TRANSITION NOT CONNECTED";
        private static readonly MethodInfo AdjustMethod = typeof(NoAtmoWeatherTransition).GetMethod(
            nameof(AdjustTimeConstant), BindingFlags.Static | BindingFlags.NonPublic);
        // Harmony targets the game's mscorlib Label; avoid exposing that generic
        // type in this netstandard assembly while retaining the branch guard.
        private static readonly FieldInfo LabelsField = typeof(CodeInstruction).GetField("labels", BindingFlags.Instance | BindingFlags.Public);

        internal static ConfigEntry<float> Entry { get; private set; }
        internal static ConfigEntry<bool> EnabledEntry { get; private set; }
        internal static bool Enabled => EnabledEntry != null && EnabledEntry.Value;
        internal static bool Available => harmony != null && matched;
        internal static string Reason => Available ? "" : reason;
        internal static float Seconds
        {
            get
            {
                float value = Entry == null ? 5f : Entry.Value;
                return float.IsNaN(value) || float.IsInfinity(value) ? 5f : Mathf.Clamp(value, 5f, 1800f);
            }
        }

        internal static void Configure(CloudModeCoordinator coordinator, ConfigFile config, ManualLogSource log)
        {
            if (coordinator == null || config == null) return;
            if (ReferenceEquals(owner, coordinator) && Available) return;
            Release();
            owner = coordinator;
            EnabledEntry = config.Bind("Weather display", "Fast volumetric weather transitions", true,
                "Use the transition time below for Unified Effects weather choices in single-player Volumetric mode. Off keeps NOAtmoFX's own transition speed.");
            Entry = config.Bind("Weather display", "Volumetric transition seconds", 5f,
                new ConfigDescription("Approximate time for NOAtmoFX clouds to settle after a Unified Effects weather choice in single player. Applies only while this panel owns that weather. Cloud drift keeps the add-on's own wind smoothing. Unsupported add-on versions retain their normal transition time.",
                    new AcceptableValueRange<float>(5f, 1800f)));
            if (!Chainloader.PluginInfos.TryGetValue("paul.no.clouds", out var info) || info.Instance == null)
            {
                reason = "NOATMOFX NOT INSTALLED";
                return;
            }
            Version version = info.Metadata.Version;
            if (version == null || version.Major != 1 || version.Minor != 4 || version.Build != 0 || version.Revision > 0)
            {
                reason = "FAST TRANSITION REQUIRES VERIFIED NOATMOFX 1.4.0";
                log?.LogWarning(reason + "; using the add-on's normal transition time.");
                return;
            }
            try
            {
                Type type = info.Instance.GetType().Assembly.GetType("NoAtmoFX.NOCloudsPlugin", false);
                MethodInfo update = type?.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (update == null || update.ReturnType != typeof(void) || update.IsStatic || update.GetMethodBody() == null)
                {
                    reason = "NOATMOFX WEATHER UPDATE CHANGED";
                    log?.LogWarning(reason + "; using the add-on's normal transition time.");
                    return;
                }
                harmony = new Harmony(PatchId);
                harmony.Patch(update, transpiler: new HarmonyMethod(typeof(NoAtmoWeatherTransition), nameof(Transpile)));
                if (!matched)
                {
                    harmony.UnpatchSelf();
                    harmony = null;
                    log?.LogWarning(reason + "; using the add-on's normal transition time.");
                    return;
                }
                log?.LogInfo("NOAtmoFX 1.4.0 weather transition compatibility verified; active only for this panel's single-player volumetric weather.");
            }
            catch (Exception error)
            {
                matched = false;
                reason = "NOATMOFX FAST TRANSITION UNAVAILABLE";
                try { harmony?.UnpatchSelf(); } catch { }
                harmony = null;
                log?.LogWarning(reason + ": " + error.GetBaseException().Message + ". Using the add-on's normal transition time.");
            }
        }

        internal static void Release(CloudModeCoordinator coordinator = null)
        {
            if (coordinator != null && !ReferenceEquals(owner, coordinator)) return;
            // Disable the callback before removing the patch, including on unload.
            owner = null;
            matched = false;
            harmony?.UnpatchSelf();
            harmony = null;
            Entry = null;
            EnabledEntry = null;
            reason = "NOATMOFX TRANSITION NOT CONNECTED";
        }

        private static float AdjustTimeConstant(float nativeSeconds)
        {
            var current = owner;
            if (!Available || !Enabled || current == null || GameManager.gameState != GameState.SinglePlayer ||
                current.Mode != CloudMode.Volumetric || !current.FastWeatherTransitionActive)
                return nativeSeconds;
            // NOAtmoFX uses two equal exponential smoothing stages for cloud
            // development: approximately 96% of a step is reached after 5 tau.
            return Seconds / 5f;
        }

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            matched = false;
            reason = "NOATMOFX WEATHER TRANSITION SHAPE CHANGED";
            int match = -1, count = 0;
            for (int i = 0; i + 7 < code.Count; ++i)
            {
                if (!MatchesTimeConstant(code, i, __originalMethod)) continue;
                match = i;
                ++count;
            }
            if (count != 1 || AdjustMethod == null) return code;
            // Preserve the native max and every instruction around it. The
            // callback consumes and replaces only that float on the stack.
            code.Insert(match + 7, new CodeInstruction(OpCodes.Call, AdjustMethod));
            matched = true;
            reason = "";
            return code;
        }

        private static bool MatchesTimeConstant(List<CodeInstruction> code, int i, MethodBase original)
        {
            if (!FloatConstant(code[i], 12f) || code[i + 1].opcode != OpCodes.Ldarg_0 ||
                code[i + 2].opcode != OpCodes.Ldfld || !(code[i + 2].operand is FieldInfo field) ||
                field.Name != "_changeMin" || field.DeclaringType != original.DeclaringType || field.FieldType != typeof(ConfigEntry<float>) ||
                code[i + 3].opcode != OpCodes.Callvirt || !(code[i + 3].operand is MethodInfo getter) ||
                getter.Name != "get_Value" || getter.DeclaringType != typeof(ConfigEntry<float>) || getter.ReturnType != typeof(float) ||
                !FloatConstant(code[i + 4], 12f) || code[i + 5].opcode != OpCodes.Mul ||
                code[i + 6].opcode != OpCodes.Call || !(code[i + 6].operand is MethodInfo maximum) ||
                maximum != typeof(Mathf).GetMethod(nameof(Mathf.Max), new[] { typeof(float), typeof(float) }) ||
                !StoresFloat(code[i + 7], original)) return false;
            // A branch into the middle or an exception boundary would require
            // separate analysis. Do not guess if another mod alters this block.
            for (int j = i + 1; j <= i + 7; ++j)
                if (!HasNoLabels(code[j]) || code[j].blocks.Count != 0) return false;
            return code[i].blocks.Count == 0;
        }

        private static bool HasNoLabels(CodeInstruction instruction)
        {
            try { return LabelsField?.GetValue(instruction) is ICollection labels && labels.Count == 0; }
            catch { return false; }
        }

        private static bool FloatConstant(CodeInstruction instruction, float value) =>
            instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float number && number == value;

        private static bool StoresFloat(CodeInstruction instruction, MethodBase original)
        {
            int index;
            if (instruction.opcode == OpCodes.Stloc_0) index = 0;
            else if (instruction.opcode == OpCodes.Stloc_1) index = 1;
            else if (instruction.opcode == OpCodes.Stloc_2) index = 2;
            else if (instruction.opcode == OpCodes.Stloc_3) index = 3;
            else if (instruction.opcode == OpCodes.Stloc || instruction.opcode == OpCodes.Stloc_S)
            {
                if (instruction.operand is LocalBuilder local) return local.LocalType == typeof(float);
                if (instruction.operand is LocalVariableInfo variable) return variable.LocalType == typeof(float);
                try { index = Convert.ToInt32(instruction.operand); } catch { return false; }
            }
            else return false;
            var locals = original.GetMethodBody()?.LocalVariables;
            return locals != null && index >= 0 && index < locals.Count && locals[index].LocalType == typeof(float);
        }
    }
}
