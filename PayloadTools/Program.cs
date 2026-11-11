using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 3) throw new ArgumentException("Usage: PayloadTools <pinned 0.8.2 client ZIP> <payload output directory> <rebuilt module directory>");
const string ExpectedZipHash = "2E32299574AD599BBB01A7966D965D069FDDDF7B15676C8235AA01570C9BFC5F";
const string RootAssembly = "BoscaliUnifiedWeather";
const string BundleResource = "UnifiedEffects.realisticnight_fx";
const string Prefix = "BepInEx/plugins/UnifiedEffects/";
var rootVersion = new Version(0, 8, 7, 0);
var zipPath = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
var rebuilt = Path.GetFullPath(args[2]);
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
if (Hash(File.ReadAllBytes(zipPath)) != ExpectedZipHash) throw new InvalidDataException("Baseline ZIP SHA256 mismatch.");
Directory.CreateDirectory(output);
using var zip = ZipFile.OpenRead(zipPath);
byte[] Bytes(string path)
{
    var entry = zip.GetEntry(Prefix + path) ?? throw new InvalidDataException("Missing baseline entry: " + path);
    using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
}
var bundle = Bytes("Lighting/realisticnight_fx");
var names = new[] {
    "Core/BoscaliUnified.dll", "KROP/KROP.UnifiedEffects.dll", "Lighting/RealisticNight.Unified.dll",
    "Lighting/Boscali.AirfieldLighting.dll", "CockpitAudio/Boscali.CockpitAudio.dll",
    "CockpitFeel/Boscali.CockpitFeel.dll", "Missile/Boscali.UnifiedMissileBoom.dll",
    "Controls/ClientEffectsControlPanel.dll"
};
var manifests = new List<object>();
foreach (var relative in names)
{
    var original = Bytes(relative); var changes = new List<string>();
    byte[] source = original;
    if (relative is "Core/BoscaliUnified.dll" or "KROP/KROP.UnifiedEffects.dll" or "CockpitAudio/Boscali.CockpitAudio.dll" or "CockpitFeel/Boscali.CockpitFeel.dll" or "Missile/Boscali.UnifiedMissileBoom.dll" or "Controls/ClientEffectsControlPanel.dll")
    {
        source = File.ReadAllBytes(Path.Combine(rebuilt, Path.GetFileName(relative)));
        changes.Add("Rebuilt optional client-effects module; SHA256 " + Hash(source));
    }
    using var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(source));
    if (!ReferenceEquals(source, original) && assembly.Name.Name != Path.GetFileNameWithoutExtension(relative)) throw new InvalidDataException("Unexpected replacement assembly identity: " + relative);
    var module = assembly.MainModule;
    if (relative.StartsWith("Core/") || relative.StartsWith("Controls/"))
    {
        var rootRef = module.AssemblyReferences.FirstOrDefault(r => r.Name == RootAssembly);
        if (rootRef == null) { rootRef = new AssemblyNameReference(RootAssembly, rootVersion); module.AssemblyReferences.Add(rootRef); }
        else if (rootRef.Version != rootVersion) throw new InvalidDataException("Unexpected existing root reference.");
        var registry = new TypeReference("UnifiedEffects", "ModuleRegistry", module, rootRef);
        foreach (var type in Flatten(module.Types)) foreach (var method in type.Methods.Where(m => m.HasBody))
        {
            int replaced = 0;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference called || called.Name != "get_PluginInfos" ||
                    called.DeclaringType.FullName != "BepInEx.Bootstrap.Chainloader") continue;
                if (instruction.OpCode != OpCodes.Call || called.Parameters.Count != 0 || called.HasThis)
                    throw new InvalidDataException("Unexpected plugin registry call shape.");
                instruction.Operand = new MethodReference("GetPluginInfos", called.ReturnType, registry) { HasThis = false };
                replaced++;
            }
            if (replaced != 0) changes.Add(method.FullName + " - registry getter calls: " + replaced);
        }
        var expectedMethods = relative.StartsWith("Core/") ? 2 : 1;
        if (changes.Count(change => change.Contains(" - registry getter calls: ")) != expectedMethods)
            throw new InvalidDataException("Unexpected number of registry consumers: " + relative);
    }
    if (relative == "Lighting/RealisticNight.Unified.dll")
    {
        var owner = module.Types.Single(t => t.FullName == "RealisticNight.StreetLightFX");
        var ensure = owner.Methods.Single(m => m.Name == "EnsureLoaded");
        var instructions = ensure.Body.Instructions;
        int first = Enumerable.Range(0,instructions.Count).Single(i =>
            instructions[i].Operand is MethodReference m && m.Name == "GetExecutingAssembly");
        var block = instructions.Skip(first).Take(8).ToArray();
        if (block.Length != 8 || block[0].Operand is not MethodReference executing ||
            block[1].Operand is not MethodReference location || location.Name != "get_Location" ||
            block[2].Operand is not MethodReference directory || directory.Name != "GetDirectoryName" ||
            block[3].OpCode != OpCodes.Ldstr || (string)block[3].Operand != "realisticnight_fx" ||
            block[4].Operand is not MethodReference combine || combine.Name != "Combine" ||
            block[5].OpCode != OpCodes.Stloc_0 || block[6].OpCode != OpCodes.Ldloc_0 ||
            block[7].Operand is not MethodReference loadFile || loadFile.Name != "LoadFromFile")
            throw new InvalidDataException("Better Night bundle-loader contract changed.");
        if (module.Resources.Any(r => r.Name == BundleResource)) throw new InvalidDataException("Resource already present.");
        module.Resources.Add(new EmbeddedResource(BundleResource, ManifestResourceAttributes.Private, bundle));

        var readerType = new TypeReference("System.IO", "BinaryReader", module, module.TypeSystem.CoreLibrary);
        var streamType = new TypeReference("System.IO", "Stream", module, module.TypeSystem.CoreLibrary);
        var getStream = new MethodReference("GetManifestResourceStream", streamType, executing.ReturnType) { HasThis = true };
        getStream.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var readerCtor = new MethodReference(".ctor", module.TypeSystem.Void, readerType) { HasThis = true };
        readerCtor.Parameters.Add(new ParameterDefinition(streamType));
        var readBytes = new MethodReference("ReadBytes", new ArrayType(module.TypeSystem.Byte), readerType) { HasThis = true };
        readBytes.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        var close = new MethodReference("Close", module.TypeSystem.Void, readerType) { HasThis = true };
        var loadMemory = new MethodReference("LoadFromMemory", loadFile.ReturnType, loadFile.DeclaringType) { HasThis = false };
        loadMemory.Parameters.Add(new ParameterDefinition(new ArrayType(module.TypeSystem.Byte)));
        var helper = new MethodDefinition("LoadUnifiedEmbeddedBundle", MethodAttributes.Private | MethodAttributes.Static,
            loadFile.ReturnType);
        owner.Methods.Add(helper);
        helper.Body.InitLocals = true;
        helper.Body.Variables.Add(new VariableDefinition(readerType));
        helper.Body.Variables.Add(new VariableDefinition(loadFile.ReturnType));
        var il = helper.Body.GetILProcessor();
        il.Append(Instruction.Create(OpCodes.Call, executing));
        il.Append(Instruction.Create(OpCodes.Ldstr, BundleResource));
        il.Append(Instruction.Create(OpCodes.Callvirt, getStream));
        il.Append(Instruction.Create(OpCodes.Newobj, readerCtor));
        il.Append(Instruction.Create(OpCodes.Stloc_0));
        var tryStart = Instruction.Create(OpCodes.Ldloc_0); il.Append(tryStart);
        il.Append(Instruction.Create(OpCodes.Ldc_I4, bundle.Length));
        il.Append(Instruction.Create(OpCodes.Callvirt, readBytes));
        il.Append(Instruction.Create(OpCodes.Call, loadMemory));
        il.Append(Instruction.Create(OpCodes.Stloc_1));
        var done = Instruction.Create(OpCodes.Ldloc_1);
        il.Append(Instruction.Create(OpCodes.Leave, done));
        var finallyStart = Instruction.Create(OpCodes.Ldloc_0); il.Append(finallyStart);
        il.Append(Instruction.Create(OpCodes.Callvirt, close));
        il.Append(Instruction.Create(OpCodes.Endfinally));
        il.Append(done); il.Append(Instruction.Create(OpCodes.Ret));
        helper.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally) {
            TryStart = tryStart, TryEnd = finallyStart, HandlerStart = finallyStart, HandlerEnd = done
        });
        // Preserve instruction objects and exception-handler boundaries in the original
        // method. Only its file-loading expression changes; shader/material code is intact.
        foreach (var instruction in block) { instruction.OpCode = OpCodes.Nop; instruction.Operand = null; }
        block[0].OpCode = OpCodes.Ldstr; block[0].Operand = "embedded:" + BundleResource;
        block[1].OpCode = OpCodes.Stloc_0;
        block[2].OpCode = OpCodes.Call; block[2].Operand = helper;
        changes.Add(ensure.FullName + " - load pinned embedded bundle instead of Assembly.Location");
        changes.Add(helper.FullName + " - resource reader with finally cleanup");
    }
    byte[] prepared = source;
    if (changes.Count != 0)
    {
        using var stream = new MemoryStream(); assembly.Write(stream); prepared = stream.ToArray();
        using var verify = AssemblyDefinition.ReadAssembly(new MemoryStream(prepared));
        if (relative == "Lighting/RealisticNight.Unified.dll")
        {
            var embedded = (EmbeddedResource)verify.MainModule.Resources.Single(r => r.Name == BundleResource);
            if (!embedded.GetResourceData().SequenceEqual(bundle)) throw new InvalidDataException("Bundle embedding mismatch.");
        }
    }
    var destination = Path.Combine(output, Path.GetFileName(relative));
    if (File.Exists(destination) && !File.ReadAllBytes(destination).SequenceEqual(prepared))
        throw new InvalidOperationException("Refusing to replace a different prepared payload: " + destination);
    File.WriteAllBytes(destination, prepared);
    manifests.Add(new { input = Prefix+relative, output = Path.GetFileName(destination), originalSha256 = Hash(original),
        preparedSha256 = Hash(prepared), bytes = prepared.Length, changes });
}
var manifest = new { baseline = Path.GetFileName(zipPath), baselineSha256 = ExpectedZipHash,
    rootAssembly = RootAssembly, rootAssemblyVersion = rootVersion.ToString(),
    privateRegistry = "UnifiedEffects.ModuleRegistry.GetPluginInfos", bundleResource = BundleResource,
    bundleSha256 = Hash(bundle), bundleBytes = bundle.Length, modules = manifests };
File.WriteAllText(Path.Combine(output,"payload-manifest.json"),JsonSerializer.Serialize(manifest,new JsonSerializerOptions { WriteIndented=true })+"\n");
Console.WriteLine("Prepared 8 private modules with rebuilt KROP, missile and control components; pinned legacy assets retained.");

static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types) =>
    types.SelectMany(t => new[] { t }.Concat(Flatten(t.NestedTypes)));
