using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class MetDriver
{
    // Exercise the same semantic colours read by AccessibilityMenu's basic
    // Primary / Secondary pickers. No save method or PlayerSettings is touched.
    private sealed class MfdColourSnapshot : IDisposable
    {
        private readonly object colours;
        private readonly FieldInfo primaryField, secondaryField;
        private readonly MethodInfo notify;
        private readonly Color primary, secondary;

        internal MfdColourSnapshot()
        {
            Type managerType = typeof(MissionManager).Assembly.GetType("NuclearOption.UIStyleSystem.ThemeManager", true);
            object active = managerType.GetProperty("Active", F).GetValue(null, null);
            colours = active.GetType().GetProperty("ColorTheme", F).GetValue(active, null);
            primaryField = AccessTools.Field(colours.GetType(), "allClear");
            secondaryField = AccessTools.Field(colours.GetType(), "hudUnitFriendly");
            notify = managerType.GetMethod("NotifyThemeGroupChanged", F);
            primary = (Color)primaryField.GetValue(colours);
            secondary = (Color)secondaryField.GetValue(colours);
        }

        internal void Set(Color primaryColour, Color secondaryColour, bool notifyChanged = true)
        {
            primaryField.SetValue(colours, primaryColour);
            secondaryField.SetValue(colours, secondaryColour);
            if (notifyChanged) notify.Invoke(null, null);
        }

        public void Dispose() => Set(primary, secondary);
    }

    private static Color OpaqueMfd(Color colour) => new Color(colour.r, colour.g, colour.b, 1f);

    private static int MfdThemeListeners(object target)
    {
        Type manager = typeof(MissionManager).Assembly.GetType("NuclearOption.UIStyleSystem.ThemeManager", true);
        var listeners = (Delegate)AccessTools.Field(manager, "ThemeGroupChanged").GetValue(null);
        return listeners == null ? 0 : listeners.GetInvocationList().Count(d => ReferenceEquals(d.Target, target));
    }

    private void NeedMfdColour(Color actual, Color expected, string label)
    {
        Need(Near(actual.r, expected.r) && Near(actual.g, expected.g) &&
             Near(actual.b, expected.b) && Near(actual.a, expected.a), label);
    }

    private void NeedMfdLabel(Button button, Color primary, string label)
    {
        Color expected = OpaqueMfd(primary);
        if (!button.interactable) expected.a *= .35f;
        NeedMfdColour(button.GetComponentInChildren<TMP_Text>(true).color, expected, label);
    }

    private Image[] MfdFrame(Button button)
    {
        Component frame = button.GetComponents<Component>().Single(c => c != null && c.GetType().Name == "MfdOutline");
        return (Image[])Field(frame, "edges");
    }

    private async UniTask TestMfdAccessibilityColours(Component launcher)
    {
        var installer = Field(launcher, "installer");
        var screen = (MFDScreen)Field(installer, "Screen");
        var effectsTab = (Button)Field(launcher, "effectsTab");
        var weatherTab = (Button)Field(launcher, "weatherTab");
        object effects = Field(launcher, "effects");
        var categories = (Button[])Field(effects, "categories");
        var previous = (Button)Field(effects, "previous");
        var page = (RectTransform)Field(launcher, "page");
        Color amber = new Color(.96f, .55f, .08f, 1f);
        Color cyan = new Color(.08f, .85f, 1f, 1f);
        Color violet = new Color(.74f, .35f, 1f, 1f);
        Color secondary = new Color(.25f, .65f, 1f, 1f);
        Color background = page.GetComponentsInChildren<Image>(true).First(i => i.name == "Background").color;
        Color idleBorder = MfdFrame(weatherTab)[0].color;
        var saved = new MfdColourSnapshot();
        try
        {
            categories[0].onClick.Invoke();
            saved.Set(amber, secondary);
            await UniTask.Delay(350, ignoreTimeScale: true);
            // A newly-created palette must start with the current picker value,
            // not merely catch the next ThemeGroupChanged notification.
            Type paletteType = launcher.GetType().Assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.NativeMfdPalette", true);
            object freshPalette = Activator.CreateInstance(paletteType, true);
            GameObject disabledFixture = null;
            try
            {
                Call(freshPalette, "Read");
                NeedMfdColour((Color)Field(freshPalette, "Selected"), amber, "fresh MFD palette uses accessibility primary");
                Need(MfdThemeListeners(freshPalette) == 1, "fresh MFD palette subscribes once to native theme changes");
                disabledFixture = new GameObject("Inactive accessibility button fixture", typeof(RectTransform), typeof(Image), typeof(Button));
                disabledFixture.SetActive(false);
                var disabledButton = disabledFixture.GetComponent<Button>();
                disabledButton.interactable = false;
                var textObject = new GameObject("Inactive accessibility label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(disabledFixture.transform, false);
                TMP_Text inactiveLabel = textObject.GetComponent<TMP_Text>();
                Call(freshPalette, "Apply", inactiveLabel);
                NeedMfdLabel(disabledButton, amber, "binding a disabled label on an inactive page preserves dimming");
                saved.Set(violet, secondary);
                NeedMfdLabel(disabledButton, violet, "inactive disabled label remains dimmed after native theme event");
                Call(freshPalette, "Reset");
                Need(MfdThemeListeners(freshPalette) == 0, "MFD palette reset removes native theme subscription");
                saved.Set(cyan, secondary);
                NeedMfdLabel(disabledButton, violet, "reset MFD palette no longer recolours its former labels");
                saved.Set(amber, secondary);
            }
            finally
            {
                Call(freshPalette, "Reset");
                if (disabledFixture != null) DestroyImmediate(disabledFixture);
            }
            NeedMfdColour(page.GetComponentsInChildren<TMP_Text>(true).Single(t => t.text == "EFFECTS & WEATHER").color,
                amber, "MFD title follows accessibility primary");
            NeedMfdLabel(effectsTab, amber, "open effects tab follows accessibility primary");
            NeedMfdLabel(weatherTab, amber, "inactive weather tab label follows accessibility primary");
            NeedMfdColour(MfdFrame(effectsTab)[0].color, amber, "selected MFD outline follows accessibility primary");
            NeedMfdColour(MfdFrame(weatherTab)[0].color, idleBorder, "unselected MFD outline retains neutral border");
            Need(!previous.interactable, "first effects page has a disabled previous button");
            NeedMfdLabel(previous, amber, "disabled MFD label retains dimmed primary colour");
            NeedMfdColour(((TMP_Text)Field(effects, "hint")).color, amber, "effects section hint follows accessibility primary");
            NeedMfdColour(((TMP_Text)Field(effects, "pageText")).color, amber, "effects page number follows accessibility primary");
            NeedMfdColour(((TMP_Text)Field(launcher, "status")).color, secondary, "hidden weather status follows accessibility secondary");
            NeedMfdColour(((TMP_Text)Field(launcher, "clock")).color, secondary, "hidden weather clock follows accessibility secondary");
            CaptureMfdColourPanel(launcher, "mfd-accessibility-amber-effects.png", amber);

            weatherTab.onClick.Invoke();
            // The bounded visible-panel poll also catches in-memory changes
            // that do not send the native theme event.
            saved.Set(cyan, violet, false);
            await UniTask.Delay(350, ignoreTimeScale: true);
            NeedMfdLabel(weatherTab, cyan, "weather tab polls changed primary without notification or reopening");
            NeedMfdColour(MfdFrame(weatherTab)[0].color, cyan, "selected weather outline changes primary live");
            NeedMfdColour(((TMP_Text)Field(launcher, "status")).color, violet, "visible weather status changes secondary live");
            NeedMfdColour(((TMP_Text)Field(launcher, "clock")).color, violet, "visible weather clock changes secondary live");
            NeedMfdLabel((Button)Field(launcher, "simpleButton"), cyan, "weather names control follows accessibility primary");
            NeedMfdLabel(((Button[])Field(launcher, "modes"))[0], cyan, "cloud mode control follows accessibility primary");
            NeedMfdColour(((TMP_Text)Field(effects, "hint")).color, cyan, "inactive effects page also receives changed primary");
            CaptureMfdColourPanel(launcher, "mfd-accessibility-cyan-weather.png", cyan);

            effectsTab.onClick.Invoke();
            NeedMfdLabel(effectsTab, cyan, "switching tabs does not restore stale green labels");
            DynamicMap.i.Minimize();
            saved.Set(violet, secondary);
            await UniTask.Delay(350, ignoreTimeScale: true);
            DynamicMap.i.Maximize();
            await UniTask.Delay(350, ignoreTimeScale: true);
            if (!screen.isActive) ((Button)Field(installer, "Bezel")).onClick.Invoke();
            await UniTask.Delay(350, ignoreTimeScale: true);
            Need(screen.isActive, "MFD colour test reopens native FX screen");
            NeedMfdLabel(effectsTab, violet, "reopened MFD uses primary changed while map was closed");
            NeedMfdColour(page.GetComponentsInChildren<Image>(true).First(i => i.name == "Background").color,
                background, "accessibility primary does not recolour panel background");

            // Unlike raw TextStyle overrides, ColorTheme RGB zero is a real
            // selected colour. It must not silently revert to stock green.
            saved.Set(Color.black, secondary);
            await UniTask.Delay(350, ignoreTimeScale: true);
            NeedMfdLabel(effectsTab, Color.black, "black accessibility primary is not treated as an inheritance sentinel");
        }
        finally
        {
            saved.Dispose();
            DynamicMap.i.Maximize();
            if (!screen.isActive) ((Button)Field(installer, "Bezel")).onClick.Invoke();
            effectsTab.onClick.Invoke();
            categories[0].onClick.Invoke();
            await UniTask.Delay(350, ignoreTimeScale: true);
        }
    }

    private async UniTask TestMfdAccessibilityCloudColours(Component launcher)
    {
        object cloud = Field(launcher, "cloudEffects");
        var page = (RectTransform)Field(launcher, "cloudPage");
        var rows = (Button[])Field(cloud, "rows");
        Color amber = new Color(.96f, .55f, .08f, 1f);
        Color cyan = new Color(.08f, .85f, 1f, 1f);
        var saved = new MfdColourSnapshot();
        try
        {
            saved.Set(amber, cyan);
            await UniTask.Delay(350, ignoreTimeScale: true);
            Need(page.gameObject.activeInHierarchy, "NOAtmoFX controls remain open after colour change");
            NeedMfdColour(((TMP_Text)Field(cloud, "hint")).color, amber, "NOAtmoFX section hint follows accessibility primary");
            NeedMfdColour(((TMP_Text)Field(cloud, "pageText")).color, amber, "NOAtmoFX page number follows accessibility primary");
            foreach (Button row in rows.Where(r => r.gameObject.activeSelf))
                NeedMfdLabel(row, amber, "NOAtmoFX control follows accessibility primary " + row.GetComponentInChildren<TMP_Text>(true).text);
            NeedMfdLabel((Button)Field(launcher, "cloudBack"), amber, "NOAtmoFX back button follows accessibility primary");
            CaptureMfdColourPanel(launcher, "mfd-accessibility-amber-cloud-controls.png", amber);
            saved.Set(cyan, amber);
            await UniTask.Delay(350, ignoreTimeScale: true);
            NeedMfdLabel(rows[0], cyan, "NOAtmoFX control changes primary live without page rebuild");
            Need(ReferenceEquals(Field(launcher, "cloudPage"), page), "colour refresh retains existing NOAtmoFX page objects");
        }
        finally
        {
            saved.Dispose();
            await UniTask.Delay(350, ignoreTimeScale: true);
        }
    }

    private void CaptureMfdColourPanel(Component launcher, string name, Color primary)
    {
        var installer = Field(launcher, "installer");
        var panel = (RectTransform)((MFDScreen)Field(installer, "Screen")).displayPanel.transform;
        var host = new GameObject("Accessibility colour review host", typeof(RectTransform), typeof(Canvas));
        host.layer = 31;
        try
        {
            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true; canvas.sortingOrder = 30004;
            var root = new GameObject("Accessibility colour review page", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(host.transform, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = panel.rect.size;
            float scale = Mathf.Min(1f, Mathf.Min(Mathf.Max(1f, Screen.width - 32f) / Mathf.Max(1f, root.rect.width),
                Mathf.Max(1f, Screen.height - 32f) / Mathf.Max(1f, root.rect.height)));
            root.localScale = Vector3.one * scale;
            foreach (Transform child in panel) Instantiate(child.gameObject, root, false);
            Canvas.ForceUpdateCanvases();
            Capture(name, true, root);
            var pixels = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Need(ImageConversion.LoadImage(pixels, File.ReadAllBytes(Path.Combine(dir, name))), "accessibility capture decodes " + name);
                float targetMax = Mathf.Max(primary.r, Mathf.Max(primary.g, primary.b));
                Vector3 expected = new Vector3(primary.r, primary.g, primary.b) / targetMax;
                Color gamma = primary.gamma, linear = primary.linear;
                Vector3 expectedGamma = new Vector3(gamma.r, gamma.g, gamma.b) / Mathf.Max(gamma.r, Mathf.Max(gamma.g, gamma.b));
                Vector3 expectedLinear = new Vector3(linear.r, linear.g, linear.b) / Mathf.Max(linear.r, Mathf.Max(linear.g, linear.b));
                int coloured = 0;
                foreach (Color32 pixel in pixels.GetPixels32())
                {
                    float max = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                    if (max < 96f) continue;
                    var normalized = new Vector3(pixel.r, pixel.g, pixel.b) / max;
                    // UI target encoding can differ from ordinary camera
                    // captures; allow either colour-space encoding, not green.
                    if (Mathf.Min((normalized - expected).sqrMagnitude,
                        Mathf.Min((normalized - expectedGamma).sqrMagnitude, (normalized - expectedLinear).sqrMagnitude)) < .08f) coloured++;
                }
                Need(coloured >= 100, "accessibility capture contains selected primary colour " + name);
            }
            finally { DestroyImmediate(pixels); }
        }
        finally { DestroyImmediate(host); }
    }
}
