using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace KropClientEffects.Patches
{
    internal static class MissileContrailPatch
    {
        private static ConditionalWeakTable<Missile, ContrailEffectPatch.EngineState> _missileStates =
            new ConditionalWeakTable<Missile, ContrailEffectPatch.EngineState>();

        internal static void ClearAll() { _missileStates = new ConditionalWeakTable<Missile, ContrailEffectPatch.EngineState>(); }

        internal static void ApplyManualPatch(Harmony harmony)
        {
            try
            {
                Type motorType = AccessTools.Inner(typeof(Missile), "Motor");
                harmony.Patch(AccessTools.Method(motorType, "Thrust"),
                    postfix: new HarmonyMethod(typeof(MissileContrailPatch), nameof(ThrustPostfix)));
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Missile contrail hook unavailable: " + ex.Message); }
        }

        private static void ThrustPostfix(Missile missile, float __result, float ___fuelMass)
        {
            if (!Settings.Active || !ContrailConfig.MissileContrailsEnabled.Value || GameManager.IsHeadless
                || missile == null || missile.rb == null)
            {
                return;
            }

            float altitude = missile.transform.position.y - Datum.LocalSeaY;
            ContrailEffectPatch.EngineState state = _missileStates.GetValue(
                missile, _ => new ContrailEffectPatch.EngineState { IsMissile = true });

            if (__result <= 0f || ___fuelMass <= 0f || altitude < ContrailEffectPatch.MinAltitudeMeters || altitude > ContrailEffectPatch.MaxAltitudeMeters)
            {
                ContrailEffectPatch.StopEmitting(state);
                return;
            }

            ContrailEffectPatch.DriveContrail(state, missile.transform.position, missile.rb.velocity);
        }
    }
}


