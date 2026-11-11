using System;
using System.Collections;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class MetDriver
{
    private void TestKropNightAudioUi(object effects, Component launcher)
    {
        object[] all = ((IEnumerable)effects.GetType().GetField("Items", F).GetValue(null)).Cast<object>().ToArray();
        object[] additions = all.Where(x => (string)Field(x, "section") == "09 Night vision" ||
            (string)Field(x, "section") == "09 Sound propagation").ToArray();
        Need(additions.Length == 18, "all optional NVG and sound propagation controls are listed in MFD");
        Need(additions.GroupBy(x => (string)Field(x, "section") + "/" + (string)Field(x, "key")).All(g => g.Count() == 1),
            "optional NVG and sound controls are not duplicated");
        var categories = (Button[])Field(effects, "categories");
        var rows = (Button[])Field(effects, "rows");
        var plus = (Button[])Field(effects, "plus");
        var minus = (Button[])Field(effects, "minus");
        var next = (Button)Field(effects, "next");
        var find = effects.GetType().GetMethod("Find", F);
        var controls = ModuleInfos()["aaron.boscali.control-unified"].Instance.GetType().Assembly;
        var panelType = controls.GetType("ClientEffectsControlPanel.Panel", true);
        var panels = Resources.FindObjectsOfTypeAll(panelType);
        Need(panels.Length == 1, "one fallback effects panel for optional NVG and sound controls");
        var panel = panels[0];
        Call(panel, "CaptureAvailable");
        var panelFind = panelType.GetMethod("Find", F);
        Need(panelFind != null && panelFind.IsGenericMethodDefinition, "fallback typed setting lookup remains available");
        try
        {
            foreach (object item in additions)
            {
                string section = (string)Field(item, "section"), key = (string)Field(item, "key");
                int cat = (int)Field(item, "category");
                Need(cat == (section == "09 Night vision" ? 2 : 5), "optional control in correct category " + key);
                object[] siblings = all.Where(x => (int)Field(x, "category") == cat).ToArray();
                int ordinal = Array.IndexOf(siblings, item), targetPage = ordinal / rows.Length, row = ordinal % rows.Length;
                categories[cat].onClick.Invoke();
                for (int p = 0; p < targetPage; p++) next.onClick.Invoke();
                Need((int)Field(effects, "page") == targetPage, "optional control reachable " + key);
                var entry = (ConfigEntryBase)find.Invoke(null, new[] { item });
                Need(entry != null && rows[row].interactable && rows[row].gameObject.activeInHierarchy, "optional control available " + key);
                object[] args = { "aaron.boscali.krop-unified", section, key, null };
                bool fallbackFound = (bool)panelFind.MakeGenericMethod(entry.SettingType).Invoke(panel, args);
                Need(fallbackFound && ReferenceEquals(entry, args[3]), "both panels share the same setting " + key);
                object original = entry.BoxedValue;
                try
                {
                    if (entry.SettingType == typeof(bool))
                    {
                        Need(!(bool)entry.DefaultValue && !(bool)original, "new optional toggle defaults off " + key);
                        rows[row].onClick.Invoke();
                        Need((bool)entry.BoxedValue, "MFD enables optional toggle " + key);
                        Need(rows[row].GetComponentInChildren<TMP_Text>(true).text.Contains("[ X ]"), "MFD shows optional toggle state " + key);
                        rows[row].onClick.Invoke();
                        Need(!(bool)entry.BoxedValue, "MFD disables optional toggle " + key);
                    }
                    else if ((string)Field(item, "kind") == "Phosphor")
                    {
                        entry.BoxedValue = 0; Call(effects, "Refresh");
                        plus[row].onClick.Invoke();
                        Need((int)entry.BoxedValue == 1 && rows[row].GetComponentInChildren<TMP_Text>(true).text.StartsWith("WHITE"), "phosphor selector shows white by name");
                        minus[row].onClick.Invoke();
                        Need((int)entry.BoxedValue == 0, "phosphor selector returns to green");
                        minus[row].onClick.Invoke();
                        Need((int)entry.BoxedValue == 2 && rows[row].GetComponentInChildren<TMP_Text>(true).text.StartsWith("AMBER"), "phosphor selector wraps to amber");
                        plus[row].onClick.Invoke();
                        Need((int)entry.BoxedValue == 0, "phosphor selector wraps forward");
                    }
                    else
                    {
                        double before = Convert.ToDouble(original);
                        plus[row].onClick.Invoke();
                        Need(Convert.ToDouble(entry.BoxedValue) > before, "MFD increases tuning " + key);
                        minus[row].onClick.Invoke();
                        Need(Math.Abs(Convert.ToDouble(entry.BoxedValue) - before) < .01, "MFD decreases tuning " + key);
                        float low = (float)Field(item, "min"), high = (float)Field(item, "max");
                        entry.BoxedValue = entry.SettingType == typeof(int) ? (object)Mathf.RoundToInt(high) : high;
                        plus[row].onClick.Invoke();
                        Need(Math.Abs(Convert.ToDouble(entry.BoxedValue) - high) < .01, "MFD upper bound " + key);
                        entry.BoxedValue = entry.SettingType == typeof(int) ? (object)Mathf.RoundToInt(low) : low;
                        minus[row].onClick.Invoke();
                        Need(Math.Abs(Convert.ToDouble(entry.BoxedValue) - low) < .01, "MFD lower bound " + key);
                    }
                    Canvas.ForceUpdateCanvases();
                    var rect = (RectTransform)rows[row].transform;
                    Need(rect.rect.width >= 300 && rect.rect.height >= 15, "optional control usable bounds " + key);
                    var text = rows[row].GetComponentInChildren<TMP_Text>(true);
                    text.ForceMeshUpdate();
                    Need(!text.isTextOverflowing, "optional label fits " + key);
                }
                finally { entry.BoxedValue = original; Call(effects, "Refresh"); }
            }
            categories[2].onClick.Invoke(); Canvas.ForceUpdateCanvases();
            CaptureOptionalKropPanel(launcher, "optional-nvg-ui.png");
            categories[5].onClick.Invoke(); Canvas.ForceUpdateCanvases();
            CaptureOptionalKropPanel(launcher, "optional-sound-delay-ui.png");
        }
        finally { categories[0].onClick.Invoke(); Call(effects, "Refresh"); }
    }

    private void CaptureOptionalKropPanel(Component launcher, string name)
    {
        var installer = Field(launcher, "installer");
        var panel = (RectTransform)((MFDScreen)Field(installer, "Screen")).displayPanel.transform;
        var host = new GameObject("Optional effects fitted review host", typeof(RectTransform), typeof(Canvas));
        // Capture moves the cloned graphics to layer 31. Its root canvas must
        // share that layer too or the review camera culls the entire canvas.
        host.layer = 31;
        try
        {
            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true; canvas.sortingOrder = 30004;
            var root = new GameObject("Optional effects fitted review page", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(host.transform, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = panel.rect.size;
            // Capture binds the host canvas to its review camera at the native
            // window resolution, then renders it into 1920x1080. Fit the clone
            // in that native canvas first; never change the real MFD's scale.
            float scale = Mathf.Min(1f, Mathf.Min(
                Mathf.Max(1f, Screen.width - 32f) / Mathf.Max(1f, root.rect.width),
                Mathf.Max(1f, Screen.height - 32f) / Mathf.Max(1f, root.rect.height)));
            root.localScale = Vector3.one * scale;
            foreach (Transform child in panel) Instantiate(child.gameObject, root, false);
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4]; root.GetWorldCorners(corners);
            Need(corners[0].x >= 15f && corners[0].y >= 15f &&
                corners[2].x <= Screen.width - 15f && corners[2].y <= Screen.height - 15f,
                "optional review clone fits native window " + name);
            Capture(name, true, root);
            var captured = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Need(ImageConversion.LoadImage(captured, File.ReadAllBytes(Path.Combine(dir, name))),
                    "optional review capture decodes " + name);
                int greenPixels = 0;
                foreach (Color32 pixel in captured.GetPixels32())
                    if (pixel.g > 96 && pixel.g > pixel.r * 1.5f && pixel.g > pixel.b * 1.3f) greenPixels++;
                Need(greenPixels >= 100, "optional review capture contains visible green UI " + name);
            }
            finally { DestroyImmediate(captured); }
        }
        finally { DestroyImmediate(host); }
    }
}
