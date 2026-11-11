using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

public sealed partial class MetDriver
{
    private void TestKropAudioRefinements()
    {
        var plugin = ModuleInfos()["aaron.boscali.krop-unified"].Instance;
        var asm = plugin.GetType().Assembly;
        var filters = asm.GetType("KropClientEffects.AudioFilters", true);
        var refinements = asm.GetType("KropClientEffects.AudioRefinements", true);
        var hooks = asm.GetType("KropClientEffects.ExtendedAudioHooks", true);
        var featureType = asm.GetType("KropClientEffects.AudioFeature", true);
        object world = Enum.Parse(asm.GetType("KropClientEffects.AudioKind", true), "World");
        var register = filters.GetMethod("RegisterOptional", F);
        var tick = filters.GetMethod("Tick", F);
        var clear = filters.GetMethod("Clear", F);
        var hosts = (IDictionary)filters.GetField("Hosts", F).GetValue(null);
        string[] keys = (string[])refinements.GetField("SourceKeys", F).GetValue(null);
        var saved = new Dictionary<ConfigEntryBase, object>();
        ConfigEntry<bool> Bool(string section, string key)
        {
            var entry = plugin.Config.Bind(section, key, false);
            if (!saved.ContainsKey(entry)) saved.Add(entry, entry.BoxedValue);
            return entry;
        }
        var objects = new List<GameObject>();
        var clip = AudioClip.Create("Silent audio refinement fixture", 44100, 1, 44100, false);
        GameObject Make(string name)
        {
            var obj = new GameObject("Audio refinement test - " + name); objects.Add(obj);
            var camera = SceneSingleton<CameraStateManager>.i;
            obj.transform.position = camera.transform.position + Vector3.right * 100f;
            return obj;
        }
        AudioSource Source(GameObject host, bool spatial = true)
        {
            var source = host.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.clip = clip; source.loop = true; source.spatialBlend = spatial ? 1f : 0f;
            source.volume = 0f; source.bypassEffects = false; source.Play();
            return source;
        }
        try
        {
            Need(keys.Length == 19, "all nineteen additional audio families have independent controls");
            foreach (string key in keys)
            {
                var option = Bool("08 Additional audio", key);
                Need(Equals(option.DefaultValue, false), "new audio family defaults off: " + key);
                option.Value = false;
            }
            Bool("00 General", "Enable pack").Value = true;
            Bool("01 Audio filters", "Atmospheric muffling").Value = true;
            Bool("01 Audio filters", "First-person cockpit muffling").Value = false;
            Bool("01 Audio filters", "Canopy-aware muffling").Value = false;
            Bool("01 Audio filters", "Open-cabin muffling").Value = false;
            clear.Invoke(null, null);

            // Every gate is exercised against live Unity audio, including restoration of an
            // existing vanilla filter. Silent samples prevent these fixtures making audible noise.
            for (int i = 0; i < keys.Length; i++)
            {
                var option = Bool("08 Additional audio", keys[i]);
                var host = Make(keys[i]); var source = Source(host);
                var native = host.AddComponent<AudioLowPassFilter>(); native.enabled = true;
                native.cutoffFrequency = 18000f; native.lowpassResonanceQ = 0.8f;
                object feature = Enum.ToObject(featureType, i);
                register.Invoke(null, new object[] { source, world, null, feature });
                Need(!hosts.Contains(host), "disabled optional source is not registered: " + keys[i]);
                option.Value = true;
                register.Invoke(null, new object[] { source, world, null, feature });
                tick.Invoke(null, null);
                Need(hosts.Contains(host) && native.enabled && native.cutoffFrequency < 17000f,
                    "optional source receives real distance filter: " + keys[i]);
                option.Value = false;
                tick.Invoke(null, null);
                Need(!hosts.Contains(host) && native.enabled && Math.Abs(native.cutoffFrequency - 18000f) < 1f && Math.Abs(native.lowpassResonanceQ - 0.8f) < 0.01f,
                    "switch off restores original filter and removes source state: " + keys[i]);
                source.Stop();
            }

            var listenerHost = Make("listener exclusion");
            listenerHost.AddComponent<AudioListener>().enabled = false;
            var listenerSource = Source(listenerHost);
            filters.GetMethod("Register", F).Invoke(null, new object[] { listenerSource, world, null });
            Need(!hosts.Contains(listenerHost) && listenerHost.GetComponent<AudioLowPassFilter>() == null,
                "audio listener host cannot receive a source filter");

            var mixed = Make("warning protection"); var engine = Source(mixed); var warning = Source(mixed, false);
            filters.GetMethod("Register", F).Invoke(null, new object[] { engine, world, null });
            tick.Invoke(null, null);
            Need(mixed.GetComponent<AudioLowPassFilter>() == null, "unregistered interface source prevents shared-host filtering");
            filters.GetMethod("ProtectWarning", F).Invoke(null, new object[] { warning });
            tick.Invoke(null, null);
            Need(warning.bypassEffects && mixed.GetComponent<AudioLowPassFilter>()?.enabled == true,
                "warning bypass protects alert while registered engine remains filtered");
            clear.Invoke(null, null);
            Need(!warning.bypassEffects && !engine.bypassEffects && mixed.GetComponent<AudioLowPassFilter>()?.enabled != true,
                "cleanup restores original warning flags and disables owned filters");

            // A warning can predate our setup hook or be supplied by another mod.
            // Its mixer identity must block a shared filter without changing its
            // bypass flag or relying on an explicit ProtectWarning registration.
            Need(SoundManager.i != null && SoundManager.i.MissileAlertMixer != null,
                "native missile warning mixer available for unregistered-warning fixture");
            var mixerHost = Make("mixer-only warning protection"); var mixerEngine = Source(mixerHost);
            var mixerFilter = mixerHost.AddComponent<AudioLowPassFilter>(); mixerFilter.enabled = true;
            mixerFilter.cutoffFrequency = 18000f; mixerFilter.lowpassResonanceQ = 0.8f;
            filters.GetMethod("Register", F).Invoke(null, new object[] { mixerEngine, world, null });
            tick.Invoke(null, null);
            Need(mixerFilter.cutoffFrequency < 17000f, "shared warning fixture starts with active world filtering");
            var mixerWarning = Source(mixerHost, false);
            mixerWarning.outputAudioMixerGroup = SoundManager.i.MissileAlertMixer;
            var protectedWarnings = (IDictionary)filters.GetField("ProtectedWarnings", F).GetValue(null);
            Need(!protectedWarnings.Contains(mixerWarning), "mixer-only warning was never explicitly registered");
            tick.Invoke(null, null);
            Need(mixerFilter.enabled && Math.Abs(mixerFilter.cutoffFrequency - 18000f) < 1f
                && Math.Abs(mixerFilter.lowpassResonanceQ - 0.8f) < 0.01f && !mixerWarning.bypassEffects,
                "unregistered mixer warning immediately restores shared native filter without changing warning flags");
            mixerWarning.bypassEffects = true;
            tick.Invoke(null, null);
            Need(mixerFilter.cutoffFrequency < 17000f && mixerWarning.bypassEffects,
                "already bypassed mixer warning permits world filtering without explicit registration");
            mixerWarning.bypassEffects = false;
            tick.Invoke(null, null);
            Need(Math.Abs(mixerFilter.cutoffFrequency - 18000f) < 1f && !mixerWarning.bypassEffects
                && !protectedWarnings.Contains(mixerWarning),
                "removing external warning bypass restores shared filter again and preserves ownership");
            clear.Invoke(null, null);
            Need(!mixerWarning.bypassEffects && !mixerEngine.bypassEffects,
                "mixer-only warning cleanup preserves original source flags");

            var bindings = (IDictionary)hooks.GetField("Bindings", F).GetValue(null);
            Need(bindings.Count == 14, "all fourteen native source-observation targets resolved");
            var coveredFamilies = new HashSet<string>();
            foreach (DictionaryEntry binding in bindings)
            {
                foreach (var registration in (IEnumerable)binding.Value)
                    coveredFamilies.Add(registration.GetType().GetField("Feature", F).GetValue(registration).ToString());
                var info = Harmony.GetPatchInfo((MethodBase)binding.Key);
                Need(info != null && info.Postfixes.Any(p => p.owner == "aaron.boscali.krop-unified" && p.PatchMethod.DeclaringType == hooks),
                    "native additional-audio hook installed: " + ((MethodBase)binding.Key).DeclaringType.Name + "." + ((MethodBase)binding.Key).Name);
            }
            var playback = (IDictionary)hooks.GetField("PlaybackFeatures", F).GetValue(null);
            Need(playback.Count == 6, "all six native playback-only patch targets resolved");
            foreach (DictionaryEntry binding in playback)
            {
                coveredFamilies.Add(binding.Value.ToString());
                var info = Harmony.GetPatchInfo((MethodBase)binding.Key);
                Need(info != null && info.Transpilers.Any(p => p.owner == "aaron.boscali.krop-unified" && p.PatchMethod.DeclaringType == hooks),
                    "native playback-only hook installed: " + binding.Value);
            }
            var alarm = AccessTools.Inner(typeof(ThreatList), "MissileAlarm");
            Need(Harmony.GetPatchInfo(AccessTools.Method(alarm, "Setup"))?.Postfixes.Any(p => p.PatchMethod.DeclaringType == hooks) == true,
                "native missile warning setup protection installed");
            Need(Harmony.GetPatchInfo(AccessTools.Method(alarm, "ManageAlarmSound"))?.Postfixes.Any(p => p.PatchMethod.DeclaringType == hooks) == true,
                "existing missile warning sources are protected again after module re-enable");
            var fuelPatch = asm.GetType("KropClientEffects.FuelIgnitionAudioPatch", true);
            var warheadPatch = asm.GetType("KropClientEffects.WarheadAudioObservationPatch", true);
            Need(Harmony.GetPatchInfo(AccessTools.Method(typeof(FuelTank), "Fireball"))?.Postfixes.Any(p => p.PatchMethod.DeclaringType == fuelPatch) == true,
                "native fuel ignition audio observation installed");
            Need(Harmony.GetPatchInfo(AccessTools.Method(typeof(Missile.Warhead), "Detonate"))?.Transpilers.Any(p => p.PatchMethod.DeclaringType == warheadPatch) == true,
                "native warhead audio observation installed");
            coveredFamilies.Add("FuelIgnition"); coveredFamilies.Add("WarheadEffects");
            Need(coveredFamilies.Count == 19, "all nineteen optional families have installed source-capture paths");

            var selector = asm.GetType("KropClientEffects.EffectMath", true).GetMethod("NonRepeatingIndex", F);
            for (int count = 2; count <= 8; count++) for (int previous = 0; previous < count; previous++)
            {
                var selected = new HashSet<int>();
                for (int candidate = 0; candidate < count - 1; candidate++)
                    selected.Add((int)selector.Invoke(null, new object[] { candidate, previous, count, true }));
                Need(selected.Count == count - 1 && !selected.Contains(previous) && selected.All(n => n >= 0 && n < count),
                    "nonrepeat selector covers every alternative once for pool " + count + ", previous " + previous);
            }

            var shaping = Bool("02 Bullet near misses", "KROP crack shaping"); shaping.Value = true;
            asm.GetType("KropClientEffects.LocalAudio", true).GetMethod("Play", F).Invoke(null,
                new object[] { null, clip, SceneSingleton<CameraStateManager>.i.transform.position, 0f, null, null, 1f, true });
            var transientType = asm.GetType("KropClientEffects.CrackTransient", true);
            var transient = UnityEngine.Object.FindObjectsOfType(transientType).Cast<Component>().Single();
            objects.Add(transient.gameObject);
            var highpass = transient.GetComponent<AudioHighPassFilter>();
            Need(highpass != null && highpass.enabled && Math.Abs(highpass.cutoffFrequency - 4000f) < 1f,
                "optional crack shaping creates the four-kilohertz high-pass");
            shaping.Value = false;
            transientType.GetMethod("Update", F).Invoke(transient, null);
            Need(!highpass.enabled, "disabling crack shaping immediately releases its owned filter");

            var cabinType = asm.GetType("KropClientEffects.ReadOnlyCabinAcoustics", true);
            var cabinMethod = cabinType.GetMethod("MuffleFraction", F);
            Need((float)cabinMethod.Invoke(null, new object[] { null }) == 1f, "no aircraft leaves cockpit acoustic baseline unchanged");
            var canopyField = AccessTools.Field(typeof(Aircraft), "canopies");
            var openField = AccessTools.Field(typeof(Canopy), "openAmount");
            var firingField = AccessTools.Field(typeof(Canopy), "firing");
            var aircraft = UnityEngine.Object.FindObjectsOfType<Aircraft>().FirstOrDefault(a => {
                var canopies = canopyField.GetValue(a) as Canopy[];
                return canopies != null && canopies.Length > 0 && canopies.All(c => c != null && !(bool)firingField.GetValue(c) && (c.attachedPart == null || !c.attachedPart.IsDetached()));
            });
            Need(aircraft != null, "mission provides a live canopy for read-only acoustic test");
            var actualCanopies = (Canopy[])canopyField.GetValue(aircraft);
            var originalOpen = actualCanopies.Select(c => (float)openField.GetValue(c)).ToArray();
            var transforms = actualCanopies.Select(c => c.transform.localRotation).ToArray();
            try
            {
                foreach (var canopy in actualCanopies) openField.SetValue(canopy, 0f);
                Bool("01 Audio filters", "Canopy-aware muffling").Value = true;
                cabinType.GetMethod("Clear", F).Invoke(null, null);
                Need(Math.Abs((float)cabinMethod.Invoke(null, new object[] { aircraft }) - 1f) < 0.001f, "closed canopy retains full muffling");
                openField.SetValue(actualCanopies[0], 0.5f);
                Need(Math.Abs((float)cabinMethod.Invoke(null, new object[] { aircraft }) - 0.5f) < 0.001f, "existing half-open canopy halves muffling");
                Need((float)openField.GetValue(actualCanopies[0]) == 0.5f && actualCanopies.Select((c, i) => c.transform.localRotation == transforms[i]).All(x => x),
                    "acoustic calculation does not move canopy transforms or overwrite observed state");
                Bool("01 Audio filters", "Canopy-aware muffling").Value = false;
                Need(Math.Abs((float)cabinMethod.Invoke(null, new object[] { aircraft }) - 1f) < 0.001f, "canopy acoustic switch restores baseline immediately");
            }
            finally { for (int i = 0; i < actualCanopies.Length; i++) openField.SetValue(actualCanopies[i], originalOpen[i]); }
        }
        finally
        {
            clear.Invoke(null, null);
            foreach (var pair in saved) pair.Key.BoxedValue = pair.Value;
            foreach (var obj in objects) if (obj != null) Destroy(obj);
            Destroy(clip);
        }
    }
}
