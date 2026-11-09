using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace UnifiedEffects.Startup
{
    // No Unity types in preloader signatures. Preserve BepInEx's normal entrypoint.
    public static class StartupPatcher
    {
        internal static bool Headless => Environment.GetCommandLineArgs().Any(a =>
            a.Equals("-batchmode", StringComparison.OrdinalIgnoreCase) || a.Equals("-nographics", StringComparison.OrdinalIgnoreCase));
        public static IEnumerable<string> TargetDLLs => Headless ? Array.Empty<string>() : new[] { "UnityEngine.CoreModule.dll" };
        public static void Patch(AssemblyDefinition assembly)
        {
            if (Headless) return;
            var module = assembly.MainModule;
            var init = module.GetType("UnityEngine.Application")?.Methods.SingleOrDefault(m => m.Name == ".cctor");
            if (init == null || !init.HasBody) { Warn(); return; }
            var calls = init.Body.Instructions.Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference m &&
                m.DeclaringType.FullName == "BepInEx.Bootstrap.Chainloader" && m.Name == "Start" && m.Parameters.Count == 0).ToArray();
            if (calls.Length != 1) { Warn(); return; }
            var start = calls[0];
            if (!init.Body.Instructions.TakeWhile(i => i != start).Any(i => i.Operand is MethodReference m &&
                m.DeclaringType.FullName == "BepInEx.Bootstrap.Chainloader" && m.Name == "Initialize")) { Warn(); return; }
            if (init.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "UnifiedEffects.Startup.RuntimeHost")) return;
            var name = typeof(StartupPatcher).Assembly.GetName();
            var scope = module.AssemblyReferences.FirstOrDefault(a => a.Name == name.Name);
            if (scope == null) { scope = new AssemblyNameReference(name.Name, name.Version); module.AssemblyReferences.Add(scope); }
            var type = new TypeReference("UnifiedEffects.Startup", "RuntimeHost", module, scope);
            var injected = Instruction.Create(OpCodes.Call, new MethodReference("BeforePlugins", module.TypeSystem.Void, type) { HasThis = false });
            init.Body.GetILProcessor().InsertBefore(start, injected);
            foreach (var instruction in init.Body.Instructions)
            {
                if (instruction.Operand == start) instruction.Operand = injected;
                else if (instruction.Operand is Instruction[] branches)
                    for (int i = 0; i < branches.Length; i++) if (branches[i] == start) branches[i] = injected;
            }
            foreach (var handler in init.Body.ExceptionHandlers)
            {
                if (handler.TryStart == start) handler.TryStart = injected;
                if (handler.HandlerStart == start) handler.HandlerStart = injected;
                if (handler.FilterStart == start) handler.FilterStart = injected;
            }
        }
        private static void Warn() => Logger.CreateLogSource("Unified Effects startup").LogWarning("Loader entrypoint changed; optional add-on host protection was not applied.");
    }
}
