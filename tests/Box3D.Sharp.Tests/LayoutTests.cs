using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Box3D.Tests;

public class LayoutTests
{
    public static TheoryData<Type> MirroredStructs()
    {
        var data = new TheoryData<Type>();
        foreach (Type type in Reflect.ExplicitStructs())
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void EveryMirroredStructHasANativeCounterpart()
    {
        string[] missing = Reflect.ExplicitStructs().Where(t => Reflect.NativeFor(t) == null).Select(t => t.Name).ToArray();
        Assert.Empty(missing);
        Assert.NotEmpty(Reflect.ExplicitStructs());
    }

    [Theory]
    [MemberData(nameof(MirroredStructs))]
    public void SizeMatchesNative(Type type)
    {
        Type native = Reflect.NativeFor(type);
        Assert.Equal(Reflect.SizeOf(native), Reflect.SizeOf(type));
    }

    [Theory]
    [MemberData(nameof(MirroredStructs))]
    public void FieldOffsetsMatchNative(Type type)
    {
        Type native = Reflect.NativeFor(type);
        List<FieldInfo> fields = Reflect.InstanceFields(type).ToList();
        if (fields.Count == 1 && fields[0].Name == "_data")
        {
            return;
        }

        foreach (FieldInfo field in fields)
        {
            string name = field.Name.TrimStart('_');
            Assert.True(Reflect.TryNativeOffset(native, name, out int nativeOffset), $"{type.Name}.{field.Name} has no native field '{name}'");
            Assert.True(nativeOffset == Reflect.OffsetOf(type, field), $"{type.Name}.{field.Name}: managed {Reflect.OffsetOf(type, field)}, native {nativeOffset}");
            Assert.Equal(Reflect.SizeOf(Reflect.InstanceFields(native).Concat(Reflect.InstanceFields(native).Where(f => f.FieldType.Name.EndsWith("_e__Union", StringComparison.Ordinal)).SelectMany(f => Reflect.InstanceFields(f.FieldType))).First(f => f.Name == name).FieldType), Reflect.SizeOf(field.FieldType));
        }
    }

    [Fact]
    public void IdsMatchNativeSize()
    {
        foreach (Type id in new[] { typeof(World), typeof(Body), typeof(Shape), typeof(Joint), typeof(Contact) })
        {
            Assert.Equal(Reflect.SizeOf(Reflect.InteropType("b3" + id.Name + "Id")), Reflect.SizeOf(id));
        }

        foreach (Type typed in Reflect.Safe.GetExportedTypes().Where(t => t.IsEnum && t.Name.EndsWith("Joint", StringComparison.Ordinal)))
        {
            Assert.Equal(Reflect.SizeOf(typeof(Box3D.Interop.b3JointId)), Reflect.SizeOf(typed));
        }
    }

    [Fact]
    public void HandlesArePointerSized()
    {
        Type[] handles = Reflect.Safe.GetExportedTypes()
            .Where(t => t.IsValueType && t.GetField("Pointer", BindingFlags.Instance | BindingFlags.NonPublic) != null)
            .ToArray();
        Assert.NotEmpty(handles);
        foreach (Type handle in handles)
        {
            Assert.Equal(IntPtr.Size, Reflect.SizeOf(handle));
        }
    }
}
