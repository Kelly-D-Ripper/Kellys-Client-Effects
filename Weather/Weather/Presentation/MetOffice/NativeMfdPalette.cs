using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    // Sample resolved stock graphic colours; Theme override values can mean inherit.
    internal sealed class NativeMfdPalette
    {
        internal Color Text=new Color(.65f,.82f,.75f,1),Selected=new Color(.2f,.7f,.5f,1),Border=new Color(.4f,.55f,.52f,1),Background=new Color(.03f,.055f,.055f,.95f),Blue=new Color(0f,.4f,1f,1f);
        private TMP_Text native;
        internal void Read()
        {
            var mfd=MfdPanelInstaller.ResolveMfd();if(mfd==null)return;
            var text=new List<Color>();var images=new List<Color>();
            var screens=AccessTools.Field(typeof(VirtualMFD),"leftScreens")?.GetValue(mfd) as List<MFDScreen>;
            if(screens==null)return;
            foreach(var screen in screens)
            {
                if(screen==null || screen.displayPanel==null)continue;
                foreach(var g in screen.displayPanel.GetComponentsInChildren<Graphic>(true))
                {
                    if(g==null || !g.enabled || g.color.a<=0f)continue;
                    if(g is TMP_Text t){text.Add(g.color);if(native==null && !string.IsNullOrWhiteSpace(t.text))native=t;}
                    else if(g is Image)images.Add(g.color);
                }
                if(text.Count>0 && images.Count>0)break;
            }
            Text=Pick(text,Color.gray,Text);Selected=Pick(text,Color.green,Selected);Blue=Pick(text,new Color(0f,.4f,1f,1f),Blue);
            Border=Pick(images,Color.gray,Border);Background=Pick(images,new Color(0,0,0,.8f),Background);
        }
        private static Color Pick(List<Color> colors,Color reference,Color fallback)
        {
            float best=float.MaxValue;Color result=fallback;
            foreach(var c in colors){float d=(c.r-reference.r)*(c.r-reference.r)+(c.g-reference.g)*(c.g-reference.g)+(c.b-reference.b)*(c.b-reference.b)+(c.a-reference.a)*(c.a-reference.a);if(d<best){best=d;result=c;}}
            return result;
        }
        internal void Apply(TMP_Text target)
        {
            if(target==null)return;target.color=Selected;
            if(native==null)return;
            target.font=native.font;target.fontSharedMaterial=native.fontSharedMaterial;target.fontStyle=native.fontStyle;target.fontWeight=native.fontWeight;
        }
    }
}
