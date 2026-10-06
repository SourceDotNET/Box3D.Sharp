using System;

namespace Box3D.Interop;

[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, AllowMultiple = false, Inherited = true)]
public sealed class NativeTypeNameAttribute : Attribute
{
    public NativeTypeNameAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
