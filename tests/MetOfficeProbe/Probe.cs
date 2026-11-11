using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[BepInPlugin("aaron.boscali.met.extreme.probe", "MET OFFICE isolated mission probe", "0.1.0")]
public sealed class MetProbe : BaseUnityPlugin
{
    internal static int ProtectedWrites;
    private void Awake()
    {
        Application.runInBackground = true;
        var h = new Harmony("aaron.boscali.met.extreme.probe");
        h.Patch(AccessTools.Method(typeof(ScreenResolutionHelper), "LoadPrefs"), prefix: new HarmonyMethod(typeof(MetProbe), nameof(Skip)));
        h.Patch(AccessTools.Method(typeof(ScreenResolutionHelper), "SavePrefs"), prefix: new HarmonyMethod(typeof(MetProbe), nameof(Skip)));
        Type recent = typeof(MissionManager).Assembly.GetType("NuclearOption.SavedMission.RecentGroup", true);
        h.Patch(AccessTools.Method(recent, "SaveToFile"), prefix: new HarmonyMethod(typeof(MetProbe), nameof(Skip)));
        // Preserve gameplay/graphics logic, but keep this isolated probe from persisting
        // the native game's preferences. Sources are the actual installed assembly's
        // managed methods that call PlayerPrefs Set/Save/Delete.
        string[] types = { "AudioMixerVolume", "DebugUI", "PlayerSettings", "GraphicsHelper", "AccessibilityMenu", "ChatSettings", "ControlsMenu", "GameplayMenu", "GraphicsMenu", "HUDSettings", "MainMenu", "TobiiSettingsUI", "NuclearOption.UIStyleSystem.ThemeManager", "NuclearOption.MissionEditorScripts.MissionAutoSaveSettings", "NuclearOption.MissionEditorScripts.MissionEditor", "NuclearOption.Effects.DetailSettings" };
        foreach (string name in types)
        {
            Type type = typeof(MissionManager).Assembly.GetType(name, true);
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetMethodBody() == null || method.ContainsGenericParameters) continue;
                var il = PatchProcessor.GetOriginalInstructions(method);
                if (!il.Any(i => i.operand is MethodInfo call && call.DeclaringType == typeof(PlayerPrefs) && (call.Name.StartsWith("Set") || call.Name.StartsWith("Delete") || call.Name == "Save"))) continue;
                h.Patch(method, transpiler: new HarmonyMethod(typeof(MetProbe), nameof(ProtectPrefs)));
            }
        }
        var root = new GameObject("MET OFFICE isolated probe") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(root);
        root.AddComponent<MetDriver>().Init(Path.GetDirectoryName(Info.Location));
    }
    private static bool Skip() => false;
    private static void SetInt(string key, int value) { ProtectedWrites++; }
    private static void SetFloat(string key, float value) { ProtectedWrites++; }
    private static void SetString(string key, string value) { ProtectedWrites++; }
    private static void DeleteKey(string key) { ProtectedWrites++; }
    private static void DeleteAll() { ProtectedWrites++; }
    private static void Save() { ProtectedWrites++; }
    private static IEnumerable<CodeInstruction> ProtectPrefs(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var i in instructions)
        {
            if (i.operand is MethodInfo call && call.DeclaringType == typeof(PlayerPrefs) && (call.Name.StartsWith("Set") || call.Name.StartsWith("Delete") || call.Name == "Save"))
            {
                MethodInfo replacement = AccessTools.Method(typeof(MetProbe), call.Name, call.GetParameters().Select(p => p.ParameterType).ToArray());
                if (replacement != null) { i.opcode = OpCodes.Call; i.operand = replacement; }
            }
            yield return i;
        }
    }
}

