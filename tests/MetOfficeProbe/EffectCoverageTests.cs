using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed partial class MetDriver
{
    private const string CoverageAtmo = "paul.no.clouds";
    [Serializable] private sealed class EffectCoverageRow
    {
        public string owner, section, key, settingType, defaultValue, value, classification, reason;
        public int mfdIndex = -1;
        public string mfdKind, mfdLabel, mfdBinding, f1Status;
        public bool sameOriginalConfigEntry, f1RowFound, f1Visible, f1SameOriginalEntry, f1Checkbox;
    }
    [Serializable] private sealed class MfdCoverageRow
    {
        public int index, category;
        public string owner, section, key, kind, label, bindingSection, bindingKey, settingType, status;
        public bool sameOriginalConfigEntry, apiAgrees, f1RowFound, f1Visible, f1SameOriginalEntry, f1Checkbox;
        public bool reachable, interactable, toggleInvoked, toggleChangedOriginal, toggleRestored;
        public string renderedLabel, toggleStatus;
    }
    [Serializable] private sealed class EffectCoverageManifest
    {
        public string capturedUtc, rootVersion, rootSha256, noAtmoVersion, f1Version;
        public bool f1Installed, noAtmoInstalled;
        public string scope = "Exact loaded ConfigEntry identities and actual menu models. This does not prove rendered appearance or physical input.";
        public int booleanCount, mappedBooleanCount, classifiedNonEffectCount, mfdRowCount;
        public EffectCoverageRow[] booleans;
        public MfdCoverageRow[] mfdRows;
        public string[] failures;
    }
    private sealed class CoverageExclusion
    {
        internal readonly string Kind, Reason;
        internal CoverageExclusion(string kind, string reason) { Kind = kind; Reason = reason; }
    }

    // Explicit reviewed identities, not name-pattern exemptions. Keep in step with
    // evidence/toggle-audit-20261111/non-effect-boolean-classifications.json.
    private static Dictionary<string, CoverageExclusion> CoverageExclusions()
    {
        var result = new Dictionary<string, CoverageExclusion>();
        void Add(string owner, string section, string key, string kind, string reason) =>
            result.Add(CoverageId(owner, section, key), new CoverageExclusion(kind, reason));
        Add("aaron.boscali.betternight", "1. General", "Diagnostics Logging", "diagnostic", "Logging only.");
        Add("aaron.boscali.betternight", "8. Street Lights - Range & Destruction", "Destructible", "intentionally-disabled-gameplay", "Prepared StreetLights.get_Destruct returns false; pole physics is disabled.");
        Add("aaron.boscali.krop-unified", "00 General", "Diagnostic logging", "diagnostic", "Startup and unsupported-host logging only.");
        foreach (string key in new[] { "Contrails", "DustWakes" })
            Add("aaron.boscali.unified", "Immersion", key, "suppressed-duplicate", "Bundled KROP suppresses the Boscali duplicate through private-registry lookup; the KROP replacement has its own control.");
        Add(OwnerId, "Weather", "TapeMoves", "weather-controller-policy", "Weather evolution policy; not a separate cosmetic effect.");
        foreach (string key in new[] { "CinematicCloudsEnabled", "CloudHalfResolution", "CloudTemporalUpdate" })
            Add(OwnerId, "Weather", key, "removed-renderer-legacy", "Old volumetric renderer is excluded from the compiled pack.");
        Add(OwnerId, "Weather", "ConsoleKeyRequiresCtrl", "input-preference", "Weather shortcut modifier preference.");
        Add(CoverageAtmo, "General", "HideGameClouds", "renderer-compatibility", "Prevents native-cloud duplication while volumetric rendering is active.");
        Add(CoverageAtmo, "General", "FollowGameWeather", "weather-controller-policy", "Chooses mission weather as an input.");
        Add(CoverageAtmo, "General", "DebugView", "diagnostic", "Cloud-opacity debug display.");
        Add(CoverageAtmo, "General", "AnimatedJitter", "render-quality", "Raymarch noise sampling quality option.");
        Add(CoverageAtmo, "General", "UseSceneDepth", "renderer-compatibility", "Depth clipping against terrain and aircraft.");
        Add(CoverageAtmo, "Multiplayer", "SyncClouds", "multiplayer-policy", "Shared mission-clock synchronization.");
        Add(CoverageAtmo, "Multiplayer", "FollowHostSettings", "multiplayer-policy", "Host settings authority and local restoration policy.");
        Add(CoverageAtmo, "Nuclear", "UnlockNukes", "excluded-gameplay", "Weapon-access bypass is outside the client effects UI.");
        Add(CoverageAtmo, "Quality", "EmptySkip", "render-quality", "Empty-space skipping optimization.");
        Add(CoverageAtmo, "Quality", "MapSlicing", "render-quality", "Weather-map update scheduling optimization.");
        Add(CoverageAtmo, "Quality", "PerformanceMeter", "diagnostic", "GPU timing readout.");
        return result;
    }
    private static string CoverageId(string owner, string section, string key) => owner + "\n" + section + "\n" + key;
    private static bool CoverageHidden(string owner, string section, string key)
    {
        return owner == OwnerId && section == "Weather" && new[] { "CinematicCloudsEnabled", "CloudHalfResolution", "CloudTemporalUpdate" }.Contains(key) ||
            owner == "aaron.boscali.unified" && section == "Immersion" && new[] { "Contrails", "DustWakes" }.Contains(key) ||
            owner == "aaron.boscali.betternight" && (section == "8. Street Lights - Range & Destruction" && key == "Destructible" ||
                section == "2. Night" && key == "Extra Ambient When NVG Active");
    }
    private static Dictionary<string, ConfigDefinition> CoverageFeatureDefinitions()
    {
        var map = new Dictionary<string, ConfigDefinition> {
            { "Clouds", new ConfigDefinition("General", "Enabled") },
            { "Shadows", new ConfigDefinition("Shadows", "Shadows") },
            { "RainShafts", new ConfigDefinition("Atmosphere", "RainShafts") },
            { "WetGlass", new ConfigDefinition("Glass", "WetGlass") },
            { "NuclearClouds", new ConfigDefinition("Nuclear", "Enabled") },
            { "KeepSun", new ConfigDefinition("Lighting", "KeepSunBelowClouds") }
        };
        foreach (string feature in new[] { "NearRain", "Lightning", "Turbulence", "GodRays", "LightShafts", "Fog", "Rainbow", "Glory", "ValleyFog", "MountainCloud" })
            map.Add(feature, new ConfigDefinition("Atmosphere", feature));
        return map;
    }

    // Invoke after the combined MFD has been built and optional NOAtmoFX attached.
    // The final traversal exercises synchronous real button callbacks, restores all
    // original entries and API memory, and verifies that no config was persisted.
    private void TestEffectMenuCoverage()
    {
        var failures = new List<string>();
        void Check(bool passed, string message)
        {
            if (!passed) { failures.Add(message); Note("FAIL COVERAGE " + message); }
        }
        var boolRows = new List<EffectCoverageRow>();
        var mfdRows = new List<MfdCoverageRow>();
        var manifest = new EffectCoverageManifest { capturedUtc = DateTime.UtcNow.ToString("o") };
        var modules = ModuleInfos();
        string[] ownerIds = PrivateIds.Concat(new[] { OwnerId }).ToArray();
        var owners = new Dictionary<string, BaseUnityPlugin>();
        foreach (string id in ownerIds)
        {
            bool exists = modules.TryGetValue(id, out var info) && info.Instance != null;
            Check(exists, "loaded pack configuration owner " + id);
            if (exists) owners.Add(id, info.Instance);
        }
        if (Chainloader.PluginInfos.TryGetValue(CoverageAtmo, out var addonInfo) && addonInfo.Instance != null)
        { owners.Add(CoverageAtmo, addonInfo.Instance); manifest.noAtmoInstalled = true; manifest.noAtmoVersion = addonInfo.Metadata.Version.ToString(); }
        var rootInfo = modules[OwnerId];
        manifest.rootVersion = rootInfo.Metadata.Version.ToString();
        if (File.Exists(rootInfo.Location))
            using (var sha = SHA256.Create()) manifest.rootSha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(rootInfo.Location))).Replace("-", "");
        var pageType = assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.EffectsMfdPage", true);
        object[] items = ((IEnumerable)pageType.GetField("Items", F).GetValue(null)).Cast<object>().ToArray();
        var find = pageType.GetMethod("Find", F);
        assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.EffectsSettingsCache", true).GetMethod("Capture", F).Invoke(null, null);
        var featureMap = CoverageFeatureDefinitions();
        var exclusions = CoverageExclusions();
        object coordinator = assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.CloudModeCoordinator", true).GetField("Live", F).GetValue(null);
        var addonApi = addonInfo?.Instance?.GetType().Assembly.GetType("NoAtmoFX.NoAtmoFXApi", false);
        MethodInfo getFeature = addonApi?.GetMethod("GetFeature");
        if (manifest.noAtmoInstalled)
        {
            Check(coordinator != null && (bool)Prop(coordinator, "AddonReady"), "installed add-on is compatible and connected for feature coverage");
            var official = addonApi?.GetField("Features")?.GetValue(null) as string[];
            Check(official != null && new HashSet<string>(official).SetEquals(featureMap.Keys), "all official NOAtmoFX features have explicit original-config mappings");
        }
        object f1 = null; PropertyInfo openProperty = null, searchProperty = null; bool f1WasOpen = false; string oldSearch = null;
        object[] f1All = Array.Empty<object>();
        var f1Visible = new HashSet<object>();
        object F1Row(BaseUnityPlugin owner, ConfigEntryBase entry)
        {
            var matches = f1All.Where(row => row.GetType().GetProperty("Entry", F) != null &&
                ReferenceEquals(Prop(row, "PluginInstance"), owner) && ReferenceEquals(Prop(row, "Entry"), entry)).ToArray();
            Check(matches.Length <= 1, "F1 contains no duplicate original entry " + owner.Info.Metadata.GUID + "/" + entry.Definition);
            return matches.FirstOrDefault();
        }
        bool Checkbox(object row, ConfigEntryBase entry)
        {
            if (row == null) return false;
            if (entry.SettingType == typeof(bool)) return true;
            var drawer = row.GetType().GetProperty("CustomDrawer", F)?.GetValue(row, null) as Action<ConfigEntryBase>;
            return drawer != null && drawer.Target != null && drawer.Target.GetType().FullName == "UnifiedEffects.NoAtmoConfigurationDrawers+FeatureControl" &&
                ReferenceEquals(Field(drawer.Target, "Entry"), entry);
        }
        try
        {
            if (Chainloader.PluginInfos.TryGetValue("com.bepis.bepinex.configurationmanager", out var cm) && cm.Instance != null)
            {
                f1 = cm.Instance; manifest.f1Installed = true; manifest.f1Version = cm.Metadata.Version.ToString();
                openProperty = f1.GetType().GetProperty("DisplayingWindow", F); searchProperty = f1.GetType().GetProperty("SearchString", F);
                f1WasOpen = (bool)openProperty.GetValue(f1, null); oldSearch = (string)searchProperty.GetValue(f1, null);
                searchProperty.SetValue(f1, "", null); openProperty.SetValue(f1, true, null); Call(f1, "BuildSettingList");
                f1All = ((IEnumerable)Field(f1, "_allSettings")).Cast<object>().ToArray();
                foreach (object group in (IEnumerable)Field(f1, "_filteredSetings"))
                    foreach (object category in (IEnumerable)Field(group, "Categories"))
                        foreach (object row in (IEnumerable)Field(category, "Settings")) f1Visible.Add(row);
            }
            for (int i = 0; i < items.Length; i++)
            {
                object item = items[i];
                var row = new MfdCoverageRow { index = i, category = (int)Field(item, "category"), owner = (string)Field(item, "id"),
                    section = (string)Field(item, "section"), key = (string)Field(item, "key"), kind = (string)Field(item, "kind"), label = (string)Field(item, "label") };
                mfdRows.Add(row); Check(row.category >= 0 && row.category <= 7, "MFD row has reachable category " + row.label);
                if (!owners.TryGetValue(row.owner, out var owner))
                { row.status = "optional-add-on-absent"; Check(row.owner == CoverageAtmo && !manifest.noAtmoInstalled, "only optional add-on may have unavailable MFD binding " + row.label); continue; }
                ConfigDefinition definition;
                if (row.kind == "Feature")
                {
                    if (!featureMap.TryGetValue(row.key, out definition)) { row.status = "unmapped-feature"; Check(false, "unknown MFD feature " + row.key); continue; }
                }
                else definition = (ConfigDefinition)Field(item, "definition");
                row.bindingSection = definition.Section; row.bindingKey = definition.Key;
                ConfigEntryBase entry = owner.Config.ContainsKey(definition) ? owner.Config[definition] : null;
                Check(entry != null, "MFD row resolves actual bound entry " + row.owner + "/" + definition);
                if (entry == null) { row.status = "missing-config-entry"; continue; }
                row.settingType = entry.SettingType.FullName;
                if (row.kind == "Feature")
                {
                    row.sameOriginalConfigEntry = ReferenceEquals(owner.Config[definition], entry);
                    row.apiAgrees = getFeature != null && coordinator != null &&
                        (bool)getFeature.Invoke(null, new object[] { row.key }) == (bool)Call(coordinator, "Feature", row.key);
                    if (entry.SettingType == typeof(bool)) row.apiAgrees &= (bool)entry.BoxedValue == (bool)getFeature.Invoke(null, new object[] { row.key });
                    Check(row.apiAgrees, "MFD feature agrees with official API and native Boolean entry " + row.key);
                }
                else
                {
                    row.sameOriginalConfigEntry = ReferenceEquals(find.Invoke(null, new[] { item }), entry);
                    bool typeValid = row.kind == "Toggle" ? entry.SettingType == typeof(bool) : row.kind == "Slider" ? entry.SettingType == typeof(float) :
                        new[] { "IntSlider", "Phosphor", "Front", "Downscale" }.Contains(row.kind) ? entry.SettingType == typeof(int) :
                        row.kind == "Preset" ? entry.SettingType == typeof(string) : row.kind == "EnumChoice" && entry.SettingType.IsEnum;
                    Check(typeValid, "MFD kind matches bound setting type " + row.label);
                }
                Check(row.sameOriginalConfigEntry, "MFD reads original ConfigEntry " + row.label);
                row.status = row.kind == "Feature" ? "official-feature-api" : "original-config-entry";
                if (manifest.f1Installed)
                {
                    object f1Row = F1Row(owner, entry); row.f1RowFound = f1Row != null; row.f1Visible = f1Row != null && f1Visible.Contains(f1Row);
                    row.f1SameOriginalEntry = f1Row != null && ReferenceEquals(Prop(f1Row, "Entry"), entry); row.f1Checkbox = Checkbox(f1Row, entry);
                    Check(row.f1SameOriginalEntry, "F1 and MFD share original entry " + row.label);
                    if (row.kind == "Toggle" || row.kind == "Feature")
                        Check(row.f1Visible && row.f1Checkbox, "F1 exposes visible checkbox for MFD effect " + row.label);
                }
            }
            foreach (var pair in owners.OrderBy(x => x.Key))
            {
                foreach (var entry in pair.Value.Config.Select(x => x.Value).Where(x => x.SettingType == typeof(bool)).OrderBy(x => x.Definition.Section).ThenBy(x => x.Definition.Key))
                {
                    var row = new EffectCoverageRow { owner = pair.Key, section = entry.Definition.Section, key = entry.Definition.Key,
                        settingType = entry.SettingType.FullName, defaultValue = Convert.ToString(entry.DefaultValue), value = Convert.ToString(entry.BoxedValue) };
                    boolRows.Add(row);
                    var control = mfdRows.FirstOrDefault(x => x.owner == row.owner && x.bindingSection == row.section && x.bindingKey == row.key && (x.kind == "Toggle" || x.kind == "Feature"));
                    if (control != null)
                    {
                        row.mfdIndex = control.index; row.mfdKind = control.kind; row.mfdLabel = control.label; row.mfdBinding = control.status;
                        row.sameOriginalConfigEntry = control.sameOriginalConfigEntry; row.classification = "effect-menu-control";
                    }
                    else if (row.owner == OwnerId && row.section == "MET OFFICE" && row.key == "Simple weather names")
                    {
                        row.classification = "dedicated-weather-ui-control"; row.mfdBinding = "Simple English / Technical buttons";
                        var simple = assembly.GetType("BoscaliUnifiedWeather.WeatherPlugin", true).GetField("SimpleNames", F)?.GetValue(null) ??
                            assembly.GetType("BoscaliUnifiedWeather.WeatherPlugin", true).GetProperty("SimpleNames", F)?.GetValue(null, null);
                        row.sameOriginalConfigEntry = ReferenceEquals(simple, entry);
                        var launcher = ((Component)manager).GetComponent(assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.MetOfficeMfdLauncher", true));
                        Check(launcher != null && Field(launcher, "simpleButton") != null && Field(launcher, "technicalButton") != null && row.sameOriginalConfigEntry,
                            "dedicated naming controls use original live config");
                    }
                    else if (exclusions.TryGetValue(CoverageId(row.owner, row.section, row.key), out var excluded))
                    { row.classification = excluded.Kind; row.reason = excluded.Reason; }
                    else { row.classification = "UNCLASSIFIED"; Check(false, "Boolean has no MFD toggle or explicit exclusion " + row.owner + "/" + row.section + "/" + row.key); }
                    if (manifest.f1Installed)
                    {
                        object f1Row = F1Row(pair.Value, entry); row.f1RowFound = f1Row != null; row.f1Visible = f1Row != null && f1Visible.Contains(f1Row);
                        row.f1SameOriginalEntry = f1Row != null && ReferenceEquals(Prop(f1Row, "Entry"), entry); row.f1Checkbox = Checkbox(f1Row, entry);
                        bool hidden = CoverageHidden(row.owner, row.section, row.key);
                        row.f1Status = hidden ? "intentionally-hidden-inert-setting" : row.f1Visible ? "visible-original-entry" : row.f1RowFound ? "filtered-original-entry" : "missing";
                        if (hidden) Check(!row.f1RowFound && !row.f1Visible, "inert legacy entry is intentionally absent from F1 " + row.key);
                        else Check(row.f1SameOriginalEntry, "F1 reaches original bound Boolean " + row.owner + "/" + row.key);
                        if (control != null || row.classification == "dedicated-weather-ui-control")
                            Check(row.f1Visible && row.f1Checkbox, "included Boolean effect has visible F1 checkbox " + row.owner + "/" + row.key);
                    }
                    else row.f1Status = "optional-configuration-manager-not-installed";
                }
            }
            // Shared masters intentionally appear in more than one category.
            // Within each category a control must still be unique and every alias
            // above must resolve the same original ConfigEntry.
            foreach (var group in mfdRows.GroupBy(x => x.category + "\n" + CoverageId(x.owner, x.section, x.key) + "\n" + x.kind))
                Check(group.Count() == 1, "MFD category has unique control identity " + group.Key.Replace('\n', '/'));
            // F1's real model was inspected above; close it during MFD traversal.
            if (f1 != null) openProperty.SetValue(f1, false, null);
            TraverseEffectCoverageMenus(owners, items, mfdRows, coordinator, getFeature, Check);
        }
        catch (Exception error) { failures.Add("Coverage inspection exception: " + error); }
        finally
        {
            if (f1 != null && openProperty != null) openProperty.SetValue(f1, f1WasOpen, null);
            if (f1 != null && searchProperty != null) searchProperty.SetValue(f1, oldSearch, null);
            manifest.booleans = boolRows.ToArray(); manifest.mfdRows = mfdRows.ToArray(); manifest.failures = failures.ToArray();
            manifest.booleanCount = boolRows.Count; manifest.mfdRowCount = mfdRows.Count;
            manifest.mappedBooleanCount = boolRows.Count(x => x.classification == "effect-menu-control" || x.classification == "dedicated-weather-ui-control");
            manifest.classifiedNonEffectCount = boolRows.Count(x => x.classification != "UNCLASSIFIED" && x.classification != "effect-menu-control" && x.classification != "dedicated-weather-ui-control");
            WriteEffectCoverageManifest(manifest, failures, Check);
        }
        Need(failures.Count == 0, "complete exact-runtime effect coverage: " + manifest.booleanCount + " booleans, " + manifest.mfdRowCount + " MFD rows, no unclassified effects");
    }

    private void WriteEffectCoverageManifest(EffectCoverageManifest manifest, List<string> failures, Action<bool, string> check)
    {
        // Unity JsonUtility silently omits arrays of private nested record types.
        // Use the game's shipped JSON serializer without adding a package/runtime
        // dependency, then deserialize the actual file and compare every field.
        var jsonType = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true);
        var serialize = jsonType.GetMethod("SerializeObject", new[] { typeof(object) });
        var deserialize = jsonType.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) });
        string path = Path.Combine(dir, "effect-menu-coverage.json");
        string Json() => (string)serialize.Invoke(null, new object[] { manifest });
        File.WriteAllText(path, Json());
        try
        {
            var read = (EffectCoverageManifest)deserialize.Invoke(null, new object[] { File.ReadAllText(path), typeof(EffectCoverageManifest) });
            check(read != null, "coverage manifest deserializes from the written file");
            if (read != null)
            {
                check(read.booleans != null && read.booleans.Length == manifest.booleans.Length && read.booleans.Length == read.booleanCount,
                    "coverage JSON contains every Boolean record, not just its total");
                check(read.mfdRows != null && read.mfdRows.Length == manifest.mfdRows.Length && read.mfdRows.Length == read.mfdRowCount,
                    "coverage JSON contains every MFD row, not just its total");
                bool EqualRecord(object expected, object actual)
                {
                    return actual != null && expected.GetType() == actual.GetType() && expected.GetType()
                        .GetFields(BindingFlags.Public | BindingFlags.Instance).All(field => Equals(field.GetValue(expected), field.GetValue(actual)));
                }
                if (read.booleans != null)
                    for (int i = 0; i < Math.Min(read.booleans.Length, manifest.booleans.Length); i++)
                        check(EqualRecord(manifest.booleans[i], read.booleans[i]), "coverage Boolean JSON preserves all identity, binding and status fields at row " + i);
                if (read.mfdRows != null)
                    for (int i = 0; i < Math.Min(read.mfdRows.Length, manifest.mfdRows.Length); i++)
                        check(EqualRecord(manifest.mfdRows[i], read.mfdRows[i]), "coverage MFD JSON preserves all identity, click and restoration fields at row " + i);
                check(read.failures != null && read.failures.SequenceEqual(manifest.failures), "coverage JSON preserves failure details");
                check(read.rootSha256 == manifest.rootSha256 && read.rootVersion == manifest.rootVersion &&
                    read.f1Installed == manifest.f1Installed && read.noAtmoInstalled == manifest.noAtmoInstalled,
                    "coverage JSON preserves exact artifact and dependency identity");
            }
        }
        catch (Exception error) { check(false, "coverage JSON roundtrip failed: " + error.GetBaseException().Message); }
        // Include any serialization-audit failure in the evidence itself.
        if (failures.Count != manifest.failures.Length)
        { manifest.failures = failures.ToArray(); File.WriteAllText(path, Json()); }
    }

    private void TraverseEffectCoverageMenus(Dictionary<string, BaseUnityPlugin> owners, object[] items,
        List<MfdCoverageRow> rows, object coordinator, MethodInfo getFeature, Action<bool, string> check)
    {
        var launcher = ((Component)manager).GetComponent(assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.MetOfficeMfdLauncher", true));
        check(launcher != null, "real MFD launcher exists for complete page traversal");
        if (launcher == null) return;
        object effects = Field(launcher, "effects"), clouds = Field(launcher, "cloudEffects");
        check(effects != null && clouds != null, "both real effects page controllers were built");
        if (effects == null || clouds == null) return;
        var configs = owners.Values.Select(x => x.Config).Distinct().ToArray();
        var originalSave = configs.ToDictionary(x => x, x => x.SaveOnConfigSet);
        var originalValues = configs.SelectMany(x => x.Select(p => p.Value)).Distinct().ToDictionary(x => x, x => x.BoxedValue);
        var disk = configs.Where(x => File.Exists(x.ConfigFilePath)).ToDictionary(x => x.ConfigFilePath, x => File.ReadAllBytes(x.ConfigFilePath));
        var uiValues = new Dictionary<string, object> {
            { "showEffects", Field(launcher, "showEffects") }, { "showCloudSettings", Field(launcher, "showCloudSettings") }
        };
        int categoryBefore = (int)Field(effects, "category"), pageBefore = (int)Field(effects, "page"), cloudPageBefore = (int)Field(clouds, "page");
        var stateField = typeof(GameManager).GetField("<gameState>k__BackingField", F);
        GameState gameStateBefore = GameManager.gameState;
        var coordinatorValues = new Dictionary<string, object>();
        if (coordinator != null)
            foreach (string field in new[] { "initialEnabled", "hasWrite", "lastWritten", "writing" }) coordinatorValues.Add(field, Field(coordinator, field));
        IDictionary remembered = null; Dictionary<object, object> rememberedBefore = null;
        if (owners.TryGetValue(CoverageAtmo, out var addon))
        {
            remembered = Field(addon, "_lastOn") as IDictionary;
            if (remembered != null) rememberedBefore = remembered.Keys.Cast<object>().ToDictionary(x => x, x => remembered[x]);
        }
        // Only synchronous UI callbacks run while this sentinel is set. It selects
        // the existing no-persist API route; no network loop or Unity frame runs.
        void SessionCallback(Action action)
        {
            try { stateField.SetValue(null, GameState.Multiplayer); action(); }
            finally { stateField.SetValue(null, gameStateBefore); }
        }
        try
        {
            foreach (var config in configs) config.SaveOnConfigSet = false;
            foreach (int category in rows.Select(x => x.category).Distinct().OrderBy(x => x))
            {
                bool cloudCategory = category == 7;
                object page = cloudCategory ? clouds : effects;
                if (cloudCategory)
                {
                    if (coordinator == null || !(bool)Prop(coordinator, "AddonReady"))
                    { foreach (var row in rows.Where(x => x.category == category)) row.toggleStatus = "optional-add-on-unavailable"; continue; }
                    ((Button)Field(launcher, "weatherTab")).onClick.Invoke();
                    SessionCallback(() => ((Button[])Field(launcher, "modes"))[2].onClick.Invoke());
                    ((Button)Field(launcher, "cloudButton")).onClick.Invoke();
                    while ((int)Field(page, "page") > 0) ((Button)Field(page, "previous")).onClick.Invoke();
                }
                else
                {
                    ((Button)Field(launcher, "effectsTab")).onClick.Invoke();
                    ((Button[])Field(page, "categories"))[category].onClick.Invoke();
                }
                var categoryRows = rows.Where(x => x.category == category).ToArray();
                for (int slot = 0; slot < categoryRows.Length; slot++)
                {
                    var row = categoryRows[slot];
                    while ((int)Field(page, "page") < slot / 9) ((Button)Field(page, "next")).onClick.Invoke();
                    var visible = ((IEnumerable)Field(page, "visible")).Cast<object>().ToArray();
                    var button = ((Button[])Field(page, "rows"))[slot % 9];
                    var text = button.GetComponentInChildren<TMP_Text>(true);
                    row.renderedLabel = text != null ? text.text : "";
                    var rect = (RectTransform)button.transform;
                    row.reachable = visible.Length > slot && ReferenceEquals(visible[slot], items[row.index]) && button.gameObject.activeInHierarchy && rect.rect.width > 0f && rect.rect.height > 0f;
                    row.interactable = button.interactable;
                    check(row.reachable && row.renderedLabel.Contains(row.label), "actual MFD row reachable with correct live label " + row.label);
                    if (row.kind != "Toggle" && row.kind != "Feature") { row.toggleStatus = "numeric-or-choice-row-reachable"; continue; }
                    if (!row.interactable)
                    {
                        bool locked = cloudCategory && coordinator != null && !(bool)Call(coordinator, "CanEdit", row.section, row.key);
                        row.toggleStatus = locked ? "host-policy-locked" : "unexpected-disabled-toggle";
                        check(locked, "only host-owned cloud settings may disable a loaded effect toggle " + row.label); continue;
                    }
                    var owner = owners[row.owner];
                    var entry = owner.Config[new ConfigDefinition(row.bindingSection, row.bindingKey)];
                    object previous = entry.BoxedValue;
                    bool apiBefore = row.kind == "Feature" && (bool)getFeature.Invoke(null, new object[] { row.key });
                    try
                    {
                        if (cloudCategory) SessionCallback(() => button.onClick.Invoke()); else button.onClick.Invoke();
                        row.toggleInvoked = true;
                        row.toggleChangedOriginal = row.kind == "Feature"
                            ? (bool)getFeature.Invoke(null, new object[] { row.key }) != apiBefore && !Equals(entry.BoxedValue, previous)
                            : entry.BoxedValue is bool current && current == !(bool)previous;
                        check(row.toggleChangedOriginal, "actual MFD click changes original entry/API " + row.label);
                        if (cloudCategory) SessionCallback(() => button.onClick.Invoke()); else button.onClick.Invoke();
                        row.toggleRestored = row.kind == "Feature"
                            ? (bool)getFeature.Invoke(null, new object[] { row.key }) == apiBefore
                            : Equals(entry.BoxedValue, previous);
                        check(row.toggleRestored, "second actual MFD click restores original effect state " + row.label);
                        row.toggleStatus = row.toggleChangedOriginal && row.toggleRestored ? "clicked-and-restored" : "callback-failed";
                    }
                    finally { entry.BoxedValue = previous; Call(page, "Refresh"); }
                }
            }
        }
        finally
        {
            stateField.SetValue(null, gameStateBefore);
            foreach (var saved in originalValues) if (!Equals(saved.Key.BoxedValue, saved.Value)) saved.Key.BoxedValue = saved.Value;
            if (remembered != null && rememberedBefore != null)
            { remembered.Clear(); foreach (var saved in rememberedBefore) remembered.Add(saved.Key, saved.Value); }
            foreach (var saved in coordinatorValues) FI(coordinator, saved.Key).SetValue(coordinator, saved.Value);
            foreach (var saved in uiValues) FI(launcher, saved.Key).SetValue(launcher, saved.Value);
            FI(effects, "category").SetValue(effects, categoryBefore); FI(effects, "page").SetValue(effects, pageBefore); FI(clouds, "page").SetValue(clouds, cloudPageBefore);
            foreach (var config in configs) config.SaveOnConfigSet = originalSave[config];
            Call(launcher, "RefreshTabs"); Call(launcher, "Refresh"); Call(effects, "Refresh"); Call(clouds, "Refresh");
            foreach (var saved in originalValues) check(Equals(saved.Key.BoxedValue, saved.Value), "coverage traversal restored setting " + saved.Key.Definition);
            foreach (var saved in disk) check(File.ReadAllBytes(saved.Key).SequenceEqual(saved.Value), "coverage traversal did not persist config changes " + Path.GetFileName(saved.Key));
            foreach (var config in configs) check(config.SaveOnConfigSet == originalSave[config], "coverage traversal restored save policy " + Path.GetFileName(config.ConfigFilePath));
        }
    }
}
