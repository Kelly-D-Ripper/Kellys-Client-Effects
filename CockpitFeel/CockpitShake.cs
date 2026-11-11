using BoscaliSummer.Modules.Immersion.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    // Mod-owned rotational envelopes. Native positional shake remains untouched.
    internal sealed class CockpitShake
    {
        private readonly MotionEnvelope events = new MotionEnvelope();
        private Aircraft subscribed;
        private float lastVerticalSpeed;
        private float strength;
        private bool active;
        private bool primed;
        private bool groundRollOn = true, recoilOn = true, touchdownOn = true, gearOn = true;
        private float groundRollStrength = 1f;
        private bool transonicOn = true, sonicJoltOn = true;
        public Vector3 AnglesDeg { get; private set; }
        public int Shots { get; private set; }
        public int Touchdowns { get; private set; }
        public int GearLocks { get; private set; }

        public void SetFeatures(bool groundRoll, bool recoil, bool touchdown, bool gear, float groundStrength)
        {
            groundRollOn = groundRoll; recoilOn = recoil; touchdownOn = touchdown; gearOn = gear;
            groundRollStrength = float.IsNaN(groundStrength) || float.IsInfinity(groundStrength) ? 1f : Mathf.Clamp(groundStrength, 0f, 3f);
            // Disabling an effect clears its tail; enabling it cannot resurrect an old event.
            events.DisableExtra(recoil, touchdown, gear);
        }

        public void SetMachFeatures(bool transonic, bool sonicJolt)
        {
            transonicOn = transonic; sonicJoltOn = sonicJolt;
            if (!sonicJolt) events.ResetMach();
        }

        public void Tick(Aircraft aircraft, bool cockpit, bool extra, bool machOn,
            float shakeStrength, float mach, float turbulence, float gust, float dt)
        {
            if (aircraft != subscribed) Follow(aircraft);
            active = cockpit && extra && aircraft != null;
            strength = shakeStrength;
            if (!cockpit || aircraft == null || aircraft.rb == null) { Release(); return; }
            lastVerticalSpeed = aircraft.rb.velocity.y;
            primed = true;
            if (!extra) { events.ClearExtra(); }
            LandingGear.GearState gear = aircraft.gearState;
            int gearDirection = gear == LandingGear.GearState.Extending || gear == LandingGear.GearState.LockedExtended ? 1 :
                gear == LandingGear.GearState.Retracting || gear == LandingGear.GearState.LockedRetracted ? -1 : 0;
            bool gearMoving = gear == LandingGear.GearState.Extending || gear == LandingGear.GearState.Retracting;
            if (events.GearTransition(gearDirection, gearMoving, active && gearOn && strength > 0f, dt)) GearLocks++;
            if (machOn && sonicJoltOn && strength > 0f) events.SonicCrossing(mach, Time.time);
            else events.ResetMach();
            events.Step(dt);

            float time = Time.time;
            // Native radar altitude already subtracts the aircraft's spawn/gear offset.
            // Retain the native near-ground envelope; do not infer wheel contact from visual meshes.
            float runway = extra && groundRollOn && aircraft.radarAlt <= 0.3f ? Mathf.Clamp01((aircraft.speed - 2f) / 70f) * 0.14f * groundRollStrength : 0f;
            float transonic = machOn && transonicOn ? ImmersionMath.MachBuffet(mach, 1f) * 0.5f : 0f;
            float weather = extra ? Mathf.Clamp01(turbulence) * Mathf.Clamp01(gust / 20f) * 0.2f : 0f;
            float buzz = runway + transonic + weather;
            AnglesDeg = new Vector3(
                -events.Recoil * 0.32f - events.Landing * 0.55f + Mathf.Sin(time * 31f) * buzz,
                Mathf.Sin(time * 23f) * buzz * 0.4f,
                Mathf.Sin(time * 41f) * (buzz + events.Recoil * 0.11f) + events.Sonic * 0.35f) * strength;
            AnglesDeg += Vector3.right * Mathf.Clamp(events.GearLock * strength, -0.08f, 0.08f);
        }

        public void OnShot(Gun gun)
        {
            if (!active || !recoilOn || strength <= 0f || gun == null || gun.info == null) return;
            events.AddGun(gun.info.massPerRound * gun.info.muzzleVelocity, 1f);
            Shots++;
        }

        private void Follow(Aircraft aircraft)
        {
            if (subscribed != null) subscribed.OnTouchdown -= OnTouchdown;
            subscribed = aircraft;
            if (subscribed != null) subscribed.OnTouchdown += OnTouchdown;
            events.Reset();
            primed = false;
            lastVerticalSpeed = aircraft != null && aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
        }

        private void OnTouchdown()
        {
            if (!active || !touchdownOn || !primed || strength <= 0f) return;
            events.AddLanding(Mathf.Min(0f, lastVerticalSpeed), 1f);
            Touchdowns++;
        }

        public void ResetMeasurements() { events.Reset(); primed = false; AnglesDeg = Vector3.zero; }
        public void Release() { Follow(null); active = false; AnglesDeg = Vector3.zero; }
    }
}
