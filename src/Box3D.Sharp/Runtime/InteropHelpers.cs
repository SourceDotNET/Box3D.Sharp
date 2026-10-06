using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace Box3D;

internal static unsafe class InteropHelpers
{
    internal static string ToManagedString(sbyte* value)
    {
        return value == null ? null : Marshal.PtrToStringUTF8((nint)value);
    }

    internal static int Utf8Capacity(string value)
    {
        return value == null ? 0 : System.Text.Encoding.UTF8.GetMaxByteCount(value.Length) + 1;
    }

    internal static void WriteUtf8(string value, Span<byte> destination)
    {
        if (value == null)
        {
            return;
        }

        int length = System.Text.Encoding.UTF8.GetBytes(value, destination);
        destination[length] = 0;
    }

    internal static T[] ToArray<T>(void* source, int count)
        where T : unmanaged
    {
        return source == null || count <= 0 ? Array.Empty<T>() : new ReadOnlySpan<T>(source, count).ToArray();
    }
}

internal unsafe struct CallbackContext
{
    public void* Dispatch;
    public void* State;
    private nint _exception;

    public CallbackContext(void* dispatch, void* state)
    {
        Dispatch = dispatch;
        State = state;
        _exception = 0;
    }

    public void Fail(Exception exception)
    {
        if (_exception == 0)
        {
            _exception = GCHandle.ToIntPtr(GCHandle.Alloc(ExceptionDispatchInfo.Capture(exception)));
        }
    }

    public void ThrowIfFailed()
    {
        if (_exception == 0)
        {
            return;
        }

        GCHandle handle = GCHandle.FromIntPtr(_exception);
        _exception = 0;
        var info = (ExceptionDispatchInfo)handle.Target;
        handle.Free();
        info.Throw();
    }
}
