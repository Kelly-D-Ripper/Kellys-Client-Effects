using System;
using System.Collections;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class MetDriver
{
    private static bool IsKropOptionalItem(object item)
    {
        string section = (string)Field(item, "section"), key = (string)Field(item, "key");
        return section == "08 Additional audio" || section == "08 Visual refinements" ||
            key == "Canopy-aware muffling" || key == "Open-cabin muffling" || key == "KROP crack shaping" ||
            key == "Avoid repeated samples" || key == "KROP missile recordings";
    }

    private void TestKropOptionalUi(object effects, Component launcher)
    {
        object[] all = ((IEnumerable)effects.GetType().GetField("Items", F).GetValue(null)).Cast<object>().ToArray();
        object[] additions = all.Where(IsKropOptionalItem).ToArray();
        var categories = (Button[])Field(effects, "categories");
        var rows = (Button[])Field(effects, "rows");
        var next = (Button)Field(effects, "next");
        var plus = (Button[])Field(effects, "plus");
        var minus = (Button[])Field(effects, "minus");
        var find = effects.GetType().GetMethod("Find", F);
        Need(additions.Length == 32, "all 29 optional switches and three visual tuning controls are listed in MFD");
        try
        {
            foreach (object item in additions)
            {
                string key = (string)Field(item, "key");
                int cat = (int)Field(item, "category");
                object[] siblings = all.Where(x => (int)Field(x, "category") == cat).ToArray();
                int ordinal = Array.IndexOf(siblings, item), targetPage = ordinal / rows.Length, row = ordinal % rows.Length;
                categories[cat].onClick.Invoke();
                for (int p = 0; p < targetPage; p++) next.onClick.Invoke();
                Need((int)Field(effects, "page") == targetPage, "MFD navigates to " + key);
                ConfigEntryBase entry = (ConfigEntryBase)find.Invoke(null, new[] { item });
                Need(entry != null && rows[row].interactable && rows[row].gameObject.activeInHierarchy, "MFD setting available " + key);
                object original = entry.BoxedValue;
                try
                {
                    if (entry.SettingType == typeof(bool))
                    {
                        Need(!(bool)entry.DefaultValue && !(bool)original, "optional feature defaults off " + key);
                        rows[row].onClick.Invoke();
                        Need((bool)entry.BoxedValue, "MFD enables " + key);
                        Need(rows[row].GetComponentInChildren<TMP_Text>(true).text.Contains("[ X ]"), "MFD shows enabled state " + key);
                        rows[row].onClick.Invoke();
                        Need(!(bool)entry.BoxedValue, "MFD disables " + key);
                    }
                    else
                    {
                        float before = (float)original;
                        plus[row].onClick.Invoke();
                        Need((float)entry.BoxedValue > before, "MFD increments " + key);
                        minus[row].onClick.Invoke();
                        Need(Mathf.Abs((float)entry.BoxedValue - before) < .01f, "MFD decrements " + key);
                    }
                    Canvas.ForceUpdateCanvases();
                    var rect = (RectTransform)rows[row].transform;
                    Need(rect.rect.width >= 300 && rect.rect.height >= 15, "MFD usable row bounds " + key);
                    TMP_Text text = rows[row].GetComponentInChildren<TMP_Text>(true);
                    text.ForceMeshUpdate();
                    Need(!text.isTextOverflowing, "MFD label fits " + key);
                }
                finally { entry.BoxedValue = original; Call(effects, "Refresh"); }
            }
            categories[5].onClick.Invoke();
            for (int i = 0; i < 3; i++) next.onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            CaptureKropPanel(launcher, "krop-additional-audio-ui.png");
            categories[6].onClick.Invoke();next.onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            CaptureKropPanel(launcher, "krop-visual-options-ui.png");
            TestMissileRecordingOptions();
        }
        finally { categories[0].onClick.Invoke(); Call(effects, "Refresh"); }
    }

    private void CaptureKropPanel(Component launcher, string name)
    {
        var installer = Field(launcher, "installer");
        var panel = ((MFDScreen)Field(installer, "Screen")).displayPanel.transform;
        var host = new GameObject("Optional effects review host", typeof(RectTransform), typeof(Canvas));
        try
        {
            host.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var root = new GameObject("Optional effects review page", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(host.transform, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = ((RectTransform)panel).rect.size;
            var nested = root.gameObject.AddComponent<Canvas>();
            nested.overrideSorting = true; nested.sortingOrder = 30004;
            foreach (Transform child in panel) Instantiate(child.gameObject, root, false);
            Canvas.ForceUpdateCanvases();
            Capture(name, true, root);
        }
        finally { DestroyImmediate(host); }
    }

    private void TestMissileRecordingOptions()
    {
        var module = ModuleInfos()["aaron.boscali.missile-unified"].Instance;
        var config = module.Config;
        var enabled = (ConfigEntry<bool>)config[new ConfigDefinition("Audio", "Missile sonic boom")];
        var custom = (ConfigEntry<bool>)config[new ConfigDefinition("Audio", "KROP missile recordings")];
        var type = module.GetType().Assembly.GetType("Boscali.ExperimentalMissileBoom.MissileBoomRuntime", true);
        var runtime = Resources.FindObjectsOfTypeAll(type).Cast<Component>().Single();
        object cache = Field(runtime, "recordings");
        bool oldEnabled = enabled.Value, oldCustom = custom.Value;
        try
        {
            Need(!custom.Value && !(bool)custom.DefaultValue, "missile custom recordings default off");
            UnityEngine.Random.State state = UnityEngine.Random.state;
            custom.Value = true;
            string previous = null;
            for (int i = 0; i < 12; i++)
            {
                AudioClip clip = (AudioClip)Call(cache, "Next", Field(runtime, "log"));
                Need(clip != null && clip.length > .1f && clip.channels == 2, "embedded missile recording decodes " + i);
                Need(previous == null || previous != clip.name, "missile recordings do not repeat consecutively " + i);
                previous = clip.name;
            }
            Need(((AudioClip[])Field(cache, "clips")).Length == 3, "all three original missile recordings loaded");
            Need(state.Equals(UnityEngine.Random.state), "missile recording selection does not consume game random state");
            enabled.Value = true;
            Vector3 position = camera != null ? camera.transform.position : Vector3.zero;
            Need((bool)Call(runtime, "Play", position, position + Vector3.forward * 100f, 100f), "custom missile recording playback available");
            var owned = ((IEnumerable)Field(runtime, "audioObjects")).Cast<GameObject>().ToArray();
            Need(owned.Length == 1 && owned[0].GetComponent<AudioSource>().clip.name.StartsWith("Unified KROP"), "custom missile sample selected by runtime");
            enabled.Value = false;Call(runtime, "Tick");
            Need(((IList)Field(runtime, "audioObjects")).Count == 0 && owned.All(x => x == null || !x.GetComponent<AudioSource>().isPlaying), "missile disable stops all owned audio immediately");
            Need(Field(cache, "clips") == null, "missile disable releases loaded recordings");
            custom.Value = true;
            FI(cache, "attempted").SetValue(cache, true);
            FI(cache, "clips").SetValue(cache, new AudioClip[0]);
            Need((bool)Call(runtime, "Play", position, position + Vector3.forward * 100f, 100f), "native missile sample fallback works when custom clips unavailable");
            owned = ((IEnumerable)Field(runtime, "audioObjects")).Cast<GameObject>().ToArray();
            Need(owned.Length == 1 && owned[0].GetComponent<AudioSource>().clip == GameAssets.i.sonicBoom, "fallback uses native recording");
        }
        finally
        {
            Call(runtime, "Clear");
            enabled.Value = oldEnabled;custom.Value = oldCustom;
        }
    }
}
