using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

var supplied = Path.GetFullPath(args[0]);
var mainPath = Directory.Exists(supplied) ? Path.Combine(supplied,"UnifiedEffects.dll") : supplied;
var mainBytes = File.ReadAllBytes(mainPath);
var root = AssemblyDefinition.ReadAssembly(new MemoryStream(mainBytes));
var resourceNames = new Dictionary<string,string> {
    ["aaron.boscali.unified"] = "BoscaliUnified.dll",
    ["aaron.boscali.krop-unified"] = "KROP.UnifiedEffects.dll",
    ["aaron.boscali.betternight"] = "RealisticNight.Unified.dll",
    ["aaron.boscali.airfield-lighting"] = "Boscali.AirfieldLighting.dll",
    ["aaron.boscali.cockpit-audio"] = "Boscali.CockpitAudio.dll",
    ["aaron.boscali.cockpitfeel"] = "Boscali.CockpitFeel.dll",
    ["aaron.boscali.missile-unified"] = "Boscali.UnifiedMissileBoom.dll",
    ["aaron.boscali.control-unified"] = "ClientEffectsControlPanel.dll"
};
var payloads = resourceNames.ToDictionary(x=>x.Key,x=>((EmbeddedResource)root.MainModule.Resources.Single(r=>r.Name=="UnifiedEffects.Modules."+x.Value)).GetResourceData());
int passed=0, failed=0;
void Check(bool ok,string message) { Console.WriteLine((ok?"PASS ":"FAIL ")+message); if(ok) passed++; else failed++; }
IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types) => types.SelectMany(t=>new[]{t}.Concat(Flatten(t.NestedTypes)));
var assemblies = payloads.ToDictionary(x=>x.Key,x=>AssemblyDefinition.ReadAssembly(new MemoryStream(x.Value)));
assemblies.Add("aaron.boscali.weather-unified",root);
try {
    var rootEntries=Flatten(root.MainModule.Types).SelectMany(t=>t.CustomAttributes).Where(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin").ToArray();
    Check(rootEntries.Length==1 && (string)rootEntries[0].ConstructorArguments[0].Value=="aaron.boscali.weather-unified","One physically discoverable pack plugin");
    Check(root.MainModule.Resources.Count(r=>r.Name.StartsWith("UnifiedEffects.Modules."))==8,"Eight private module DLLs embedded in the root assembly");
    Check(!root.MainModule.Resources.Any(r=>r.Name.Contains("NoAtmoFX",StringComparison.OrdinalIgnoreCase)),"NOAtmoFX remains an external download");
    Check(root.MainModule.Types.Any(t=>t.FullName=="UnifiedEffects.ModuleRegistry"),"Private module registry present");
    Check(assemblies["aaron.boscali.betternight"].MainModule.Resources.Any(r=>r.Name=="UnifiedEffects.realisticnight_fx"),"Better Night shader bundle is embedded in its private module");
    foreach(var pair in assemblies) {
        var attrs=Flatten(pair.Value.MainModule.Types).SelectMany(t=>t.CustomAttributes).Where(a=>a.AttributeType.FullName=="BepInEx.BepInPlugin").ToArray();
        Check(attrs.Length==1 && (string)attrs[0].ConstructorArguments[0].Value==pair.Key,"Component identity "+pair.Key);
    }
    var krop=Flatten(assemblies["aaron.boscali.krop-unified"].MainModule.Types).ToArray();
    var kropOperands=krop.SelectMany(t=>t.Methods.Where(m=>m.HasBody)).SelectMany(m=>m.Body.Instructions).Select(i=>i.Operand?.ToString()??"").ToArray();
    Check(krop.Any(t=>t.Name=="NightVisionCameraChoicePatch") && krop.Any(t=>t.Name=="NightVisionEffects"),"Optional NVG renderer and scoped camera-choice policy present");
    Check(krop.Any(t=>t.Name=="LocalLifeboats"),"Cosmetic lifeboats present");
    var optionalNames = new[]{"AudioRefinements","ExtendedAudioHooks","ReadOnlyCabinAcoustics","VisualRefinements","ParticleVisualCopy"};
    foreach(var name in optionalNames) Check(krop.Any(t=>t.Name==name),"Optional presentation implementation present "+name);
    var forbiddenTypes = new[]{"AircraftDoorControl","PeriodicCountermeasureControl","FragmentSim","PilotParkedStatePatch","KineticNoProximityFusePatch","FlareCountOverridePatch","FlareEjectorVelocityPatch"};
    foreach(var name in forbiddenTypes) Check(!krop.Any(t=>t.Name==name),"Excluded gameplay implementation absent "+name);
    var copier=krop.Single(t=>t.Name=="ParticleVisualCopy");
    var copyMethods=Flatten(new[]{copier}).SelectMany(t=>t.Methods.Where(m=>m.HasBody)).SelectMany(m=>m.Body.Instructions).ToArray();
    Check(!copyMethods.Any(i=>i.Operand is MethodReference m && m.Name=="Instantiate"),"Fuel visual copier cannot instantiate source prefabs or run their scripts");
    Check(!copyMethods.Any(i=>i.Operand is MethodReference m && (m.Name=="AddHostedParticles" || m.Name=="AddForce" || m.Name=="ApplyDamage")),"Fuel copier has no damage, hosted-gameplay-particle or force calls");
    var dangerousCalls=new[]{"Fire","Eject","OpenHinges","OpenDoor","RequestRearm","ApplyDamage","AddDamage","AddForce","SendRPC","CallCmd"};
    Check(!krop.SelectMany(t=>t.Methods.Where(m=>m.HasBody)).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m && dangerousCalls.Contains(m.Name)),"KROP-derived module makes no identified weapon, canopy, ramp, damage or rearm mutations");
    Check(!krop.SelectMany(t=>t.Methods.Where(m=>m.HasBody)).SelectMany(m=>m.Body.Instructions).Any(i=>i.OpCode==OpCodes.Stfld && i.Operand is FieldReference f && new[]{"ControlInputs","Aircraft","Countermeasure","FlareEjector","ChaffEjector","BulletSim/Bullet","Canopy","CargoRamp"}.Contains(f.DeclaringType.FullName)),"No direct writes to controlled gameplay fields");
    Check(kropOperands.Any(s=>s.Contains("UnityEngine.AudioListener")),"Audio listener host exclusion compiled");
    var bn=Flatten(assemblies["aaron.boscali.betternight"].MainModule.Types).ToArray();
    var post=bn.Single(t=>t.FullName=="RealisticNight.LevelInfo_Lighting_Patch").Methods.Single(m=>m.Name=="Postfix");
    Check(!post.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldsfld && i.Operand is FieldReference f && f.Name=="NvgWorldAmbient"),"Better Night ignores NVG-specific ambient setting");
    var street=bn.Single(t=>t.FullName=="RealisticNight.StreetLights");
    var destruct=street.Methods.Single(m=>m.Name=="get_Destruct").Body.Instructions;
    Check(destruct.Count==2 && destruct[0].OpCode==OpCodes.Ldc_I4_0 && destruct[1].OpCode==OpCodes.Ret,"Better Night lamp destruction hard-disabled");
    Check(!bn.SelectMany(t=>t.Methods.Where(m=>m.HasBody)).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m && m.Name=="SetupPoleLayer"),"No direct global collision setup calls");
    var all=assemblies.SelectMany(p=>Flatten(p.Value.MainModule.Types).SelectMany(t=>t.Methods.Where(m=>m.HasBody)).SelectMany(m=>m.Body.Instructions.Select(i=>(plugin:p.Key,method:m,i)))).ToArray();
    var weatherTypes=Flatten(assemblies["aaron.boscali.weather-unified"].MainModule.Types).ToArray();
    Check(!weatherTypes.Any(t=>new[]{"WeatherCloudPass","CloudRenderer","WeatherCloudShadows","CloudSeekerPatch"}.Contains(t.Name)),"Replacement volumetric cloud rendering and cloud gameplay patches absent");
    Check(!assemblies.SelectMany(p=>Flatten(p.Value.MainModule.Types)).Any(t=>t.Name=="PilotReflectionCapture"),"Pilot reflection capture absent from all components");
    Check(!all.Any(x=>x.i.Operand is MethodReference mr && (mr.Name=="PlayDelayed"||mr.Name=="PlayScheduled") && !(x.plugin=="aaron.boscali.krop-unified" && x.method.DeclaringType.Name.StartsWith("AudioPropagation",StringComparison.Ordinal))),"Scheduled playback confined to optional local propagation implementation");
    Check(!all.Any(x=>x.i.Operand is MethodReference mr && ((mr.DeclaringType.FullName.StartsWith("Mirage.") && (mr.Name.Contains("Send",StringComparison.OrdinalIgnoreCase)||mr.Name.Contains("Rpc",StringComparison.OrdinalIgnoreCase))) || mr.Name.StartsWith("CallCmd",StringComparison.Ordinal))),"No identified network send calls");
    Check(!all.Any(x=>x.i.OpCode==OpCodes.Stfld && x.i.Operand is FieldReference fr && fr.DeclaringType.FullName=="NightVision"),"No direct night vision field writes");
    var resources=assemblies["aaron.boscali.unified"].MainModule.Resources.Select(r=>r.Name).ToHashSet();
    foreach(var name in new[]{"BoscaliClientExperimental.weatherrain.bundle","BoscaliClientExperimental.Immersion.pilot.bundle","BoscaliClientExperimental.Voice.vo-pull-up.wav","BoscaliClientExperimental.Voice.vo-terrain.wav","BoscaliClientExperimental.Voice.vo-over-g.wav","BoscaliClientExperimental.Voice.vo-bingo.wav"}) Check(resources.Contains(name),"Embedded resource "+name);
    Console.WriteLine("SHA256 physical pack "+Convert.ToHexString(SHA256.HashData(mainBytes)));
    foreach(var pair in payloads) Console.WriteLine("SHA256 private module "+pair.Key+" "+Convert.ToHexString(SHA256.HashData(pair.Value)));
    Console.WriteLine($"{passed} passed, {failed} failed. Static checks do not establish actual rendering, sound, input, performance or multiplayer.");
    return failed==0?0:1;
} finally { foreach(var a in assemblies.Values)a.Dispose(); }
