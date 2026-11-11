using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace KropClientEffects
{
    // Each entry gates presentation only; none of these options changes native playback timing.
    internal enum AudioFeature
    {
        Base = -1, TurbineStartup, EngineFire, SplitAirbrakes, AirframeScrape, GroundLaunchers,
        VlsBoosters, Lasers, ShipEngines, Ambient, EscapeCapsule, LiftIntakes, MissileStartup,
        MissileFlight, PylonRelease, WarheadEffects, CanopyJettison, EjectionSeat,
        CountermeasureEjection, FuelIgnition
    }

    internal static class AudioRefinements
    {
        internal static readonly string[] SourceKeys = {
            "Turbine startup", "Engine fires", "Split airbrakes", "Airframe scrapes",
            "Ground and ship launchers", "VLS boosters", "Lasers", "Ship engines",
            "World ambience", "Escape capsules", "Lift intake doors", "Missile startup",
            "Missile motors and flight", "Pylon releases", "Warhead effects", "Canopy jettison",
            "Ejection seats", "Countermeasure ejection", "Fuel ignition"
        };
        internal static readonly ConfigEntry<bool>[] Sources = new ConfigEntry<bool>[SourceKeys.Length];
        internal static ConfigEntry<bool> CanopyMuffling, OpenCabin, CrackShaping, AvoidRepeatedBooms;
        internal static void Initialize()
        {
            for (int i = 0; i < Sources.Length; i++)
                Sources[i] = Settings.Toggle("08 Additional audio", SourceKeys[i], false,
                    "Optional local filtering for " + SourceKeys[i].ToLowerInvariant() + ". Uses the cockpit/distance filter settings. No added sound delay or gameplay change.");
            CanopyMuffling = Settings.Toggle("01 Audio filters", "Canopy-aware muffling", false,
                "Reduce local cockpit muffling as an existing canopy opens or is lost. Reads canopy state; does not open, close or damage canopies.");
            OpenCabin = Settings.Toggle("01 Audio filters", "Open-cabin muffling", false,
                "Reduce local cockpit muffling in an Ibis fitted with a door gun. Reads the existing loadout only.");
            CrackShaping = Settings.Toggle("02 Bullet near misses", "KROP crack shaping", false,
                "Use KROP's 4 kHz high-pass and shorter bullet-crack transient. Existing volume/pitch settings are preserved.");
            AvoidRepeatedBooms = Settings.Toggle("03 Aircraft sonic booms", "Avoid repeated samples", false,
                "Avoid playing the same aircraft boom recording twice consecutively in each near/far pool.");
        }
        internal static bool Enabled(AudioFeature feature)
            => feature == AudioFeature.Base || ((int)feature >= 0 && (int)feature < Sources.Length && Sources[(int)feature]?.Value == true);
    }

    internal static class ReadOnlyCabinAcoustics
    {
        private static readonly FieldInfo Canopies = AccessTools.Field(typeof(Aircraft), "canopies");
        private static readonly FieldInfo OpenAmount = AccessTools.Field(typeof(Canopy), "openAmount");
        private static readonly FieldInfo Firing = AccessTools.Field(typeof(Canopy), "firing");
        private sealed class Cabin { internal bool[] Seen; internal bool Lost; }
        private static ConditionalWeakTable<Aircraft, Cabin> cabins = new ConditionalWeakTable<Aircraft, Cabin>();

        internal static float MuffleFraction(Aircraft aircraft)
        {
            if (aircraft == null) return 1f;
            float fraction = 1f;
            if (AudioRefinements.CanopyMuffling.Value && Canopies != null && OpenAmount != null)
            {
                var canopies = Canopies.GetValue(aircraft) as Canopy[];
                if (canopies != null)
                {
                    if (!cabins.TryGetValue(aircraft, out Cabin cabin))
                    { cabin = new Cabin { Seen = new bool[canopies.Length] }; cabins.Add(aircraft, cabin); }
                    float open = 0f;
                    for (int i = 0; i < canopies.Length && i < cabin.Seen.Length; i++)
                    {
                        Canopy canopy = canopies[i];
                        if (canopy == null) { if (cabin.Seen[i]) cabin.Lost = true; continue; }
                        cabin.Seen[i] = true;
                        if ((Firing != null && (bool)Firing.GetValue(canopy)) ||
                            (canopy.attachedPart != null && canopy.attachedPart.IsDetached())) cabin.Lost = true;
                        else open = Mathf.Max(open, Mathf.Clamp01((float)OpenAmount.GetValue(canopy)));
                    }
                    fraction *= cabin.Lost ? 0f : 1f - open;
                }
            }
            if (AudioRefinements.OpenCabin.Value && aircraft.definition?.jsonKey == "UtilityHelo1")
            {
                var mounts = aircraft.weaponManager?.hardpointSets;
                if (mounts != null) foreach (var mount in mounts)
                {
                    string key = mount?.weaponMount != null ? mount.weaponMount.jsonKey : null;
                    if (key == "Turret_12.7mm_door" || key == "Turret_40mm_grenade") { fraction *= 0.5f; break; }
                }
            }
            return fraction;
        }
        internal static void Clear() { cabins = new ConditionalWeakTable<Aircraft, Cabin>(); }
    }
}
