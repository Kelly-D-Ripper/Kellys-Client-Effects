using System;
using BepInEx.Logging;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    // A full native-bezel page. The larger O-key console remains available for the
    // detailed fronts chart; both surfaces use the same guarded weather adapter.
    internal sealed class MetOfficeMfdLauncher : MonoBehaviour, ISceneService
    {
        private readonly MfdPanelInstaller installer = new MfdPanelInstaller("FX", "FX", "EffectsWeather.Mfd", false) { Plate = true };
        private readonly MetOfficeModel model = new MetOfficeModel();
        private readonly Button[] classes = new Button[7], tiles = new Button[7], times = new Button[9];
        private readonly int[] tileSky = new int[7];
        private static readonly string[] SimpleClasses = { "CLEAR", "FEW", "SOME", "MOSTLY", "CLOUDY", "RAIN", "STORMS" };
        private static readonly string[] ClockNames = { "-1 HR", "-15 MIN", "+15 MIN", "+1 HR", "MIDNIGHT", "MORNING", "NOON", "EVENING", "RESTORE" };
        private readonly NativeMfdPalette palette=new NativeMfdPalette();
        private WeatherManager weather;
        private WeatherSettings settings;
        private MetOfficeAdapter adapter;
        private TMP_Text status, clock,weatherHeading,timeHeading,weatherFooter;
        private Button launch, simpleButton, technicalButton, auto, reroll;
        private WeatherRegimeType browse;
        private bool touched;
        private float nextRefresh;
        private RectTransform page;
        private RectTransform weatherPage,effectsPage,weatherContent,cloudPage;
        private EffectsMfdPage effects,cloudEffects;
        private readonly Button[] modes = new Button[3];
        private Button cloudButton,cloudBack;
        private TMP_Text cloudStatus;
        private bool showCloudSettings;
        private Button effectsTab,weatherTab;
        private bool showEffects=true;
        private Vector3 originalScale;
        private float layoutHeight;
        private readonly Vector3[] bounds = new Vector3[4], bezelBounds = new Vector3[4];

        internal void Configure(WeatherSettings config, WeatherManager manager, ManualLogSource log)
        {
            settings=config; weather=manager; adapter=new MetOfficeAdapter(manager);
            installer.Log=log; installer.Builder=Build;
        }
        private RectTransform Build(RectTransform root, float height)
        {
            page=root; originalScale=root.localScale;layoutHeight=height;
            palette.Read();
            Image background=AvLay.Solid(root,"Background",palette.Background); AvLay.Fill(background.rectTransform);
            TMP_Text title=Text(root,"EFFECTS & WEATHER",16,8,448,30,AvTextRole.Title,TextAlignmentOptions.Center);title.fontSize=26f;AvText.Fit(title,false);
            Text(root,"CLIENT SETTINGS  /  MET OFFICE",16,39,448,20,AvTextRole.Micro,TextAlignmentOptions.Center);
            effectsTab=ButtonAt(root,"EFFECTS",16f,64f,220f,32f,()=>{showEffects=true;RefreshTabs();});
            weatherTab=ButtonAt(root,"WEATHER",244f,64f,220f,32f,()=>{showEffects=false;RefreshTabs();});
            float pageHeight=Mathf.Max(460f,height-108f);
            effectsPage=new GameObject("Effects page",typeof(RectTransform)).GetComponent<RectTransform>();effectsPage.SetParent(root,false);AvLay.Place(effectsPage,0,104,480,pageHeight);
            weatherPage=new GameObject("Weather page",typeof(RectTransform)).GetComponent<RectTransform>();weatherPage.SetParent(root,false);AvLay.Place(weatherPage,0,104,480,pageHeight);
            effects=new EffectsMfdPage(effectsPage,ButtonAt,Label,palette.Apply,false,pageHeight);
            Text(weatherPage,"WEATHER NAMES",16,3,448,22,AvTextRole.Micro,TextAlignmentOptions.Center);
            simpleButton=ButtonAt(weatherPage,"SIMPLE ENGLISH",16,26,220,31,()=>{SetNames(true);});
            technicalButton=ButtonAt(weatherPage,"TECHNICAL",244,26,220,31,()=>{SetNames(false);});
            Text(weatherPage,"CLOUD SYSTEM",16,66,448,20,AvTextRole.Micro);
            string[] modeNames={"VANILLA","ENHANCED","VOLUMETRIC"};
            for(int i=0;i<modes.Length;i++) { int ix=i; modes[i]=ButtonAt(weatherPage,modeNames[i],16+i*150,88,146,30,()=>{CloudModeCoordinator.Live?.Select((CloudMode)ix);showCloudSettings=false;Refresh();}); }
            cloudStatus=Text(weatherPage,"",16,124,292,28,AvTextRole.Micro);
            cloudButton=ButtonAt(weatherPage,"CLOUD SETTINGS  >",312,124,152,28,()=>{showCloudSettings=true;Refresh();});
            float contentHeight=Mathf.Max(300f,pageHeight-160f);
            weatherContent=new GameObject("Weather controls",typeof(RectTransform)).GetComponent<RectTransform>();weatherContent.SetParent(weatherPage,false);AvLay.Place(weatherContent,0,160,480,contentHeight);
            cloudPage=new GameObject("Volumetric controls",typeof(RectTransform)).GetComponent<RectTransform>();cloudPage.SetParent(weatherPage,false);AvLay.Place(cloudPage,0,160,480,contentHeight);
            cloudEffects=new EffectsMfdPage(cloudPage,ButtonAt,Label,palette.Apply,true,contentHeight-42f);
            cloudBack=ButtonAt(cloudPage,"<  BACK TO WEATHER",16,contentHeight-36,448,30,()=>{showCloudSettings=false;Refresh();});
            root=weatherContent;
            // All vertical spacing tracks the available native MFD bay height.
            float sy=contentHeight/500f;
            weatherHeading=Text(root,"CURRENT WEATHER",16f,4f*sy,448f,22f*sy,AvTextRole.Micro);
            status=Text(root,"WAITING FOR MISSION",16f,28f*sy,448f,28f*sy,AvTextRole.Label);
            for(int i=0;i<classes.Length;i++)
            {
                int index=i;
                classes[i]=ButtonAt(root,"",16f+i*64f,66f*sy,60f,30f*sy,()=>{browse=(WeatherRegimeType)index;touched=true;Refresh();});
            }
            for(int i=0;i<tiles.Length;i++)
            {
                int index=i; tileSky[i]=-1;
                tiles[i]=ButtonAt(root,"",16f+(i%2)*226f,(108f+(i/2)*49f)*sy,222f,42f*sy,()=>{
                    if(tileSky[index]>=0 && adapter.PinSky(tileSky[index])) Refresh();
                });
            }
            auto=ButtonAt(root,"MISSION WEATHER",242f,255f*sy,222f,42f*sy,()=>{adapter.PinSky(-1);Refresh();});
            timeHeading=Text(root,"TIME OF DAY",16f,307f*sy,220f,25f*sy,AvTextRole.Micro);
            clock=Text(root,"--:--",255f,307f*sy,209f,25f*sy,AvTextRole.Head,TextAlignmentOptions.MidlineRight);
            status.color=clock.color=palette.Blue;
            for(int i=0;i<times.Length;i++)
            {
                int index=i;
                times[i]=ButtonAt(root,ClockNames[i],16f+(i%3)*151f,(338f+(i/3)*33f)*sy,146f,28f*sy,()=>{SetClock(index);Refresh();});
            }
            reroll=ButtonAt(root,"NEW CLOUD PATTERN",16f,445f*sy,218f,30f*sy,()=>{adapter.Reroll();Refresh();});
            launch=ButtonAt(root,"DETAILED WEATHER",242f,445f*sy,222f,30f*sy,Open);
            weatherFooter=Text(root,"SINGLE-PLAYER WEATHER CONTROL",16f,480f*sy,448f,18f*sy,AvTextRole.Micro,TextAlignmentOptions.Center);
            RefreshTabs();
            return page;
        }
        private void SetNames(bool simple)
        {
            var entry=BoscaliUnifiedWeather.WeatherPlugin.SimpleNames;
            if(entry!=null)entry.Value=simple;
            Refresh();
        }
        private void Reflow(float height)
        {
            if(page==null || effectsPage==null || weatherContent==null)return;
            layoutHeight=height;page.sizeDelta=new Vector2(page.sizeDelta.x,height);
            float pageHeight=Mathf.Max(460f,height-108f),contentHeight=Mathf.Max(300f,pageHeight-160f),sy=contentHeight/500f;
            AvLay.Place(effectsPage,0,104,480,pageHeight);AvLay.Place(weatherPage,0,104,480,pageHeight);
            AvLay.Place(weatherContent,0,160,480,contentHeight);AvLay.Place(cloudPage,0,160,480,contentHeight);
            effects?.Reflow(pageHeight);cloudEffects?.Reflow(contentHeight-42f);
            AvLay.Place((RectTransform)cloudBack.transform,16,contentHeight-36f,448,30);
            AvLay.Place(weatherHeading,16,4*sy,448,22*sy);AvLay.Place(status,16,28*sy,448,28*sy);
            for(int i=0;i<classes.Length;i++)AvLay.Place((RectTransform)classes[i].transform,16+i*64,66*sy,60,30*sy);
            for(int i=0;i<tiles.Length;i++)AvLay.Place((RectTransform)tiles[i].transform,16+(i%2)*226,(108+(i/2)*49)*sy,222,42*sy);
            AvLay.Place((RectTransform)auto.transform,242,255*sy,222,42*sy);
            AvLay.Place(timeHeading,16,307*sy,220,25*sy);AvLay.Place(clock,255,307*sy,209,25*sy);
            for(int i=0;i<times.Length;i++)AvLay.Place((RectTransform)times[i].transform,16+(i%3)*151,(338+(i/3)*33)*sy,146,28*sy);
            AvLay.Place((RectTransform)reroll.transform,16,445*sy,218,30*sy);AvLay.Place((RectTransform)launch.transform,242,445*sy,222,30*sy);
            AvLay.Place(weatherFooter,16,480*sy,448,18*sy);
        }
        private void RefreshTabs()
        {
            if(effectsPage!=null)effectsPage.gameObject.SetActive(showEffects);
            if(weatherPage!=null)weatherPage.gameObject.SetActive(!showEffects);
            Label(effectsTab,"EFFECTS",showEffects,true);Label(weatherTab,"WEATHER",!showEffects,true);
            if(showEffects)effects?.Refresh();else Refresh();
        }
        private TMP_Text Text(RectTransform root,string label,float x,float y,float w,float h,AvTextRole role,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft)
        {
            TMP_Text t=AvText.Make(root,"Text",role,label,align);
            AvLay.Place(t,x,y,w,h); palette.Apply(t); return t;
        }
        private Button ButtonAt(RectTransform root,string caption,float x,float y,float w,float h,Action action)
        {
            var go=new GameObject("MET " + caption,typeof(RectTransform),typeof(Image),typeof(Button));
            go.transform.SetParent(root,false);
            var image=go.GetComponent<Image>(); image.color=Color.clear;
            var button=go.GetComponent<Button>(); button.targetGraphic=image;
            button.transition=Selectable.Transition.None;
            button.navigation=new Navigation{mode=Navigation.Mode.None}; button.onClick.AddListener(()=>action());
            AvLay.Place((RectTransform)go.transform,x,y,w,h);
            var frame=go.AddComponent<MfdOutline>();frame.Build(palette.Border,palette.Selected);
            var text=AvText.Make((RectTransform)go.transform,"Label",AvTextRole.Head,caption,TextAlignmentOptions.Center,true);
            AvLay.Fill(text.rectTransform); AvText.Fit(text,true);palette.Apply(text);
            return button;
        }
        private void Label(Button button,string value,bool selected,bool enabled)
        {
            if(button==null) return;
            TMP_Text label=button.GetComponentInChildren<TMP_Text>(true);
            if(label!=null)
            {
                if(label.text!=value)label.text=value;
                Color color=palette.Selected;if(!enabled)color.a*=.35f;
                if(label.color!=color)label.color=color;
            }
            button.interactable=enabled;
            button.GetComponent<MfdOutline>()?.Set(selected,enabled);
        }
        private void SetClock(int index)
        {
            switch(index) {
                case 0: adapter.ShiftTime(-1f); break; case 1: adapter.ShiftTime(-.25f); break;
                case 2: adapter.ShiftTime(.25f); break; case 3: adapter.ShiftTime(1f); break;
                case 4: adapter.SetTime(0f); break; case 5: adapter.SetTime(6f); break;
                case 6: adapter.SetTime(12f); break; case 7: adapter.SetTime(18f); break;
                case 8: adapter.RestoreTime(); break;
            }
        }
        private void Open() => BoscaliUnifiedWeather.WeatherUiBridge.OpenMetOffice();
        private void FitBesideBezel()
        {
            if(page==null || installer.Bezel==null || !(page.parent is RectTransform parent)) return;
            Canvas canvas=page.GetComponentInParent<Canvas>();
            if(canvas==null) return;
            Camera camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            page.GetWorldCorners(bounds); ((RectTransform)installer.Bezel.transform).GetWorldCorners(bezelBounds);
            Vector2 left=RectTransformUtility.WorldToScreenPoint(camera,bounds[0]);
            Vector2 right=RectTransformUtility.WorldToScreenPoint(camera,bounds[2]);
            Vector2 bezelLeft=RectTransformUtility.WorldToScreenPoint(camera,bezelBounds[0]);
            Vector2 bezelRight=RectTransformUtility.WorldToScreenPoint(camera,bezelBounds[2]);
            float width=right.x-left.x;
            if(width<=1f || Mathf.Abs(originalScale.x)<.0001f || Mathf.Abs(page.localScale.x)<.0001f) return;
            bool pageRight=(bezelLeft.x+bezelRight.x)<(left.x+right.x);
            float boundary=pageRight ? bezelRight.x+4f : bezelLeft.x-4f;
            float available=pageRight ? Screen.width-8f-boundary : boundary-8f;
            float fullWidth=width*Mathf.Abs(originalScale.x/page.localScale.x);
            float scale=Mathf.Clamp(available/fullWidth,.1f,1f);
            page.localScale=originalScale*scale;
            page.GetWorldCorners(bounds);
            left=RectTransformUtility.WorldToScreenPoint(camera,bounds[0]);
            right=RectTransformUtility.WorldToScreenPoint(camera,bounds[2]);
            float dx=pageRight ? boundary-left.x : boundary-right.x;
            if(Mathf.Abs(dx)<.05f) return;
            if(RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,Vector2.zero,camera,out Vector2 a) &&
               RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,new Vector2(dx,0f),camera,out Vector2 b))
                page.anchoredPosition+=b-a;
        }
        private void Refresh()
        {
            if(adapter==null || status==null) return;
            var cloud=CloudModeCoordinator.Live;
            string[] names={"VANILLA","ENHANCED","VOLUMETRIC"};
            for(int i=0;i<modes.Length;i++) Label(modes[i],names[i],cloud!=null && (int)cloud.Mode==i,i!=2 || (cloud?.AddonReady ?? false));
            if(cloudStatus!=null) cloudStatus.text=cloud?.Status ?? "CLOUD SYSTEM UNAVAILABLE";
            bool volumetric=cloud!=null && cloud.Mode==CloudMode.Volumetric && cloud.AddonReady;
            if(!volumetric)showCloudSettings=false;
            Label(cloudButton,"CLOUD SETTINGS  >",showCloudSettings,volumetric);
            if(weatherContent!=null)weatherContent.gameObject.SetActive(!showCloudSettings);
            if(cloudPage!=null)cloudPage.gameObject.SetActive(showCloudSettings);
            if(showCloudSettings)cloudEffects?.Refresh();
            adapter.Fill(model);
            bool ready=model.Host && model.Captured;
            bool simple=BoscaliUnifiedWeather.WeatherPlugin.SimpleNames?.Value ?? true;
            if(!touched && model.Captured) browse=model.Class;
            string name=model.SkyNumber>0 && simple ? SimpleWeatherNames.Get(model.SkyNumber-1) : model.SkyName;
            status.text=ready ? name.ToUpperInvariant()+"  ·  WIND "+model.WindFromDeg.ToString("000")+"° / "+Mathf.RoundToInt(model.WindMps)+" M/S" : "AVAILABLE IN SINGLE-PLAYER MISSIONS";
            for(int i=0;i<classes.Length;i++) Label(classes[i],simple ? SimpleClasses[i] : MetCatalog.ClassWords[i],(int)browse==i,true);
            Label(simpleButton,"SIMPLE ENGLISH",simple,true);
            Label(technicalButton,"TECHNICAL",!simple,true);
            int n=0;
            for(int i=0;i<SkyLibrary.Count && n<tiles.Length;i++)
            {
                if(SkyLibrary.ClassOf(i)!=browse) continue;
                SkyDef def=SkyLibrary.Get(i); tileSky[n]=i; tiles[n].gameObject.SetActive(true);
                Label(tiles[n],(simple ? SimpleWeatherNames.Get(i) : def.Name)+"  #"+def.Number,i==model.PinnedSky,ready); n++;
            }
            for(int i=n;i<tiles.Length;i++) {tileSky[i]=-1; tiles[i].gameObject.SetActive(false);}
            Label(auto,"MISSION WEATHER",model.PinnedSky<0 && !model.Forced,ready && (model.PinnedSky>=0 || model.Forced));
            if(clock!=null) clock.text=ready ? TimeOfDayMath.ClockText(model.TimeOfDayHours) : "--:--";
            for(int i=0;i<times.Length;i++)Label(times[i],ClockNames[i],false,ready && (i!=8 || model.TimeChanged));
            Label(reroll,"NEW CLOUD PATTERN",false,ready);
            Label(launch,"DETAILED WEATHER",false,ready);
        }
        private void Update()
        {
            if(settings==null || weather==null) return;
            installer.Tick();
            if(installer.Screen==null || !installer.Screen.isActive) return;
            if(Time.unscaledTime<nextRefresh) return;
            nextRefresh=Time.unscaledTime+.25f; if(showEffects)effects?.Refresh();else Refresh();
        }
        private void LateUpdate()
        {
            // Native MFD layout can move the page after Update when the map opens or resizes.
            if(installer.Screen!=null && installer.Screen.isActive)
            {
                float available=AvLay.ResolveHeight(page!=null?page.parent as RectTransform:null,AvTokens.PanelHeight,AvTokens.PanelHeightMax);
                if(Mathf.Abs(available-layoutHeight)>1f)Reflow(available);
                FitBesideBezel();
            }
        }
        public void ResetForScene() { installer.Reset(); page=weatherPage=effectsPage=weatherContent=cloudPage=null;effects=cloudEffects=null;effectsTab=weatherTab=cloudButton=null;status=clock=cloudStatus=null;technicalButton=null; launch=simpleButton=auto=reroll=null;showCloudSettings=false;touched=false; nextRefresh=0f; }
        private void OnDestroy()=>ResetForScene();
    }

    // Thin framed buttons like the native AIRLIFT/MEDALS controls.  A separate
    // transparent hit surface retains the full click area without an opaque fill.
    internal sealed class MfdOutline : MonoBehaviour
    {
        private readonly Image[] edges=new Image[4];
        private Color normal,active,disabled;
        internal void Build(Color border,Color selected)
        {
            normal=border;active=selected;disabled=border;disabled.a*=.4f;
            for(int i=0;i<edges.Length;i++)
            {
                var go=new GameObject("Frame",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
                go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.raycastTarget=false;edges[i]=image;
                RectTransform t=image.rectTransform;
                if(i==0){t.anchorMin=new Vector2(0,1);t.anchorMax=Vector2.one;t.offsetMin=new Vector2(0,-1.5f);t.offsetMax=Vector2.zero;}
                else if(i==1){t.anchorMin=Vector2.zero;t.anchorMax=new Vector2(1,0);t.offsetMin=Vector2.zero;t.offsetMax=new Vector2(0,1.5f);}
                else if(i==2){t.anchorMin=Vector2.zero;t.anchorMax=new Vector2(0,1);t.offsetMin=Vector2.zero;t.offsetMax=new Vector2(1.5f,0);}
                else{t.anchorMin=new Vector2(1,0);t.anchorMax=Vector2.one;t.offsetMin=new Vector2(-1.5f,0);t.offsetMax=Vector2.zero;}
            }
            Set(false,true);
        }
        internal void Set(bool selected,bool enabled)
        {var c=enabled?(selected?active:normal):disabled;foreach(var edge in edges)if(edge!=null && edge.color!=c)edge.color=c;}
    }
}
