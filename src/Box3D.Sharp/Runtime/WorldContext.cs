using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Box3D.Interop;

namespace Box3D;

internal sealed unsafe class WorldContext
{
    public object UserData;
    public ICustomFilterHandler CustomFilter;
    public IPreSolveHandler PreSolve;
    public IDebugShapeHandler DebugShapes;
    public readonly GCHandle Handle;

    public WorldContext()
    {
        Handle = GCHandle.Alloc(this);
    }

    public void* NativePointer => (void*)GCHandle.ToIntPtr(Handle);

    public static WorldContext Of(World world)
    {
        return From(Native.b3World_GetUserData(Unsafe.BitCast<World, b3WorldId>(world)));
    }

    public void Free()
    {
        Handle.Free();
    }

    private static WorldContext From(void* nativeContext)
    {
        return nativeContext == null ? null : (WorldContext)GCHandle.FromIntPtr((nint)nativeContext).Target;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static bool CustomFilterThunk(b3ShapeId shapeIdA, b3ShapeId shapeIdB, void* nativeContext)
    {
        return From(nativeContext).CustomFilter.OnCustomFilter(Unsafe.BitCast<b3ShapeId, Shape>(shapeIdA), Unsafe.BitCast<b3ShapeId, Shape>(shapeIdB));
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static bool PreSolveThunk(b3ShapeId shapeIdA, b3ShapeId shapeIdB, Vector3 point, Vector3 normal, void* nativeContext)
    {
        return From(nativeContext).PreSolve.OnPreSolve(Unsafe.BitCast<b3ShapeId, Shape>(shapeIdA), Unsafe.BitCast<b3ShapeId, Shape>(shapeIdB), point, normal);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static void* CreateDebugShapeThunk(b3DebugShape* debugShape, void* nativeContext)
    {
        return (void*)From(nativeContext).DebugShapes.OnCreateDebugShape(in Unsafe.AsRef<DebugShape>(debugShape));
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static void DestroyDebugShapeThunk(void* userShape, void* nativeContext)
    {
        From(nativeContext).DebugShapes.OnDestroyDebugShape((IntPtr)userShape);
    }
}

internal static unsafe class GlobalCallbacks
{
    public static IAssertHandler Assert;
    public static ILogHandler Log;
    public static IAllocatorHandler Allocator;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static int AssertThunk(sbyte* condition, sbyte* fileName, int lineNumber)
    {
string conditionText = InteropHelpers.ToManagedString(condition);
        string fileText = InteropHelpers.ToManagedString(fileName);
        IAssertHandler handler = Assert;
        if (handler != null)
        {
            return handler.OnAssert(conditionText, fileText, lineNumber);
        }

        Console.WriteLine($"BOX3D ASSERTION: {conditionText}, {fileText}, line {lineNumber}");
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static void LogThunk(sbyte* message)
    {
string text = InteropHelpers.ToManagedString(message);
        ILogHandler handler = Log;
        if (handler != null)
        {
            handler.OnLog(text);
            return;
        }

        Console.WriteLine("Box3D: " + text);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static void* AllocThunk(nuint size, int alignment)
    {
        return (void*)Allocator.OnAlloc(size, alignment);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static void FreeThunk(void* memory, nuint size)
    {
        Allocator.OnFree((IntPtr)memory, size);
    }
}
