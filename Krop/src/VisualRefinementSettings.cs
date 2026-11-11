using BepInEx.Configuration;

namespace KropClientEffects
{
    internal static class VisualRefinementSettings
    {
        internal const string Section = "08 Visual refinements";
        internal static ConfigEntry<bool> FuelExplosions, FuelAudio, ExtendedEmbers, WaterWakes, DelayedBeacons;
        internal static ConfigEntry<float> FuelChance, FuelLifetime, FuelDistance;
        internal static ConfigEntry<int> FuelLimit;
        internal static void Initialize()
        {
            FuelExplosions = Settings.Toggle(Section, "Varied fuel explosions", false, "Add local fireball, flame, smoke and ember particles when a fuel tank ignites. Never adds damage, physics or network objects.");
            FuelAudio = Settings.Toggle(Section, "Fuel explosion audio", false, "Play the additional local cook-off sound with varied fuel explosions. No propagation delay.");
            ExtendedEmbers = Settings.Toggle(Section, "Extended burning embers", false, "New fuel effects use longer-lived embers with reduced drag and inherit the aircraft's velocity. Existing particles clear when this setting changes.");
            WaterWakes = Settings.Toggle(Section, "KROP water wakes", false, "Requires Air wake effects. Replaces all Boscali aircraft spray/foam wash while active; fixed-wing only, default 400 km/h minimum and 30 m maximum height. Leave off for Boscali rotorcraft and slower passes. Cosmetic only.");
            DelayedBeacons = Settings.Toggle(Section, "Delayed lifeboat beacons", false, "Delay a new lifeboat's beacon activation by a random 0-5 seconds, as in KROP. Turning this off restores immediate blinking.");
            FuelChance = Settings.Number(Section, "Fuel explosion chance", 0.25f, 0f, 1f, "Chance for an additional local visual effect on a newly ignited fuel tank.");
            FuelLifetime = Settings.Number(Section, "Fuel effect lifetime seconds", 20f, 3f, 45f, "Maximum lifetime of additional fuel effects, including looping flame and smoke.");
            FuelDistance = Settings.Number(Section, "Fuel effect maximum distance m", 6000f, 250f, 12000f, "Do not create additional fuel effects beyond this camera distance.");
            FuelLimit = Settings.Count(Section, "Maximum fuel effects", 8, 1, 24, "Maximum simultaneous additional fuel effects. Existing vanilla explosions are unchanged.");
        }
    }
}
