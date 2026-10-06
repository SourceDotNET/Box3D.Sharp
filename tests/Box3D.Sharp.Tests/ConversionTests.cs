using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Box3D.Tests;

public unsafe class ConversionTests
{
    public static TheoryData<Type> DefaultedStructs()
    {
        var data = new TheoryData<Type>();
        foreach (Type type in Reflect.Safe.GetExportedTypes().Where(t => t.IsValueType && !t.IsByRefLike && t.Namespace == "Box3D" && t.GetProperty("Default", BindingFlags.Public | BindingFlags.Static) != null))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DefaultedStructs))]
    public void DefaultMatchesNativeDefault(Type type)
    {
        Type native = Reflect.InteropType("b3" + type.Name);
        MethodInfo nativeDefault = typeof(Box3D.Interop.Native).GetMethod("b3Default" + type.Name);
        Assert.NotNull(nativeDefault);
        object expected = nativeDefault.Invoke(null, null);
        object managed = type.GetProperty("Default").GetValue(null);
        object converted = ToNative(type, native, managed, nativeDefault);
        AssertSameFields(native, expected, converted);
    }

    [Fact]
    public void ShapeDefDefaultMatchesNativeDefault()
    {
        Box3D.Interop.b3ShapeDef converted = Box3D.Interop.Native.b3DefaultShapeDef();
        ShapeDef.Default.ToNative(ref converted);
        AssertSameFields(typeof(Box3D.Interop.b3ShapeDef), Box3D.Interop.Native.b3DefaultShapeDef(), converted);
    }

    private static void AssertSameFields(Type native, object expected, object converted)
    {
        byte[] expectedBytes = Bytes(native, expected);
        byte[] convertedBytes = Bytes(native, converted);
        bool[] covered = new bool[expectedBytes.Length];
        MarkFields(native, 0, covered);
        for (int i = 0; i < covered.Length; ++i)
        {
            if (!covered[i])
            {
                expectedBytes[i] = 0;
                convertedBytes[i] = 0;
            }
        }

        Assert.Equal(expectedBytes, convertedBytes);
    }

    [Fact]
    public void BodyDefRoundTripsEveryField()
    {
        BodyDef def = BodyDef.Default;
        def.type = BodyType.Kinematic;
        def.position = new System.Numerics.Vector3(1, 2, 3);
        def.linearDamping = 0.25f;
        def.name = "body";
        def.userData = (IntPtr)1234;
        def.isBullet = true;

        Box3D.Interop.b3BodyDef raw = Box3D.Interop.Native.b3DefaultBodyDef();
        object[] args = { raw };
        typeof(BodyDef).GetMethod("ToNative", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(def, args);
        raw = (Box3D.Interop.b3BodyDef)args[0];

        Assert.Equal(Box3D.Interop.b3BodyType.b3_kinematicBody, raw.type);
        Assert.Equal(new System.Numerics.Vector3(1, 2, 3), raw.position);
        Assert.Equal(0.25f, raw.linearDamping);
        Assert.Equal((IntPtr)1234, (IntPtr)raw.userData);
        Assert.True(raw.isBullet);

        object back = typeof(BodyDef).GetMethod("FromNative", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { raw });
        Assert.Equal((IntPtr)1234, ((BodyDef)back).userData);

        World world = World.Create(WorldDef.Default);
        try
        {
            Body body = Body.Create(world, def);
            Assert.Equal("body", body.Name);
            Assert.Equal((IntPtr)1234, body.UserData);
        }
        finally
        {
            world.Destroy();
        }
    }

    private static object ToNative(Type type, Type native, object managed, MethodInfo nativeDefault)
    {
        MethodInfo toNative = type.GetMethod("ToNative", BindingFlags.Instance | BindingFlags.NonPublic);
        if (toNative == null)
        {
            return managed.GetType() == native ? managed : BitCast(type, native, managed);
        }

        object[] args = { nativeDefault.Invoke(null, null) };
        toNative.Invoke(managed, args);
        return args[0];
    }

    private static void MarkFields(Type type, int offset, bool[] covered)
    {
        if (type.IsPrimitive || type.IsEnum || type.IsPointer || type.IsFunctionPointer || type.IsDefined(typeof(InlineArrayAttribute)))
        {
            Array.Fill(covered, true, offset, type.IsPointer || type.IsFunctionPointer ? IntPtr.Size : Reflect.SizeOf(type));
            return;
        }

        foreach (FieldInfo field in Reflect.InstanceFields(type))
        {
            MarkFields(field.FieldType, offset + Reflect.OffsetOf(type, field), covered);
        }
    }

    private static object BitCast(Type from, Type to, object value)
    {
        MethodInfo cast = typeof(Unsafe).GetMethod("BitCast").MakeGenericMethod(from, to);
        return cast.Invoke(null, new[] { value });
    }

    private static byte[] Bytes(Type type, object boxed)
    {
        int size = Reflect.SizeOf(type);
        var result = new byte[size];
        MethodInfo write = typeof(ConversionTests).GetMethod(nameof(Write), BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(type);
        write.Invoke(null, new[] { boxed, result });
        return result;
    }

    private static void Write<T>(T value, byte[] destination)
    {
        Unsafe.WriteUnaligned(ref destination[0], value);
    }
}
