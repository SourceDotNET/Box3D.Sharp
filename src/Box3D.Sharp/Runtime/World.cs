using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Box3D.Interop;

namespace Box3D;

public static unsafe partial class WorldExtensions
{
    extension(World)
    {
        public static World Create(in WorldDef def, IDebugShapeHandler debugShapes = null)
        {
            var context = new WorldContext();
            b3WorldDef raw = Native.b3DefaultWorldDef();
            def.ToNative(ref raw);
            raw.userData = context.NativePointer;
            if (debugShapes != null)
            {
                context.DebugShapes = debugShapes;
                raw.createDebugShape = &WorldContext.CreateDebugShapeThunk;
                raw.destroyDebugShape = &WorldContext.DestroyDebugShapeThunk;
                raw.userDebugShapeContext = context.NativePointer;
            }

            World world = Unsafe.BitCast<b3WorldId, World>(Native.b3CreateWorld(&raw));
            if (world == World.Null)
            {
                context.Free();
            }

            return world;
        }
    }
}

public static unsafe partial class B3
{
    public static void SetAssertHandler(IAssertHandler handler)
    {
        GlobalCallbacks.Assert = handler;
        Native.b3SetAssertFcn(&GlobalCallbacks.AssertThunk);
    }

    public static void SetLogHandler(ILogHandler handler)
    {
        GlobalCallbacks.Log = handler;
        Native.b3SetLogFcn(&GlobalCallbacks.LogThunk);
    }

    public static void SetAllocator(IAllocatorHandler handler)
    {
        GlobalCallbacks.Allocator = handler;
        if (handler == null)
        {
            Native.b3SetAllocator(null, null);
        }
        else
        {
            Native.b3SetAllocator(&GlobalCallbacks.AllocThunk, &GlobalCallbacks.FreeThunk);
        }
    }
}

public static unsafe partial class WorldExtensions
{
    extension(World world)
    {
        public object UserData
        {
            get => WorldContext.Of(world).UserData;
            set => WorldContext.Of(world).UserData = value;
        }

        public void Destroy()
        {
            WorldContext context = WorldContext.Of(world);
            Native.b3DestroyWorld(Unsafe.BitCast<World, b3WorldId>(world));
            context?.Free();
        }

        public void SetCustomFilterHandler(ICustomFilterHandler handler)
        {
            WorldContext context = WorldContext.Of(world);
            context.CustomFilter = handler;
            Native.b3World_SetCustomFilterCallback(Unsafe.BitCast<World, b3WorldId>(world), handler == null ? null : &WorldContext.CustomFilterThunk, context.NativePointer);
        }

        public void SetPreSolveHandler(IPreSolveHandler handler)
        {
            WorldContext context = WorldContext.Of(world);
            context.PreSolve = handler;
            Native.b3World_SetPreSolveCallback(Unsafe.BitCast<World, b3WorldId>(world), handler == null ? null : &WorldContext.PreSolveThunk, context.NativePointer);
        }
    }
}

public readonly unsafe partial struct Recording
{
    public ReadOnlySpan<byte> Data => Pointer == null ? default : new ReadOnlySpan<byte>(Native.b3Recording_GetData(Pointer), Native.b3Recording_GetSize(Pointer));
}

public readonly unsafe partial struct CompoundData
{
    public ReadOnlySpan<SurfaceMaterial> Materials => Pointer == null ? default : new ReadOnlySpan<SurfaceMaterial>(Native.b3GetCompoundMaterials(Pointer), MaterialCount);

    public static CompoundData Create(in CompoundDef def)
    {
        b3CompoundDef raw = default;
        def.ToNative(ref raw);
        ReadOnlySpan<CompoundMeshDef> meshes = def.meshes;
        int materialCount = 0;
        foreach (CompoundMeshDef mesh in meshes)
        {
            materialCount += mesh.materials.Length;
        }

        nuint meshBytes = (nuint)(meshes.Length * sizeof(b3CompoundMeshDef));
        byte* block = (byte*)NativeMemory.Alloc(meshBytes + (nuint)(materialCount * sizeof(b3SurfaceMaterial)) + 1);
        try
        {
            var nativeMeshes = (b3CompoundMeshDef*)block;
            var nativeMaterials = (SurfaceMaterial*)(block + meshBytes);
            for (int i = 0; i < meshes.Length; ++i)
            {
                b3CompoundMeshDef nativeMesh = default;
                meshes[i].ToNative(ref nativeMesh);
                meshes[i].materials.Span.CopyTo(new Span<SurfaceMaterial>(nativeMaterials, meshes[i].materials.Length));
                nativeMesh.materials = (b3SurfaceMaterial*)nativeMaterials;
                nativeMaterials += meshes[i].materials.Length;
                nativeMeshes[i] = nativeMesh;
            }

            fixed (CompoundCapsuleDef* capsules = def.capsules)
            fixed (CompoundHullDef* hulls = def.hulls)
            fixed (CompoundSphereDef* spheres = def.spheres)
            {
                raw.capsules = (b3CompoundCapsuleDef*)capsules;
                raw.hulls = (b3CompoundHullDef*)hulls;
                raw.spheres = (b3CompoundSphereDef*)spheres;
                raw.meshes = meshes.Length == 0 ? null : nativeMeshes;
                return new CompoundData(Native.b3CreateCompound(&raw));
            }
        }
        finally
        {
            NativeMemory.Free(block);
        }
    }

    public static CompoundData FromBytes(Span<byte> bytes)
    {
        fixed (byte* pointer = bytes)
        {
            return new CompoundData(Native.b3ConvertBytesToCompound(pointer, bytes.Length));
        }
    }

    public Span<byte> ConvertToBytes()
    {
        return Pointer == null ? default : new Span<byte>(Native.b3ConvertCompoundToBytes(Pointer), ByteCount);
    }
}
