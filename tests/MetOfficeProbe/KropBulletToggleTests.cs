using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

public sealed partial class MetDriver
{
    private static CameraStateManager bulletShakeObserver;
    private static int bulletShakeCalls;
    private static void ObserveBulletShake(CameraStateManager __instance)
    {
        if (ReferenceEquals(__instance, bulletShakeObserver)) bulletShakeCalls++;
    }

    private void TestKropBulletToggles()
    {
        var plugin = ModuleInfos()["aaron.boscali.krop-unified"].Instance;
        var asm = plugin.GetType().Assembly;
        var cracks = asm.GetType("KropClientEffects.BulletCracks", true);
        var postfix = cracks.GetMethod("Postfix", F);
        var clear = cracks.GetMethod("Clear", F);
        object savedHeard = cracks.GetField("Heard", F).GetValue(null);
        object savedLast = cracks.GetField("last", F).GetValue(null);
        var objects = (IList)asm.GetType("KropClientEffects.VisualObjects", true).GetField("Objects", F).GetValue(null);
        var existingObjects = new HashSet<object>(objects.Cast<object>());
        var hosts = (IDictionary)asm.GetType("KropClientEffects.AudioFilters", true).GetField("Hosts", F).GetValue(null);
        var cameras = SceneSingleton<CameraStateManager>.i;
        Need(cameras != null && cameras.mainCamera != null && Datum.origin != null, "bullet toggle fixture has native camera and floating origin");
        var stateField = FI(cameras, "<currentState>k__BackingField");
        object oldState = stateField.GetValue(cameras);
        var low = FI(cameras.cockpitState, "lowFreqShake");
        var high = FI(cameras.cockpitState, "highFreqShake");
        object oldLow = low.GetValue(cameras.cockpitState), oldHigh = high.GetValue(cameras.cockpitState);
        var saved = new Dictionary<ConfigEntryBase, object>();
        ConfigEntry<T> Setting<T>(string section, string key)
        {
            Need(plugin.Config.TryGetEntry<T>(new ConfigDefinition(section, key), out var result), "bullet fixture setting exists: " + key);
            if (!saved.ContainsKey(result)) saved.Add(result, result.BoxedValue);
            return result;
        }
        var observer = new Harmony("aaron.boscali.probe.bullet-toggle-observer");
        WeaponInfo weapon = null;
        try
        {
            var master = Setting<bool>("00 General", "Enable pack");
            var sound = Setting<bool>("02 Bullet near misses", "Bullet cracks");
            var shake = Setting<bool>("02 Bullet near misses", "Large-round camera shake");
            Setting<float>("02 Bullet near misses", "Detection radius m").Value = 50f;
            Setting<float>("02 Bullet near misses", "Camera shake strength").Value = .5f;
            Setting<float>("02 Bullet near misses", "Shake pitch threshold").Value = .75f;
            Setting<float>("02 Bullet near misses", "Small-round pitch").Value = 1.5f;
            Setting<float>("02 Bullet near misses", "Large-round pitch").Value = .5f;
            // Exercise real source creation/playback with zero gain; no audible fixture cracks.
            Setting<float>("02 Bullet near misses", "Small-round volume").Value = 0f;
            Setting<float>("02 Bullet near misses", "Large-round volume").Value = 0f;
            stateField.SetValue(cameras, cameras.cockpitState);
            observer.Patch(AccessTools.Method(typeof(CameraStateManager), "ShakeCamera", new[] { typeof(float), typeof(float) }),
                prefix: new HarmonyMethod(typeof(MetDriver), nameof(ObserveBulletShake)));
            bulletShakeObserver = cameras;
            weapon = ScriptableObject.CreateInstance<WeaponInfo>(); weapon.massPerRound = 50f;

            void RunCase(bool masterOn, bool soundOn, bool shakeOn, bool expectSound, bool expectShake, string label)
            {
                master.Value = masterOn; sound.Value = soundOn; shake.Value = shakeOn;
                clear.Invoke(null, null);
                low.SetValue(cameras.cockpitState, 0f); high.SetValue(cameras.cockpitState, 0f); bulletShakeCalls = 0;
                var before = new HashSet<object>(objects.Cast<object>());
                GlobalPosition cameraPosition = cameras.mainCamera.transform.GlobalPosition();
                GlobalPosition start = cameraPosition + new Vector3(5f, 0f, -20f);
                // A managed object only: no constructor RNG, BulletSim registration, tracer, trajectory trace or damage callback.
                var bullet = (BulletSim.Bullet)FormatterServices.GetUninitializedObject(typeof(BulletSim.Bullet));
                bullet.position = cameraPosition + new Vector3(5f, 0f, 20f);
                bullet.velocity = Vector3.forward * 1200f;
                postfix.Invoke(null, new object[] { bullet, start, null, weapon, null, true, cameraPosition });
                var created = objects.Cast<object>().Where(x => !before.Contains(x)).Select(x => (GameObject)Field(x, "Object")).ToArray();
                Need(created.Length == (expectSound ? 1 : 0), label + ": independent sound source count");
                if (expectSound)
                {
                    var source = created[0].GetComponent<AudioSource>();
                    Need(source != null && source.clip != null && source.isPlaying && source.volume == 0f,
                        label + ": real muted crack source plays embedded recording");
                }
                Need(bulletShakeCalls == (expectShake ? 1 : 0), label + ": native ShakeCamera invocation count");
                Need(Mathf.Abs((float)low.GetValue(cameras.cockpitState) - (expectShake ? .45f : 0f)) < .0001f &&
                     (float)high.GetValue(cameras.cockpitState) == 0f,
                    label + ": native cockpit shake envelope matches distance-scaled effect");
                Need(!bullet.active && bullet.tracer == null && !bullet.explosive && !bullet.proximityFuse,
                    label + ": synthetic bullet stays inert and unregistered");
            }

            RunCase(true, false, true, false, true, "sound off / shake on");
            RunCase(true, true, false, true, false, "sound on / shake off");
            RunCase(true, false, false, false, false, "both effects off");
            RunCase(false, true, true, false, false, "module master off");
        }
        finally
        {
            bulletShakeObserver = null; observer.UnpatchSelf();
            low.SetValue(cameras.cockpitState, oldLow); high.SetValue(cameras.cockpitState, oldHigh);
            stateField.SetValue(cameras, oldState);
            foreach (var pair in saved) pair.Key.BoxedValue = pair.Value;
            cracks.GetField("Heard", F).SetValue(null, savedHeard); cracks.GetField("last", F).SetValue(null, savedLast);
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                object entry = objects[i];
                if (existingObjects.Contains(entry)) continue;
                var obj = (GameObject)Field(entry, "Object");
                if (obj != null)
                {
                    foreach (var source in obj.GetComponents<AudioSource>()) source.Stop();
                    hosts.Remove(obj); UnityEngine.Object.Destroy(obj);
                }
                objects.RemoveAt(i);
            }
            if (weapon != null) UnityEngine.Object.Destroy(weapon);
        }
    }
}
