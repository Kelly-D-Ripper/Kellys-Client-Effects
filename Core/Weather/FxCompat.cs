using System;
using UnityEngine;

namespace BoscaliClientExperimental.Weather
{
    // This extraction owns at most eight pairs of 256x256 pane textures and
    // one half-resolution scene texture. Each owner releases its own targets.
    internal static class FxRtPool
    {
        internal static bool Own(RenderTexture target)
        {
            try { return target != null && (target.IsCreated() || target.Create()); }
            catch (Exception) { return false; }
        }
        internal static void Disown(RenderTexture target) { if (target != null) target.Release(); }
    }
    internal static class FxBus
    {
        private sealed class Marker : IDisposable { public void Dispose() { } }
        internal static IDisposable Time(string effect) => new Marker();
    }
}
