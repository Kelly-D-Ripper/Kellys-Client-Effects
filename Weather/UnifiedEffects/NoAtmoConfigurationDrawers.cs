using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Modules.Weather.Presentation;
using UnityEngine;

namespace UnifiedEffects
{
    // F1 ordinarily renders these upstream entries as sliders. Attach transient
    // drawers to the original entries so both menus offer the same API switch,
    // while keeping the add-on's strength/mode setting and configuration identity.
    internal static class NoAtmoConfigurationDrawers
    {
        private static readonly string[] NumericFeatures = {
            "NearRain", "Lightning", "Turbulence", "GodRays", "LightShafts",
            "Fog", "Rainbow", "Glory", "ValleyFog", "MountainCloud"
        };
        private static readonly FieldInfo DescriptionField = typeof(ConfigEntryBase).GetField(
            "<Description>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly List<FeatureControl> Controls = new List<FeatureControl>();

        // Configuration Manager's tag contract explicitly recognises this name.
        private sealed class ConfigurationManagerAttributes
        {
            public Action<ConfigEntryBase> CustomDrawer;
        }

        internal static void Attach(CloudModeCoordinator owner, ConfigFile config, ManualLogSource log)
        {
            if (!ConfigurationManagerBridge.IsActive || DescriptionField == null || owner == null) return;
            Detach(owner);
            foreach (string feature in NumericFeatures)
            {
                var definition = new ConfigDefinition("Atmosphere", feature);
                ConfigEntryBase entry = null;
                if (feature == "NearRain" && config.TryGetEntry<int>(definition, out var mode)) entry = mode;
                else if (feature != "NearRain" && config.TryGetEntry<float>(definition, out var strength)) entry = strength;
                if (entry == null)
                {
                    log.LogWarning("F1 feature switch unavailable: NOAtmoFX changed " + definition + ".");
                    continue;
                }
                try
                {
                    var control = new FeatureControl(owner, config, entry, feature);
                    control.Attach();
                    Controls.Add(control);
                }
                catch (Exception error) { log.LogWarning("F1 feature switch unavailable for " + feature + ": " + error.Message); }
            }
        }

        internal static void Detach(CloudModeCoordinator owner)
        {
            for (int i = Controls.Count - 1; i >= 0; i--)
                if (ReferenceEquals(Controls[i].Owner, owner))
                {
                    Controls[i].Detach();
                    Controls.RemoveAt(i);
                }
        }

        internal static void DetachAll()
        {
            foreach (var control in Controls) control.Detach();
            Controls.Clear();
        }

        internal sealed class FeatureControl
        {
            internal readonly CloudModeCoordinator Owner;
            internal readonly ConfigEntryBase Entry;
            internal readonly string Feature;
            private readonly ConfigFile config;
            private readonly ConfigDescription original;
            private readonly ConfigurationManagerAttributes tag;
            private ConfigDescription decorated;
            private bool attached;
            private static readonly string[] RainModes = { "Auto", "Always", "Never" };

            internal FeatureControl(CloudModeCoordinator owner, ConfigFile file, ConfigEntryBase entry, string feature)
            {
                Owner = owner; config = file; Entry = entry; Feature = feature;
                original = entry.Description;
                tag = new ConfigurationManagerAttributes { CustomDrawer = Draw };
            }

            internal bool Enabled => Owner != null && Owner.Feature(Feature);
            internal bool Editable => attached && Owner != null && Owner.CanEdit(Entry.Definition.Section, Feature);

            internal void Attach()
            {
                object[] tags = original?.Tags ?? Array.Empty<object>();
                decorated = new ConfigDescription(original?.Description ?? "", original?.AcceptableValues, tags.Concat(new object[] { tag }).ToArray());
                DescriptionField.SetValue(Entry, decorated);
                attached = true;
            }

            internal void Detach()
            {
                attached = false;
                if (ReferenceEquals(Entry.Description, decorated)) DescriptionField.SetValue(Entry, original);
                else if (Entry.Description?.Tags != null && Entry.Description.Tags.Any(x => ReferenceEquals(x, tag)))
                {
                    // If another menu added metadata after us, remove only ours.
                    var current = Entry.Description;
                    DescriptionField.SetValue(Entry, new ConfigDescription(current.Description, current.AcceptableValues,
                        current.Tags.Where(x => !ReferenceEquals(x, tag)).ToArray()));
                }
            }

            internal bool SetEnabled(bool desired)
            {
                if (!Editable) return false;
                return Enabled == desired || Owner.ToggleFeature(Feature);
            }

            internal bool SetNumeric(object value)
            {
                if (!Editable || value == null || value.GetType() != Entry.SettingType) return false;
                bool save = config.SaveOnConfigSet;
                if (Owner.SessionOnly) config.SaveOnConfigSet = false;
                try { Entry.BoxedValue = value; return true; }
                finally { config.SaveOnConfigSet = save; }
            }

            private void Draw(ConfigEntryBase entry)
            {
                if (!ReferenceEquals(entry, Entry)) return;
                bool previousEnabled = GUI.enabled;
                try
                {
                    GUI.enabled = previousEnabled && Editable;
                    GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                    try
                    {
                        bool before = Enabled;
                        bool desired = GUILayout.Toggle(before, "Enabled");
                        if (desired != before) SetEnabled(desired);
                        if (Entry.BoxedValue is int mode)
                        {
                            int selected = GUILayout.SelectionGrid(Mathf.Clamp(mode, 0, 2), RainModes, 3);
                            if (selected != mode) SetNumeric(selected);
                        }
                        else if (Entry.BoxedValue is float strength && Entry.Description.AcceptableValues is AcceptableValueRange<float> range)
                        {
                            GUILayout.Label("Strength: " + strength.ToString("0.00"));
                            float selected = GUILayout.HorizontalSlider(strength, range.MinValue, range.MaxValue);
                            if (selected != strength) SetNumeric(selected);
                        }
                    }
                    finally { GUILayout.EndVertical(); }
                }
                finally { GUI.enabled = previousEnabled; }
            }
        }
    }
}
