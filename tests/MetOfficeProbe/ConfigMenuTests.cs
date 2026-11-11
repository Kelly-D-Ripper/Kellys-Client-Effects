using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

public sealed partial class MetDriver
{
    [Serializable] private sealed class F1OwnerAudit
    {
        public string guid;
        public string name;
        public int liveEntries;
        public int liveToggles;
        public int f1Entries;
        public int f1Toggles;
        public int defaultVisibleToggles;
        public int dormantHiddenEntries;
    }
    [Serializable] private sealed class F1Audit
    {
        public string version;
        public int privateOwnersWithoutBridge;
        public int privateOwnersWithBridge;
        public int publicPackEntries;
        public int ownerCount;
        public int noAtmoApiCheckboxes;
    }

    private async UniTask TestConfigurationManagerMenus()
    {
        const string id = "com.bepis.bepinex.configurationmanager";
        bool expected = File.Exists(Path.Combine(dir, "configuration-manager.txt"));
        if (!expected)
        {
            Need(!Chainloader.PluginInfos.ContainsKey(id), "F1 Configuration Manager remains optional in standalone fixture");
            return;
        }
        Need(Chainloader.PluginInfos.TryGetValue(id, out var info) && info.Instance != null, "exact F1 Configuration Manager loaded");
        var manager = info.Instance;
        Need(info.Metadata.Version.ToString() == "19.1", "F1 compatibility exercised against installed Configuration Manager 19.1");
        var managerType = manager.GetType();
        var searcher = managerType.Assembly.GetType("ConfigurationManager.SettingSearcher", true);
        var found = (BaseUnityPlugin[])searcher.GetMethod("FindPlugins", F).Invoke(null, null);
        var identities = new HashSet<string>(new[] {
            "aaron.boscali.weather-unified", "aaron.boscali.unified", "aaron.boscali.krop-unified",
            "aaron.boscali.betternight", "aaron.boscali.airfield-lighting", "aaron.boscali.cockpit-audio",
            "aaron.boscali.cockpitfeel", "aaron.boscali.missile-unified", "aaron.boscali.control-unified"
        });
        var owners = ModuleInfos().Where(x => identities.Contains(x.Key)).Select(x => x.Value.Instance).ToArray();
        Need(owners.Length == 9 && owners.All(x => x != null), "F1 audit has root and eight real private configuration owners");
        // Exact native search algorithm, without our postfix. Record the observed
        // hiding behaviour rather than assuming a particular Unity implementation.
        var native = Chainloader.PluginInfos.Values.Select(x => x.Instance).Where(x => x != null)
            .Union(UnityEngine.Object.FindObjectsOfType(typeof(BaseUnityPlugin)).Cast<BaseUnityPlugin>()).ToArray();
        var privateOwners = owners.Where(x => x.Info.Metadata.GUID != "aaron.boscali.weather-unified").ToArray();
        int nativePrivate = privateOwners.Count(x => native.Contains(x));
        Note("INFO F1 private owners without bridge=" + nativePrivate + " with bridge=" + privateOwners.Count(x => found.Contains(x)));
        foreach (var owner in owners)
            Need(found.Count(x => ReferenceEquals(x, owner)) == 1, "F1 discovers configuration owner exactly once " + owner.Info.Metadata.GUID);
        Need(!privateOwners.Any(x => Chainloader.PluginInfos.ContainsKey(x.Info.Metadata.GUID)), "F1 bridge does not register private modules as DOORMAN plugins");
        Need(found.Where(x => x != null).Distinct().Count() == found.Count(x => x != null), "F1 plugin search introduces no duplicate owners");

        var openProperty = managerType.GetProperty("DisplayingWindow", F);
        bool wasOpen = (bool)openProperty.GetValue(manager, null);
        string search = (string)managerType.GetProperty("SearchString", F).GetValue(manager, null);
        var rows = new List<F1OwnerAudit>();
        var dormant = new HashSet<string>(new[] {
            "aaron.boscali.weather-unified/Weather/CinematicCloudsEnabled", "aaron.boscali.weather-unified/Weather/CloudHalfResolution",
            "aaron.boscali.weather-unified/Weather/CloudTemporalUpdate", "aaron.boscali.unified/Immersion/Contrails",
            "aaron.boscali.unified/Immersion/DustWakes", "aaron.boscali.betternight/8. Street Lights - Range & Destruction/Destructible",
            "aaron.boscali.betternight/2. Night/Extra Ambient When NVG Active"
        });
        var foundDormant = new HashSet<string>();
        try
        {
            managerType.GetProperty("SearchString", F).SetValue(manager, "", null);
            openProperty.SetValue(manager, true, null);
            Call(manager, "BuildSettingList");
            Need((bool)openProperty.GetValue(manager, null), "actual F1 menu model opens");
            var all = ((IEnumerable)Field(manager, "_allSettings")).Cast<object>().ToArray();
            var visible = new List<object>();
            foreach (object group in (IEnumerable)Field(manager, "_filteredSetings"))
                foreach (object category in (IEnumerable)Field(group, "Categories"))
                    visible.AddRange(((IEnumerable)Field(category, "Settings")).Cast<object>());
            foreach (var owner in owners)
            {
                string guid = owner.Info.Metadata.GUID;
                object[] settings = all.Where(x => ReferenceEquals(Prop(x, "PluginInstance"), owner) &&
                    x.GetType().GetProperty("Entry", F) != null).ToArray();
                var original = owner.Config.Select(x => x.Value).ToArray();
                Func<ConfigEntryBase, bool> hidden = x => dormant.Contains(guid + "/" + x.Definition.Section + "/" + x.Definition.Key);
                Need(settings.Length == original.Count(x => !hidden(x)), "F1 includes every active configuration entry " + guid);
                foreach (var entry in original)
                {
                    object row = settings.SingleOrDefault(x => ReferenceEquals(Prop(x, "Entry"), entry));
                    if (hidden(entry))
                    {
                        string identity = guid + "/" + entry.Definition.Section + "/" + entry.Definition.Key;
                        foundDormant.Add(identity);
                        Need(row == null, "F1 hides proven inactive setting without deleting configuration " + identity);
                        continue;
                    }
                    Need(row != null, "F1 uses original ConfigEntry " + guid + "/" + entry.Definition.Section + "/" + entry.Definition.Key);
                    if (entry.SettingType == typeof(bool))
                        Need(visible.Contains(row), "F1 default filters expose effect toggle " + guid + "/" + entry.Definition.Section + "/" + entry.Definition.Key);
                }
                var toggle = original.FirstOrDefault(x => x.SettingType == typeof(bool) && !hidden(x));
                if (toggle != null)
                {
                    object row = settings.Single(x => ReferenceEquals(Prop(x, "Entry"), toggle));
                    bool previous = (bool)toggle.BoxedValue;
                    try
                    {
                        row.GetType().GetMethod("Set", F).Invoke(row, new object[] { !previous });
                        Need((bool)toggle.BoxedValue == !previous, "F1 editing reaches original configuration " + guid);
                    }
                    finally { toggle.BoxedValue = previous; }
                }
                rows.Add(new F1OwnerAudit { guid = guid, name = owner.Info.Metadata.Name,
                    liveEntries = original.Length, liveToggles = original.Count(x => x.SettingType == typeof(bool)),
                    f1Entries = settings.Length, f1Toggles = settings.Count(x => ((ConfigEntryBase)Prop(x, "Entry")).SettingType == typeof(bool)),
                    dormantHiddenEntries = original.Count(hidden),
                    defaultVisibleToggles = settings.Count(x => ((ConfigEntryBase)Prop(x, "Entry")).SettingType == typeof(bool) && visible.Contains(x)) });
            }
            Need(foundDormant.SetEquals(dormant), "exactly seven documented dormant entries are hidden from F1");
            TestF1DormantMetadataCleanup(searcher);
            int noAtmoCheckboxes = TestNoAtmoF1Checkboxes(all, visible);
            if (noAtmoCheckboxes > 0) await TestF1CustomDrawerRendering(manager, all);
            var audit = new F1Audit { version = info.Metadata.Version.ToString(), privateOwnersWithoutBridge = nativePrivate,
                privateOwnersWithBridge = privateOwners.Count(x => found.Contains(x)),
                publicPackEntries = Chainloader.PluginInfos.Keys.Count(identities.Contains), ownerCount = rows.Count, noAtmoApiCheckboxes = noAtmoCheckboxes };
            // Unity's serializer omits private nested typed arrays in this runtime.
            // Serialize scalar records individually and explicitly attach the array.
            var records = rows.Select(row => JsonUtility.ToJson(row, true)).ToArray();
            for (int i = 0; i < records.Length; i++)
                Need(JsonUtility.FromJson<F1OwnerAudit>(records[i]).guid == rows[i].guid, "F1 audit owner record serializes " + rows[i].guid);
            string header = JsonUtility.ToJson(audit, true).TrimEnd();
            string json = header.Substring(0, header.Length - 1).TrimEnd() + ",\n\"owners\": [\n" + string.Join(",\n", records) + "\n]\n}\n";
            string auditPath = Path.Combine(dir, "f1-effect-coverage.json");
            File.WriteAllText(auditPath, json);
            string saved = File.ReadAllText(auditPath);
            Need(System.Text.RegularExpressions.Regex.Matches(saved, "\"guid\"\\s*:").Count == rows.Count && rows.Count == 9,
                "F1 audit persists all nine owner records");
        }
        finally
        {
            openProperty.SetValue(manager, wasOpen, null);
            managerType.GetProperty("SearchString", F).SetValue(manager, search, null);
        }
    }