public sealed partial class MetDriver : MonoBehaviour
{
    private const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const string OwnerId = "aaron.boscali.weather-unified";
    private static readonly string[] PrivateIds = { "aaron.boscali.unified", "aaron.boscali.krop-unified", "aaron.boscali.betternight", "aaron.boscali.missile-unified", "aaron.boscali.control-unified", "aaron.boscali.cockpit-audio", "aaron.boscali.cockpitfeel", "aaron.boscali.airfield-lighting" };
    private static readonly string[] PrivateAssemblies = { "BoscaliUnified", "KROP.UnifiedEffects", "RealisticNight.Unified", "Boscali.UnifiedMissileBoom", "ClientEffectsControlPanel", "Boscali.CockpitAudio", "Boscali.CockpitFeel", "Boscali.AirfieldLighting" };
    private static IDictionary<string,PluginInfo> ModuleInfos()
    {
        var owner=Chainloader.PluginInfos[OwnerId].Instance;
        var registry=owner.GetType().Assembly.GetType("UnifiedEffects.ModuleRegistry",true);
        return (IDictionary<string,PluginInfo>)registry.GetMethod("GetPluginInfos",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
    }
    private void TestSinglePluginLayout()
    {
        Need(Chainloader.PluginInfos.ContainsKey(OwnerId),"single physical pack plugin loaded");
        Need(PrivateIds.All(id=>!Chainloader.PluginInfos.ContainsKey(id)),"all eight private modules are absent from global plugin discovery");
        var owner=Chainloader.PluginInfos[OwnerId];var modules=ModuleInfos();
        var ids=PrivateIds.Concat(new[]{OwnerId}).ToArray();
        foreach(string id in ids) {
            Need(modules.TryGetValue(id,out var info) && info.Instance!=null,"full-pack module loaded "+id);
            Need(info.Metadata.GUID==id && Path.GetFileName(info.Instance.Config.ConfigFilePath)==id+".cfg","existing module config identity preserved "+id);
        }
        Need(ReferenceEquals(modules[OwnerId].Instance,owner.Instance),"NOAtmoFX weather GUID still resolves the actual weather instance");
        Need(owner.Instance.GetType().Assembly.GetTypes().Count(t=>t.GetCustomAttributes(typeof(BepInPlugin),false).Length>0)==1,"physical assembly exposes exactly one BepInPlugin entry");
        string folder=Path.GetDirectoryName(owner.Location);
        var files=Directory.GetFiles(folder,"*",SearchOption.AllDirectories);
        Need(files.Length==1 && Path.GetFileName(files[0])=="UnifiedEffects.dll","one physical runtime file in UnifiedEffects folder");
        Need(owner.Instance.GetType().Assembly.GetType("BoscaliSummer.Modules.Weather.Runtime.WeatherManager",false)!=null,"root assembly retains NOAtmoFX weather reflection contract");
    }
    private void VerifyLegacyShortcut(string stage)
    {
        if(!File.Exists(Path.Combine(dir,"config-preservation.txt")))return;
        var config=ModuleInfos()["aaron.boscali.control-unified"].Instance.Config;
        Need(config.TryGetEntry<KeyboardShortcut>(new ConfigDefinition("Controls","Open effects panel"),out var key) && key.Value.MainKey==KeyCode.Home && !key.Value.Modifiers.Any(),"non-default legacy control shortcut preserved "+stage);
        string saved=File.ReadAllText(config.ConfigFilePath);
        Need(saved.Contains("Open effects panel = Home") && saved.Contains("Unbound sentinel = keep-me"),"legacy setting and unbound config value retained on disk "+stage);
    }
    private void TestDoorman(bool disabled)
    {
        var da=Chainloader.PluginInfos["kelly.nuclearoption.joincheck"].Instance.GetType().Assembly;
        var catalog=da.GetType("KellysJOINCHECK.ModCatalog",true);var profile=Activator.CreateInstance(da.GetType("KellysJOINCHECK.ModProfile",true),true);
        if(disabled) FI(profile,"DisabledPlugins").SetValue(profile,new[]{OwnerId});
        var contents=Array.CreateInstance(da.GetType("KellysJOINCHECK.InstalledContent",true),0);
        var all=catalog.GetMethod("Read",F).Invoke(null,new object[]{contents,profile,(Action<string>)(x=>Note("INFO doorman "+x))});
        var menu=da.GetType("KellysJOINCHECK.ModMenuEntries",true);var visible=menu.GetMethod("Visible",F);
        Need(menu.GetField("UnifiedEffects",F)==null && menu.GetMethod("UnifiedGroup",F)==null,"installed DOORMAN has no special-case Unified Effects grouping");
        Func<object,bool> ours=x=>((string)Field(x,"Id")).StartsWith("aaron.boscali.") && (string)Field(x,"Id")!="aaron.boscali.met.extreme.probe";
        var entries=((IEnumerable)all).Cast<object>().Where(ours).ToArray();
        var shown=((IEnumerable)visible.Invoke(null,new[]{all})).Cast<object>().ToArray();
        Need(entries.Length==1 && shown.Count(ours)==1,"actual DOORMAN inventory and menu each contain one pack entry");
        var owner=entries.Single();var toggle=menu.GetMethod("ToggleGroup",F);
        Need((string)Field(owner,"Id")==OwnerId && Path.GetFileName((string)Field(owner,"Path"))=="UnifiedEffects.dll","DOORMAN pack owner is the single runtime DLL");
        Need((bool)Field(owner,"Enabled")==!disabled && (bool)Field(owner,"Wanted")==!disabled,"DOORMAN reflects actual boot enabled state");
        var addon=((IEnumerable)all).Cast<object>().SingleOrDefault(x=>(string)Field(x,"Id")=="paul.no.clouds");
        if(disabled) Need((bool)toggle.Invoke(null,new[]{owner,all}) && (bool)Field(owner,"Wanted"),"DOORMAN re-enable selects the complete disabled pack");
        Need(!(bool)toggle.Invoke(null,new[]{owner,all}) && !(bool)Field(owner,"Wanted"),"DOORMAN disable selects the complete pack");
        FI(owner,"Enabled").SetValue(owner,false);
        shown=((IEnumerable)visible.Invoke(null,new[]{all})).Cast<object>().ToArray();
        Need(shown.Count(ours)==1,"disabled pack remains one recoverable DOORMAN entry");
        Need((bool)toggle.Invoke(null,new[]{owner,all}) && (bool)Field(owner,"Wanted"),"DOORMAN re-enable selects the complete pack");
        if(File.Exists(Path.Combine(dir,"noatmo.txt"))) Need(addon!=null && shown.Any(x=>ReferenceEquals(x,addon)) && (bool)Field(addon,"Wanted"),"NOAtmoFX remains separate and unaffected by pack toggles");
    }
    private void RunDisabledBoot()
    {
        bool passed=false;
        try {
            Need(!Chainloader.PluginInfos.ContainsKey(OwnerId),"DOORMAN startup excludes the single pack owner");
            Need(PrivateIds.All(id=>!Chainloader.PluginInfos.ContainsKey(id)),"no private module globals loaded when pack disabled");
            Need(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>PrivateAssemblies.Contains(a.GetName().Name)),"no embedded effect assembly initialized when pack disabled");
            Need(!File.Exists(Path.Combine(Paths.ConfigPath,OwnerId+".cfg")) && PrivateIds.All(id=>!File.Exists(Path.Combine(Paths.ConfigPath,id+".cfg"))),"disabled pack does not create owner or private module configs");
            TestDoorman(true);
            Note("LIMIT disabled boot verifies loader exclusion and actual DOORMAN inventory; toggles are in-memory preview actions and do not restart the game.");
            passed=true;
        } catch(Exception e) { Note("EXCEPTION "+e); }
        File.WriteAllText(Path.Combine(dir,"result.txt"),(passed?"PASS\n":"FAIL\n")+string.Join("\n",notes));
        Application.Quit();
    }
    private readonly List<string> notes = new List<string>();
    private string dir;
    private bool started;
    private Assembly assembly;
    private object manager, console;
    private Type controlType;
    private Camera camera;
    internal void Init(string folder) => dir = folder;
    private void Note(string note) { notes.Add(note); File.WriteAllLines(Path.Combine(dir, "progress.txt"), notes); }
    private void Need(bool ok, string label) { Note((ok ? "PASS " : "FAIL ") + label); if (!ok) throw new Exception(label); }
    private static FieldInfo FI(object o, string name) => o.GetType().GetField(name, F) ?? throw new Exception("Missing field " + name);
    private static object Field(object o, string name) => FI(o,name).GetValue(o);
    private static object Prop(object o, string name) => o.GetType().GetProperty(name,F).GetValue(o,null);
    private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name,F).Invoke(o,args);
    private object Static(string name, string method, params object[] args) => assembly.GetType(name,true).GetMethod(method,F).Invoke(null,args);
    private bool Press(string name, int sky=-1) => (bool)Call(console,"Press",Enum.Parse(controlType,name),sky);
    private static float Clock() => NetworkSceneSingleton<MissionManager>.i.MissionTime;
    private static bool Near(float a,float b,float eps=.002f) => Mathf.Abs(a-b)<eps;
    private void Update()
    {
        if (started) return;
        if (Time.realtimeSinceStartup>8 && FindObjectOfType<MainMenu>()!=null && NetworkManagerNuclearOption.i!=null) { started=true; Run().Forget(); }
        else if (Time.realtimeSinceStartup>60) { started=true; File.WriteAllText(Path.Combine(dir,"result.txt"),"FAIL waiting for menu"); Application.Quit(); }
    }
    private async UniTask Until(Func<bool> condition,float seconds,string label)
    {
        float deadline=Time.realtimeSinceStartup+seconds;
        while(!condition() && Time.realtimeSinceStartup<deadline) await UniTask.Yield();
        Need(condition(),label);
    }
    private int VerifyGroundedBeacon(ParticleSystemRenderer renderer,string label)
    {
        var ps=renderer.GetComponent<ParticleSystem>();var particles=new ParticleSystem.Particle[ps.particleCount];int n=ps.GetParticles(particles);float maxOffset=0f;
        for(int i=0;i<n;i++)
        {
            Vector3 world=renderer.transform.TransformPoint(particles[i].position);RaycastHit hit;
            bool found=Physics.Raycast(world+Vector3.up*2f,Vector3.down,out hit,4f,PhysicsLayers.StaticsMask,QueryTriggerInteraction.Ignore);
            if(!found || hit.point.y<=Datum.LocalSeaY+.1f || Mathf.Abs(world.y-hit.point.y)>.15f)
                throw new Exception(label+" ungrounded marker at "+world+" hit="+(found?hit.collider.name+" y="+hit.point.y:"none"));
            maxOffset=Mathf.Max(maxOffset,Mathf.Abs(world.y-hit.point.y));
        }
        if(n>0)Note("GROUND "+label+" points="+n+" maxOffset="+maxOffset.ToString("F3"));
        return n;
    }
    private int VerifyGroundedMesh(GameObject group,string label)
    {
        var mesh=group.GetComponent<MeshFilter>().sharedMesh;Vector3[] vertices=mesh.vertices;int lamps=vertices.Length/12;
        for(int i=0;i<lamps;i++)
        {
            Vector3 center=group.transform.TransformPoint((vertices[i*12]+vertices[i*12+1])*.5f);RaycastHit hit;
            bool found=Physics.Raycast(center+Vector3.up*2f,Vector3.down,out hit,4f,PhysicsLayers.StaticsMask,QueryTriggerInteraction.Ignore);
            if(!found || hit.point.y<=Datum.LocalSeaY+.1f || Mathf.Abs(center.y-hit.point.y)>.15f)
                throw new Exception(label+" ungrounded fixture at "+center+" hit="+(found?hit.collider.name:"none"));
        }
        Note("GROUND "+label+" mesh fixtures="+lamps);return lamps;
    }
    private bool IsPavedAt(Vector3 point)
    {
        RaycastHit hit;if(!Physics.Raycast(point+Vector3.up*12f,Vector3.down,out hit,30f,PhysicsLayers.StaticsMask,QueryTriggerInteraction.Ignore))return false;
        string n=hit.collider.name;return n.IndexOf("road",StringComparison.OrdinalIgnoreCase)>=0 || n.IndexOf("runway",StringComparison.OrdinalIgnoreCase)>=0 || n.IndexOf("pavement",StringComparison.OrdinalIgnoreCase)>=0 || n.IndexOf("asphalt",StringComparison.OrdinalIgnoreCase)>=0;
    }
    private void VerifyAirfieldGeometry(object field,Airbase baseObject,string label)
    {
        var ends=(System.Collections.IList)Field(field,"ends");Need(ends.Count>=2,label+" has grounded runway ends");
        int samples=VerifyGroundedMesh((GameObject)Field(field,"taxi"),label+" taxi")+VerifyGroundedMesh((GameObject)Field(field,"pads"),label+" helipad");
        foreach(var e in ends)
        {
            foreach(string k in new[]{"greenBeacon","redBeacon","steadyBeacon"}) samples+=VerifyGroundedBeacon((ParticleSystemRenderer)Field(e,k),label+" "+k);
            foreach(var beacon in (ParticleSystemRenderer[])Field(e,"flashBeacon"))samples+=VerifyGroundedBeacon(beacon,label+" flash");
            foreach(var beacon in (ParticleSystemRenderer[])Field(e,"papiWhiteBeacon"))samples+=VerifyGroundedBeacon(beacon,label+" papi");
            Vector3 pos=(Vector3)Field(e,"position"),outward=(Vector3)Field(e,"outward");Vector3 world=new GlobalPosition(pos.x,pos.y,pos.z).ToLocalPosition();
            var closest=baseObject.runways.Where(x=>x!=null && x.Start!=null && x.End!=null).OrderBy(x=>Mathf.Min((x.Start.position-world).sqrMagnitude,(x.End.position-world).sqrMagnitude)).First();
            float half=closest.GetWidth()*.5f;Vector3 side=Vector3.Cross(Vector3.up,outward).normalized;
            bool inner=IsPavedAt(world-outward*3f) && IsPavedAt(world-outward*3f+side*half*.7f) && IsPavedAt(world-outward*3f-side*half*.7f);
            int outer=(IsPavedAt(world+outward*5f)?1:0)+(IsPavedAt(world+outward*5f+side*half*.7f)?1:0)+(IsPavedAt(world+outward*5f-side*half*.7f)?1:0);
            Need(inner && outer<3,label+" threshold is at the detected end of the runway pavement");
        }
        Need(samples>100,label+" inspected "+samples+" markers against terrain and dry land");
    }
    private async UniTask TestLighting()
    {
        Need(ModuleInfos().TryGetValue("aaron.boscali.airfield-lighting",out var info),"airfield prototype plugin loaded");
        var type=info.Instance.GetType().Assembly.GetType("BoscaliAirfieldLighting.LightingRuntime",true);
        var all=Resources.FindObjectsOfTypeAll(type);
        Need(all.Length==1,"one persistent lighting controller");
        var runtime=(Component)all[0];
        var aircraftCamera=SceneSingleton<CameraStateManager>.i;
        var cameraField=AccessTools.Field(typeof(CameraStateManager),"mainCamera");
        var originalCamera=cameraField.GetValue(aircraftCamera);
        bool originalDay=LevelInfo.i.isDayLight;
        float originalTime=LevelInfo.i.timeOfDay;
        var target=FindObjectsOfType<Airbase>().FirstOrDefault(a=>a!=null && !a.AttachedAirbase && a.center!=null && a.runways!=null && a.runways.Length>0 && a.runways[0].Start!=null && a.runways[0].End!=null && a.hangars!=null && a.hangars.Any(h=>h!=null && h.name.IndexOf("shelter",StringComparison.OrdinalIgnoreCase)>=0));
        Need(target!=null,"land airbase with runway and hangars found");
        Need(originalCamera is Camera,"native camera available for lighting capture mask");
        var go=new GameObject("Airfield prototype test camera");
        var test=go.AddComponent<Camera>();go.AddComponent<UniversalAdditionalCameraData>();test.farClipPlane=20000f;test.fieldOfView=55f;
        test.cullingMask=((Camera)originalCamera).cullingMask;
        Note("INFO lighting capture masks native="+((Camera)originalCamera).cullingMask+" test="+test.cullingMask);
        try
        {
            var r=target.runways[0];Vector3 d=(r.End.position-r.Start.position).normalized;
            Note("SURVEY field="+target.name+" center="+target.center.position+" sea="+Datum.LocalSeaY);
            foreach(var t in target.GetComponentsInChildren<Transform>(true))
                if(t.name.IndexOf("runway",StringComparison.OrdinalIgnoreCase)>=0 || t.name.IndexOf("light",StringComparison.OrdinalIgnoreCase)>=0 || t.name.IndexOf("threshold",StringComparison.OrdinalIgnoreCase)>=0)
                {var rr=t.GetComponent<Renderer>();var ll=t.GetComponent<Light>();var ps=t.GetComponent<ParticleSystem>();Note("NATIVE "+t.name+" world="+t.position+" renderer="+(rr!=null?rr.GetType().Name:"-")+" light="+(ll!=null)+" particles="+(ps!=null));}
            foreach(var rr in target.runways) if(rr!=null && rr.Start!=null && rr.End!=null)
            {
                Vector3 a=rr.Start.position,b=rr.End.position;Vector3 dir=(b-a).normalized;
                Note("SURVEY runway="+rr.index+" start="+a+" end="+b+" width="+rr.GetWidth()+" startName="+rr.Start.name+" endName="+rr.End.name);
                foreach(float along in new[]{-732f,-300f,-100f,0f,10f,100f})
                {
                    Vector3 q=a+dir*along;var hits=Physics.RaycastAll(q+Vector3.up*100f,Vector3.down,300f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).OrderBy(x=>Mathf.Abs(x.point.y-q.y)).Take(4).ToArray();
                    Note("SURFACE "+rr.index+" offset="+along+" base="+q+" hits="+string.Join(" | ",hits.Select(x=>x.collider.name+" y="+x.point.y.ToString("F2")+" n="+x.normal.y.ToString("F2")+" layer="+x.collider.gameObject.layer+" mat="+(x.collider.sharedMaterial!=null?x.collider.sharedMaterial.name:"null")).ToArray()));
                }
            }
            foreach(var ab in FindObjectsOfType<Airbase>().Where(x=>x!=null && !x.AttachedAirbase && x.runways!=null)) foreach(var runwayScan in ab.runways)
            {
                if(runwayScan==null || runwayScan.Start==null || runwayScan.End==null)continue;
                Vector3 aa=runwayScan.Start.position,bb=runwayScan.End.position;Vector3 forward=Vector3.ProjectOnPlane(bb-aa,Vector3.up).normalized;Vector3 right=Vector3.Cross(Vector3.up,forward);float half=runwayScan.GetWidth()*.5f;
                foreach(int endIndex in new[]{0,1})
                {
                    Vector3 node=endIndex==0?aa:bb;Vector3 outward=endIndex==0?-forward:forward;
                    string line="PAVEMENT "+ab.name+" runway="+runwayScan.index+" end="+endIndex;
                    foreach(float distance in new[]{-30f,0f,30f,60f,100f,150f,200f,300f,400f})
                    {
                        string code="";
                        foreach(float across in new[]{-.7f,0f,.7f})
                        {Vector3 q=node+outward*distance+right*(half*across);RaycastHit hit;bool ok=Physics.Raycast(q+Vector3.up*100f,Vector3.down,out hit,300f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);code+=!ok?"-":hit.collider.name.IndexOf("roads",StringComparison.OrdinalIgnoreCase)>=0?"R":"T";}
                        line+=" "+distance+":"+code;
                    }
                    Note(line);
                }
            }
            test.transform.position=r.Start.position-d*260f+Vector3.up*65f;
            test.transform.LookAt(r.Start.position+d*400f);
            cameraField.SetValue(aircraftCamera,test);camera=test;LevelInfo.i.SetTimeOfDay(0f);LevelInfo.i.isDayLight=false;
            for(int i=0;i<4;i++){FI(runtime,"nextUpdate").SetValue(runtime,0f);Call(runtime,"Update");await UniTask.Yield();}
            var fields=(System.Collections.IList)Field(runtime,"fields");
            Need(fields.Count>0 && fields.Count<=3,"nearby land airfield generated under field budget");
            object field=null;foreach(var f in fields) if(ReferenceEquals(Field(f,"airbase"),target))field=f;
            Need(field!=null,"selected airfield lights generated");
            VerifyAirfieldGeometry(field,target,target.name);
            var fieldClass=type.Assembly.GetType("BoscaliAirfieldLighting.Field",true);
            foreach(string name in new[]{"airbase_NW","airbase_city","airbase_island7","airbase_island5","airbase_island14","airbase_island9","airbase_SW","airbase_SE"})
            {
                Airbase sample=FindObjectsOfType<Airbase>().FirstOrDefault(x=>x!=null && x.name==name);
                Need(sample!=null,"survey field available "+name);var watch=System.Diagnostics.Stopwatch.StartNew();
                object other=Activator.CreateInstance(fieldClass,F,null,new object[]{sample,Datum.origin,Field(runtime,"material"),Field(runtime,"beaconMaterial")},null);watch.Stop();
                try {Note("INFO "+name+" lighting creation "+watch.ElapsedMilliseconds+" ms");VerifyAirfieldGeometry(other,sample,name);if(name=="airbase_NW")Need(((System.Collections.IList)Field(other,"pairs")).Count==2,"overlapping sub-runways do not duplicate approach lighting");}
                finally {Call(other,"Dispose");}
            }
            Need((int)Prop(field,"Count")>50,"runway and taxi markers constructed");
            var root=(GameObject)Field(field,"root");
            Need(root.transform.parent==Datum.origin,"airfield meshes use floating-origin parent");
            cameraField.SetValue(aircraftCamera,null);Call(runtime,"Update");Need(!root.activeSelf,"missing camera hides owned marker geometry");
            cameraField.SetValue(aircraftCamera,test);Call(runtime,"Update");Need(root.activeSelf,"marker geometry returns with camera");
            var runway=(GameObject)Field(field,"runway");var taxi=(GameObject)Field(field,"taxi");var pads=(GameObject)Field(field,"pads");
            Need(runway.activeSelf && taxi.activeSelf && pads.activeSelf,"runway taxiway and helipad groups active at night");
            var renderer=runway.GetComponent<MeshRenderer>();
            Need(renderer!=null && renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount==0,"native runway edge lights are not duplicated");
            
            var ends=(System.Collections.IList)Field(field,"ends");
            Need(ends.Count>=2,"both runway ends have separate directional markers");
            var e=ends[0];var opposite=ends[1]; var green=(MeshRenderer)Field(e,"green");var red=(MeshRenderer)Field(e,"red");
            var flash=info.Instance.Config.Bind("Airfield lighting","Legacy flashing runway ends",false);flash.Value=false;
            FI(runtime,"nextUpdate").SetValue(runtime,0f);Call(runtime,"Update");
            Need(green.enabled && !red.enabled,"approach sees green threshold lights");
            var steady=(MeshRenderer)Field(e,"steady");var steadyParticles=(ParticleSystemRenderer)Field(e,"steadyBeacon");
            Need(steady.enabled && steadyParticles.enabled && steadyParticles.GetComponent<ParticleSystem>().particleCount>20,"grounded approach bars are present");
            var phase0=(MeshRenderer[])Field(e,"flash");Need(phase0.Length==24,"all 24 approach bars can pulse");
            var flashBeacons=(ParticleSystemRenderer[])Field(e,"flashBeacon");
            Need(flashBeacons.All(x=>x.GetComponent<ParticleSystem>().particleCount<=5) && flashBeacons.Any(x=>x.GetComponent<ParticleSystem>().particleCount==5),"valid flashing bars have up to five lights");
            Need(phase0.All(x=>x.GetComponent<MeshFilter>().sharedMesh.vertexCount<=60),"each grounded bar has at most five compact lamps");
            Need(steadyParticles.GetComponent<ParticleSystem>().main.startSize.constant<.3f && flashBeacons[0].GetComponent<ParticleSystem>().main.startSize.constant<.4f,"steady and flash lamps stay compact near camera");
            Vector3 globalEye=test.transform.GlobalPosition().AsVector3();
            var papiWhite=(MeshRenderer[])Field(e,"papiWhite");var papiRed=(MeshRenderer[])Field(e,"papiRed");
            Vector3 papi=(Vector3)Field(e,"papiPosition");Vector3 outwards=(Vector3)Field(e,"outward");
            foreach(var sample in new[]{new[]{2.2f,0f},new[]{2.7f,1f},new[]{3.0f,2f},new[]{3.3f,3f},new[]{3.8f,4f}})
            {
                Vector3 sight=papi+outwards*2000f+Vector3.up*(Mathf.Tan(sample[0]*Mathf.Deg2Rad)*2000f);
                Call(field,"Set",true,true,true,true,true,true,false,sight,0f);
                Need(papiWhite.Count(x=>x.enabled)==(int)sample[1] && papiRed.Count(x=>x.enabled)==4-(int)sample[1],"PAPI angle "+sample[0]+" gives "+sample[1]+" white lamps");
            }
            Call(field,"Set",true,true,true,true,true,true,false,globalEye,0f);
            Need(phase0[23].enabled,"strobe starts at 2400 feet");
            Call(field,"Set",true,true,true,true,true,true,false,globalEye,.481f);
            Need(phase0[0].enabled && !phase0[23].enabled,"strobe reaches the nearest row");
            Call(field,"Set",true,true,true,true,true,true,false,globalEye,.5f);
            Need(phase0[23].enabled,"strobe repeats twice per second");
            // A real rendered A/B pair, with no simulation step between captures.
            test.transform.position=r.Start.position-d*900f+Vector3.up*55f;test.transform.LookAt(r.Start.position+d*250f);
            Vector3 captureEye=test.transform.GlobalPosition().AsVector3();
            Call(field,"Set",true,true,true,true,true,true,false,captureEye,.15f);Capture("approach-row-flash-on.png");
            Call(field,"Set",true,true,true,true,true,false,false,captureEye,.15f);Capture("approach-row-flash-off.png");
            double rowDelta=ImageDelta("approach-row-flash-on.png","approach-row-flash-off.png");
            Note("INFO five-lamp strobe rendered pixel difference="+rowDelta);
            Need(rowDelta>.00000001f,"five-lamp strobe visibly changes rendered pixels");
            Need(steady.enabled && !phase0.Any(x=>x.enabled),"strobe switch leaves steady bars on");
            test.transform.position=r.Start.position+d*80f+Vector3.up*5f;Call(runtime,"Update");
            Need(green.enabled && !red.enabled && !steady.enabled,"rollout retains threshold direction and hides outward-facing bars");
            test.transform.position=r.End.position+d*250f+Vector3.up*50f;Call(runtime,"Update");
            Need(!green.enabled && red.enabled && ((MeshRenderer)Field(opposite,"green")).enabled && ((MeshRenderer)Field(opposite,"steady")).enabled,"opposite approach swaps directional lights");
            test.transform.position=r.Start.position-d*260f+Vector3.up*65f;Call(runtime,"Update");
            Need(green.enabled && ((MeshRenderer)Field(e,"steady")).enabled,"return approach swaps back");
            test.transform.position=r.Start.position-d*260f+Vector3.up*65f;test.transform.LookAt(r.Start.position+d*400f);
            Call(runtime,"Update");Capture("airfield-runway.png");
            var rw=info.Instance.Config.Bind("Airfield lighting","Runways",true);
            var tx=info.Instance.Config.Bind("Airfield lighting","Taxiways",true);
            rw.Value=false;tx.Value=false;Call(runtime,"Update");Capture("airfield-runway-off.png");
            Need(!File.ReadAllBytes(Path.Combine(dir,"airfield-runway.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(dir,"airfield-runway-off.png"))),"runway and taxi markers change rendered pixels");
            rw.Value=true;tx.Value=true;Call(runtime,"Update");
            var nativeDuplicateBeacon=(ParticleSystemRenderer)Field(field,"runwayBeacons");
            Need(nativeDuplicateBeacon.GetComponent<ParticleSystem>().particleCount==0,"native runway beacons are not duplicated");
            var runwayBeacon=steadyParticles;
            Need(runwayBeacon!=null && runwayBeacon.GetComponent<ParticleSystem>().particleCount>0 && runwayBeacon.enabled,"grounded approach beacons are active");
            test.transform.position=r.Start.position-d*1700f+Vector3.up*105f;test.transform.LookAt(r.Start.position+d*400f);Call(runtime,"Update");Capture("papi-three-degree-approach.png");
            test.transform.position=r.Start.position-d*1700f+Vector3.up*250f;test.transform.LookAt(r.Start.position+d*400f);Call(runtime,"Update");
            Capture("runway-1700m-on.png");runwayBeacon.enabled=false;Capture("runway-1700m-beacons-off.png");
            var beaconDelta=ImageDelta("runway-1700m-on.png","runway-1700m-beacons-off.png");
            Note("INFO isolated beacon delta="+beaconDelta+" bounds="+runwayBeacon.bounds);
            Need(beaconDelta>.0000001,"distant beacons alone produce visible runway pixels");
            runwayBeacon.enabled=true;rw.Value=false;Call(runtime,"Update");Capture("runway-1700m-off.png");
            Need(!runwayBeacon.enabled,"approach lights obey the runway switch");
            rw.Value=true;Call(runtime,"Update");
            test.transform.position=r.Start.position-d*4000f+Vector3.up*500f;test.transform.LookAt(r.Start.position+d*400f);Call(runtime,"Update");Capture("runway-4000m.png");
            test.transform.position=(r.Start.position+r.End.position)*.5f+Vector3.up*4000f;test.transform.rotation=Quaternion.LookRotation(Vector3.down,Vector3.forward);Call(runtime,"Update");Capture("runway-above.png");
            Need(runwayBeacon.minParticleSize<=.0015f && runwayBeacon.maxParticleSize<=.0061f,"distant beacon apparent size stays bounded from altitude");
            test.transform.position=r.Start.position+d*130f+Vector3.up*10f;test.transform.LookAt(r.Start.position);Call(runtime,"Update");Capture("airfield-runway-red-end.png");
            var taxiVertices=taxi.GetComponent<MeshFilter>().sharedMesh.vertices;
            if(taxiVertices.Length>24)
            {
                var first=taxi.transform.TransformPoint(taxiVertices[0]);var next=taxi.transform.TransformPoint(taxiVertices[24]);var heading=Vector3.ProjectOnPlane(next-first,Vector3.up).normalized;
                test.transform.position=first-heading*35f+Vector3.up*12f;test.transform.LookAt(first+heading*40f);Call(runtime,"Update");Capture("airfield-taxiway.png");
                tx.Value=false;Call(runtime,"Update");Capture("airfield-taxiway-off.png");tx.Value=true;
            }
            var pad=target.hangars.FirstOrDefault(h=>h!=null && h.name.IndexOf("helipad",StringComparison.OrdinalIgnoreCase)>=0 && h.GetSpawnTransform()!=null);
            if(pad!=null)
            {
                var p=pad.GetSpawnTransform().position;test.transform.position=p+new Vector3(0f,38f,-48f);test.transform.LookAt(p);Call(runtime,"Update");Capture("airfield-helipad.png");
                var padSwitch=info.Instance.Config.Bind("Airfield lighting","Helipads",true);padSwitch.Value=false;Call(runtime,"Update");Capture("airfield-helipad-off.png");padSwitch.Value=true;
            }
            tx.Value=false;Call(runtime,"Update");Need(!taxi.activeSelf && runway.activeSelf,"taxi toggle independently disables taxi lighting");tx.Value=true;
            rw.Value=false;Call(runtime,"Update");Need(!runway.activeSelf && taxi.activeSelf,"runway toggle leaves taxi lighting active");rw.Value=true;
            var hp=info.Instance.Config.Bind("Airfield lighting","Helipads",true);hp.Value=false;Call(runtime,"Update");Need(!pads.activeSelf,"helipad toggle disables pad markers");hp.Value=true;
            var hangar=target.hangars.FirstOrDefault(h=>h!=null && h.name.IndexOf("hangar",StringComparison.OrdinalIgnoreCase)>=0 && h.GetSpawnTransform()!=null)
                ?? target.hangars.FirstOrDefault(h=>h!=null && h.name.IndexOf("shelter",StringComparison.OrdinalIgnoreCase)>=0 && h.GetSpawnTransform()!=null);
            if(hangar!=null)
            {
                var siteToggle=info.Instance.Config.Bind("Airfield lighting","Industrial and ports",true);siteToggle.Value=false;
                float previousScale=Time.timeScale;Time.timeScale=0f;
                test.transform.position=hangar.GetSpawnTransform().position+Vector3.up*2.5f;
                test.transform.rotation=hangar.GetSpawnTransform().rotation;FI(runtime,"nextLampUpdate").SetValue(runtime,0f);Call(runtime,"Update");
                Need((int)Prop(runtime,"ActiveLocalLamps")>0 && (int)Prop(runtime,"ActiveLocalLamps")<=4,"nearby hangar lighting is active and capped");
                var liveLights=((System.Collections.IEnumerable)Field(runtime,"lamps")).Cast<Light>().Where(x=>x!=null && x.enabled).ToArray();
                Need(liveLights.Any(x=>Vector3.Distance(x.transform.position,hangar.GetSpawnTransform().position)<10f),"owned light positioned inside target hangar");
                foreach(var light in liveLights) Note("INFO hangar light "+light.transform.position+" range="+light.range+" intensity="+light.intensity+" mask="+light.cullingMask);
                foreach(var hRenderer in hangar.GetComponentsInChildren<Renderer>().Take(3)) Note("INFO hangar shader "+hRenderer.name+" "+(hRenderer.sharedMaterial!=null && hRenderer.sharedMaterial.shader!=null?hRenderer.sharedMaterial.shader.name:"none"));
                var pipeline=GraphicsSettings.currentRenderPipeline;
                var maxLights=pipeline.GetType().GetProperty("maxAdditionalLightsCount",F);
                Need((int)maxLights.GetValue(pipeline,null)>=4,"hangar temporarily enables additional surface lighting");
                int previousBudget=(int)Field(runtime,"previousLightLimit");bool borrowed=Field(runtime,"leasedPipeline")!=null;
                if(borrowed){maxLights.SetValue(pipeline,2,null);Call(runtime,"UpdateLightBudget",true);Call(runtime,"UpdateLightBudget",false);
                    Need((int)maxLights.GetValue(pipeline,null)==2,"later external graphics setting is preserved");
                    maxLights.SetValue(pipeline,previousBudget,null);Call(runtime,"UpdateLightBudget",true);
                    Need((int)maxLights.GetValue(pipeline,null)>=4,"hangar lighting reacquires budget after leaving and returning");}
                await UniTask.Delay(250,ignoreTimeScale:true);
                test.transform.position=hangar.GetSpawnTransform().position+Vector3.up*2.5f;test.transform.rotation=hangar.GetSpawnTransform().rotation;
                Vector3 fixedPosition=test.transform.position;Quaternion fixedRotation=test.transform.rotation;
                var oldAmbientMode=RenderSettings.ambientMode;var oldAmbient=RenderSettings.ambientLight;float oldSun=LevelInfo.i.sun.intensity;
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;LevelInfo.i.sun.intensity=0f;
                Capture("airfield-hangar.png");
                var hg=info.Instance.Config.Bind("Airfield lighting","Hangars",true);hg.Value=false;FI(runtime,"nextLampUpdate").SetValue(runtime,0f);Call(runtime,"Update");
                Need((int)Prop(runtime,"ActiveLocalLamps")==0,"hangar toggle extinguishes owned point lights");
                Need(!((GameObject)Field(field,"hangarFixtures")).activeSelf,"hangar toggle hides its fixtures");
                Need(!borrowed || (int)maxLights.GetValue(pipeline,null)==previousBudget,"hangar toggle restores original lighting budget");
                test.transform.position=fixedPosition;test.transform.rotation=fixedRotation;Capture("airfield-hangar-off.png");
                Need(ImageDelta("airfield-hangar.png","airfield-hangar-off.png")>.001,"hangar lighting changes actual interior surface pixels");
                RenderSettings.ambientMode=oldAmbientMode;RenderSettings.ambientLight=oldAmbient;LevelInfo.i.sun.intensity=oldSun;
                hg.Value=true;siteToggle.Value=true;Time.timeScale=previousScale;
            }
            var knownSites=(System.Collections.IList)Field(runtime,"siteCandidates");
            Need(knownSites.Count>0,"native airfield service sites discovered");
            for(int i=0;i<8;i++){FI(runtime,"nextUpdate").SetValue(runtime,0f);Call(runtime,"Update");}
            Need((int)Prop(runtime,"ActiveSites")>0 && (int)Prop(runtime,"ActiveSites")<=8,"nearby native site markers created within budget");
            var candidates=(System.Collections.IList)Field(runtime,"siteCandidates");var existingSites=(System.Collections.IList)Field(runtime,"sites");
            var savedSites=existingSites.Cast<object>().ToArray();var savedCandidates=candidates.Cast<object>().ToArray();
            var fixtures=new List<GameObject>();existingSites.Clear();candidates.Clear();
            var siteClass=type.Assembly.GetType("BoscaliAirfieldLighting.Site",true);
            try
            {
                for(int i=0;i<8;i++)
                {
                    var far=new GameObject("warehouse_far_"+i);fixtures.Add(far);far.transform.position=test.transform.position+new Vector3(600+i*20,0,600);
                    existingSites.Add(Activator.CreateInstance(siteClass,F,null,new object[]{far.transform,Datum.origin,Field(runtime,"material")},null));
                }
                var nearestSite=new GameObject("warehouse_nearest");fixtures.Add(nearestSite);nearestSite.transform.position=test.transform.position;candidates.Add(nearestSite.transform);
                Call(runtime,"Maintain",test);
                bool found=false;foreach(var s in existingSites)if(ReferenceEquals(Field(s,"source"),nearestSite.transform))found=true;
                Need(found && existingSites.Count==8,"closer service site replaces a farther site without exceeding budget");
            }
            finally
            {
                foreach(var s in existingSites)((IDisposable)s).Dispose();existingSites.Clear();foreach(var s in savedSites)existingSites.Add(s);
                candidates.Clear();foreach(var s in savedCandidates)candidates.Add(s);foreach(var f in fixtures)Destroy(f);
            }
            var siteList=(System.Collections.IList)Field(runtime,"sites");
            if(siteList.Count>0){var source=(Transform)Field(siteList[0],"source");var p=source.position;test.transform.position=p+new Vector3(55f,30f,-55f);test.transform.LookAt(p+Vector3.up*5f);Call(runtime,"Update");Capture("airfield-service-site.png");}
            var siteType=type.Assembly.GetType("BoscaliAirfieldLighting.Site",true);
            Need((bool)type.GetMethod("IsSite",F).Invoke(null,new object[]{"refinery_test"}) && !(bool)type.GetMethod("IsSite",F).Invoke(null,new object[]{"transport_hud"}),"industrial recognition rejects unrelated objects");
            var mockSite=new GameObject("refinery_test");mockSite.transform.position=test.transform.position;
            object site=Activator.CreateInstance(siteType,F,null,new object[]{mockSite.transform,Datum.origin,Field(runtime,"material")},null);
            var sites=(System.Collections.IList)Field(runtime,"sites");sites.Add(site);
            var industrialToggle=info.Instance.Config.Bind("Airfield lighting","Industrial and ports",true);
            var hangarToggle=info.Instance.Config.Bind("Airfield lighting","Hangars",true);hangarToggle.Value=false;
            FI(runtime,"nextLampUpdate").SetValue(runtime,0f);Call(runtime,"Update");
            Need(((GameObject)Field(site,"root")).activeSelf && (int)Prop(runtime,"ActiveLocalLamps")>0,"recognized industrial fixture creates bounded site illumination");
            industrialToggle.Value=false;FI(runtime,"nextLampUpdate").SetValue(runtime,0f);Call(runtime,"Update");
            Need(!((GameObject)Field(site,"root")).activeSelf && (int)Prop(runtime,"ActiveLocalLamps")==0,"industrial toggle clears its markers and illumination");
            industrialToggle.Value=true;hangarToggle.Value=true;Destroy(mockSite);
            LevelInfo.i.isDayLight=true;FI(runtime,"nextLampUpdate").SetValue(runtime,0f);Call(runtime,"Update");
            Need(!runway.activeSelf && !taxi.activeSelf && (int)Prop(runtime,"ActiveLocalLamps")==0,"daylight extinguishes added lighting");
            var master=info.Instance.Config.Bind("Airfield lighting","Enabled",true);master.Value=false;Call(runtime,"Update");Need((int)Prop(runtime,"ActiveFields")==0 && (int)Prop(runtime,"ActiveLocalLamps")==0,"master toggle tears down added lighting");master.Value=true;
            LevelInfo.i.isDayLight=false;
            if(hangar!=null)test.transform.position=hangar.GetSpawnTransform().position+Vector3.up*2.5f;
            for(int i=0;i<4;i++){FI(runtime,"nextUpdate").SetValue(runtime,0f);FI(runtime,"nextLampUpdate").SetValue(runtime,0f);Call(runtime,"Update");await UniTask.Yield();}
            Need((int)Prop(runtime,"ActiveFields")>0,"lighting can recreate fields after master re-enable");
            var leasedBefore=Field(runtime,"leasedPipeline");int priorLightLimit=(int)Field(runtime,"previousLightLimit");
            ((Behaviour)runtime).enabled=false;
            Need((int)Prop(runtime,"ActiveFields")==0 && (int)Prop(runtime,"ActiveSites")==0 && (int)Prop(runtime,"ActiveLocalLamps")==0 && Field(runtime,"leasedPipeline")==null,"disabling lighting component releases fields sites lamps and graphics lease");
            if(leasedBefore!=null)Need((int)leasedBefore.GetType().GetProperty("maxAdditionalLightsCount",F).GetValue(leasedBefore,null)==priorLightLimit,"component disable restores the borrowed additional-light budget");
            ((Behaviour)runtime).enabled=true;
            for(int i=0;i<4;i++){FI(runtime,"nextUpdate").SetValue(runtime,0f);FI(runtime,"nextLampUpdate").SetValue(runtime,0f);Call(runtime,"Update");await UniTask.Yield();}
            Need((int)Prop(runtime,"ActiveFields")>0,"lighting component re-enable reacquires nearby fields");
            foreach(var key in new[]{"Enabled","Runways","Taxiways","Hangars","Helipads","Industrial and ports","Legacy flashing runway ends","Approach strobes"})
                Need(info.Instance.Config.ContainsKey(new ConfigDefinition("Airfield lighting",key)),"toggle configured "+key);
            var controlAssembly=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="ClientEffectsControlPanel");
            Need(controlAssembly!=null,"unified effects control panel assembly loaded");
            var panelType=controlAssembly.GetType("ClientEffectsControlPanel.Panel",true);
            var panels=Resources.FindObjectsOfTypeAll(panelType);
            Need(panels.Length==1,"unified effects control panel host remains active");
            Call(panels[0],"CaptureAvailable");
            var configs=(System.Collections.IDictionary)Field(panels[0],"configs");
            Need(configs.Contains("aaron.boscali.airfield-lighting"),"control panel discovers prototype toggles");
        }
        finally {cameraField.SetValue(aircraftCamera,originalCamera);camera=(Camera)originalCamera;LevelInfo.i.SetTimeOfDay(originalTime);LevelInfo.i.isDayLight=originalDay;Destroy(go);}
    }

    private static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
    private static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
    private void TestSettingsCacheCoverage()
    {
        var cacheType=assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.EffectsSettingsCache",true);
        var cache=(System.Collections.IDictionary)cacheType.GetField("Configs",F).GetValue(null);
        var ids=(string[])cacheType.GetField("Ids",F).GetValue(null);
        var complete=cacheType.GetField("complete",F);
        var updater=Resources.FindObjectsOfTypeAll(cacheType).Single();
        var saved=cache.Keys.Cast<object>().Select(key=>new System.Collections.DictionaryEntry(key,cache[key])).ToArray();
        bool wasComplete=(bool)complete.GetValue(null);object previousNext=Field(updater,"next");
        string missing=ids[0];object required=cache[missing];
        Need(required!=null,"required core config exists before cache regression fixture");
        try
        {
            cache.Remove(missing);cache["test.unrelated.optional.config"]=required;
            complete.SetValue(null,false);FI(updater,"next").SetValue(updater,0f);
            Call(updater,"Update");
            Need(cache.Contains(missing) && ReferenceEquals(cache[missing],required),"optional extra config cannot hide a missing required pack config");
            Need((bool)complete.GetValue(null),"cache completion requires all named pack configs");
        }
        finally
        {
            cache.Clear();foreach(var entry in saved)cache[entry.Key]=entry.Value;
            complete.SetValue(null,wasComplete);FI(updater,"next").SetValue(updater,previousNext);
        }
    }
    private void TestCoreLifeboatFixtures()
    {
        Assembly core = ModuleInfos()["aaron.boscali.unified"].Instance.GetType().Assembly;
        Type scorch = core.GetType("BoscaliClientExperimental.Scorch.ScorchManager", true);
        MethodInfo same = Method(scorch, "IsSameOwner");
        Need((bool)same.Invoke(null, new object[] { 1, 1 }), "same impact owner accepted");
        Need(!(bool)same.Invoke(null, new object[] { 1, 2 }), "different impact owner rejected");
        Need(!(bool)same.Invoke(null, new object[] { 0, 0 }), "unowned impact rejected");
        object manager = Field(scorch, "Instance").GetValue(null);
        Need(manager != null, "scorch manager available");
        IList marks = (IList)Field(scorch, "marks").GetValue(manager);
        Need(marks.Count == 0, "expected empty isolated scorch pool");
        Type mark = scorch.GetNestedType("Mark", BindingFlags.NonPublic);
        Type feature = core.GetType("BoscaliClientExperimental.Scorch.ScorchFeature", true);
        var limit = (ConfigEntry<int>)Field(feature, "Limit").GetValue(null);
        int savedLimit = limit.Value;
        try
        {
            for (int i = 0; i < 13; i++)
            {
                object item = Activator.CreateInstance(mark, true);
                Field(mark, "Object").SetValue(item, new GameObject("test pooled impact mark"));
                marks.Add(item);
            }
            limit.Value = 10;
            Method(scorch, "TrimToLimit").Invoke(manager, null);
            Need(marks.Count == 10, "reduced scorch pool limit applied");
        }
        finally { Method(scorch, "Clear").Invoke(manager, null); limit.Value = savedLimit; }
        Need(Datum.origin == null, "test requires no active floating origin");
        Assembly krop = ModuleInfos()["aaron.boscali.krop-unified"].Instance.GetType().Assembly;
        Type boats = krop.GetType("KropClientEffects.LocalLifeboats", true);
        IList queue = (IList)Field(boats, "Queue").GetValue(null);
        Need(queue.Count == 0, "expected empty liferaft queue");
        Type pending = boats.GetNestedType("Pending", BindingFlags.NonPublic);
        queue.Add(Activator.CreateInstance(pending, true));
        try
        {
            Method(boats, "Tick").Invoke(null, null);
            Need(queue.Count == 1, "pending liferaft retained until floating origin exists");
        }
        finally { Method(boats, "Clear").Invoke(null, null); }
    }

    private void TestKropLifecycle(System.Collections.Generic.List<GameObject> destroyed)
    {
        var plugin = ModuleInfos()["aaron.boscali.krop-unified"].Instance;
        Assembly krop = plugin.GetType().Assembly;
        Type boats = krop.GetType("KropClientEffects.LocalLifeboats", true);
        Type visuals = krop.GetType("KropClientEffects.VisualObjects", true);
        Type pending = boats.GetNestedType("Pending", BindingFlags.NonPublic);
        Type driverType = krop.GetType("KropClientEffects.RuntimeDriver", true);
        var drivers = Resources.FindObjectsOfTypeAll(driverType);
        Need(drivers.Length == 1, "expected one protected KROP runtime driver");
        object driver = drivers[0];
        var config = plugin.Config;
        var master = (ConfigEntry<bool>)config[new ConfigDefinition("00 General", "Enable pack")];
        var boatOn = (ConfigEntry<bool>)config[new ConfigDefinition("07 Cosmetic lifeboats", "Lifeboats")];
        var aircraftOn = (ConfigEntry<bool>)config[new ConfigDefinition("05 Contrails", "Aircraft contrails")];
        var missileOn = (ConfigEntry<bool>)config[new ConfigDefinition("05 Contrails", "Missile contrails")];
        bool savedMaster = master.Value, savedBoat = boatOn.Value, savedAircraft = aircraftOn.Value, savedMissile = missileOn.Value;
        IList queue = (IList)Field(boats, "Queue").GetValue(null);
        MethodInfo create = Method(visuals, "Create"), count = Method(visuals, "Count");
        Func<string, int> countCategory = category => (int)count.Invoke(null, new object[] { category });
        Func<string, int, GameObject> make = (category, limit) => {
            var obj = (GameObject)create.Invoke(null, new object[] { "lifecycle fixture", category, limit });
            if (obj != null) destroyed.Add(obj);
            return obj;
        };
        try
        {
            master.Value = boatOn.Value = aircraftOn.Value = missileOn.Value = true;
            Method(plugin.GetType(), "ClearScene").Invoke(null, null);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                queue.Add(Activator.CreateInstance(pending, true));
                Need(make("boat", 2) != null && make("boat", 2) != null && make("boat", 2) == null,
                    "lifeboat cap enforced");
                boatOn.Value = false;
                Method(driverType, "Update").Invoke(driver, null);
                Need(queue.Count == 0 && countCategory("boat") == 0, "lifeboat switch clears queued and tracked boats");
                boatOn.Value = true;
                Method(driverType, "Update").Invoke(driver, null);
                Need(queue.Count == 0 && countCategory("boat") == 0, "lifeboat re-enable does not replay cancelled work");
                queue.Add(Activator.CreateInstance(pending, true));
                Method(boats, "Tick").Invoke(null, null);
                Need(queue.Count == 1, "fresh lifeboat work survives absent origin");
                Method(boats, "Clear").Invoke(null, null);
            }
            Need(make("trail-aircraft", 2) != null && make("trail-missile", 2) != null && make("trail-missile", 2) == null,
                "combined contrail emitter cap enforced");
            aircraftOn.Value = false;
            Method(visuals, "Tick").Invoke(null, null);
            Need(countCategory("trail-aircraft") == 0 && countCategory("trail-missile") == 1,
                "aircraft trail switch preserves missile trails");
            aircraftOn.Value = true;
            Need(make("trail-aircraft", 2) != null, "aircraft trail allocation recovers after re-enable");
            missileOn.Value = false;
            Method(visuals, "Tick").Invoke(null, null);
            Need(countCategory("trail-aircraft") == 1 && countCategory("trail-missile") == 0,
                "missile trail switch preserves aircraft trails");
            queue.Add(Activator.CreateInstance(pending, true));
            master.Value = false;
            Method(driverType, "Update").Invoke(driver, null);
            Need(queue.Count == 0 && countCategory("trail-aircraft") == 0, "KROP master off clears work and effects");
            master.Value = true;
            Method(driverType, "Update").Invoke(driver, null);
            Need(make("boat", 1) != null, "KROP master re-enable permits effects");
            object oldSeen = Field(boats, "Seen").GetValue(null);
            queue.Add(Activator.CreateInstance(pending, true));
            Method(plugin.GetType(), "ClearScene").Invoke(null, null);
            Need(queue.Count == 0 && countCategory("boat") == 0 && !ReferenceEquals(oldSeen, Field(boats, "Seen").GetValue(null)),
                "scene cleanup clears effects and sinking deduplication");
        }
        finally
        {
            Method(plugin.GetType(), "ClearScene").Invoke(null, null);
            master.Value = savedMaster; boatOn.Value = savedBoat; aircraftOn.Value = savedAircraft; missileOn.Value = savedMissile;
        }
    }
    private void TestVoiceWarningSwitch()
    {
        var info=ModuleInfos()["aaron.boscali.unified"];var core=info.Instance.GetType().Assembly;
        var warningType=core.GetType("BoscaliClientExperimental.Weather.CockpitVoiceWarnings",true);
        var instances=Resources.FindObjectsOfTypeAll(warningType);Need(instances.Length==1,"one cockpit warning controller exists");
        var controller=(Component)instances[0];var sourceField=warningType.GetField("source",F);var oldSource=sourceField.GetValue(controller);
        var config=info.Instance.Config.Bind("Audio","Cockpit voice warnings",false);bool old=config.Value;
        var host=new GameObject("Warning switch test");var source=host.AddComponent<AudioSource>();var clip=AudioClip.Create("warning switch fixture",44100,1,44100,false);
        try
        {
            Need(!old,"cockpit voice warnings default off in a clean configuration");
            source.clip=clip;source.loop=true;sourceField.SetValue(controller,source);config.Value=true;source.Play();Need(source.isPlaying,"warning test voice starts");
            config.Value=false;warningType.GetMethod("Update",F).Invoke(controller,null);Need(!source.isPlaying,"disabling cockpit warnings stops a playing warning");
        }
        finally{config.Value=old;sourceField.SetValue(controller,oldSource);Destroy(host);Destroy(clip);}
    }
    private void TestKropPerformance()
    {
        var plugin=ModuleInfos()["aaron.boscali.krop-unified"].Instance;
        var asm=plugin.GetType().Assembly;
        var trail=asm.GetType("KropClientEffects.Patches.ContrailEffectPatch",true);
        var stateType=trail.GetNestedType("EngineState",F);var state=Activator.CreateInstance(stateType,true);
        var fixture=new GameObject("Isolated contrail tuning fixture");var ps=fixture.AddComponent<ParticleSystem>();
        var tune=trail.GetMethod("ApplyTuningIfChanged",F);FI(state,"System").SetValue(state,ps);
        var width=plugin.Config.Bind("05 Contrails","Starting width m",1.553991f);var original=width.Value;
        try
        {
            Need((bool)tune.Invoke(null,new[]{state}),"contrail initial settings are applied");
            Need(!(bool)tune.Invoke(null,new[]{state}),"unchanged contrail settings do not rebuild curves");
            width.Value=original<9.8f?original+.1f:original-.1f;
            Need((bool)tune.Invoke(null,new[]{state}) && Near(ps.main.startSize.constant,width.Value),"contrail tuning follows live width changes");
            Need(!(bool)tune.Invoke(null,new[]{state}),"changed contrail settings are cached after update");
            width.Value=original;Need((bool)tune.Invoke(null,new[]{state}),"contrail tuning restores previous setting");
            FI(state,"HasTuning").SetValue(state,false);Need((bool)tune.Invoke(null,new[]{state}),"replacement particle system receives full tuning");
            var oldTune=trail.GetMethod("ApplyTuning",F);var cachedArgs=new object[]{state};var rebuildArgs=new object[]{ps};
            for(int i=0;i<20;i++){tune.Invoke(null,cachedArgs);oldTune.Invoke(null,rebuildArgs);}
            long Measure(MethodInfo method,object[] args){var watch=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<1000;i++)method.Invoke(null,args);watch.Stop();return watch.ElapsedTicks;}
            long cacheA=Measure(tune,cachedArgs),rebuild=Measure(oldTune,rebuildArgs),cacheB=Measure(tune,cachedArgs);
            Note("INFO contrail tuning microbenchmark A/B/A 1000 calls: cached="+cacheA+" old-rebuild="+rebuild+" cached="+cacheB+" ticks; Stopwatch frequency="+System.Diagnostics.Stopwatch.Frequency);
        }
        finally{width.Value=original;Destroy(fixture);}
        var audio=asm.GetType("KropClientEffects.AudioFilters",true);var register=audio.GetMethod("Register",F);var kind=asm.GetType("KropClientEffects.AudioKind",true);
        var hosts=(System.Collections.IDictionary)audio.GetField("Hosts",F).GetValue(null);var sound=new GameObject("Isolated audio registration fixture");
        var source=sound.AddComponent<AudioSource>();
        try
        {
            object world=Enum.Parse(kind,"World"),mechanical=Enum.Parse(kind,"Mechanical");
            for(int i=0;i<20;i++)register.Invoke(null,new object[]{source,world,null});
            Need(hosts.Contains(sound),"audio source registered");
            var host=hosts[sound];var sources=(System.Collections.IList)Field(host,"Sources");
            Need(sources.Count==1,"repeated audio registration reuses source state");
            register.Invoke(null,new object[]{source,mechanical,null});
            Need(sources.Count==1 && Field(sources[0],"Kind").Equals(mechanical),"audio registration updates existing source kind");
        }
        finally{hosts.Remove(sound);Destroy(sound);}
    }

    private async UniTask Run()
    {
        if(File.Exists(Path.Combine(dir,"unified-disabled.txt"))) { RunDisabledBoot(); return; }
        bool passed=false;
        try
        {
            if(Chainloader.PluginInfos.TryGetValue("paul.no.clouds",out var initialAddon)) { var a=initialAddon.Instance; Note("INFO addon initial alive="+(a!=null)+" name="+(a!=null?a.gameObject.name:"none")+" parent="+(a!=null && a.transform.parent!=null?a.transform.parent.name:"none")+" scene="+(a!=null?a.gameObject.scene.name:"none")); }
            foreach(var pi in Chainloader.PluginInfos) Note("INFO alive "+pi.Key+"="+(pi.Value.Instance!=null));
            TestSinglePluginLayout();
            VerifyLegacyShortcut("after module initialization");
            if(File.Exists(Path.Combine(dir,"other-mfd-mods.txt")))
                foreach(string id in new[]{"kelly.nuclearoption.airlift.publicui","kelly.nuclearoption.talon.publicui","kelly.nuclearoption.medals.publicui"})
                    Need(Chainloader.PluginInfos.ContainsKey(id),"installed MFD companion loaded "+id);
            if(File.Exists(Path.Combine(dir,"doorman.txt"))) TestDoorman(false);
            Need(!Chainloader.PluginInfos.ContainsKey("aaron.boscali.clouds-hybrid") && !Chainloader.PluginInfos.ContainsKey("aaron.nuclearoption.volumetricclouds"),"replacement cloud plugins absent");
            var weather=Chainloader.PluginInfos["aaron.boscali.weather-unified"].Instance;
            assembly=weather.GetType().Assembly;
            await TestConfigurationManagerMenus();
            TestPureContracts();
            TestVoiceWarningSwitch();
            TestCoreLifeboatFixtures();
            var destroyedEffects=new List<GameObject>();TestKropLifecycle(destroyedEffects);await UniTask.Yield();
            Need(destroyedEffects.All(obj=>obj==null),"lifeboat, contrail and master cleanup destroys tracked effects");
            Need(MissionSaveLoad.TryReadJson(new MissionKey("MET OFFICE offline probe",MissionGroup.User),File.ReadAllText(Path.Combine(dir,"input-mission.json")),out var mission,out var error),"fixture read: "+error);
            MissionManager.SetMission(mission,false);
            await NetworkManagerNuclearOption.i.StartHostAsync(new HostOptions(SocketType.Offline,GameState.SinglePlayer,mission.MapKey));
            await Until(()=>MissionManager.IsRunning && LevelInfo.i!=null && SceneSingleton<CameraStateManager>.i?.mainCamera!=null,90,"offline mission and native camera ready");
            await Until(()=>GameManager.GetLocalAircraft(out _),35,"native local aircraft ready");
            camera=SceneSingleton<CameraStateManager>.i.mainCamera;
            TestCockpitEffectToggles();
            TestKropBulletToggles();
            if(Chainloader.PluginInfos.TryGetValue("paul.no.clouds",out var missionAddon)) Note("INFO addon after mission alive="+(missionAddon.Instance!=null));
            TestKropPerformance();
            TestKropAudioRefinements();
            TestKropCameraChoice();
            TestKropPropagation();
            await TestKropNightVision();
            TestKropVisualRefinements();
            await TestLighting();
            Type mt=assembly.GetType("BoscaliSummer.Modules.Weather.Runtime.WeatherManager",true);
            manager=mt.GetProperty("Live",F).GetValue(null,null);
            await Until(()=>(bool)Prop(manager,"IsCaptured"),30,"weather environment captured");
            var launcherType=assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.MetOfficeMfdLauncher",true);
            Component launcher=((Component)manager).GetComponent(launcherType);
            Need(launcher!=null,"MET MFD launcher installed");
            object installer=Field(launcher,"installer");
            await Until(()=>Field(installer,"Screen")!=null,30,"MET MFD acquired a native free slot");
            var bezel=(UnityEngine.UI.Button)Field(installer,"Bezel");
            Need(bezel!=null,"MET MFD native button registered");
            var nativeMfd=(VirtualMFD)installer.GetType().GetMethod("ResolveMfd",F).Invoke(null,null);
            var right=(System.Collections.Generic.List<UnityEngine.UI.Button>)AccessTools.Field(typeof(VirtualMFD),"rightButtons").GetValue(nativeMfd);
            Need(right.Contains(bezel),"MET uses the right-hand bezel");
            DynamicMap.i.Maximize();
            await UniTask.Delay(300,ignoreTimeScale:true);
            bezel.onClick.Invoke();
            Need(((MFDScreen)Field(installer,"Screen")).isActive,"native right-hand MET button opens its screen");
            Need(((RectTransform)Field(launcher,"effectsPage")).gameObject.activeInHierarchy && !((RectTransform)Field(launcher,"weatherPage")).gameObject.activeSelf,"combined MFD opens on effects tab");
            await TestMfdAccessibilityColours(launcher);
            object effects=Field(launcher,"effects");
            TestEffectMenuCoverage();
            CaptureGranularEffectControls(effects, launcher);
            TestKropOptionalUi(effects, launcher);
            TestKropNightAudioUi(effects, launcher);
            var rows=(UnityEngine.UI.Button[])Field(effects,"rows");
            var categories=(UnityEngine.UI.Button[])Field(effects,"categories");
            var effectsType=effects.GetType();var allItems=(System.Collections.IEnumerable)effectsType.GetField("Items",F).GetValue(null);
            var find=effectsType.GetMethod("Find",F);int itemCount=0,unavailable=0;
            foreach(var item in allItems){itemCount++;if((int)Field(item,"category")==7 && (!File.Exists(Path.Combine(dir,"noatmo.txt")) || (string)Field(item,"kind")=="Feature" || (string)Field(item,"kind")=="Preset"))continue;if(find.Invoke(null,new[]{item})==null){unavailable++;Note("INFO unavailable setting "+Field(item,"id")+"/"+Field(item,"section")+"/"+Field(item,"key"));}}
            Need(itemCount>=85,"all original effects controls imported into MFD");
            Need(unavailable==0,"each MFD control resolves its loaded setting");
            TestSettingsCacheCoverage();
            await UniTask.Delay(200,ignoreTimeScale:true);Canvas.ForceUpdateCanvases();
            var effectsEvents=UnityEngine.EventSystems.EventSystem.current;var rowCorners=new Vector3[4];((RectTransform)rows[1].transform).GetWorldCorners(rowCorners);
            var effectsCanvas=rows[1].GetComponentInParent<Canvas>();var effectsCamera=effectsCanvas.renderMode==RenderMode.ScreenSpaceOverlay?null:effectsCanvas.worldCamera;
            var rowCentre=RectTransformUtility.WorldToScreenPoint(effectsCamera,(rowCorners[0]+rowCorners[2])*.5f);
            var effectHits=new List<UnityEngine.EventSystems.RaycastResult>();effectsEvents.RaycastAll(new UnityEngine.EventSystems.PointerEventData(effectsEvents){position=rowCentre},effectHits);
            Need(effectHits.Count>0 && (effectHits[0].gameObject==rows[1].gameObject || effectHits[0].gameObject.transform.IsChildOf(rows[1].transform)),"native UI raycast reaches effects checkbox");
            var weatherEnabled=weather.Config.Bind("Weather","Enabled",true);bool originalEnabled=weatherEnabled.Value;
            rows[1].onClick.Invoke();Need(weatherEnabled.Value!=originalEnabled,"effects MFD checkbox changes weather master");
            await UniTask.Delay(300,ignoreTimeScale:true);Need(Field(installer,"Screen")!=null && ((MFDScreen)Field(installer,"Screen")).isActive,"combined MFD remains available with weather disabled across frames");
            rows[1].onClick.Invoke();Need(weatherEnabled.Value==originalEnabled,"effects checkbox restores weather master");
            await Until(()=>(bool)Prop(manager,"IsCaptured"),20,"weather recaptures after MFD re-enable");
            foreach(var category in categories){category.onClick.Invoke();Need(rows[0].gameObject.activeInHierarchy,"effects category displays its controls");}
            categories[0].onClick.Invoke();
            var rain=weather.Config.Bind("Weather","RainVisualsEnabled",true);bool oldRain=rain.Value;
            rows[2].onClick.Invoke();Need(rain.Value!=oldRain,"individual rain checkbox changes its setting");rows[2].onClick.Invoke();Need(rain.Value==oldRain,"individual rain checkbox restores its setting");
            var density=weather.Config.Bind("Weather","RainDensity",1f);float oldDensity=density.Value;
            var plus=(UnityEngine.UI.Button[])Field(effects,"plus");plus[3].onClick.Invoke();Need(density.Value>oldDensity || Near(oldDensity,2f),"strength increment updates setting");density.Value=oldDensity;
            var pageNext=(UnityEngine.UI.Button)Field(effects,"next");pageNext.onClick.Invoke();Need((int)Field(effects,"page")==1,"effects pagination opens second page");
            ((UnityEngine.UI.Button)Field(effects,"previous")).onClick.Invoke();Need((int)Field(effects,"page")==0,"effects pagination returns to first page");
            var reviewHost=new GameObject("Effects MFD review host",typeof(RectTransform),typeof(Canvas));
            var reviewCanvasFx=reviewHost.GetComponent<Canvas>();reviewCanvasFx.renderMode=RenderMode.ScreenSpaceOverlay;
            var reviewRoot=new GameObject("Effects MFD review page",typeof(RectTransform)).GetComponent<RectTransform>();
            reviewRoot.SetParent(reviewHost.transform,false);reviewRoot.anchorMin=reviewRoot.anchorMax=reviewRoot.pivot=new Vector2(.5f,.5f);
            reviewRoot.sizeDelta=((RectTransform)((MFDScreen)Field(installer,"Screen")).displayPanel.transform).rect.size;
            reviewRoot.localScale=Vector3.one;
            var reviewNested=reviewRoot.gameObject.AddComponent<Canvas>();reviewNested.overrideSorting=true;reviewNested.sortingOrder=30004;
            foreach(Transform child in ((MFDScreen)Field(installer,"Screen")).displayPanel.transform)Instantiate(child.gameObject,reviewRoot,false);
            Canvas.ForceUpdateCanvases();Capture("effects-mfd.png",true,reviewRoot);Destroy(reviewHost);
            ((UnityEngine.UI.Button)Field(launcher,"weatherTab")).onClick.Invoke();
            Need(((RectTransform)Field(launcher,"weatherPage")).gameObject.activeInHierarchy && !((RectTransform)Field(launcher,"effectsPage")).gameObject.activeSelf,"top tab switches to weather");
            Call(launcher,"Refresh");
            var simple=(UnityEngine.UI.Button)Field(launcher,"simpleButton");var technical=(UnityEngine.UI.Button)Field(launcher,"technicalButton");
            var simpleConfig=weather.Config.Bind("MET OFFICE","Simple weather names",true);simple.onClick.Invoke();Need(simpleConfig.Value,"simple English names selected from prominent button");
            await UniTask.Delay(250,ignoreTimeScale:true);Canvas.ForceUpdateCanvases();var nameCorners=new Vector3[4];((RectTransform)technical.transform).GetWorldCorners(nameCorners);
            var nameCanvas=technical.GetComponentInParent<Canvas>();var nameCam=nameCanvas.renderMode==RenderMode.ScreenSpaceOverlay?null:nameCanvas.worldCamera;
            var namePoint=RectTransformUtility.WorldToScreenPoint(nameCam,(nameCorners[0]+nameCorners[2])*.5f);
            var nameEvents=UnityEngine.EventSystems.EventSystem.current;var nameHits=new List<UnityEngine.EventSystems.RaycastResult>();
            nameEvents.RaycastAll(new UnityEngine.EventSystems.PointerEventData(nameEvents){position=namePoint},nameHits);
            Note("INFO weather names pointer="+namePoint+" hit="+(nameHits.Count>0?nameHits[0].gameObject.name:"none")+" expected="+technical.name);
            Need(nameHits.Count>0 && (nameHits[0].gameObject==technical.gameObject || nameHits[0].gameObject.transform.IsChildOf(technical.transform)),"native pointer raycast reaches prominent name switch");
            var categoryButtons=(UnityEngine.UI.Button[])Field(launcher,"classes");string simpleClass=categoryButtons[3].GetComponentInChildren<TMPro.TMP_Text>(true).text;
            technical.onClick.Invoke();Need(!simpleConfig.Value,"technical names selected from prominent button");
            Need(categoryButtons[3].GetComponentInChildren<TMPro.TMP_Text>(true).text!=simpleClass,"technical switch changes the visible weather terminology");
            simple.onClick.Invoke();Need(simpleConfig.Value,"simple English names can be restored");
            var effectsPageRect=(RectTransform)Field(launcher,"effectsPage");var nextControl=(UnityEngine.UI.Button)Field(effects,"next");
            Need(Mathf.Abs(((RectTransform)nextControl.transform).anchoredPosition.y+effectsPageRect.rect.height-40f)<2f,"effects pagination sits at the bottom of available panel height");
            var rootPage=(RectTransform)Field(launcher,"page");float oldHeight=rootPage.rect.height;
            Call(launcher,"Reflow",620f);float smallFooter=((RectTransform)nextControl.transform).anchoredPosition.y;
            Call(launcher,"Reflow",840f);float largeFooter=((RectTransform)nextControl.transform).anchoredPosition.y;
            Need(largeFooter<smallFooter-200f && Mathf.Abs(effectsPageRect.rect.height-732f)<2f,"panel controls expand with available window height");
            Need(simpleConfig.Value,"window reflow preserves weather name selection");Call(launcher,"Reflow",oldHeight);
            var coordType=assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.CloudModeCoordinator",true);
            var coord=coordType.GetField("Live",F).GetValue(null);var select=coordType.GetMethod("Select",F);
            var cloudEnum=assembly.GetType("BoscaliSummer.Modules.Weather.Presentation.CloudMode",true);
            bool hasReleaseAddon=File.Exists(Path.Combine(dir,"noatmo.txt"));
            bool addon=false;
            if(Chainloader.PluginInfos.TryGetValue("paul.no.clouds",out var addonInfo) && addonInfo.Instance!=null && addonInfo.Instance.isActiveAndEnabled && addonInfo.Metadata.Version>=new Version(1,4,0)) {
                var actualApi=addonInfo.Instance.GetType().Assembly.GetType("NoAtmoFX.NoAtmoFXApi",false);
                addon=actualApi?.GetProperty("Ready",F)?.GetValue(null) is bool isReady && isReady;
            }
            if(hasReleaseAddon && (File.Exists(Path.Combine(dir,"unified-startup.txt")) || File.Exists(Path.Combine(dir,"doorman.txt"))))Need(addon,"official release API remains alive with protected startup host");
            if(hasReleaseAddon && !addon)Need(!string.IsNullOrWhiteSpace((string)Prop(coord,"Status")),"stopped add-on has an explanatory status");
            Need((bool)Prop(coord,"AddonReady")==addon,"optional add-on availability detected");
            var modeButtons=(UnityEngine.UI.Button[])Field(launcher,"modes");
            Need(modeButtons[2].interactable==addon,"volumetric button only enabled with add-on");
            if(!addon)Need(modeButtons[2].GetComponentInChildren<TMPro.TMP_Text>(true).color.a < modeButtons[1].GetComponentInChildren<TMPro.TMP_Text>(true).color.a,"missing add-on is visually disabled");
            if(addon) {
                var addonPlugin=Chainloader.PluginInfos["paul.no.clouds"].Instance;
                var cfg=addonPlugin.Config;
                var api=addonPlugin.GetType().Assembly.GetType("NoAtmoFX.NoAtmoFXApi",true);
                Need((bool)select.Invoke(coord,new[]{Enum.ToObject(cloudEnum,2)}),"select volumetric mode");
                await UniTask.Delay(1500,ignoreTimeScale:true);
                Need((bool)api.GetProperty("IsEnabled").GetValue(null),"API reports volumetric on");
                api.GetMethod("SetEnabled").Invoke(null,new object[]{false,false});
                await UniTask.Delay(150,ignoreTimeScale:true);
                Need((int)Convert.ChangeType(Prop(coord,"Mode"),typeof(int))==1,"external F10-equivalent off is reflected in MFD mode");
                api.GetMethod("SetEnabled").Invoke(null,new object[]{true,false});
                await UniTask.Delay(150,ignoreTimeScale:true);
                Need((int)Convert.ChangeType(Prop(coord,"Mode"),typeof(int))==2,"external F10-equivalent on is reflected in MFD mode");
                var names=(string[])api.GetField("Features").GetValue(null);
                Need(names.Length==16,"all sixteen 1.4 feature names exposed");
                var get=api.GetMethod("GetFeature"); var toggle=coordType.GetMethod("ToggleFeature",F);
                foreach(var name in names.Where(x=>x!="Clouds")) {
                    bool before=(bool)get.Invoke(null,new object[]{name});
                    Need((bool)toggle.Invoke(coord,new object[]{name}) && (bool)get.Invoke(null,new object[]{name})!=before,"feature toggles "+name);
                    Need((bool)toggle.Invoke(coord,new object[]{name}) && (bool)get.Invoke(null,new object[]{name})==before,"feature restores "+name);
                }
                var preset=coordType.GetMethod("Preset",F);
                foreach(string name in new[]{"Low","Medium","High","Ultra"})
                    Need((bool)preset.Invoke(coord,new object[]{name}) && (string)Prop(coord,"CurrentPreset")==name,"quality preset "+name);
                var gameStateBefore=GameManager.gameState;byte[] addonFileBefore=File.ReadAllBytes(cfg.ConfigFilePath);
                var stateBacking=AccessTools.Field(typeof(GameManager),"<gameState>k__BackingField");
                Need(stateBacking!=null,"game-state backing field available for isolated persistence check");
                try
                {
                    // No frame is advanced while game state is overridden: this exercises only
                    // the bridge's persistence decision, never networking or a live host.
                    stateBacking.SetValue(null,GameState.Multiplayer);
                    Need((bool)preset.Invoke(coord,new object[]{"High"}) && (string)Prop(coord,"CurrentPreset")=="High","multiplayer quality preset applies to the current session");
                    Need(addonFileBefore.SequenceEqual(File.ReadAllBytes(cfg.ConfigFilePath)),"multiplayer quality preset does not save host settings into player config");
                }
                finally {stateBacking.SetValue(null,gameStateBefore);}
                Need((bool)preset.Invoke(coord,new object[]{"Ultra"}),"quality preset restored after synthetic multiplayer check");
                var cloudButton=(UnityEngine.UI.Button)Field(launcher,"cloudButton");Call(launcher,"Refresh");cloudButton.onClick.Invoke();
                Need(((RectTransform)Field(launcher,"cloudPage")).gameObject.activeInHierarchy,"cloud settings page opens inside weather MFD");
                await TestMfdAccessibilityCloudColours(launcher);
                var cloudUi=Field(launcher,"cloudEffects");var cloudPlus=(UnityEngine.UI.Button[])Field(cloudUi,"plus");
                cloudPlus[0].onClick.Invoke(); Need((string)Prop(coord,"CurrentPreset")=="Low","quality control changes preset from menu");
                var downscale=cfg.Bind("Quality","Downscale",2);int originalDownscale=downscale.Value;
                downscale.Value=originalDownscale==1?2:1;
                Call(cloudUi,"Refresh");
                Need((string)Prop(coord,"CurrentPreset")=="Custom","manual quality change reports Custom");
                var cloudRows=(UnityEngine.UI.Button[])Field(cloudUi,"rows");
                Need(cloudRows[0].GetComponentInChildren<TMPro.TMP_Text>(true).text.Contains("CUSTOM"),"MFD displays externally changed Custom preset");
                downscale.Value=originalDownscale;
                Need((bool)preset.Invoke(coord,new object[]{"Low"}),"known quality preset restored after Custom check");
                cloudRows[1].onClick.Invoke();await UniTask.Delay(150,ignoreTimeScale:true);
                Need(!(bool)api.GetProperty("IsEnabled").GetValue(null) && (int)Convert.ChangeType(Prop(coord,"Mode"),typeof(int))==1,"cloud feature checkbox disables actual clouds and updates mode");
                cloudRows[1].onClick.Invoke();await UniTask.Delay(150,ignoreTimeScale:true);
                Need((bool)api.GetProperty("IsEnabled").GetValue(null) && (int)Convert.ChangeType(Prop(coord,"Mode"),typeof(int))==2,"cloud feature checkbox re-enables actual clouds and updates mode");
                var nextCloud=(UnityEngine.UI.Button)Field(cloudUi,"next");nextCloud.onClick.Invoke();nextCloud.onClick.Invoke();
                var changeTime=cfg.Bind("Weather","ChangeTime",10f);float oldChange=changeTime.Value;
                cloudPlus[1].onClick.Invoke();Need(changeTime.Value>oldChange || Near(oldChange,30f),"cloud transition duration adjustable in MFD");
                changeTime.Value=oldChange;
                var previousCloud=(UnityEngine.UI.Button)Field(cloudUi,"previous");previousCloud.onClick.Invoke();previousCloud.onClick.Invoke();
                Capture("official-noatmo14-volumetric.png");
                Need((Field(addonPlugin,"CloudRT") is RenderTexture cloudRT && cloudRT.IsCreated()) || (Field(addonPlugin,"TFresh") is RenderTexture temporalRT && temporalRT.IsCreated()),"official 1.4 cloud render target produced");
                Need((bool)select.Invoke(coord,new[]{Enum.ToObject(cloudEnum,0)}) && !(bool)api.GetProperty("IsEnabled").GetValue(null),"vanilla disables optional clouds");
                Need((bool)select.Invoke(coord,new[]{Enum.ToObject(cloudEnum,1)}) && !(bool)api.GetProperty("IsEnabled").GetValue(null),"enhanced disables optional clouds");
            } else {
                Need(!(bool)select.Invoke(coord,new[]{Enum.ToObject(cloudEnum,2)}),"missing, stopped or old add-on cannot enable volumetric mode");
                Need((bool)select.Invoke(coord,new[]{Enum.ToObject(cloudEnum,0)}) && (int)Convert.ChangeType(Prop(coord,"Mode"),typeof(int))==0,"vanilla remains selectable without compatible add-on");
                Need((bool)select.Invoke(coord,new[]{Enum.ToObject(cloudEnum,1)}) && (int)Convert.ChangeType(Prop(coord,"Mode"),typeof(int))==1,"enhanced remains selectable without compatible add-on");
            }
            Call(launcher,"Refresh");
            var classes=(UnityEngine.UI.Button[])Field(launcher,"classes");
            var tiles=(UnityEngine.UI.Button[])Field(launcher,"tiles");
            var clocks=(UnityEngine.UI.Button[])Field(launcher,"times");
            classes[6].onClick.Invoke();
            var mfdScreen=(MFDScreen)Field(installer,"Screen");
            Need(mfdScreen.displayPanel.activeInHierarchy,"MET page is active in native map hierarchy");
            await UniTask.Delay(500,ignoreTimeScale:true);
            Canvas.ForceUpdateCanvases();
            var eventSystem=UnityEngine.EventSystems.EventSystem.current;
            Need(eventSystem!=null,"native UI event system present");
            var tileCorners=new Vector3[4]; ((RectTransform)tiles[6].transform).GetWorldCorners(tileCorners);
            Canvas tileCanvas=tiles[6].GetComponentInParent<Canvas>();
            Camera tileCamera=tileCanvas.renderMode==RenderMode.ScreenSpaceOverlay ? null : tileCanvas.worldCamera;
            Vector2 centre=RectTransformUtility.WorldToScreenPoint(tileCamera,(tileCorners[0]+tileCorners[2])*.5f);
            var pointer=new UnityEngine.EventSystems.PointerEventData(eventSystem){position=centre};
            var hits=new List<UnityEngine.EventSystems.RaycastResult>(); eventSystem.RaycastAll(pointer,hits);
            Need(hits.Count>0 && (hits[0].gameObject==tiles[6].gameObject || hits[0].gameObject.transform.IsChildOf(tiles[6].transform)),"native UI raycast reaches the weather preset");
            var nativeRect=(RectTransform)mfdScreen.displayPanel.transform;
            var actualCanvas=nativeRect.GetComponentInParent<Canvas>();
            var corners=new Vector3[4]; nativeRect.GetWorldCorners(corners);
            Camera uiCamera=actualCanvas.renderMode==RenderMode.ScreenSpaceOverlay ? null : actualCanvas.worldCamera;
            var low=RectTransformUtility.WorldToScreenPoint(uiCamera,corners[0]);
            var high=RectTransformUtility.WorldToScreenPoint(uiCamera,corners[2]);
            Note("INFO native MFD bounds "+low+" to "+high+" screen "+Screen.width+"x"+Screen.height);
            ((RectTransform)bezel.transform).GetWorldCorners(corners);
            var bLeft=RectTransformUtility.WorldToScreenPoint(uiCamera,corners[0]);
            var bRight=RectTransformUtility.WorldToScreenPoint(uiCamera,corners[2]);
            Note("INFO native MET bezel bounds "+bLeft+" to "+bRight);
            Need(high.x>0 && low.x<Screen.width && high.y>0 && low.y<Screen.height,"MFD page overlaps the visible screen");
            Need(low.x>=bRight.x+2 && high.x<=Screen.width-4,"right-side MFD fits between bezel and screen edge");
            // A layout-only render: the hidden test window cannot be captured by
            // ScreenCapture. Clone the page's graphics into a temporary camera canvas.
            var cloneHost=new GameObject("MFD review host",typeof(RectTransform),typeof(Canvas));
            var reviewCanvas=cloneHost.GetComponent<Canvas>(); reviewCanvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var cloneRoot=new GameObject("MFD review page",typeof(RectTransform)).GetComponent<RectTransform>();
            cloneRoot.SetParent(cloneHost.transform,false); cloneRoot.anchorMin=cloneRoot.anchorMax=cloneRoot.pivot=new Vector2(.5f,.5f);
            cloneRoot.sizeDelta=nativeRect.rect.size;
            cloneRoot.localScale=Vector3.one;
            var nestedCanvas=cloneRoot.gameObject.AddComponent<Canvas>(); nestedCanvas.overrideSorting=true; nestedCanvas.sortingOrder=30004;
            foreach(Transform child in nativeRect) Instantiate(child.gameObject,cloneRoot,false);
            Canvas.ForceUpdateCanvases();
            Capture("met-office-mfd.png",true,cloneRoot);
            Destroy(cloneHost);
            tiles[6].onClick.Invoke();
            Need((byte)Prop(Prop(manager,"LiveKey"),"SkyPin")==47,"MFD preset directly applies extreme storm");
            if(addon) {
                Need((bool)select.Invoke(coord,new[]{Enum.ToObject(cloudEnum,2)}),"volumetric selected over extreme storm");
                await UniTask.Delay(1800,ignoreTimeScale:true);
                var ap=Chainloader.PluginInfos["paul.no.clouds"].Instance;
                var at=ap.GetType().Assembly.GetType("NoAtmoFX.NoAtmoFXApi",true);
                Note("INFO weather source "+at.GetProperty("WeatherSource").GetValue(null));
                Need((string)at.GetProperty("WeatherSource").GetValue(null)=="boscali","NOAtmoFX follows Boscali weather on extreme storm");
                Capture("official-noatmo14-extreme-storm.png");
                var adapter=Field(ap,"Boscali");
                var rainFlag=weather.Config.Bind("Weather","RainVisualsEnabled",true);var fogFlag=weather.Config.Bind("Weather","RainAtmosphereEnabled",true);
                bool savedRain=rainFlag.Value,savedFog=fogFlag.Value;rainFlag.Value=true;fogFlag.Value=true;
                Need((bool)Prop(adapter,"YieldNearRain") && (bool)Prop(adapter,"YieldFog"),"add-on yields duplicate rain and fog to Boscali");
                var yieldSetting=ap.Config.Bind("Integration","YieldToBoscali",true);yieldSetting.Value=false;
                Need(!(bool)Prop(adapter,"YieldNearRain") && !(bool)Prop(adapter,"YieldFog"),"yield preference controls hand-off");
                yieldSetting.Value=true;rainFlag.Value=savedRain;fogFlag.Value=savedFog;
                var follow=ap.Config.Bind("Integration","FollowBoscaliWeather",true);follow.Value=false;
                await UniTask.Delay(800,ignoreTimeScale:true);
                Need((string)at.GetProperty("WeatherSource").GetValue(null)=="mission","turning follow off returns weather to mission");
                follow.Value=true;await UniTask.Delay(800,ignoreTimeScale:true);
                Need((string)at.GetProperty("WeatherSource").GetValue(null)=="boscali","turning follow on reacquires Boscali weather");
                Need((bool)select.Invoke(coord,new[]{Enum.ToObject(cloudEnum,1)}),"storm returns to enhanced mode");
            }
            clocks[6].onClick.Invoke();
            Need(Near(LevelInfo.i.timeOfDay,12),"MFD time control directly applies noon");
            clocks[8].onClick.Invoke();
            ((UnityEngine.UI.Button)Field(launcher,"auto")).onClick.Invoke();
            Need((byte)Prop(Prop(manager,"LiveKey"),"SkyPin")>=48,"MFD mission weather restores the mission sky");
            var mfdLaunch=(UnityEngine.UI.Button)Field(launcher,"launch");
            mfdLaunch.onClick.Invoke();
            Need((bool)Static("BoscaliUnifiedWeather.WeatherUiBridge","get_IsMetOfficeOpen"),"MFD weather action opens MET OFFICE");
            Call(Field(manager,"console"),"Close");
            var rightScreens=(System.Collections.Generic.List<MFDScreen>)AccessTools.Field(typeof(VirtualMFD),"rightScreens").GetValue(nativeMfd);
            int slot=right.IndexOf(bezel);
            var unaffected=rightScreens.ToArray();
            Call(launcher,"ResetForScene");
            Need(rightScreens[slot]==null,"scene reset releases the native MFD slot");
            Need(unaffected.Where((s,i)=>i!=slot).Select((s,i)=>s).All(s=>s==null || rightScreens.Contains(s)),"scene reset preserves other right-hand screens");
            await Until(()=>Field(installer,"Screen")!=null,10,"MET MFD rebinds after reset");
            bezel=(UnityEngine.UI.Button)Field(installer,"Bezel");
            bezel.onClick.Invoke();
            Need(((MFDScreen)Field(installer,"Screen")).isActive,"one native bezel press opens MET after rebind");
            DynamicMap.i.Minimize();
            Need(assembly.GetType("BoscaliSummer.Modules.Weather.Visuals.WeatherCloudPass")==null && assembly.GetType("BoscaliSummer.Modules.Weather.Visuals.WeatherVolumeDressing")==null,"rejected custom cloud renderers absent");
            Need((bool)Static("BoscaliUnifiedWeather.WeatherUiBridge","OpenMetOffice"),"MET OFFICE opened");
            console=Field(manager,"console");
            controlType=assembly.GetType("BoscaliSummer.Modules.Weather.Domain.MetControl",true);
            TestSimpleNames();
            TestTime();
            await UniTask.Delay(500,ignoreTimeScale:true);
            TestUiBounds();
            Capture("met-office.png",true);
            Call(console,"Close");
            TestRainAltitude();
            TestWeatherSkyFeatureToggles();
            TestWeatherSunAndRainFeatureToggles(weather.Config);
            await TestStorm(weather.Config);
            await TestThunder();
            GameManager.GetLocalAircraft(out var sprayPlane); TestSeaSpray(sprayPlane);
            await Until(()=>(bool)Prop(manager,"IsCaptured"),20,"weather recaptured after sea-spray regression");
            TestTransition();
            VerifyLegacyShortcut("after mission and UI regression checks");
            Need(MetProbe.ProtectedWrites>0,"native preference persistence suppressed");
            Note("INFO protected preference writes="+MetProbe.ProtectedWrites);
            Note("LIMIT isolated offline mission and programmatic UI actions; physical input, audio perception, sustained FPS and live multiplayer untested; offscreen screenshots use native URP rendering with UI canvas temporarily camera-bound");
            passed=true;
        }
        catch(Exception e) { Note("EXCEPTION "+e); }
        File.WriteAllText(Path.Combine(dir,"result.txt"),(passed?"PASS\n":"FAIL\n")+string.Join("\n",notes));
        try { if(NetworkManagerNuclearOption.i!=null) await NetworkManagerNuclearOption.i.StopAsync(false); } catch(Exception e) { File.AppendAllText(Path.Combine(dir,"result.txt"),"\nShutdown "+e); }
        Application.Quit();
    }
    private void TestPureContracts()
    {
        const string time="BoscaliSummer.Modules.Weather.Domain.TimeOfDayMath";
        Need(Near((float)Static(time,"Wrap",-.25f),23.75f) && Near((float)Static(time,"Wrap",48.25f),.25f),"clock arithmetic wraps negative and multiple days");
        Need(Near((float)Static(time,"Delta",23.75f,.25f),.5f) && Near((float)Static(time,"Delta",.25f,23.75f),-.5f),"clock restore delta crosses midnight");
        Need((string)Static(time,"ClockText",-.25f)=="23:45" && (string)Static(time,"ClockText",24f)=="00:00","clock formats exact quarter-hour and midnight");
        const string profile="BoscaliSummer.Modules.Weather.Domain.ExtremeStormProfile";
        Need((float)Static(profile,"FogFloor",50f,0f,4000f)>(float)Static(profile,"FogFloor",1000f,1000f,4000f),"extreme fog is stronger near terrain");
        Need(Near((float)Static(profile,"FogFloor",4600f,0f,4000f),0f),"extreme fog floor clears above cloud layer");
        Need(Near((float)Static(profile,"MistAlpha",0f,0f),0f) && Near((float)Static(profile,"MistAlpha",2f,1000f),.38f) && Near((float)Static(profile,"MistAlpha",2f,1900f),.19f) && Near((float)Static(profile,"MistAlpha",2f,2400f),0f),"terrain mist fades in and fades at distance");
        Type library=assembly.GetType("BoscaliSummer.Modules.Weather.Domain.SkyLibrary",true);
        Need((int)library.GetField("Count",F).GetRawConstantValue()==48,"48 presets with original indices retained");
        Type simple=assembly.GetType("BoscaliSummer.Modules.Weather.Domain.SimpleWeatherNames",true);
        Need((int)simple.GetProperty("Count",F).GetValue(null,null)==48,"all 48 weather presets have plain-English names");
        var names=Enumerable.Range(0,48).Select(i=>(string)simple.GetMethod("Get",F).Invoke(null,new object[]{i})).ToArray();
        Need(names.All(x=>!string.IsNullOrWhiteSpace(x)) && names.Distinct(StringComparer.OrdinalIgnoreCase).Count()==48,"simple names are unique and readable");
        object sky=library.GetMethod("Get",F).Invoke(null,new object[]{47});
        object nominal=Field(sky,"Nominal");
        Need((bool)Field(sky,"ManualOnly") && (float)Field(nominal,"Convective")==1f && (float)Field(nominal,"Severity")==1f,"extreme preset explicitly selected and maximum intensity");
        var regime=assembly.GetType("BoscaliSummer.Modules.Weather.Domain.WeatherRegimeType",true);
        int[] pool=(int[])library.GetMethod("OpeningPool",F).Invoke(null,new[]{Enum.Parse(regime,"Storm")});
        Need(!pool.Contains(47) && pool.Length==6,"extreme preset excluded from ordinary mission opening pool");
    }
    private void TestSimpleNames()
    {
        var plugin=Chainloader.PluginInfos["aaron.boscali.weather-unified"].Instance;
        var pref=plugin.Config.Bind("MET OFFICE","Simple weather names",true);
        bool initial=pref.Value;
        var key=Prop(manager,"LiveKey");
        byte sky=(byte)Prop(key,"SkyPin");
        Call(console,"ToggleSimpleNames");
        Need(pref.Value!=initial && (byte)Prop(Prop(manager,"LiveKey"),"SkyPin")==sky,"simple-name toggle does not change weather");
        Call(console,"ToggleSimpleNames");
        Need(pref.Value==initial,"simple-name preference toggles back");
    }
    private void TestTime()
    {
        object time=Field(manager,"TimeOfDayControl");
        float start=LevelInfo.i.timeOfDay, elapsed=Clock();
        object factor=AccessTools.Field(typeof(LevelInfo),"timeFactor").GetValue(LevelInfo.i);
        object settings=LevelInfo.i.LoadedMapSettings;
        Need(Press("TimeMidnight") && Near(LevelInfo.i.timeOfDay,0),"MET OFFICE midnight control sets native clock");
        Vector3 night=RenderSettings.sun.transform.forward;
        Need(Press("TimeNoon") && Near(LevelInfo.i.timeOfDay,12),"MET OFFICE noon control sets native clock");
        Need(Vector3.Distance(night,RenderSettings.sun.transform.forward)>.5f,"native sun direction refreshes immediately");
        Need(Press("TimePlusHour") && Near(LevelInfo.i.timeOfDay,13),"MET OFFICE hour adjustment");
        Need(Press("TimeMinusQuarter") && Near(LevelInfo.i.timeOfDay,12.75f),"MET OFFICE quarter-hour adjustment");
        Need(Press("TimeRestore") && Near(LevelInfo.i.timeOfDay,start),"cumulative clock override restores original time");
        Need(Near(Clock(),elapsed) && Equals(factor,AccessTools.Field(typeof(LevelInfo),"timeFactor").GetValue(LevelInfo.i)) && ReferenceEquals(settings,LevelInfo.i.LoadedMapSettings),"time controls preserve elapsed mission time, time rate and loaded mission settings");
        Need(!(bool)Call(time,"Set",float.NaN) && !(bool)Call(time,"Set",float.PositiveInfinity),"invalid time requests rejected");
        Need(!(bool)Prop(time,"Active"),"restored time control releases ownership");
        Need((bool)Call(time,"Set",12f),"noon selected for controlled storm comparison");
    }
    private void TestUiBounds()
    {
        var window=Field(console,"window");
        var root=(RectTransform)Prop(window,"Root");
        Canvas.ForceUpdateCanvases();
        Vector3[] outer=new Vector3[4]; root.GetWorldCorners(outer);
        foreach(var item in (System.Collections.IEnumerable)Field(console,"hitList"))
        {
            Component hit=(Component)item;
            if(hit==null || !hit.gameObject.activeInHierarchy) continue;
            var rect=(RectTransform)hit.transform;
            Vector3[] corners=new Vector3[4]; rect.GetWorldCorners(corners);
            Need(corners.All(v=>v.x>=outer[0].x-2 && v.x<=outer[2].x+2 && v.y>=outer[0].y-2 && v.y<=outer[2].y+2),"visible control within MET OFFICE window: "+hit.name);
        }
    }
    private double ImageDelta(string first,string second)
    {
        var a=new Texture2D(2,2);var b=new Texture2D(2,2);
        try {ImageConversion.LoadImage(a,File.ReadAllBytes(Path.Combine(dir,first)));ImageConversion.LoadImage(b,File.ReadAllBytes(Path.Combine(dir,second)));
            var x=a.GetPixels32();var y=b.GetPixels32();long delta=0;int n=0;for(int i=0;i<x.Length;i+=16){delta+=Math.Abs(x[i].r-y[i].r)+Math.Abs(x[i].g-y[i].g)+Math.Abs(x[i].b-y[i].b);n++;}return delta/(double)(n*3*255);}
        finally{Destroy(a);Destroy(b);}
    }
    private void Capture(string name,bool withUi=false,RectTransform overrideRoot=null)
    {
        RenderTexture target=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32); target.Create();
        RenderTexture prior=RenderTexture.active;
        var ui=new List<(Canvas canvas,RenderMode mode,Camera camera,float distance)>();
        var layers=new List<(GameObject obj,int layer)>();
        GameObject captureHost=null;
        Camera renderCamera=camera;
        try
        {
            if(withUi)
            {
                var window=overrideRoot==null ? Field(console,"window") : null;
                var root=overrideRoot ?? (RectTransform)Prop(window,"Root");
                Canvas canvas=root.GetComponentInParent<Canvas>();
                Need(canvas!=null,"MET OFFICE canvas available for capture");
                captureHost=new GameObject("Isolated MET OFFICE capture camera");
                renderCamera=captureHost.AddComponent<Camera>();
                renderCamera.cullingMask=1<<31; renderCamera.clearFlags=CameraClearFlags.SolidColor;
                renderCamera.backgroundColor=new Color(.035f,.05f,.06f,1f);
                renderCamera.nearClipPlane=.1f; renderCamera.farClipPlane=100f;
                captureHost.AddComponent<UniversalAdditionalCameraData>();
                foreach(Transform item in root.GetComponentsInChildren<Transform>(true)) { layers.Add((item.gameObject,item.gameObject.layer)); item.gameObject.layer=31; }
                ui.Add((canvas,canvas.renderMode,canvas.worldCamera,canvas.planeDistance));
                canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=renderCamera; canvas.planeDistance=1f;
                Canvas.ForceUpdateCanvases();
            }
            RenderPipeline.SubmitRenderRequest(renderCamera,new UniversalRenderPipeline.StandardRequest{destination=target});
            RenderTexture.active=target;
            var pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false); pixels.ReadPixels(new Rect(0,0,1920,1080),0,0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(dir,name),pixels.EncodeToPNG()); Destroy(pixels);
            Note("CAPTURE "+name);
        }
        finally
        {
            foreach(var c in ui) { c.canvas.renderMode=c.mode; c.canvas.worldCamera=c.camera; c.canvas.planeDistance=c.distance; }
            foreach(var l in layers) l.obj.layer=l.layer;
            if(captureHost!=null) Destroy(captureHost);
            RenderTexture.active=prior; target.Release(); Destroy(target);
        }
    }
    private void TestRainAltitude()
    {
        // A preceding missing-camera fixture deliberately releases the thickened deck.
        // Reacquire the real camera's effects before measuring its current native ceiling.
        var cloudCache=Field(manager,"clouds");FI(cloudCache,"nextDimensionCheck").SetValue(cloudCache,0f);
        Call(manager,"UpdateRainSystems",Clock());
        var layer=AccessTools.Field(typeof(LevelInfo),"cloudLayer").GetValue(LevelInfo.i);
        float thickness=(float)Field(layer,"layerThickness"),size=(float)Field(layer,"cloudSizeMax");
        float top=LevelInfo.i.cloudHeight+thickness+size*.55f;
        Vector3 position=camera.transform.position;
        object forced=Field(manager,"forcedRainIntensity"),point=Field(manager,"localWeather"),exposure=Field(manager,"rainExposure"),shelter=Field(manager,"sheltered"),next=Field(manager,"nextShelterCheck");
        try
        {
            FI(manager,"forcedRainIntensity").SetValue(manager,(float?)1f); FI(manager,"rainExposure").SetValue(manager,1f); FI(manager,"sheltered").SetValue(manager,false); FI(manager,"nextShelterCheck").SetValue(manager,float.MaxValue);
            object synthetic=Field(manager,"localWeather");
            FI(synthetic,"RainBase").SetValue(synthetic,LevelInfo.i.cloudHeight);
            FI(synthetic,"RainTop").SetValue(synthetic,top+3000f);
            FI(synthetic,"RainRate").SetValue(synthetic,20f);
            FI(manager,"localWeather").SetValue(manager,synthetic);
            camera.transform.position=new Vector3(position.x,Datum.LocalSeaY+Mathf.Max(5f,LevelInfo.i.cloudHeight-500f),position.z);
            Call(manager,"UpdateRainSystems",123f);
            Need((float)Field(manager,"viewPrecipitation")>.1f,"canopy/rain precipitation remains active below clouds");
            camera.transform.position=new Vector3(position.x,Datum.LocalSeaY+top+250f,position.z); Call(manager,"UpdateRainSystems",123f);
            Need((float)Field(manager,"viewPrecipitation")<.001f,"canopy/rain precipitation stops above native cloud ceiling");
        }
        finally
        {
            camera.transform.position=position; FI(manager,"forcedRainIntensity").SetValue(manager,forced); FI(manager,"localWeather").SetValue(manager,point); FI(manager,"rainExposure").SetValue(manager,exposure); FI(manager,"sheltered").SetValue(manager,shelter); FI(manager,"nextShelterCheck").SetValue(manager,next);
        }
    }
    private async UniTask TestStorm(ConfigFile config)
    {
        // Filled after manager integration; assertions operate on the real native level.
        var layer=AccessTools.Field(typeof(LevelInfo),"cloudLayer").GetValue(LevelInfo.i);
        float originalThickness=(float)Field(layer,"layerThickness");
        Need((bool)Call(manager,"PinSky",(byte)44),"ordinary cold-front preset selected");
        await UniTask.Delay(1000,ignoreTimeScale:true);
        Call(manager,"SnapToTargets",LevelInfo.i);
        Capture("ordinary-storm.png");
        Need((bool)Call(manager,"PinSky",(byte)47),"extreme storm preset selected");
        await Until(()=>(float)Field(manager,"targetConditions")>.99f,5,"extreme preset targets maximum native cloud conditions");
        Need(LevelInfo.i.conditions>.99f,"extreme native conditions applied immediately by actual selection");
        await UniTask.Delay(4500,ignoreTimeScale:true);
        Need(Near(LevelInfo.i.cloudHeight,1800f,2f),"extreme upper native deck base set");
        Need((float)Field(layer,"layerThickness")>=1100f,"extreme native cloud deck thickened");
        object dressing=Field(manager,"extremeDressing");
        Need((bool)Prop(dressing,"DeckApplied") && (int)Prop(dressing,"MistParticles")>0 && (int)Prop(dressing,"MistParticles")<=36,"extreme terrain mist exists within bounded particle budget");
        TestWeatherExtremeFeatureToggles(config);
        Need((byte)Prop(Field(manager,"fieldKey"),"SkyPin")==47,"new preset survives key serialization range");
        Capture("extreme-storm.png");
        TestFogAndMissingCamera();
        TestRainAltitude();
        Need((bool)Static("BoscaliUnifiedWeather.WeatherUiBridge","OpenMetOffice"),"extreme preset console opened");
        await UniTask.Delay(500,ignoreTimeScale:true);
        Capture("met-office-extreme.png",true); Call(console,"Close");
        Need((bool)Call(manager,"PinSky",(byte)44),"ordinary storm restored");
        await UniTask.Delay(1000,ignoreTimeScale:true);
        Need(Near((float)Field(layer,"layerThickness"),originalThickness),"leaving extreme preset restores native cloud thickness");
        Need(LevelInfo.i.conditions<=.6001f,"leaving extreme preset restores ordinary native weather cap");
        Need(!(bool)Prop(dressing,"DeckApplied") && (int)Prop(dressing,"MistParticles")==0,"leaving extreme preset clears terrain mist");
        var enabled=(ConfigEntry<bool>)config[new ConfigDefinition("Weather","Enabled")];
        Need((bool)Call(manager,"PinSky",(byte)47),"extreme storm selected again for disable cleanup");
        await UniTask.Delay(1000,ignoreTimeScale:true); enabled.Value=false;
        await UniTask.Delay(1000,ignoreTimeScale:true);
        Need(!(bool)Prop(manager,"IsCaptured") && Near((float)Field(layer,"layerThickness"),originalThickness),"master weather off releases extreme native override");
        enabled.Value=true;
        await Until(()=>(bool)Prop(manager,"IsCaptured"),20,"weather recaptured after toggle");
    }
    private async UniTask TestThunder()
    {
        Type t=assembly.GetType("BoscaliSummer.Modules.Weather.Audio.ThunderSoundscape",true);
        object thunder=Activator.CreateInstance(t,true);
        try
        {
            Call(thunder,"Tick",true,false);
            await UniTask.Delay(1000,ignoreTimeScale:true);
            for(int i=0;i<12;i++) Call(thunder,"Play",1000f,(uint)(123+i),0f,false);
            Need((int)Prop(thunder,"Queued")==0,"extreme thunder has no delayed propagation queue");
            Need((int)Prop(thunder,"Playing")<=3,"thunder audio voices bounded at three");
            Need((int)Prop(thunder,"Played")>0,"extreme thunder creates immediate audio playback");
            Call(thunder,"Tick",false,false);
            Need((int)Prop(thunder,"Playing")==0,"turning thunder off stops owned audio voices");
        }
        finally { Call(thunder,"Dispose"); }
    }
    private void TestFogAndMissingCamera()
    {
        object atmosphere=Field(manager,"atmosphere");
        Call(atmosphere,"Restore");
        float oldFog=RenderSettings.fogDensity,oldAmbient=RenderSettings.ambientIntensity;
        Color oldColor=RenderSettings.fogColor;
        var cameraOwner=SceneSingleton<CameraStateManager>.i;
        var cameraField=AccessTools.Field(typeof(CameraStateManager),"mainCamera");
        Need(cameraField!=null,"native camera field available for missing-camera lifecycle fixture");
        object oldCamera=cameraField.GetValue(cameraOwner);
        try
        {
            RenderSettings.fogDensity=.0001f;
            Call(atmosphere,"Apply",1f,false,1f,RenderSettings.sun,0f,false,50f,true,0f,4000f);
            float ground=RenderSettings.fogDensity;
            Need(ground>=.0031f,"extreme haze provides dense terrain-level visibility restriction");
            Color tint=RenderSettings.fogColor;
            Need(tint.g<=Mathf.Max(tint.r,tint.b)+.005f,"extreme fog does not add green channel bias");
            Call(atmosphere,"Apply",1f,false,1f,RenderSettings.sun,0f,false,4600f,true,4600f,4000f);
            Need(RenderSettings.fogDensity<ground*.5f,"extreme fog thins above native cloud ceiling");
            Call(atmosphere,"Apply",1f,false,1f,RenderSettings.sun,0f,false,50f,true,0f,4000f);
            cameraField.SetValue(cameraOwner,null);
            Call(manager,"UpdateRainSystems",Clock());
            Need(!(bool)Prop(atmosphere,"Applied") && Near(RenderSettings.fogDensity,.0001f,.000001f) && Near(RenderSettings.ambientIntensity,oldAmbient),"missing camera restores owned extreme fog and ambient values");
            cameraField.SetValue(cameraOwner,oldCamera);
            Call(atmosphere,"Apply",1f,false,1f,RenderSettings.sun,0f,false,50f,true,0f,4000f);
            RenderSettings.fogDensity=.00123f;
            Call(atmosphere,"Restore");
            Need(Near(RenderSettings.fogDensity,.00123f,.000001f),"fog cleanup preserves a later external fog write");
        }
        finally
        {
            cameraField.SetValue(cameraOwner,oldCamera); Call(atmosphere,"Restore");
            RenderSettings.fogDensity=oldFog; RenderSettings.ambientIntensity=oldAmbient; RenderSettings.fogColor=oldColor;
        }
    }
    private void TestTransition()
    {
        object time=Field(manager,"TimeOfDayControl");
        Need((bool)Call(time,"Set",0f),"time override active before transition");
        var setter=AccessTools.PropertySetter(typeof(GameManager),"gameState");
        Need(Harmony.GetPatchInfo(setter).Owners.Contains("aaron.boscali.weather-unified.transition"),"single-player cleanup guard installed");
        setter.Invoke(null,new object[]{GameState.Menu});
        Need(!(bool)Prop(manager,"IsCaptured") && !(bool)Prop(time,"Active"),"leaving solo clears weather and time ownership");
        Need(!(bool)Call(time,"Set",12f) && !(bool)Call(manager,"PinSky",(byte)47),"non-solo time and extreme weather requests rejected");
    }
    private void TestSeaSpray(Aircraft plane)
    {
        var plugin = Chainloader.PluginInfos["aaron.boscali.weather-unified"].Instance;
        var type = plugin.GetType().Assembly.GetType("BoscaliSummer.Modules.Weather.Visuals.OceanDressing", true);
        var ocean = (MonoBehaviour)Resources.FindObjectsOfTypeAll(type).Single();
        var tick = type.GetMethod("Tick", F);
        var reset = type.GetMethod("ResetForScene", F);
        var spray = Field(ocean, "spray");
        var weather = (ConfigEntry<bool>)plugin.Config[new ConfigDefinition("Weather", "Enabled")];
        var waves = (ConfigEntry<bool>)plugin.Config[new ConfigDefinition("Weather", "OceanWavesEnabled")];
        var enabledSpray = (ConfigEntry<bool>)plugin.Config[new ConfigDefinition("Weather", "SeaSprayEnabled")];
        var level = LevelInfo.i;
        var camera = SceneSingleton<CameraStateManager>.i.mainCamera;
        Need(plane != null && plane.rb != null && camera != null && Datum.origin != null && level != null && level.ocean != null,
            "live aircraft, camera, ocean and floating origin available");
        bool savedWeather = weather.Value, savedWaves = waves.Value, savedSpray = enabledSpray.Value;
        Vector3 savedPosition = plane.transform.position, savedVelocity = plane.rb.velocity, savedEye = camera.transform.position;
        var speed = AccessTools.Field(typeof(Aircraft), "speed");
        var radar = AccessTools.Field(typeof(Aircraft), "radarAlt");
        object savedSpeed = speed.GetValue(plane), savedRadar = radar.GetValue(plane);
        var material = level.ocean.GetComponent<MeshRenderer>().sharedMaterial;
        var nativeShader = material.shader;
        int queue = material.renderQueue;
        var underlay = level.ocean.transform.Find("oceanUnderlay");
        Vector3 underlayHome = underlay != null ? underlay.localPosition : Vector3.zero;
        // Installed 0.6.1 has no explicit tick seam: its real LateUpdate is sufficient to
        // reproduce the weather-OFF failure before the later synthetic MP cases are reached.
        void RunTick(GameState state)
        {
            if (tick != null) tick.Invoke(ocean, new object[] { level, camera, state, .05f });
            else
            {
                Need(state == GameManager.gameState, "legacy build only tested in its actual game state");
                type.GetMethod("LateUpdate", F).Invoke(ocean, null);
            }
        }
        int Count(string name) => (int)Prop(spray, name);
        // Foam is laid by travelled distance, not instantly on activation. Use the
        // same 0.2-second flight interval for each fresh-emitter assertion.
        void RunEmissionWindow(GameState state)
        {
            for (int i = 0; i < 4; i++) RunTick(state);
        }
        void AssertNative(string label) => Need(material.shader == nativeShader && material.renderQueue == queue &&
            (underlay == null || underlay.localPosition == underlayHome), label);
        try
        {
            weather.Value = false; waves.Value = false; enabledSpray.Value = true;
            reset.Invoke(ocean, null);
            // This integrated fixture has already exercised default enabled ocean waves.
            // Capture the restored native baseline after their cleanup, as the standalone
            // spray fixture does by starting with both switches off in its test config.
            Note("INFO water before disable shader="+nativeShader.name+" queue="+queue+" underlay="+underlayHome+
                "; restored native shader="+material.shader.name+" queue="+material.renderQueue+" underlay="+(underlay!=null?underlay.localPosition:Vector3.zero));
            nativeShader=material.shader; queue=material.renderQueue;
            underlayHome=underlay!=null?underlay.localPosition:Vector3.zero;
            plane.transform.position = new Vector3(savedEye.x, Datum.LocalSeaY + 5f, savedEye.z);
            camera.transform.position = plane.transform.position + new Vector3(0f, 20f, -50f);
            plane.rb.velocity = new Vector3(0f, 0f, 160f);
            speed.SetValue(plane, 160f); radar.SetValue(plane, 5f);
            RunEmissionWindow(GameState.SinglePlayer);
            Need(Count("Particles") > 0 && Count("WakeParticles") > 0 && Count("WashCount") > 0,
                "weather OFF + ocean waves OFF still emits spray and foam on native water");
            AssertNative("spray leaves native water shader, queue and underlay unchanged");
            var particles = (ParticleSystem)Field(spray, "system");
            Need(particles.main.simulationSpace == ParticleSystemSimulationSpace.Custom && particles.main.customSimulationSpace == Datum.origin,
                "spray stays in floating-origin simulation space");
            Need(particles.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader.isSupported, "spray material shader supported by running graphics device");
            TestKropWaterProvider(plane);
            TestWeatherSeaFeatureToggles();

            enabledSpray.Value = false; RunTick(GameState.SinglePlayer);
            Need(Count("Particles") == 0 && Count("WakeParticles") == 0 && Count("WashCount") == 0,
                "spray switch OFF clears particles, foam and shader wash");
            enabledSpray.Value = true; RunEmissionWindow(GameState.SinglePlayer);
            Need(Count("Particles") > 0 && Count("WakeParticles") > 0, "spray switch ON recreates both effects");
            waves.Value = true; RunTick(GameState.SinglePlayer);
            AssertNative("waves setting cannot replace native water while weather master is OFF");

            reset.Invoke(ocean, null); weather.Value = true;
            RunEmissionWindow(GameState.Multiplayer);
            Need(Count("Particles") > 0 && Count("WakeParticles") > 0, "synthetic multiplayer branch allows client-local spray");
            AssertNative("synthetic multiplayer branch cannot activate replacement ocean shader");
            RunTick(GameState.Menu);
            Need(Count("Particles") == 0 && Count("WakeParticles") == 0 && Count("WashCount") == 0, "menu state clears spray");

            weather.Value = false; RunTick(GameState.SinglePlayer);
            Need(Count("Particles") > 0, "return to mission recreates spray");
            ocean.enabled = false;
            Need(Count("Particles") == 0 && Count("WakeParticles") == 0, "component disable releases owned particles");
            ocean.enabled = true; RunTick(GameState.SinglePlayer);
            reset.Invoke(ocean, null);
            Need(Count("Particles") == 0 && Count("WakeParticles") == 0, "scene reset releases owned particles");
        }
        finally
        {
            reset.Invoke(ocean, null); ocean.enabled = true;
            plane.transform.position = savedPosition; plane.rb.velocity = savedVelocity; camera.transform.position = savedEye;
            speed.SetValue(plane, savedSpeed); radar.SetValue(plane, savedRadar);
            weather.Value = savedWeather; waves.Value = savedWaves; enabledSpray.Value = savedSpray;
        }
    }
}



