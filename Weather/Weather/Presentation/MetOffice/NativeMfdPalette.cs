using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    // Accessibility's basic Primary/Secondary pickers map to ColorTheme.AllClear
    // and HudUnitFriendly. Read those roles, not whichever stock label looks green.
    internal sealed class NativeMfdPalette
    {
        internal Color Selected=Color.green,Border=new Color(.4f,.55f,.52f,1),Background=new Color(.03f,.055f,.055f,.95f),Blue=new Color(0f,.4f,1f,1f);
        private TMP_Text native;
        private sealed class TextBinding
        {
            internal TMP_Text Target;
            internal Button Button;
            internal bool Secondary;
        }
        private readonly List<TextBinding> texts=new List<TextBinding>();
        private readonly List<MfdOutline> outlines=new List<MfdOutline>();
        internal void Read()
        {
            Reset();
            ThemeManager.ThemeGroupChanged+=Refresh;
            Refresh();
            var mfd=MfdPanelInstaller.ResolveMfd();if(mfd==null)return;
            var images=new List<Color>();
            var screens=AccessTools.Field(typeof(VirtualMFD),"leftScreens")?.GetValue(mfd) as List<MFDScreen>;
            if(screens==null)return;
            foreach(var screen in screens)
            {
                if(screen==null || screen.displayPanel==null)continue;
                foreach(var g in screen.displayPanel.GetComponentsInChildren<Graphic>(true))
                {
                    if(g==null || !g.enabled || g.color.a<=0f)continue;
                    if(g is TMP_Text t){if(native==null && !string.IsNullOrWhiteSpace(t.text))native=t;}
                    else if(g is Image)images.Add(g.color);
                }
                if(native!=null && images.Count>0)break;
            }
            Border=Pick(images,Color.gray,Border);Background=Pick(images,new Color(0,0,0,.8f),Background);
        }
        internal void Refresh()
        {
            var theme=ThemeManager.Active?.ColorTheme;
            Color primary=theme!=null?theme.AllClear:Color.green;
            Color secondary=theme!=null?theme.HudUnitFriendly:new Color(0f,.4f,1f,1f);
            // The panel owns opacity for enabled/disabled controls. Black remains
            // a valid chosen RGB value; ColorTheme has no style-inheritance sentinel.
            primary.a=secondary.a=1f;
            if(primary==Selected && secondary==Blue)return;
            Selected=primary;Blue=secondary;
            foreach(var binding in texts)Tint(binding);
            foreach(var outline in outlines)if(outline!=null)outline.Recolor(Border,Selected);
        }
        private void Tint(TextBinding binding)
        {
            if(binding.Target==null)return;
            Color color=binding.Secondary?Blue:Selected;
            if(binding.Button!=null && !binding.Button.interactable)color.a*=.35f;
            if(binding.Target.color!=color)binding.Target.color=color;
        }
        internal void Secondary(TMP_Text target)
        {
            foreach(var binding in texts)if(binding.Target==target){binding.Secondary=true;Tint(binding);return;}
        }
        internal void Track(MfdOutline outline) { outlines.Add(outline); }
        internal void Reset()
        {
            ThemeManager.ThemeGroupChanged-=Refresh;
            texts.Clear();outlines.Clear();native=null;
        }
        private static Color Pick(List<Color> colors,Color reference,Color fallback)
        {
            float best=float.MaxValue;Color result=fallback;
            foreach(var c in colors){float d=(c.r-reference.r)*(c.r-reference.r)+(c.g-reference.g)*(c.g-reference.g)+(c.b-reference.b)*(c.b-reference.b)+(c.a-reference.a)*(c.a-reference.a);if(d<best){best=d;result=c;}}
            return result;
        }
        internal void Apply(TMP_Text target)
        {
            if(target==null)return;
            var binding=new TextBinding{Target=target,Button=target.GetComponentInParent<Button>(true)};
            texts.Add(binding);Tint(binding);
            if(native==null)return;
            target.font=native.font;target.fontSharedMaterial=native.fontSharedMaterial;target.fontStyle=native.fontStyle;target.fontWeight=native.fontWeight;
        }
    }
}