    private void TestF1DormantMetadataCleanup(Type searcher)
    {
        var bridge = assembly.GetType("UnifiedEffects.ConfigurationManagerBridge", true);
        var saved = (IDictionary)bridge.GetField("HiddenEntries", F).GetValue(null);
        var originals = saved.Keys.Cast<ConfigEntryBase>().ToDictionary(x => x, x => (ConfigDescription)Field(saved[x], "Original"));
        var values = originals.Keys.ToDictionary(x => x, x => x.BoxedValue);
        Need(originals.Count == 7, "only the seven dormant entries receive hidden metadata");
        try
        {
            bridge.GetMethod("RestoreDormantEntries", F).Invoke(null, null);
            foreach (var pair in originals)
                Need(ReferenceEquals(pair.Key.Description, pair.Value) && Equals(pair.Key.BoxedValue, values[pair.Key]), "F1 shutdown restores original dormant metadata and value " + pair.Key.Definition);
        }
        finally { searcher.GetMethod("FindPlugins", F).Invoke(null, null); }
    }

    private int TestNoAtmoF1Checkboxes(object[] settings, List<object> visible)
    {
        if (!Chainloader.PluginInfos.TryGetValue("paul.no.clouds", out var info) || info.Instance == null) return 0;
        var coordType = assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.CloudModeCoordinator", true);
        object coordinator = coordType.GetField("Live", F).GetValue(null);
        if (coordinator == null || !(bool)Prop(coordinator, "AddonReady")) return 0;
        var addon = info.Instance;
        var api = addon.GetType().Assembly.GetType("NoAtmoFX.NoAtmoFXApi", true);
        var getFeature = api.GetMethod("GetFeature");
        var features = (string[])api.GetField("Features").GetValue(null);
        string[] numeric = { "NearRain", "Lightning", "Turbulence", "GodRays", "LightShafts", "Fog", "Rainbow", "Glory", "ValleyFog", "MountainCloud" };
        var bools = new Dictionary<string, ConfigDefinition> {
            { "Clouds", new ConfigDefinition("General", "Enabled") }, { "Shadows", new ConfigDefinition("Shadows", "Shadows") },
            { "RainShafts", new ConfigDefinition("Atmosphere", "RainShafts") }, { "WetGlass", new ConfigDefinition("Glass", "WetGlass") },
            { "NuclearClouds", new ConfigDefinition("Nuclear", "Enabled") }, { "KeepSun", new ConfigDefinition("Lighting", "KeepSunBelowClouds") }
        };
        Need(new HashSet<string>(features).SetEquals(numeric.Concat(bools.Keys)), "F1 mapping covers all sixteen official API features");
        var addonRows = settings.Where(x => ReferenceEquals(Prop(x, "PluginInstance"), addon) && x.GetType().GetProperty("Entry", F) != null).ToArray();
        foreach (var pair in bools)
        {
            Need(addon.Config.TryGetEntry<bool>(pair.Value, out var entry), "official boolean feature config " + pair.Key);
            object row = addonRows.SingleOrDefault(x => ReferenceEquals(Prop(x, "Entry"), entry));
            Need(row != null && visible.Contains(row), "F1 default filters expose native checkbox " + pair.Key);
            Need(entry.Value == (bool)getFeature.Invoke(null, new object[] { pair.Key }), "native F1 checkbox agrees with feature API " + pair.Key);
        }
        var bridgeType = assembly.GetType("UnifiedEffects.NoAtmoConfigurationDrawers", true);
        object[] controls = ((IEnumerable)bridgeType.GetField("Controls", F).GetValue(null)).Cast<object>().ToArray();
        Need(controls.Length == numeric.Length, "F1 has ten numeric API feature checkbox drawers");
        var stateBacking = AccessTools.Field(typeof(GameManager), "<gameState>k__BackingField");
        var stateBefore = GameManager.gameState;
        bool saveBefore = addon.Config.SaveOnConfigSet;
        byte[] diskBefore = File.ReadAllBytes(addon.Config.ConfigFilePath);
        object[] savedValues = controls.Select(x => ((ConfigEntryBase)Field(x, "Entry")).BoxedValue).ToArray();
        var remembered = (IDictionary)Field(addon, "_lastOn");
        var rememberedBefore = remembered.Keys.Cast<object>().ToDictionary(x => x, x => remembered[x]);
        var following = coordType.GetField("followingHost", F);
        object originalFollowing = following.GetValue(coordinator);
        var originalFollowHost = addon.Config[new ConfigDefinition("Multiplayer", "FollowHostSettings")].BoxedValue;
        try
        {
            // No Unity frame or real network operation runs while this synthetic
            // state is active. Check the same no-persist rule as MFD changes.
            stateBacking.SetValue(null, GameState.Multiplayer);
            foreach (var control in controls)
            {
                string feature = (string)Field(control, "Feature");
                Need(numeric.Contains(feature), "F1 custom checkbox belongs to documented visual feature " + feature);
                var entry = (ConfigEntryBase)Field(control, "Entry");
                var originalDescription = (ConfigDescription)Field(control, "original");
                Need(entry.Description.Description == originalDescription.Description && ReferenceEquals(entry.Description.AcceptableValues, originalDescription.AcceptableValues), "F1 preserves upstream description and limits " + feature);
                Need((originalDescription.Tags ?? Array.Empty<object>()).All(entry.Description.Tags.Contains), "F1 retains existing metadata tags " + feature);
                object row = addonRows.Single(x => ReferenceEquals(Prop(x, "Entry"), entry));
                var drawer = (Action<ConfigEntryBase>)Prop(row, "CustomDrawer");
                Need(drawer != null && ReferenceEquals(drawer.Target, control) && visible.Contains(row), "real F1 row routes to visible checkbox drawer " + feature);
                bool before = (bool)getFeature.Invoke(null, new object[] { feature });
                Need((bool)Prop(control, "Enabled") == before, "F1 checkbox reads official API " + feature);
                Need((bool)Call(control, "SetEnabled", !before), "F1 checkbox callback accepts toggle " + feature);
                Need((bool)getFeature.Invoke(null, new object[] { feature }) == !before && (bool)Prop(control, "Enabled") == !before, "F1 checkbox changes actual API state " + feature);
                Need((bool)Call(control, "SetEnabled", before), "F1 checkbox restores feature " + feature);
                object numericValue = entry.SettingType == typeof(int) ? (object)1 : ((AcceptableValueRange<float>)entry.Description.AcceptableValues).MaxValue * .5f;
                Need((bool)Call(control, "SetNumeric", numericValue) && Equals(entry.BoxedValue, numericValue), "F1 retains numeric mode or strength control " + feature);
                Need((bool)getFeature.Invoke(null, new object[] { feature }), "F1 numeric control and API remain synchronized " + feature);
                Need(addon.Config.SaveOnConfigSet == saveBefore, "F1 numeric change restores config persistence flag " + feature);
            }
            Need(diskBefore.SequenceEqual(File.ReadAllBytes(addon.Config.ConfigFilePath)), "F1 multiplayer API toggles and numeric changes remain session-only");
            // Simulate the API's host-following flag without touching transport.
            // These Atmosphere features are local-editable under the MFD policy;
            // host-owned Weather and Nuclear fields must remain protected.
            following.SetValue(coordinator, typeof(MetDriver).GetProperty(nameof(F1FollowingHost), F));
            addon.Config.SaveOnConfigSet = false;
            addon.Config[new ConfigDefinition("Multiplayer", "FollowHostSettings")].BoxedValue = true;
            Need((bool)Prop(coordinator, "HostOwnsWeather"), "F1 test exercises host-owned weather policy");
            Need(!(bool)Call(coordinator, "CanEdit", "Weather", "Conditions") && !(bool)Call(coordinator, "CanEdit", "Nuclear", "Enabled"), "F1 bridge respects coordinator host-owned weather policy");
            Need(controls.All(x => (bool)Prop(x, "Editable") == (bool)Call(coordinator, "CanEdit", "Atmosphere", Field(x, "Feature"))), "F1 feature editability matches MFD policy while following a host");
            foreach (var control in controls)
            {
                var entry = (ConfigEntryBase)Field(control, "Entry");
                object value = entry.BoxedValue;
                Call(control, "Detach");
                Need(ReferenceEquals(entry.Description, Field(control, "original")), "F1 cleanup restores original metadata exactly " + Field(control, "Feature"));
                Need(!(bool)Prop(control, "Editable") && !(bool)Call(control, "SetEnabled", !(bool)Prop(control, "Enabled")), "stale F1 checkbox cannot edit after detach");
                Need(Equals(value, entry.BoxedValue), "F1 cleanup preserves value " + Field(control, "Feature"));
                Call(control, "Attach");
            }
            var unlock = addonRows.SingleOrDefault(x => ((ConfigEntryBase)Prop(x, "Entry")).Definition.Equals(new ConfigDefinition("Nuclear", "UnlockNukes")));
            Need(unlock == null || !(Prop(unlock, "CustomDrawer") is Action<ConfigEntryBase> custom) || !controls.Contains(custom.Target), "no pack custom control added to gameplay UnlockNukes");
        }
        finally
        {
            addon.Config.SaveOnConfigSet = false;
            for (int i = 0; i < controls.Length; i++) ((ConfigEntryBase)Field(controls[i], "Entry")).BoxedValue = savedValues[i];
            remembered.Clear(); foreach (var pair in rememberedBefore) remembered.Add(pair.Key, pair.Value);
            addon.Config[new ConfigDefinition("Multiplayer", "FollowHostSettings")].BoxedValue = originalFollowHost;
            following.SetValue(coordinator, originalFollowing);
            addon.Config.SaveOnConfigSet = saveBefore;
            stateBacking.SetValue(null, stateBefore);
        }
        Need(diskBefore.SequenceEqual(File.ReadAllBytes(addon.Config.ConfigFilePath)), "F1 feature audit preserves original config bytes");
        return features.Length;
    }

