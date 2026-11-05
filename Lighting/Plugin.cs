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
            flashEnds=config.Bind("Airfield lighting","Legacy flashing runway ends",false,"Optional nonstandard flashing of runway ends; FAA-style ends are steady by default.");
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
        private readonly float size,minSize,maxSize;
        internal BeaconBuilder(float size=1.2f,float minSize=.0014f,float maxSize=.006f){this.size=size;this.minSize=minSize;this.maxSize=maxSize;}
        internal void Add(Vector3 point,Color color){if(points.Count>=3000)return;points.Add(point);colors.Add(color);}
        internal ParticleSystemRenderer Finish(Transform parent,string name,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            var ps=go.AddComponent<ParticleSystem>();var main=ps.main;main.loop=false;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.startLifetime=100000f;main.startSpeed=0f;main.startSize=size;main.maxParticles=Mathf.Max(1,points.Count);
            var emission=ps.emission;emission.enabled=false;
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.minParticleSize=minSize;renderer.maxParticleSize=maxSize;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            go.AddComponent<BeaconLifecycle>().Configure(ps,points.ToArray(),colors.ToArray(),size);
            return renderer;
        }
    }
    internal sealed class BeaconLifecycle:MonoBehaviour
    {
        private ParticleSystem particles;private Vector3[] points;private Color32[] colors;private float size;
        internal void Configure(ParticleSystem ps,Vector3[] positions,Color32[] shades,float particleSize){particles=ps;points=positions;colors=shades;size=particleSize;Rebuild();}
        private void OnEnable(){if(particles!=null)Rebuild();}
        private void Rebuild()
        {
            particles.Clear();particles.Play();
            for(int i=0;i<points.Length;i++){var p=new ParticleSystem.EmitParams{position=points[i],startColor=colors[i],startSize=size,startLifetime=100000f};particles.Emit(p,1);}
            // Calculate particle bounds before pausing, otherwise Unity may cull
            // the static cloud to the emitter's tiny default bounds.
            particles.Simulate(.001f,false,false,true);
            particles.Pause();
        }
    }
    internal sealed class Field:IDisposable
    {
        internal readonly Airbase airbase;internal readonly GameObject root;internal readonly List<Hangar> hangars=new List<Hangar>();
        private readonly List<End> ends=new List<End>();private readonly List<RunwayPair> pairs=new List<RunwayPair>();
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
                // FAA approach-light planes are horizontal at the threshold elevation.
                Vector3 approach=new Vector3(d.x,0f,d.z).normalized;
                var a=AddEnd(start,-approach,side,half,length,material,beaconMaterial);var b=AddEnd(end,approach,side,half,length,material,beaconMaterial);
                pairs.Add(new RunwayPair{first=a,second=b});
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
        private End AddEnd(Vector3 p,Vector3 outward,Vector3 side,float half,float length,Material material,Material beaconMaterial)
        {
            const float feet=.3048f;
            var g=new MeshBuilder();var r=new MeshBuilder();var gb=new BeaconBuilder();var rb=new BeaconBuilder();
            float greenHalf=half+45f*feet;int greenSteps=Mathf.CeilToInt(2f*greenHalf/(5f*feet));
            for(int i=0;i<=greenSteps;i++){Vector3 point=p+side*Mathf.Lerp(-greenHalf,greenHalf,i/(float)greenSteps)+Vector3.up*.3f;g.Lamp(point,.6f,Green);gb.Add(point,Green);}
            int redSteps=Mathf.Max(1,Mathf.CeilToInt(half*2f/(10f*feet)));
            for(int i=0;i<=redSteps;i++){Vector3 point=p+side*Mathf.Lerp(-half,half,i/(float)redSteps)+Vector3.up*.3f;r.Lamp(point,.6f,Red);rb.Add(point,Red);}
            var green=g.Finish(root.transform,"Threshold green",material).GetComponent<MeshRenderer>();var red=r.Finish(root.transform,"Runway end red",material).GetComponent<MeshRenderer>();
            var greenBeacon=gb.Finish(root.transform,"Threshold green beacons",beaconMaterial);var redBeacon=rb.Finish(root.transform,"Runway end red beacons",beaconMaterial);
            var e=new End{position=p+origin,outward=outward,green=green,red=red,greenBeacon=greenBeacon,redBeacon=redBeacon};ends.Add(e);Count+=g.Count+r.Count;
            var steady=new MeshBuilder();var distant=new BeaconBuilder(.29f,.00085f,.003f);
            void Light(Vector3 q,Color color){steady.Lamp(q,.22f,color);distant.Add(q,color);}
            Color steadyWhite=new Color(.68f,.66f,.58f,1f),steadyRed=new Color(.85f,.045f,.02f,1f);
            // ALSF-2: 24 five-lamp centerline barrettes, 100 feet apart.
            for(int station=1;station<=24;station++)
            {
                Vector3 center=p+outward*(station*100f*feet)+Vector3.up*.35f;
                for(int i=-2;i<=2;i++)Light(center+side*(i*1.03f),steadyWhite);
                if(station<=9)for(int sign=-1;sign<=1;sign+=2)for(int i=-1;i<=1;i++)Light(center+side*(sign*(50f*feet)+i*1.03f),steadyRed);
                if(station==5)for(int sign=-1;sign<=1;sign+=2)for(int i=0;i<4;i++)Light(center+side*(sign*(7.62f+(i-1.5f)*1.03f)),steadyWhite);
                if(station==10)for(int sign=-1;sign<=1;sign+=2)for(int i=0;i<8;i++)Light(center+side*(sign*(3.6f+i*1.67f)),steadyWhite);
            }
            e.steady=steady.Finish(root.transform,"ALSF-2 steady bars",material).GetComponent<MeshRenderer>();
            e.steadyBeacon=distant.Finish(root.transform,"ALSF-2 distance beacons",beaconMaterial);Count+=steady.Count;
            // Creative five-lamp row chase requested for visibility: unlike standard
            // ALSF-2 individual flashers, each of the 24 steady bars brightens together.
            e.flash=new MeshRenderer[24];e.flashBeacon=new ParticleSystemRenderer[24];
            for(int i=0;i<24;i++)
            {
                Vector3 center=p+outward*((1+i)*100f*feet)+Vector3.up*.36f;var flash=new MeshBuilder();var beacon=new BeaconBuilder(.38f,.0012f,.003f);
                for(int lamp=-2;lamp<=2;lamp++){Vector3 point=center+side*(lamp*1.03f);flash.Lamp(point,.26f,White);beacon.Add(point,White);}
                e.flash[i]=flash.Finish(root.transform,"Approach row flash "+(1+i),material).GetComponent<MeshRenderer>();
                e.flashBeacon[i]=beacon.Finish(root.transform,"Approach row flash beacon "+(1+i),beaconMaterial);Count+=5;
            }
            // Four-unit PAPI on the left as seen by an aircraft approaching the runway.
            // Placement is approximate because the game's airbases have no surveyed PAPI data.
            Vector3 left=Vector3.Cross(Vector3.up,outward).normalized;
            e.papiPosition=p+origin-outward*Mathf.Min(1000f*feet,length*.3f)+left*(half+15f)+Vector3.up*.35f;
            e.papiWhite=new MeshRenderer[4];e.papiRed=new MeshRenderer[4];e.papiWhiteBeacon=new ParticleSystemRenderer[4];e.papiRedBeacon=new ParticleSystemRenderer[4];
            for(int i=0;i<4;i++)
            {
                Vector3 point=e.papiPosition-origin+left*((3-i)*9f);var white=new MeshBuilder();var redLight=new MeshBuilder();var whitePoint=new BeaconBuilder();var redPoint=new BeaconBuilder();
                white.Lamp(point,.8f,White);redLight.Lamp(point,.8f,Red);whitePoint.Add(point,White);redPoint.Add(point,Red);
                e.papiWhite[i]=white.Finish(root.transform,"PAPI white "+i,material).GetComponent<MeshRenderer>();e.papiRed[i]=redLight.Finish(root.transform,"PAPI red "+i,material).GetComponent<MeshRenderer>();
                e.papiWhiteBeacon[i]=whitePoint.Finish(root.transform,"PAPI white beacon "+i,beaconMaterial);e.papiRedBeacon[i]=redPoint.Finish(root.transform,"PAPI red beacon "+i,beaconMaterial);Count+=2;
            }
            return e;
        }
        internal void Set(bool night,bool runwayOn,bool taxiOn,bool padsOn,bool hangarOn,bool chaseOn,bool flashing,Vector3 eye,float time)
        {
            bool available=night && airbase!=null && !airbase.disabled;
            SetActive(runway,available && runwayOn);if(runwayBeacons!=null)runwayBeacons.enabled=available && runwayOn;SetActive(taxi,available && taxiOn);SetActive(pads,available && padsOn);
            SetActive(hangarFixtures,available && hangarOn);
            bool pulse=!flashing || time%1.5f<1.1f;
            foreach(var pair in pairs)
            {
                // Keep the current landing end during a rollout. Flip only after the camera
                // moves beyond the opposite threshold, rather than at runway midpoint.
                float firstOutside=Vector3.Dot(eye-pair.first.position,pair.first.outward);
                float secondOutside=Vector3.Dot(eye-pair.second.position,pair.second.outward);
                if(!pair.initialized){pair.firstActive=firstOutside>=secondOutside;pair.initialized=true;}
                else if(firstOutside>100f)pair.firstActive=true;
                else if(secondOutside>100f)pair.firstActive=false;
                SetEnd(pair.first,pair.firstActive,firstOutside,eye,available,runwayOn,chaseOn,pulse,time);
                SetEnd(pair.second,!pair.firstActive,secondOutside,eye,available,runwayOn,chaseOn,pulse,time);
            }
        }
        private static void SetEnd(End e,bool landing,float outside,Vector3 eye,bool available,bool runwayOn,bool chaseOn,bool pulse,float time)
        {
            bool enabled=available && runwayOn;
            e.green.enabled=enabled&&pulse&&landing;e.red.enabled=enabled&&pulse&&!landing;
            e.greenBeacon.enabled=e.green.enabled;e.redBeacon.enabled=e.red.enabled;
            bool approach=enabled&&landing&&outside>=0f;
            e.steady.enabled=approach;e.steadyBeacon.enabled=approach;
            // A short trailing pulse survives a 30 fps frame interval without
            // skipping rows, while preserving the two-per-second inward chase.
            float phase=Mathf.Repeat(time,.5f)*48f;
            for(int i=0;i<e.flash.Length;i++){float age=Mathf.Repeat(phase-(23-i),24f);bool on=approach&&chaseOn&&age<2.4f;e.flash[i].enabled=on;e.flashBeacon[i].enabled=on;}
            Vector3 sight=eye-e.papiPosition;float along=Vector3.Dot(sight,e.outward);
            float angle=Mathf.Atan2(sight.y,Mathf.Max(.01f,along))*Mathf.Rad2Deg;
            for(int i=0;i<4;i++)
            {
                float cutoff=i==0?2.5f:i==1?2.833333f:i==2?3.166667f:3.5f;
                bool white=approach&&along>0f&&angle>=cutoff;bool red=approach&&along>0f&&!white;
                e.papiWhite[i].enabled=white;e.papiWhiteBeacon[i].enabled=white;e.papiRed[i].enabled=red;e.papiRedBeacon[i].enabled=red;
            }
        }
        private static void SetActive(GameObject value,bool active){if(value.activeSelf!=active)value.SetActive(active);}
        public void Dispose(){if(root!=null)UnityEngine.Object.Destroy(root);}
        private sealed class End{internal Vector3 position,outward,papiPosition;internal MeshRenderer green,red,steady;internal ParticleSystemRenderer greenBeacon,redBeacon,steadyBeacon;internal MeshRenderer[] flash,papiWhite,papiRed;internal ParticleSystemRenderer[] flashBeacon,papiWhiteBeacon,papiRedBeacon;}
        private sealed class RunwayPair{internal End first,second;internal bool initialized,firstActive;}
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
