using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Box3D.Tests;

internal static class Reflect
{
    public static readonly Assembly Safe = typeof(World).Assembly;
    public static readonly Assembly Interop = typeof(Box3D.Interop.Native).Assembly;

    private static readonly Dictionary<string, string> Renames = new Dictionary<string, string>
    {
        { "RecordPlayer", "b3RecPlayer" },
        { "RecordPlayerInfo", "b3RecPlayerInfo" },
    };

    public static Type InteropType(string name) => Interop.GetType("Box3D.Interop." + name);

    public static Type NativeFor(Type managed) => InteropType(Renames.TryGetValue(managed.Name, out string native) ? native : "b3" + managed.Name);

    public static IEnumerable<FieldInfo> InstanceFields(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static int SizeOf(Type type)
    {
        var method = new DynamicMethod("SizeOf", typeof(int), Type.EmptyTypes, typeof(Reflect).Module, true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Sizeof, type);
        il.Emit(OpCodes.Ret);
        return (int)method.Invoke(null, null);
    }

    public static int OffsetOf(Type type, FieldInfo field)
    {
        var method = new DynamicMethod("OffsetOf", typeof(int), Type.EmptyTypes, typeof(Reflect).Module, true);
        ILGenerator il = method.GetILGenerator();
        LocalBuilder local = il.DeclareLocal(type);
        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Ldflda, field);
        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);
        return (int)method.Invoke(null, null);
    }

    public static bool TryNativeOffset(Type nativeType, string name, out int offset)
    {
        foreach (FieldInfo field in InstanceFields(nativeType))
        {
            if (field.Name == name)
            {
                offset = OffsetOf(nativeType, field);
                return true;
            }
        }

        foreach (FieldInfo field in InstanceFields(nativeType))
        {
            if (field.FieldType.IsNested && field.FieldType.Name.EndsWith("_e__Union", StringComparison.Ordinal)
                && TryNativeOffset(field.FieldType, name, out int inner))
            {
                offset = OffsetOf(nativeType, field) + inner;
                return true;
            }
        }

        offset = -1;
        return false;
    }

    public static IEnumerable<Type> ExplicitStructs() =>
        Safe.GetExportedTypes().Where(t => t.IsValueType && !t.IsEnum && t.Namespace == "Box3D" && !t.IsNested
            && t.StructLayoutAttribute?.Value == LayoutKind.Explicit);

    public static IEnumerable<Type> ManagedStructs() =>
        Safe.GetExportedTypes().Where(t => t.IsValueType && !t.IsEnum && t.Namespace == "Box3D" && !t.IsNested
            && t.StructLayoutAttribute?.Value != LayoutKind.Explicit
            && t.GetMethod("ToNative", BindingFlags.Instance | BindingFlags.NonPublic) != null);

    public static IEnumerable<MethodInfo> ExtensionMethods(Type receiver) =>
        Safe.GetExportedTypes().Where(t => t.IsAbstract && t.IsSealed && t.Name.EndsWith("Extensions", StringComparison.Ordinal))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == receiver);
}
