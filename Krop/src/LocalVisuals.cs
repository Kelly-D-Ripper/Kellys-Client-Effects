using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace KropClientEffects
{
    internal static class VisualObjects
    {
        private sealed class Entry { internal GameObject Object; internal string Category; internal WeakReference<Unit> Owner; }
        private static readonly List<Entry> Objects = new List<Entry>();
        internal static GameObject Create(string name, string category, int limit)
        {
            Objects.RemoveAll(x => x.Object == null);
            // Count in place rather than allocating a temporary list on every effect spawn.
            bool trails = category.StartsWith("trail");
            int count = 0;
            foreach (var entry in Objects)
                if (trails ? entry.Category.StartsWith("trail") : entry.Category == category) count++;
            if (count >= limit) return null;
            var obj = new GameObject("KROP.ClientEffects." + name);
            Objects.Add(new Entry { Object=obj, Category=category });
            return obj;
        }
        internal static void Tick()
        {
            for (int i=Objects.Count-1;i>=0;i--)
            {
                var entry=Objects[i];
                bool ownerGone = entry.Owner != null && (!entry.Owner.TryGetTarget(out Unit owner) || owner == null || owner.disabled);
                bool disabled=(entry.Category=="wake" && !AirWakeConfig.Enabled.Value)
                    ||(entry.Category=="trail-aircraft" && !ContrailConfig.Enabled.Value)
                    ||(entry.Category=="trail-missile" && !ContrailConfig.MissileContrailsEnabled.Value)
                    ||(entry.Category=="boat" && !LifeboatConfig.Enabled.Value);
                if (entry.Object==null || disabled || ownerGone)
                {
                    if (entry.Object!=null) UnityEngine.Object.Destroy(entry.Object);
                    Objects.RemoveAt(i);
                }
            }
        }
        internal static void TrackOwner(GameObject obj, Unit owner)
        {
            var entry = Objects.Find(x => x.Object == obj);
            if (entry != null) entry.Owner = new WeakReference<Unit>(owner);
        }
        internal static int Count(string category)
        {
            Objects.RemoveAll(x=>x.Object==null);
            int count = 0;
            foreach (var entry in Objects) if (entry.Category == category) count++;
            return count;
        }
        internal static void Clear()
        {
            foreach (var entry in Objects) if (entry.Object!=null) UnityEngine.Object.Destroy(entry.Object);
            Objects.Clear();
        }
    }
    internal static class LocalLifeboats
    {
        private sealed class Pending { internal GlobalPosition Position; internal Quaternion Rotation; internal float When; }
        private static readonly List<Pending> Queue=new List<Pending>();
        private static ConditionalWeakTable<Ship,object> Seen=new ConditionalWeakTable<Ship,object>();
        private static AssetBundle bundle;
        private static GameObject model;
        private static bool loadFailed;
        internal static void ShipSank(Ship ship)
        {
            if (!Settings.Active || !LifeboatConfig.Enabled.Value || ship==null || ship.definition==null || Datum.origin==null || Seen.TryGetValue(ship,out _)) return;
            Seen.Add(ship,new object());
            int available=LifeboatConfig.GlobalLimit.Value-VisualObjects.Count("boat")-Queue.Count;
            int count=EffectMath.BoatCount(ship.definition.manpower,LifeboatConfig.CrewPerBoat.Value,LifeboatConfig.MaxLifeboatsPerShip.Value,available);
            for (int i=0;i<count;i++)
            {
                float angle=EffectRandom.Range(0f,Mathf.PI*2f);
                float radius=Mathf.Sqrt(EffectRandom.Range(25f*25f,150f*150f));
                Vector3 position=ship.transform.position+new Vector3(Mathf.Cos(angle)*radius,0f,Mathf.Sin(angle)*radius);
                position.y=Datum.LocalSeaY+0.15f;
                Queue.Add(new Pending { Position=position.ToGlobalPosition(),
                    Rotation=Quaternion.Euler(0f,EffectRandom.Range(0f,360f),0f)*Quaternion.Euler(-90f,0f,0f),
                    When=Time.time+EffectRandom.Range(0f,LifeboatConfig.Delay.Value) });
            }
        }
        private static bool LoadModel()
        {
            if (model!=null) return true;
            if (loadFailed) return false;
            loadFailed=true;
            try
            {
                using (Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("KropClientEffects.lifeboat"))
                using (var data=new MemoryStream())
                {
                    if (stream==null) return false;
                    stream.CopyTo(data); bundle=AssetBundle.LoadFromMemory(data.ToArray());
                }
                if (bundle==null) return false;
                model=bundle.LoadAsset<GameObject>("Navy_Liferaft_symmetrictexfix");
                return model!=null;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Cosmetic lifeboat model unavailable: "+ex.Message); return false; }
        }
        internal static void Tick()
        {
            if (!LifeboatConfig.Enabled.Value) { Queue.Clear(); return; }
            // The floating origin can disappear briefly while a mission unloads.
            // Keep pending positions until it exists again or scene cleanup clears them.
            if (Datum.origin == null) return;
            for (int i=Queue.Count-1;i>=0;i--)
            {
                Pending pending=Queue[i]; if (Time.time<pending.When) continue;
                Queue.RemoveAt(i);
                if (!LoadModel()) continue;
                GameObject root=VisualObjects.Create("Lifeboat","boat",LifeboatConfig.GlobalLimit.Value);
                if (root==null) continue;
                root.transform.SetParent(Datum.origin,false);
                root.transform.position=pending.Position.ToLocalPosition(); root.transform.rotation=pending.Rotation;
                root.transform.localScale=model.transform.localScale;
                // Reconstruct only Transform/MeshFilter/MeshRenderer data. Never Instantiate the
                // supplied prefab: no embedded MonoBehaviour, Collider, Rigidbody or NetworkIdentity can run.
                CopyMeshes(model.transform,root.transform);
                var visuals=root.AddComponent<LifeboatVisual>(); visuals.Initialize();
                UnityEngine.Object.Destroy(root,LifeboatConfig.LifetimeMinutes.Value*60f);
            }
        }
        private static void CopyMeshes(Transform source,Transform target)
        {
            var filter=source.GetComponent<MeshFilter>(); var renderer=source.GetComponent<MeshRenderer>();
            if (filter!=null && renderer!=null)
            {
                target.gameObject.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;
                var copy=target.gameObject.AddComponent<MeshRenderer>(); copy.sharedMaterials=renderer.sharedMaterials;
                copy.enabled=renderer.enabled; copy.shadowCastingMode=renderer.shadowCastingMode; copy.receiveShadows=renderer.receiveShadows;
            }
            for (int i=0;i<source.childCount;i++)
            {
                Transform child=source.GetChild(i);
                var obj=new GameObject(child.name); obj.transform.SetParent(target,false);
                obj.transform.localPosition=child.localPosition; obj.transform.localRotation=child.localRotation; obj.transform.localScale=child.localScale;
                CopyMeshes(child,obj.transform); obj.SetActive(child.gameObject.activeSelf);
            }
        }
        internal static void Clear() { Queue.Clear(); Seen=new ConditionalWeakTable<Ship,object>(); }
    }
    internal sealed class LifeboatVisual : MonoBehaviour
    {
        private float baseY,phase,beaconPhase;
        private readonly List<Tuple<Renderer,int>> lights=new List<Tuple<Renderer,int>>();
        private MaterialPropertyBlock block;
        private bool beaconWritten, previousBeaconOn;
        internal void Initialize()
        {
            baseY=transform.localPosition.y; phase=EffectRandom.Range(0f,Mathf.PI*2f); beaconPhase=EffectRandom.Range(0f,1.5f);
            block=new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
            {
                var materials=renderer.sharedMaterials;
                for (int i=0;i<materials.Length;i++)
                    if (materials[i]!=null && materials[i].name.IndexOf("Exterior light",StringComparison.OrdinalIgnoreCase)>=0)
                        lights.Add(Tuple.Create((Renderer)renderer,i));
            }
        }
        private void Update()
        {
            Vector3 position=transform.localPosition;
            position.y=baseY+(LifeboatConfig.Bob.Value?Mathf.Sin(Time.time*Mathf.PI*0.5f+phase)*0.08f:0f);
            transform.localPosition=position;
            bool on=LifeboatConfig.Beacon.Value && (Time.time+beaconPhase)%1.5f<0.1f;
            // The beacon changes only twice per cycle. Avoid repeated native renderer
            // property-block reads and writes every frame for every liferaft.
            if (beaconWritten && on == previousBeaconOn) return;
            beaconWritten = true; previousBeaconOn = on;
            foreach (var light in lights)
            {
                if (light.Item1 == null) continue;
                light.Item1.GetPropertyBlock(block,light.Item2);
                block.SetColor("_EmissionColor",on?new Color(4f,2.5f,0.5f):Color.black);
                light.Item1.SetPropertyBlock(block,light.Item2);
            }
        }
    }
    [HarmonyPatch(typeof(Ship),"UnitDisabled")]
    internal static class LifeboatPatch
    {
        private static void Postfix(Ship __instance,bool oldState,bool newState) { if (newState && !oldState) LocalLifeboats.ShipSank(__instance); }
    }
}
