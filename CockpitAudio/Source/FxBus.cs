namespace BoscaliSummer.Core.Fx
{
    // This extraction has its own bounded budget; it does not control the weather plugin's voices.
    internal static class FxBus
    {
        internal readonly struct VoiceScales { internal float Voices => 1f; }
        internal static VoiceScales Scales => new VoiceScales();
    }
}
