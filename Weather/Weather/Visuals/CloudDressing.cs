using System.Reflection;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    // Read-only observation for canopy moisture. Never changes native cloud state.
    internal sealed class CloudDressing
    {
        private static readonly FieldInfo LayerField = typeof(LevelInfo).GetField("cloudLayer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FlyThroughField = typeof(CloudLayer).GetField("flyThroughSystem", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ThicknessField = typeof(CloudLayer).GetField("layerThickness", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SizeMaxField = typeof(CloudLayer).GetField("cloudSizeMax", BindingFlags.Instance | BindingFlags.NonPublic);
        private LevelInfo observedOwner;
        private ParticleSystem flyThrough;
        private CloudLayer observedLayer;
        private float nextDimensionCheck, thickness, maxSize;

        internal bool InCloud(LevelInfo level)
        {
            if (level == null || LayerField == null || FlyThroughField == null) return false;
            if (observedOwner != level || flyThrough == null)
            {
                observedOwner = level;
                var layer = LayerField.GetValue(level) as CloudLayer;
                observedLayer = layer;
                nextDimensionCheck = 0f;
                flyThrough = layer != null ? FlyThroughField.GetValue(layer) as ParticleSystem : null;
            }
            return flyThrough != null && flyThrough.isPlaying;
        }

        internal float? ApproximateTop(LevelInfo level)
        {
            // The native layer places particle centres near cloudHeight and uses at
            // most 1.1 * cloudSizeMax for their diameter. Cache only the component,
            // and read its tunables without scene searches. Unknown/invalid data
            // leaves the weather field's own ceiling in charge.
            if (level == null || LayerField == null || ThicknessField == null || SizeMaxField == null) return null;
            if (observedOwner != level || observedLayer == null)
            {
                observedOwner = level;
                observedLayer = LayerField.GetValue(level) as CloudLayer;
                nextDimensionCheck = 0f;
                flyThrough = observedLayer != null && FlyThroughField != null
                    ? FlyThroughField.GetValue(observedLayer) as ParticleSystem : null;
            }
            if (observedLayer == null || !observedLayer.isActiveAndEnabled) return null;
            if (nextDimensionCheck <= 0f || Time.unscaledTime >= nextDimensionCheck)
            {
                nextDimensionCheck = Time.unscaledTime + 2f;
                thickness = (float)ThicknessField.GetValue(observedLayer);
                maxSize = (float)SizeMaxField.GetValue(observedLayer);
            }
            return BoscaliSummer.Modules.Weather.Domain.NativeCloudCeiling.Estimate(level.cloudHeight, thickness, maxSize);
        }
    }
}
