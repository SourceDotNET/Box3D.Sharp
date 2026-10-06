using System;
using System.Runtime.InteropServices;
using Box3D.Interop;

namespace Box3D;

public sealed unsafe partial class DynamicTree : IDisposable
{
    internal b3DynamicTree* Pointer;

    internal DynamicTree(b3DynamicTree value)
    {
        Pointer = (b3DynamicTree*)NativeMemory.Alloc((nuint)sizeof(b3DynamicTree));
        *Pointer = value;
    }

    ~DynamicTree()
    {
        Release();
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    private void Release()
    {
        if (Pointer == null)
        {
            return;
        }

        Native.b3DynamicTree_Destroy(Pointer);
        NativeMemory.Free(Pointer);
        Pointer = null;
    }
}
