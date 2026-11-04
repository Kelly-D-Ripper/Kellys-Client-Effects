using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BoscaliAirfieldLighting
{
    [BepInPlugin("aaron.boscali.airfield-lighting", "Airfield and industrial lighting prototype", "0.2.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || GameManager.IsHeadless) return;
            var root = new GameObject("Airfield lighting prototype") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(root);
            root.AddComponent<LightingRuntime>().Configure(Config, Logger);
        }
    }

    internal sealed class LightingRuntime : MonoBehaviour
    {
        private ConfigEntry<bool> master, runways, taxiways, hangars, helipads, industrial, flashEnds, strobes;
        private BepInEx.Logging.ManualLogSource log;
        private readonly List<Field> fields = new List<Field>();
        private readonly List<Transform> siteCandidates = new List<Transform>();
        private readonly List<Site> sites = new List<Site>();
        private readonly List<Light> lamps = new List<Light>();
        private Material material,beaconMaterial;
        private Texture2D beaconTexture;
        private UniversalRenderPipelineAsset leasedPipeline;
        private int previousLightLimit;
        private bool externalLightLimit;
        private LevelInfo level;
        private Transform datum;
        private float nextScan, nextUpdate, nextLampUpdate;
        private bool discovered;
        private bool shaderUnavailable;
        private Airbase[] knownAirbases = Array.Empty<Airbase>();
        private readonly Vector3[] nearestLamps = new Vector3[4];
        private readonly float[] lampDistances = new float[4];
        private const int MaxFields=3, MaxSites=8, MaxLamps=4;
        private const float FieldDistance=14000f, SiteDistance=4500f;
        internal int ActiveFields => fields.Count;
        internal int ActiveSites => sites.Count;
        internal int ActiveLocalLamps { get { int n=0; foreach(var light in lamps) if(light!=null && light.enabled) n++; return n; } }

        internal void Configure(ConfigFile config, BepInEx.Logging.ManualLogSource logger)
        {
            log=logger;
            master=config.Bind("Airfield lighting","Enabled",true,"Enable the local experimental airfield and site lighting.");
            runways=config.Bind("Airfield lighting","Runways",true,"Runway edges, directional green/red ends, and approach strobes.");
            taxiways=config.Bind("Airfield lighting","Taxiways",true,"Blue taxiway lights estimated from the airbase taxi routes.");
            hangars=config.Bind("Airfield lighting","Hangars",true,"Nearby hangar interior lights.");
            helipads=config.Bind("Airfield lighting","Helipads",true,"Perimeter lights on airbase helipad models.");
            industrial=config.Bind("Airfield lighting","Industrial and ports",true,"Light recognized static industrial or port buildings where present.");
            flashEnds=config.Bind("Airfield lighting","Flash runway ends",true,"Flash the directional green/red end markers. Turn off for steady real-world-style threshold/end lights.");
            strobes=config.Bind("Airfield lighting","Approach strobes",true,"White sequential lights toward each runway threshold.");
            SceneManager.activeSceneChanged += OnSceneChanged;
        }
        private void OnSceneChanged(Scene from, Scene to) => Clear();
        private void OnDestroy() { SceneManager.activeSceneChanged -= OnSceneChanged; Clear(); if(material!=null) Destroy(material);if(beaconMaterial!=null)Destroy(beaconMaterial);if(beaconTexture!=null)Destroy(beaconTexture); }
        private void Update()
        {
            var current=LevelInfo.i;
            if(master==null || !master.Value || current==null || Datum.origin==null || GameManager.gameState==GameState.Menu)
            { if(level!=null || fields.Count>0 || sites.Count>0) Clear(); return; }
            if(current!=level || datum!=Datum.origin) { Clear(); level=current; datum=Datum.origin; }
            Camera camera=SceneSingleton<CameraStateManager>.i?.mainCamera;
            if(camera==null) { DisableLamps(); foreach(var f in fields) if(f.root!=null) f.root.SetActive(false); foreach(var s in sites) if(s.root!=null) s.root.SetActive(false); return; }
            foreach(var f in fields) if(f.root!=null && !f.root.activeSelf) f.root.SetActive(true);
            if(material==null)
            {
                if(shaderUnavailable) return;
                Shader shader=Shader.Find("Sprites/Default");
                if(shader==null || !shader.isSupported) { log.LogWarning("Airfield lights unavailable: compatible unlit shader not found."); shaderUnavailable=true; return; }
                material=new Material(shader) {name="Airfield lighting prototype",hideFlags=HideFlags.HideAndDontSave};
                beaconTexture=MakeBeaconTexture();
                beaconMaterial=new Material(shader) {name="Runway distance beacons",hideFlags=HideFlags.HideAndDontSave,mainTexture=beaconTexture};
            }
            if(!discovered && Time.unscaledTime>=nextScan) { nextScan=Time.unscaledTime+5f; Discover(); }
            if(Time.unscaledTime>=nextUpdate) {nextUpdate=Time.unscaledTime+1f; Maintain(camera);}
            bool night=!current.isDayLight;
            Vector3 eye=camera.transform.GlobalPosition().AsVector3();
            float now=Time.time;
            foreach(var field in fields) field.Set(night,runways.Value,taxiways.Value,helipads.Value,hangars.Value,strobes.Value,flashEnds.Value,eye,now);
            foreach(var site in sites) if(site.root!=null) site.root.SetActive(night && industrial.Value);
            if(Time.unscaledTime>=nextLampUpdate) {nextLampUpdate=Time.unscaledTime+.5f; UpdateLamps(eye,night);}
        }
        private void Discover()
        {
            if(LevelInfo.i==null || Datum.origin==null) return;
            siteCandidates.Clear();
            // Conservative matching is intentional. Never turn generic city blocks or UI into ports.
            foreach(var b in FindObjectsOfType<MapBuilding>()) if(b!=null && IsSite(b.name)) siteCandidates.Add(b.transform);
            foreach(var b in FindObjectsOfType<Building>()) if(b!=null && IsSite(b.name) && !siteCandidates.Contains(b.transform)) siteCandidates.Add(b.transform);
            knownAirbases=FindObjectsOfType<Airbase>();
            discovered=knownAirbases.Length>0;
            log.LogInfo("Lighting discovery: "+knownAirbases.Length+" airbases; "+siteCandidates.Count+" named industrial/port buildings.");
        }
        internal static bool IsSite(string name)
        {
            if(string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("refinery",StringComparison.OrdinalIgnoreCase) || name.StartsWith("industrial",StringComparison.OrdinalIgnoreCase) || name.StartsWith("warehouse",StringComparison.OrdinalIgnoreCase) || name.StartsWith("dock",StringComparison.OrdinalIgnoreCase) || name.StartsWith("pier",StringComparison.OrdinalIgnoreCase) || name.StartsWith("harbour",StringComparison.OrdinalIgnoreCase) || name.StartsWith("harbor",StringComparison.OrdinalIgnoreCase) || name.StartsWith("port_",StringComparison.OrdinalIgnoreCase) || name.StartsWith("portbuilding",StringComparison.OrdinalIgnoreCase) || name.StartsWith("crane",StringComparison.OrdinalIgnoreCase) || name.StartsWith("controltower",StringComparison.OrdinalIgnoreCase) || name.StartsWith("ammunitionbunker",StringComparison.OrdinalIgnoreCase) || name.StartsWith("terminal",StringComparison.OrdinalIgnoreCase);
        }
        private void Maintain(Camera camera)
        {
            if(!discovered) return;
            Vector3 eye=camera.transform.GlobalPosition().AsVector3();
            for(int i=fields.Count-1;i>=0;i--)
            {
                Field f=fields[i];
                if(f.airbase==null || f.root==null || (f.Center-eye).sqrMagnitude>FieldDistance*FieldDistance) {f.Dispose();fields.RemoveAt(i);}
            }
            {
                Airbase best=null; float distance=FieldDistance*FieldDistance;
                foreach(var a in knownAirbases)
                {
                    if(a==null || a.AttachedAirbase || a.center==null || fields.Exists(f=>f.airbase==a)) continue;
                    float d=(a.center.GlobalPosition().AsVector3()-eye).sqrMagnitude;
                    if(d<distance) {distance=d;best=a;}
                }
                if(best!=null && fields.Count>=MaxFields)
                {
                    int farthest=-1;float farDistance=0f;
                    for(int i=0;i<fields.Count;i++){float d=(fields[i].Center-eye).sqrMagnitude;if(d>farDistance){farDistance=d;farthest=i;}}
                    if(farthest>=0 && distance<farDistance*.64f){fields[farthest].Dispose();fields.RemoveAt(farthest);}
                }
                if(best!=null && fields.Count<MaxFields) {try {var f=new Field(best,datum,material,beaconMaterial);fields.Add(f);log.LogInfo("Airfield lighting created for "+best.name+": "+f.Count+" markers.");} catch(Exception ex){log.LogWarning("Airfield lighting skipped "+best.name+": "+ex.Message);}}
            }
            for(int i=sites.Count-1;i>=0;i--) {var s=sites[i];if(s.source==null || s.root==null || (s.source.GlobalPosition().AsVector3()-eye).sqrMagnitude>SiteDistance*SiteDistance){s.Dispose();sites.RemoveAt(i);}}
            if(industrial.Value)
            {
                Transform best=null;float distance=SiteDistance*SiteDistance;
                foreach(var t in siteCandidates) {if(t==null || sites.Exists(s=>s.source==t)) continue;float d=(t.GlobalPosition().AsVector3()-eye).sqrMagnitude;if(d<distance){distance=d;best=t;}}
                if(best!=null && sites.Count>=MaxSites)
                {
                    int farthest=-1;float farDistance=0f;
                    for(int i=0;i<sites.Count;i++){float d=(sites[i].source.GlobalPosition().AsVector3()-eye).sqrMagnitude;if(d>farDistance){farDistance=d;farthest=i;}}
                    if(farthest>=0 && distance<farDistance*.64f){sites[farthest].Dispose();sites.RemoveAt(farthest);}
                }
                if(best!=null && sites.Count<MaxSites) {try{sites.Add(new Site(best,datum,material));}catch(Exception ex){log.LogWarning("Site lighting skipped: "+ex.Message);}}
            }
        }
        private void UpdateLamps(Vector3 eye,bool active)
        {
            while(lamps.Count<MaxLamps) {var go=new GameObject("Hangar illumination");go.transform.SetParent(datum,false);var light=go.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1f,.82f,.59f);light.range=32f;light.intensity=25f;light.shadows=LightShadows.None;light.enabled=false;lamps.Add(light);}
            var nearest=nearestLamps;var dist=lampDistances;for(int i=0;i<MaxLamps;i++)dist[i]=300f*300f;
            bool nearHangar=false;
            void Candidate(Vector3 p)
            {float d=(p-eye).sqrMagnitude;for(int i=0;i<MaxLamps;i++) if(d<dist[i]){for(int j=MaxLamps-1;j>i;j--){dist[j]=dist[j-1];nearest[j]=nearest[j-1];}dist[i]=d;nearest[i]=p;break;}}
            if(active && hangars.Value) foreach(var field in fields) if(field.airbase!=null && !field.airbase.disabled) foreach(var h in field.hangars)
                if(h!=null && !h.Disabled && h.GetSpawnTransform()!=null) {var t=h.GetSpawnTransform();var p=t.GlobalPosition().AsVector3()+Vector3.up*5f;if((p-eye).sqrMagnitude<150f*150f)nearHangar=true;Candidate(p+t.right*6f);Candidate(p-t.right*6f);}
            if(active && industrial.Value) foreach(var site in sites) if(site.source!=null) foreach(var p in site.lights) Candidate(p);
            for(int i=0;i<MaxLamps;i++) {bool on=active && dist[i]<300f*300f;lamps[i].enabled=on;if(on)lamps[i].transform.localPosition=nearest[i];}
            UpdateLightBudget(active && hangars.Value && nearHangar);
        }
        private static Texture2D MakeBeaconTexture()
        {
            const int size=32;var tex=new Texture2D(size,size,TextureFormat.RGBA32,false,true){name="Runway beacon",hideFlags=HideFlags.HideAndDontSave,wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var pixels=new Color32[size*size];for(int y=0;y<size;y++)for(int x=0;x<size;x++){float dx=(x+0.5f-size*.5f)/(size*.5f),dy=(y+0.5f-size*.5f)/(size*.5f);float r=Mathf.Sqrt(dx*dx+dy*dy);byte a=(byte)Mathf.RoundToInt(Mathf.Clamp01((1f-r)*5f)*255f);pixels[y*size+x]=new Color32(255,255,255,a);}
            tex.SetPixels32(pixels);tex.Apply(false,true);return tex;
        }
        private void UpdateLightBudget(bool needed)
        {
            var current=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(!needed || current!=leasedPipeline && leasedPipeline!=null){ReleaseLightBudget();if(!needed)return;}
            if(externalLightLimit)return;
            if(leasedPipeline!=null && leasedPipeline.maxAdditionalLightsCount!=4){leasedPipeline=null;externalLightLimit=true;return;}
            if(leasedPipeline==null && current!=null && current.maxAdditionalLightsCount<4){previousLightLimit=current.maxAdditionalLightsCount;current.maxAdditionalLightsCount=4;leasedPipeline=current;}
        }
        private void ReleaseLightBudget()
        {
            if(leasedPipeline!=null && leasedPipeline.maxAdditionalLightsCount==4)leasedPipeline.maxAdditionalLightsCount=previousLightLimit;
            leasedPipeline=null;externalLightLimit=false;
        }
        private void DisableLamps(){foreach(var light in lamps) if(light!=null) light.enabled=false;ReleaseLightBudget();}
        private void Clear()
        {
            ReleaseLightBudget();foreach(var f in fields) f.Dispose();fields.Clear();foreach(var s in sites)s.Dispose();sites.Clear();
            foreach(var l in lamps) if(l!=null) Destroy(l.gameObject);lamps.Clear();siteCandidates.Clear();
            level=null;datum=null;knownAirbases=Array.Empty<Airbase>();discovered=false;nextScan=nextUpdate=nextLampUpdate=0f;
        }
    }

    internal sealed class MeshBuilder
    {
        private readonly List<Vector3> vertices=new List<Vector3>();
        private readonly List<Color> colors=new List<Color>();
        private readonly List<Vector2> uv=new List<Vector2>();
        private readonly List<int> triangles=new List<int>();
        internal int Count {get;private set;}
        private void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
        {int k=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);vertices.Add(d);for(int i=0;i<4;i++)colors.Add(color);uv.Add(Vector2.zero);uv.Add(Vector2.right);uv.Add(Vector2.one);uv.Add(Vector2.up);triangles.Add(k);triangles.Add(k+1);triangles.Add(k+2);triangles.Add(k);triangles.Add(k+2);triangles.Add(k+3);}
        internal void Lamp(Vector3 p,float size,Color color)
        {
            if(Count>=3000)return;Count++;float h=size*.5f;Vector3 x=Vector3.right*h,z=Vector3.forward*h,u=Vector3.up*size;
            Quad(p-x,p+x,p+x+u,p-x+u,color);Quad(p-z,p+z,p+z+u,p-z+u,color);Quad(p-x-z,p+x-z,p+x+z,p-x+z,color);
        }
        internal GameObject Finish(Transform parent,string name,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            var mesh=new Mesh{name=name}; mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            go.AddComponent<MeshOwner>().mesh=mesh;return go;
        }
    }
    internal sealed class MeshOwner:MonoBehaviour {internal Mesh mesh;private void OnDestroy(){if(mesh!=null)Destroy(mesh);}}
    // Billboard beacons retain a small minimum on-screen size at distance without
    // growing into the large high-altitude blobs of the old street-light effect.
    internal sealed class BeaconBuilder
    {
        private readonly List<Vector3> points=new List<Vector3>();
        private readonly List<Color32> colors=new List<Color32>();
        internal void Add(Vector3 point,Color color){if(points.Count>=3000)return;points.Add(point);colors.Add(color);}
        internal ParticleSystemRenderer Finish(Transform parent,string name,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            var ps=go.AddComponent<ParticleSystem>();var main=ps.main;main.loop=false;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.startLifetime=100000f;main.startSpeed=0f;main.startSize=1.2f;main.maxParticles=Mathf.Max(1,points.Count);
            var emission=ps.emission;emission.enabled=false;
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.minParticleSize=.0014f;renderer.maxParticleSize=.006f;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            go.AddComponent<BeaconLifecycle>().Configure(ps,points.ToArray(),colors.ToArray());
            return renderer;
        }
    }
    internal sealed class BeaconLifecycle:MonoBehaviour
    {
        private ParticleSystem particles;private Vector3[] points;private Color32[] colors;
        internal void Configure(ParticleSystem ps,Vector3[] positions,Color32[] shades){particles=ps;points=positions;colors=shades;Rebuild();}
        private void OnEnable(){if(particles!=null)Rebuild();}
        private void Rebuild()
        {
            particles.Clear();particles.Play();
            for(int i=0;i<points.Length;i++){var p=new ParticleSystem.EmitParams{position=points[i],startColor=colors[i],startSize=1.2f,startLifetime=100000f};particles.Emit(p,1);}
            // Calculate particle bounds before pausing, otherwise Unity may cull
            // the static cloud to the emitter's tiny default bounds.
            particles.Simulate(.001f,false,false,true);
            particles.Pause();
        }
    }
    internal sealed class Field:IDisposable
    {
        internal readonly Airbase airbase;internal readonly GameObject root;internal readonly List<Hangar> hangars=new List<Hangar>();
        private readonly List<End> ends=new List<End>();private readonly List<MeshRenderer[]> chase=new List<MeshRenderer[]>();
        private readonly GameObject runway, taxi, pads, hangarFixtures;private ParticleSystemRenderer runwayBeacons;private readonly Vector3 origin;internal int Count{get;private set;}
        internal Vector3 Center=>airbase!=null && airbase.center!=null ? airbase.center.GlobalPosition().AsVector3() : origin;
        private static readonly Color White=new Color(2f,1.9f,1.65f,1),Green=new Color(.15f,2f,.35f,1),Red=new Color(2f,.06f,.025f,1),Blue=new Color(.06f,.35f,2f,1);
        internal Field(Airbase source,Transform datum,Material material,Material beaconMaterial)
        {
            airbase=source;origin=source.center.GlobalPosition().AsVector3();root=new GameObject("Airfield lights "+source.name);root.transform.SetParent(datum,false);root.transform.localPosition=origin;
            try {
            var rw=new MeshBuilder();var distant=new BeaconBuilder();
            if(source.runways!=null) foreach(var r in source.runways)
            {
                if(r==null || r.Start==null || r.End==null)continue;
                Vector3 start=r.Start.GlobalPosition().AsVector3()-origin, end=r.End.GlobalPosition().AsVector3()-origin;
                Vector3 d=end-start;float length=d.magnitude;if(length<100f || length>6000f)continue;d/=length;Vector3 side=Vector3.Cross(Vector3.up,d).normalized;float half=Mathf.Clamp(r.GetWidth()*.5f,5f,40f);
                int count=Mathf.Clamp(Mathf.CeilToInt(length/70f),2,100);
                for(int i=0;i<=count;i++){Vector3 p=Vector3.Lerp(start,end,i/(float)count)+Vector3.up*.2f;rw.Lamp(p+side*half,.65f,White);rw.Lamp(p-side*half,.65f,White);distant.Add(p+side*half,White);distant.Add(p-side*half,White);}
                AddEnd(start,-d,side,half,material,beaconMaterial);AddEnd(end,d,side,half,material,beaconMaterial);
            }
            runway=rw.Finish(root.transform,"Runway edges",material);Count+=rw.Count;
            runwayBeacons=distant.Finish(root.transform,"Runway distance beacons",beaconMaterial);
            var tx=new MeshBuilder();var network=source.GetTaxiNetwork();
            if(network?.roads!=null) foreach(var road in network.roads)
            {
                if(road?.points==null)continue;
                for(int i=1;i<road.points.Count && tx.Count<1800;i++)
                {
                    Vector3 a=road.points[i-1].AsVector3()-origin,b=road.points[i].AsVector3()-origin;Vector3 d=b-a;float len=d.magnitude;
                    if(len<1f || len>1000f || ((a+b)*.5f).sqrMagnitude>Mathf.Pow(source.GetRadius()+300f,2))continue;Vector3 side=Vector3.Cross(Vector3.up,d/len).normalized;int n=Mathf.Clamp(Mathf.CeilToInt(len/55f),1,20);
                    for(int j=0;j<n;j++){Vector3 p=Vector3.Lerp(a,b,j/(float)n)+Vector3.up*.25f;tx.Lamp(p+side*6f,.45f,Blue);tx.Lamp(p-side*6f,.45f,Blue);}
                }
            }
            taxi=tx.Finish(root.transform,"Taxiway guides",material);Count+=tx.Count;
            var hp=new MeshBuilder();var hf=new MeshBuilder();
            if(source.hangars!=null)foreach(var h in source.hangars)
            {
                if(h==null || h.GetSpawnTransform()==null)continue;Transform spawn=h.GetSpawnTransform();Vector3 p=spawn.GlobalPosition().AsVector3();
                if((p-origin).sqrMagnitude>Mathf.Pow(source.GetRadius()+300f,2))continue;
                if(h.name.IndexOf("helipad",StringComparison.OrdinalIgnoreCase)>=0){for(int k=0;k<12;k++){float a=k*Mathf.PI*2f/12f;hp.Lamp(p-origin+new Vector3(Mathf.Cos(a)*12f,.2f,Mathf.Sin(a)*12f),.55f,Green);}}
                else if(h.name.IndexOf("hangar",StringComparison.OrdinalIgnoreCase)>=0 || h.name.IndexOf("shelter",StringComparison.OrdinalIgnoreCase)>=0){hangars.Add(h);var light=p-origin+Vector3.up*5f;hf.Lamp(light+spawn.right*6f,.75f,new Color(2f,1.5f,.9f,1f));hf.Lamp(light-spawn.right*6f,.75f,new Color(2f,1.5f,.9f,1f));}
            }
            pads=hp.Finish(root.transform,"Helipads",material);Count+=hp.Count;
            hangarFixtures=hf.Finish(root.transform,"Hangar fixtures",material);Count+=hf.Count;
            } catch { if(root!=null) UnityEngine.Object.Destroy(root); throw; }
        }
        private void AddEnd(Vector3 p,Vector3 outward,Vector3 side,float half,Material material,Material beaconMaterial)
        {
            var g=new MeshBuilder();var r=new MeshBuilder();var gb=new BeaconBuilder();var rb=new BeaconBuilder();int count=Mathf.Clamp(Mathf.CeilToInt(half*2f/4f),4,20);
            for(int i=0;i<=count;i++){Vector3 point=p+side*Mathf.Lerp(-half,half,i/(float)count)+Vector3.up*.3f;g.Lamp(point,.7f,Green);r.Lamp(point,.7f,Red);gb.Add(point,Green);rb.Add(point,Red);}
            var green=g.Finish(root.transform,"Threshold green",material).GetComponent<MeshRenderer>();var red=r.Finish(root.transform,"Runway end red",material).GetComponent<MeshRenderer>();
            var greenBeacon=gb.Finish(root.transform,"Threshold green beacons",beaconMaterial);var redBeacon=rb.Finish(root.transform,"Runway end red beacons",beaconMaterial);
            ends.Add(new End{position=p+origin,outward=outward,green=green,red=red,greenBeacon=greenBeacon,redBeacon=redBeacon});Count+=g.Count+r.Count;
            var array=new MeshRenderer[6];for(int i=0;i<array.Length;i++){var b=new MeshBuilder();b.Lamp(p+outward*(40f+45f*i)+Vector3.up*.35f,.85f,White);array[i]=b.Finish(root.transform,"Approach strobe "+i,material).GetComponent<MeshRenderer>();Count+=b.Count;}chase.Add(array);
        }
        internal void Set(bool night,bool runwayOn,bool taxiOn,bool padsOn,bool hangarOn,bool chaseOn,bool flashing,Vector3 eye,float time)
        {
            bool available=night && airbase!=null && !airbase.disabled;
            SetActive(runway,available && runwayOn);if(runwayBeacons!=null)runwayBeacons.enabled=available && runwayOn;SetActive(taxi,available && taxiOn);SetActive(pads,available && padsOn);
            SetActive(hangarFixtures,available && hangarOn);
            bool pulse=!flashing || time%1.5f<1.1f;
            foreach(var e in ends){bool outside=Vector3.Dot(eye-e.position,e.outward)>=0f;e.green.enabled=available&&runwayOn&&pulse&&outside;e.red.enabled=available&&runwayOn&&pulse&&!outside;e.greenBeacon.enabled=e.green.enabled;e.redBeacon.enabled=e.red.enabled;}
            int phase=5-Mathf.FloorToInt((time%1.5f)/1.5f*6f);
            foreach(var group in chase)for(int i=0;i<group.Length;i++)group[i].enabled=available&&runwayOn&&chaseOn&&i==phase;
        }
        private static void SetActive(GameObject value,bool active){if(value.activeSelf!=active)value.SetActive(active);}
        public void Dispose(){if(root!=null)UnityEngine.Object.Destroy(root);}
        private sealed class End{internal Vector3 position,outward;internal MeshRenderer green,red;internal ParticleSystemRenderer greenBeacon,redBeacon;}
    }
    internal sealed class Site:IDisposable
    {
        internal readonly Transform source;internal readonly GameObject root;internal readonly List<Vector3> lights=new List<Vector3>();
        internal Site(Transform t,Transform datum,Material material)
        {
            source=t;Vector3 p=t.GlobalPosition().AsVector3();root=new GameObject("Industrial light "+t.name);root.transform.SetParent(datum,false);root.transform.localPosition=p;
            try {
                var builder=new MeshBuilder();var renderer=t.GetComponentInChildren<Renderer>();
                Vector3 center=renderer!=null ? renderer.bounds.center.ToGlobalPosition().AsVector3()-p : Vector3.zero;
                float radius=renderer!=null ? Mathf.Clamp(Mathf.Max(renderer.bounds.extents.x,renderer.bounds.extents.z)*.65f,4f,24f) : 5f;
                float top=renderer!=null ? Mathf.Clamp(renderer.bounds.extents.y*1.6f,4f,16f) : 8f;
                for(int i=0;i<4;i++){float a=i*Mathf.PI*.5f;Vector3 point=center+new Vector3(Mathf.Cos(a)*radius,3.5f,Mathf.Sin(a)*radius);builder.Lamp(point,.7f,new Color(2f,1.2f,.6f,1f));if(i%2==0)lights.Add(p+point);}
                builder.Lamp(center+Vector3.up*top,.5f,new Color(2f,.08f,.03f,1));builder.Finish(root.transform,"Site markers",material);
            } catch {if(root!=null)UnityEngine.Object.Destroy(root);throw;}
        }
        public void Dispose(){if(root!=null)UnityEngine.Object.Destroy(root);}
    }
}
