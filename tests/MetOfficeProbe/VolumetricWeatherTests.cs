using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class MetDriver
{
    private sealed class VolumetricTransitionSample
    {
        public int selection, sequence;
        public float gameSeconds, realSeconds, targetConditions, conditions, coverage, humidity, instability;
        public string source;
    }

    // These are real frames in the isolated single-player mission, not direct writes
    // to NOAtmoFX's smoothed fields. The saved configuration and all borrowed state
    // are restored even when an assertion fails.
    private async UniTask TestVolumetricWeather(object launcher, object coordinator, object weatherManager)
    {
        var addon = Chainloader.PluginInfos["paul.no.clouds"].Instance;
        var owner = Chainloader.PluginInfos[OwnerId].Instance;
        Type api = addon.GetType().Assembly.GetType("NoAtmoFX.NoAtmoFXApi", true);
        Type transition = owner.GetType().Assembly.GetType("UnifiedEffects.NoAtmoWeatherTransition", true);
        Type profiles = owner.GetType().Assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.VolumetricWeatherProfiles", true);
        var samples = new List<VolumetricTransitionSample>();
        var configValues = new Dictionary<ConfigEntryBase, object>();
        var configs = new[] { owner.Config, addon.Config };
        var savePolicies = configs.ToDictionary(c => c, c => c.SaveOnConfigSet);
        var diskBytes = configs.ToDictionary(c => c.ConfigFilePath, c => File.ReadAllBytes(c.ConfigFilePath));
        foreach (var config in configs)
        {
            foreach (var entry in config.Select(pair => pair.Value)) configValues[entry] = entry.BoxedValue;
            config.SaveOnConfigSet = false;
        }
        var input = api.GetField("In", F);
        object initialInput = input.GetValue(null);
        object initialMode = Prop(coordinator, "Mode");
        byte initialSky = (byte)Prop(Prop(weatherManager, "LiveKey"), "SkyPin");
        float Read(string property) => (float)api.GetProperty(property, F).GetValue(null, null);
        string Source() => (string)api.GetProperty("WeatherSource", F).GetValue(null, null);
        object Target() => input.GetValue(null);
        bool Pick(int profile) => (bool)Call(coordinator, "SelectWeather", profile);
        MethodInfo adjust = transition.GetMethod("AdjustTimeConstant", F);
        var stateField = AccessTools.Field(typeof(GameManager), "<gameState>k__BackingField");
        var originalGameState = GameManager.gameState;
        var conditionsOverride = (ConfigEntry<float>)addon.Config[new ConfigDefinition("Weather", "ConditionsOverride")];
        var duration = (ConfigEntry<float>)transition.GetProperty("Entry", F).GetValue(null, null);
        var fastEnabled = (ConfigEntry<bool>)transition.GetProperty("EnabledEntry", F).GetValue(null, null);
        bool originalCloudPage = (bool)Field(launcher, "showCloudSettings");
        try
        {
            Need((bool)transition.GetProperty("Available", F).GetValue(null, null), "five-second compatibility patch accepts the exact installed NOAtmoFX 1.4 method");
            Need(adjust != null && duration != null, "volumetric transition setting and bounded time-constant hook exist");
            var update = addon.GetType().GetMethod("Update", F);
            var instructions = PatchProcessor.GetOriginalInstructions(update);
            var matchMethod = transition.GetMethod("MatchesTimeConstant", F);
            var matches = new List<int>();
            for (int i = 0; i + 7 < instructions.Count; i++)
                if ((bool)matchMethod.Invoke(null, new object[] { instructions, i, update })) matches.Add(i);
            Need(matches.Count == 1, "compatibility matcher identifies exactly one original smoothing block");
            var changedInstructions = instructions.Select(i => new CodeInstruction(i)).ToList();
            changedInstructions[matches[0]].operand = 13f;
            Need(!(bool)matchMethod.Invoke(null, new object[] { changedInstructions, matches[0], update }),
                 "compatibility matcher rejects changed upstream smoothing constants");
            Need((bool)transition.GetProperty("Available", F).GetValue(null, null), "read-only mismatch fixture leaves active compatibility patch intact");
            Need(Near((float)duration.DefaultValue, 5f), "volumetric transition default is five seconds");
            Need((int)profiles.GetProperty("Count", F).GetValue(null, null) == 7, "volumetric weather exposes seven supported profiles");
            duration.Value = 5f;
            fastEnabled.Value = true;
            conditionsOverride.Value = -1f;
            Need((bool)Call(coordinator, "Select", Enum.ToObject(initialMode.GetType(), 2)), "volumetric regression selects actual optional provider");
            FI(launcher, "showCloudSettings").SetValue(launcher, false);
            Call(launcher, "Refresh");
            var tiles = (Button[])Field(launcher, "tiles");
            Need(tiles.Length == 7 && tiles.All(t => t.gameObject.activeSelf && t.interactable), "all seven volumetric weather presets are reachable in the native MFD");
            Call(weatherManager, "OpenConsole");
            Need((bool)Prop(launcher, "IsWeatherPageOpen"), "volumetric O action opens the provider-specific native MFD weather page");
            object legacyConsole = Field(weatherManager, "console");
            Need(legacyConsole == null || !(bool)Prop(legacyConsole, "IsOpen"), "volumetric weather action does not expose the incompatible Boscali sky gallery");
            Call(weatherManager, "ToggleConsole");
            Need(!(bool)Prop(launcher, "IsWeatherPageOpen"), "repeating the volumetric O action closes the weather page");
            Call(weatherManager, "ToggleConsole");
            Need((bool)Prop(launcher, "IsWeatherPageOpen"), "volumetric O action reopens the weather page");
            Need(!((Button)Field(launcher, "reroll")).gameObject.activeSelf &&
                 ((Button[])Field(launcher, "classes")).All(button => !button.gameObject.activeSelf),
                 "volumetric menu hides unsupported Boscali reroll and classification tabs");
            ((Button)Field(launcher, "launch")).onClick.Invoke();
            Need(((RectTransform)Field(launcher, "cloudPage")).gameObject.activeInHierarchy,
                 "volumetric detail action opens actual NOAtmoFX cloud settings");
            Need((bool)Call(launcher, "OpenWeatherPage"), "weather page returns from cloud settings through the same MFD entry point");
            ((Button)Field(launcher, "simpleButton")).onClick.Invoke();
            string simpleCaption = tiles[0].GetComponentInChildren<TMPro.TMP_Text>(true).text;
            ((Button)Field(launcher, "technicalButton")).onClick.Invoke();
            string technicalCaption = tiles[0].GetComponentInChildren<TMPro.TMP_Text>(true).text;
            Need(simpleCaption != technicalCaption && technicalCaption.IndexOf("dry", StringComparison.OrdinalIgnoreCase) >= 0,
                 "front-and-centre names selector changes volumetric preset captions");
            ((Button)Field(launcher, "simpleButton")).onClick.Invoke();
            var transitionButton = (Button)Field(launcher, "transition");
            Need(transitionButton.gameObject.activeInHierarchy && transitionButton.interactable,
                 "volumetric transition duration is visible on the main weather page");
            Need(ReferenceEquals(duration, owner.Config[new ConfigDefinition("Weather display", "Volumetric transition seconds")]),
                 "MFD transition duration and F1 setting share the exact same config entry");
            foreach (float seconds in new[] { 15f, 60f, 600f, 5f })
            {
                transitionButton.onClick.Invoke();
                Need(Near(duration.Value, seconds) && Near((float)Prop(coordinator, "TransitionSeconds"), seconds),
                     "main weather transition button cycles the F1 value to " + seconds + " seconds");
            }
            CaptureOptionalKropPanel((Component)launcher, "volumetric-menu.png");
            for (int selection = 0; selection < tiles.Length; selection++)
            {
                tiles[selection].onClick.Invoke();
                object profile = profiles.GetMethod("Get", F).Invoke(null, new object[] { selection });
                Need((int)Prop(coordinator, "SelectedWeatherIndex") == selection &&
                     Near((float)Field(Target(), "Conditions"), (float)Field(profile, "Conditions")),
                     "every visible volumetric preset applies its own weather target " + selection);
            }

            int[] sequence = { 0, 5, 0, 5 };
            for (int pass = 0; pass < sequence.Length; pass++)
            {
                int selection = sequence[pass];
                Call(launcher, "Refresh");
                tiles[selection].onClick.Invoke();
                Need((int)Prop(coordinator, "SelectedWeatherIndex") == selection, "MFD applies repeated volumetric selection " + pass + ": " + selection);
                Need((bool)Prop(coordinator, "FastWeatherTransitionActive"), "explicit preset owns the quick transition " + pass);
                Need(Near((float)adjust.Invoke(null, new object[] { 120f }), 1f), "five-second transition uses one-second smoothing stages " + pass);
                object profile = profiles.GetMethod("Get", F).Invoke(null, new object[] { selection });
                object target = Target();
                float targetConditions = (float)Field(profile, "Conditions");
                Need(Near((float)Field(target, "Conditions"), targetConditions), "actual NOAtmoFX API receives the selected uncapped target " + pass);
                Need(Near((float)Field(target, "Base"), (float)Field(profile, "CloudBaseMetres")) &&
                     Near((float)Field(target, "Hum"), (float)Field(profile, "HumidityBias")) &&
                     Near((float)Field(target, "Ins"), (float)Field(profile, "InstabilityBias")) &&
                     Near((float)Field(target, "Cov"), (float)Field(profile, "CoverageScale")) &&
                     (int)Field(target, "Front") == (int)Field(profile, "Front"),
                     "complete cloud model targets replace the preceding preset " + pass);
                Need((byte)Prop(Prop(weatherManager, "LiveKey"), "SkyPin") == (byte)Field(profile, "BoscaliSkyIndex"),
                     "Boscali rain and atmosphere follow the corresponding volumetric selection " + pass);
                float gameStart = Time.time, realStart = Time.realtimeSinceStartup;
                await UniTask.Yield();
                if (pass > 0)
                    Need(Mathf.Abs(Read("Conditions") - targetConditions) > .25f, "repeated weather change starts smoothly instead of snapping " + pass);
                await Until(() => Time.time - gameStart >= 5f, 22f, "five simulation seconds advance for volumetric transition " + pass);
                var sample = new VolumetricTransitionSample
                {
                    selection = selection, sequence = pass, gameSeconds = Time.time - gameStart,
                    realSeconds = Time.realtimeSinceStartup - realStart, targetConditions = targetConditions,
                    conditions = Read("Conditions"), coverage = Read("Coverage"), humidity = Read("Humidity"),
                    instability = Read("Instability"), source = Source()
                };
                samples.Add(sample);
                Need(sample.source == "api", "NOAtmoFX renders the direct weather source on transition " + pass);
                Need(Mathf.Abs(sample.conditions - targetConditions) < .06f, "actual cloud conditions reach the requested weather within about five seconds " + pass);
                Need(selection == 0 ? sample.coverage < .03f : sample.coverage > .5f,
                     "actual cloud coverage clears and rebuilds on repeated selections " + pass);
                if (pass > 0)
                {
                    var previous = samples[pass - 1];
                    Need(selection == 0 ? sample.humidity < previous.humidity - .2f : sample.humidity > previous.humidity + .2f,
                         "cloud humidity changes in the requested direction " + pass);
                    Need(selection == 0 ? sample.instability < previous.instability - .1f : sample.instability > previous.instability + .1f,
                         "cloud convection changes in the requested direction " + pass);
                }
                Call(launcher, "Refresh");
                if (pass == 2) Capture("volumetric-repeated-clear.png");
                if (pass == 3) Capture("volumetric-repeated-thunderstorm.png");
            }

            Need(Read("Conditions") > .85f, "ordinary volumetric thunderstorm exceeds the former native 0.6 cap");
            Need((float)Prop(weatherManager, "CurrentConditions") > .9f, "automatic integration export also exposes uncapped storm severity");
            Need((float)Field(weatherManager, "currentConditions") <= .6001f && LevelInfo.i.conditions <= .6001f,
                 "ordinary native sky conditions retain their protective cap");
            fastEnabled.Value = false;
            Need(Near((float)adjust.Invoke(null, new object[] { 120f }), 120f), "fast-transition toggle restores the add-on's normal smoothing");
            fastEnabled.Value = true;
            Need(Near((float)adjust.Invoke(null, new object[] { 120f }), 1f), "fast-transition toggle re-enables five-second smoothing");
            object beforeRefusal = Target();
            int selectedBefore = (int)Prop(coordinator, "SelectedWeatherIndex");
            conditionsOverride.Value = .25f;
            Need(!Pick(0), "NOAtmoFX personal weather override refuses a misleading pack selection");
            Need(Target().Equals(beforeRefusal) && (int)Prop(coordinator, "SelectedWeatherIndex") == selectedBefore,
                 "refused override selection preserves active API targets and selection");
            conditionsOverride.Value = -1f;

            try
            {
                // No frame advances in synthetic multiplayer state. No network loop,
                // host or client is started; only the authority guard is exercised.
                stateField.SetValue(null, GameState.Multiplayer);
                Need(!Pick(0), "volumetric mission preset refuses multiplayer command");
                Need(Near((float)adjust.Invoke(null, new object[] { 120f }), 120f), "five-second compatibility hook leaves multiplayer timing untouched");
                Need(Target().Equals(beforeRefusal), "multiplayer guard leaves API weather unchanged");
            }
            finally { stateField.SetValue(null, originalGameState); }

            Need(!Pick(-1) && !Pick(7), "invalid volumetric profile indices are rejected");
            Call(coordinator, "ResumeWeather");
            Need(!(bool)Prop(Target(), "Any") && (int)Prop(coordinator, "SelectedWeatherIndex") < 0,
                 "mission weather releases the pack-owned API targets");
            Need(Near((float)adjust.Invoke(null, new object[] { 120f }), 1f) &&
                 (float)Field(coordinator, "releaseBlendUntil") <= Time.unscaledTime + 10.1f,
                 "mission weather uses a bounded quick handback after releasing API targets");
            Call(weatherManager, "PinSky", (byte)44);
            var adapterInput = api.GetField("Adapter", F);
            await Until(() => (float)Field(adapterInput.GetValue(null), "Conditions") > .9f, 3f,
                "automatic Boscali adapter receives uncapped storm severity after direct weather is released");
            Need(Source() == "boscali", "automatic weather source resumes after releasing direct targets");
            Need(Pick(5), "volumetric weather can be selected again after resuming automatic weather");
            Call(coordinator, "Select", Enum.ToObject(initialMode.GetType(), 1));
            Need(!(bool)Prop(Target(), "Any") && !(bool)Prop(coordinator, "FastWeatherTransitionActive"),
                 "leaving volumetric mode releases weather ownership and quick timing");
            Need(Near((float)adjust.Invoke(null, new object[] { 120f }), 120f), "enhanced mode preserves native NOAtmoFX transition duration");
            Call(coordinator, "Select", Enum.ToObject(initialMode.GetType(), 2));
            Need(Pick(5), "weather ownership can be reacquired after changing cloud providers");
            api.GetMethod("SetConditions", F).Invoke(null, new object[] { .33f });
            object externalInput = Target();
            Call(coordinator, "Select", Enum.ToObject(initialMode.GetType(), 1));
            Need(Target().Equals(externalInput), "leaving volumetric mode preserves weather taken over by another API caller");
            api.GetMethod("ClearWeather", F).Invoke(null, null);
            foreach (var file in diskBytes)
                Need(file.Value.SequenceEqual(File.ReadAllBytes(file.Key)), "volumetric weather changes do not rewrite saved settings: " + Path.GetFileName(file.Key));
        }
        finally
        {
            stateField.SetValue(null, originalGameState);
            Call(coordinator, "ResumeWeather");
            Call(coordinator, "Select", initialMode);
            foreach (var saved in configValues) saved.Key.BoxedValue = saved.Value;
            input.SetValue(null, initialInput);
            Call(weatherManager, "PinSky", initialSky);
            FI(launcher, "showCloudSettings").SetValue(launcher, originalCloudPage);
            Call(launcher, "Refresh");
            foreach (var config in configs) config.SaveOnConfigSet = savePolicies[config];
            var serializer = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true)
                .GetMethod("SerializeObject", new[] { typeof(object) });
            var report = new
            {
                addonVersion = Chainloader.PluginInfos["paul.no.clouds"].Metadata.Version.ToString(),
                transitionSeconds = 5, samples = samples.ToArray(),
                configurationFilesUnchanged = diskBytes.All(file => file.Value.SequenceEqual(File.ReadAllBytes(file.Key))),
                configurationsRestored = configValues.All(saved => Equals(saved.Key.BoxedValue, saved.Value)),
                visibleWindowFlightAcceptance = "OPEN - isolated explicit render captures and real simulation frames only"
            };
            File.WriteAllText(Path.Combine(dir, "volumetric-weather-transitions.json"),
                (string)serializer.Invoke(null, new object[] { report }));
        }
    }
}