    private static bool F1FollowingHost => true;

    [Serializable] private sealed class F1RenderAudit
    {
        public string status = "FAIL";
        public string reason;
        public int onGuiEvents;
        public int repaintEvents;
        public int drawerInvocations;
        public int customRows;
    }

    private async UniTask TestF1CustomDrawerRendering(object manager, object[] rows)
    {
        var fixture = new GameObject("F1 isolated custom drawer rendering probe").AddComponent<F1DrawerRenderProbe>();
        var audit = new F1RenderAudit { reason = "Rendering fixture did not complete." };
        try
        {
            var fieldDrawer = Field(manager, "_fieldDrawer");
            var draw = fieldDrawer.GetType().GetMethod("DrawSettingValue", F);
            var customRows = rows.Where(x => Prop(x, "CustomDrawer") is Action<ConfigEntryBase> d && d.Target?.GetType().FullName == "UnifiedEffects.NoAtmoConfigurationDrawers+FeatureControl").ToArray();
            audit.customRows = customRows.Length;
            Need(customRows.Length == 10, "F1 rendering fixture routes all ten numeric feature checkbox rows");
            fixture.Render = () => {
                foreach (object row in customRows)
                {
                    GUI.enabled = true;
                    draw.Invoke(fieldDrawer, new[] { row });
                    audit.drawerInvocations++;
                    Need(GUI.enabled, "actual F1 drawer restores enabled GUI state");
                    GUI.enabled = false;
                    draw.Invoke(fieldDrawer, new[] { row });
                    audit.drawerInvocations++;
                    Need(!GUI.enabled, "actual F1 drawer preserves disabled GUI state");
                }
            };
            for (int i = 0; i < 120 && !fixture.Finished; i++) await UniTask.Yield();
            audit.onGuiEvents = fixture.OnGuiEvents;
            audit.repaintEvents = fixture.RepaintEvents;
            if (fixture.Error != null)
            {
                audit.reason = fixture.Error;
                Need(false, "actual F1 custom drawer raised a rendering error: " + fixture.Error);
            }
            if (fixture.OnGuiEvents == 0)
            {
                audit.status = "OPEN";
                audit.reason = "Hidden isolated window received no OnGUI events in 120 frames. Discovery, configuration identity and checkbox callbacks were tested; visible F1 drawing still needs an interactive check.";
                Note("OPEN F1 custom checkbox rendering: " + audit.reason);
                return;
            }
            audit.reason = fixture.Finished && fixture.RepaintEvents > 0 ? "Actual Configuration Manager FieldDrawer completed its repaint without errors." : "OnGUI ran but no complete repaint arrived within 120 frames.";
            Need(fixture.Finished && fixture.RepaintEvents > 0, "actual F1 custom drawer completed repaint after OnGUI events: " + audit.reason);
            audit.status = "PASS";
        }
        finally
        {
            if (fixture != null)
            {
                audit.onGuiEvents = fixture.OnGuiEvents;
                audit.repaintEvents = fixture.RepaintEvents;
                Destroy(fixture.gameObject);
            }
            File.WriteAllText(Path.Combine(dir, "f1-render-status.json"), JsonUtility.ToJson(audit, true));
        }
    }
}

public sealed class F1DrawerRenderProbe : MonoBehaviour
{
    internal Action Render;
    internal bool Finished;
    internal string Error;
    internal int OnGuiEvents, RepaintEvents;
    private void OnGUI()
    {
        OnGuiEvents++;
        if (Event.current.type == EventType.Repaint) RepaintEvents++;
        if (Finished || Render == null) return;
        bool enabled = GUI.enabled;
        try { Render(); }
        catch (Exception error) { Error = error.ToString(); }
        finally { GUI.enabled = enabled; Finished = Error != null || Event.current.type == EventType.Repaint; }
    }
}
