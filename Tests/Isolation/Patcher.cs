using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

public static class Patcher
{
    public static IEnumerable<string> TargetDLLs => new[] { "UnityEngine.CoreModule.dll" };
    public static void Patch(AssemblyDefinition assembly)
    {
        string root = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(root, "quit-to-menu-test.instance")))
            throw new InvalidOperationException("Test isolation requires an explicitly marked game copy.");
        string data = Path.Combine(root, "TestData");
        Directory.CreateDirectory(data);
        var method = assembly.MainModule.Types.Single(t => t.FullName == "UnityEngine.Application")
            .Methods.Single(m => m.Name == "get_persistentDataPath");
        method.ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed;
        method.Body = new MethodBody(method);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, data));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        File.WriteAllText(Path.Combine(root, "isolation.txt"), data);
    }
}
