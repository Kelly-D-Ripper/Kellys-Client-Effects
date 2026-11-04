using TMPro;
using UnityEngine;
namespace NOAvionics
{
    internal static class AvType
    {
        internal static TMP_FontAsset VanillaFallback
        {
            get
            {
                var screen = Object.FindObjectOfType<MFDScreen>();
                return screen != null && screen.label != null && screen.label.font != null
                    ? screen.label.font : TMP_Settings.defaultFontAsset;
            }
        }
    }
}
