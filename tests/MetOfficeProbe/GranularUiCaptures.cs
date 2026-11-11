using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public sealed partial class MetDriver
{
    private void CaptureGranularEffectControls(object effects, Component launcher)
    {
        int category = (int)Field(effects, "category"), page = (int)Field(effects, "page");
        try
        {
            var all = ((IEnumerable)effects.GetType().GetField("Items", F).GetValue(null)).Cast<object>().ToArray();
            foreach (var target in new[] {
                new { Category = 2, Key = "Ground roll vibration", File = "ground-roll-controls.png" },
                new { Category = 0, Key = "RainStreaksEnabled", File = "granular-weather-controls.png" }
            })
            {
                var entries = all.Where(x => (int)Field(x, "category") == target.Category).ToArray();
                int index = Array.FindIndex(entries, x => (string)Field(x, "key") == target.Key);
                Need(index >= 0, "granular review control exists " + target.Key);
                FI(effects, "category").SetValue(effects, target.Category);
                FI(effects, "page").SetValue(effects, index / 9);
                Call(effects, "Refresh");
                CaptureOptionalKropPanel(launcher, target.File);
            }
        }
        finally
        {
            FI(effects, "category").SetValue(effects, category);
            FI(effects, "page").SetValue(effects, page);
            Call(effects, "Refresh");
        }
    }
}
