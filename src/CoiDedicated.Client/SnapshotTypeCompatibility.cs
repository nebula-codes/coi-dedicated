using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mafi.Serialization;

namespace CoiDedicated.Client;

// .NET server saves retain assembly identities in nested generic arguments.
// Patch only the game's two type-name readers, and only while loading our snapshot.
internal static class SnapshotTypeCompatibility
{
    internal static volatile bool LoadingDedicatedSnapshot;

    internal static void Install(Harmony harmony)
    {
        var patch = new HarmonyMethod(typeof(SnapshotTypeCompatibility), nameof(ReplaceTypeLookup));
        harmony.Patch(AccessTools.Method(typeof(BlobReader), "ReadTypeFromStrNoRef"), transpiler: patch);
        harmony.Patch(AccessTools.Method(typeof(BlobReader), "TryReadTypeFromStrNoRef"), transpiler: patch);
    }

    private static IEnumerable<CodeInstruction> ReplaceTypeLookup(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo original = typeof(Type).GetMethod(nameof(Type.GetType), new[] { typeof(string) })!;
        MethodInfo replacement = AccessTools.Method(typeof(SnapshotTypeCompatibility), nameof(ResolveSavedType));
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(original)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; replaced++; }
            yield return instruction;
        }
        if (replaced != 1) throw new InvalidOperationException("Unexpected CoI save type reader; compatibility patch disabled.");
    }

    private static Type? ResolveSavedType(string name)
    {
        // The original Type.GetType call is inside Mafi.dll. Preserve that lookup
        // origin for its unqualified names; the CLR resolves nested arguments.
        return Type.GetType(name,
            assemblyName => LoadingDedicatedSnapshot && assemblyName.Name == "System.Private.CoreLib"
                ? typeof(object).Assembly : Assembly.Load(assemblyName),
            (assembly, typeName, ignoreCase) => assembly != null
                ? assembly.GetType(typeName, false, ignoreCase)
                : typeof(BlobReader).Assembly.GetType(typeName, false, ignoreCase)
                    ?? typeof(object).Assembly.GetType(typeName, false, ignoreCase),
            false);
    }
}
