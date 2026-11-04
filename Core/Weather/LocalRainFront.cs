using UnityEngine;

namespace BoscaliClientExperimental.Weather
{
    // All inputs are read-only. One field is anchored when the mission becomes available.
    internal sealed class LocalRainFront
    {
        private bool ready;
        private Vector3 anchor;
        private Vector2 direction;
        private float speed;
        private double started;

        internal void Reset() { ready = false; }
        internal float Sample(LevelInfo level, Camera camera)
        {
            if (level == null || camera == null) return 0f;
            Vector3 position = camera.transform.GlobalPosition().AsVector3();
            if (!ready)
            {
                Vector3 wind = level.windVelocity;
                Vector2 horizontal = new Vector2(wind.x, wind.z);
                speed = horizontal.magnitude;
                direction = speed > 0.5f ? horizontal / speed : Vector2.right;
                anchor = position;
                started = Time.time;
                ready = true;
            }
            double along = (position.x - anchor.x) * (double)direction.x +
                           (position.z - anchor.z) * (double)direction.y;
            return LocalFrontMath.Intensity(along, Time.time - started, speed,
                level.conditions, position.y, level.cloudHeight);
        }
    }
}

