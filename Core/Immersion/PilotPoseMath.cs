using System;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    internal static class PilotPoseMath
    {
        internal static float Input(float value) => BoscaliSummer.Core.Math.Scalar.IsFinite(value) ? Math.Max(-1f, Math.Min(1f, value)) : 0f;

        internal static float Smoothing(float dt, float rate) =>
            BoscaliSummer.Core.Math.Scalar.IsFinite(dt) && BoscaliSummer.Core.Math.Scalar.IsFinite(rate) && dt > 0f && dt <= .25f && rate > 0f ? (float)(1.0 - Math.Exp(-Math.Min(dt, .05f) * rate)) : 0f;

        internal static float BreathMetres(float lift01, bool comfort) =>
            !comfort && BoscaliSummer.Core.Math.Scalar.IsFinite(lift01) ? (Math.Max(0f, Math.Min(1f, lift01)) - .5f) * .006f : 0f;
        internal static float TorsoDegrees(float lateralG, bool comfort) =>
            BoscaliSummer.Core.Math.Scalar.IsFinite(lateralG) ? Math.Max(-2f, Math.Min(2f, -lateralG * .7f)) * (comfort ? .25f : 1f) : 0f;

        internal static bool CaptureDue(double now, double previous) =>
            !double.IsNaN(now) && !double.IsInfinity(now) && now >= previous && now - previous >= .1;
    }
}
