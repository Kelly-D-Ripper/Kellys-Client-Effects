namespace KropClientEffects
{
    // Presentation has its own RNG: adding effects must not consume Unity's shared
    // random sequence used by the game's weapons/physics or other gameplay systems.
    internal static class EffectRandom
    {
        private static readonly System.Random Random = new System.Random();
        internal static float value => (float)Random.NextDouble();
        internal static float Range(float min, float max) => min + (max - min) * value;
        internal static int Range(int min, int max) => Random.Next(min, max);
    }
}

