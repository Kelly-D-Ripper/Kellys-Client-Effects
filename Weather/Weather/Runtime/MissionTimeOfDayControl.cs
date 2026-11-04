using BoscaliSummer.Modules.Weather.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    /// <summary>
    /// Changes only the live solo mission's sun clock through the game's own lighting refresh.
    /// The elapsed mission clock, time acceleration, date and mission asset are never written.
    /// RESTORE removes our accumulated clock offset, allowing native time to keep progressing.
    /// </summary>
    internal sealed class MissionTimeOfDayControl
    {
        private static readonly AccessTools.FieldRef<LevelInfo, float> TimeFactor = AccessTools.FieldRefAccess<LevelInfo, float>("timeFactor");
        private LevelInfo owner;
        private object ownedMap;
        private float offset, observed, observedAt, observedFactor;
        private bool active;

        internal bool Active => active && owner != null && owner == LevelInfo.i && ReferenceEquals(owner.LoadedMapSettings, ownedMap);
        internal float Hours => LevelInfo.i != null ? TimeOfDayMath.Wrap(LevelInfo.i.timeOfDay) : 0f;
        private static bool CanChange => GameManager.gameState == GameState.SinglePlayer && LevelInfo.i != null;

        internal bool Set(float hours)
        {
            if (!CanChange || !TimeOfDayMath.Finite(hours)) return false;
            Tick();
            LevelInfo level = LevelInfo.i;
            float current = TimeOfDayMath.Wrap(level.timeOfDay);
            float target = TimeOfDayMath.Wrap(hours);
            float previousOffset = Active ? offset : 0f;
            level.SetTimeOfDay(target);
            owner = level;
            ownedMap = level.LoadedMapSettings;
            offset = TimeOfDayMath.Delta(0f, previousOffset + TimeOfDayMath.Delta(current, target));
            active = Mathf.Abs(offset) > 0.00001f;
            Observe(level);
            return true;
        }

        internal bool Shift(float hours) => CanChange && TimeOfDayMath.Finite(hours) && Set(Hours + hours);

        internal bool Restore()
        {
            if (!CanChange) return false;
            Tick();
            if (!Active) return false;
            LevelInfo level = owner;
            float target = TimeOfDayMath.Wrap(level.timeOfDay - offset);
            Clear();
            level.SetTimeOfDay(target);
            return true;
        }

        internal void Tick()
        {
            if (!active) return;
            if (!CanChange || !Active) { Clear(); return; }
            // Native UpdateSimulation adds timeFactor * deltaTime / 3600. Allow one frame
            // for Update ordering; a mission script's separate time jump relinquishes ownership.
            float elapsed = Mathf.Max(0f, Time.time - observedAt);
            float expected = TimeOfDayMath.Wrap(observed + elapsed * observedFactor / 3600f);
            float tolerance = 0.001f + Mathf.Abs(observedFactor) * Mathf.Max(Time.deltaTime, 0.01f) / 3600f;
            if (Mathf.Abs(TimeOfDayMath.Delta(expected, owner.timeOfDay)) > tolerance) { Clear(); return; }
            Observe(owner);
        }

        internal void ResetForScene()
        {
            if (CanChange && Active) Restore();
            Clear();
        }

        private void Observe(LevelInfo level)
        {
            observed = TimeOfDayMath.Wrap(level.timeOfDay);
            observedAt = Time.time;
            observedFactor = TimeFactor(level);
        }

        private void Clear() { active = false; owner = null; ownedMap = null; offset = 0f; }
    }
}
