using System;
namespace KropClientEffects
{
    // Pure presentation calculations shared with the bounded regression tests.
    internal static class EffectMath
    {
        internal static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        internal static float AtmosphericCutoff(float meters, bool enabled, float strength, float floor)
        {
            if (!enabled || strength <= 0f || meters <= 0f) return 22000f;
            return Clamp((float)Math.Sqrt(20f / (2.05e-6f * strength * meters / 1000f)), floor, 22000f);
        }
        internal static float CaliberPitch(float mass, float small, float large)
        {
            if (mass <= 0f) return (small + large) * 0.5f;
            float t=Clamp((float)((Math.Log(mass)-Math.Log(0.04))/(Math.Log(50)-Math.Log(0.04))),0f,1f);
            return small+(large-small)*t;
        }
        internal static int BoatCount(float manpower, int crewPerBoat, int shipCap, int available)
            => Math.Max(0,Math.Min(available,Math.Min(shipCap,Math.Max(1,(int)Math.Ceiling((double)manpower/Math.Max(1,crewPerBoat))))));
        // candidate is drawn from [0,count-1) when avoiding a valid previous slot.
        internal static int NonRepeatingIndex(int candidate, int previous, int count, bool avoid)
        {
            if (count <= 1) return 0;
            if (!avoid || previous < 0 || previous >= count) return Math.Max(0, Math.Min(count - 1, candidate));
            int slot = Math.Max(0, Math.Min(count - 2, candidate));
            return slot >= previous ? slot + 1 : slot;
        }
    }
}

