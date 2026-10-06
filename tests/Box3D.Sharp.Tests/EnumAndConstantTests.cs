using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Box3D.Tests;

public class EnumAndConstantTests
{
    public static TheoryData<Type> Enums()
    {
        var data = new TheoryData<Type>();
        foreach (Type type in Reflect.Safe.GetExportedTypes().Where(t => t.IsEnum && Reflect.InteropType("b3" + t.Name)?.IsEnum == true))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Enums))]
    public void EnumValuesMatchNative(Type type)
    {
        Type native = Reflect.InteropType("b3" + type.Name);
        Assert.Equal(Enum.GetUnderlyingType(native), Enum.GetUnderlyingType(type));
        object[] expected = native.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => Convert.ChangeType(f.GetRawConstantValue(), Enum.GetUnderlyingType(native))).ToArray();
        object[] actual = type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => Convert.ChangeType(f.GetRawConstantValue(), Enum.GetUnderlyingType(type))).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EveryNativeEnumIsMirrored()
    {
        string[] missing = Reflect.Interop.GetExportedTypes()
            .Where(t => t.IsEnum && t.Name.StartsWith("b3", StringComparison.Ordinal) && Reflect.Safe.GetType("Box3D." + t.Name.Substring(2)) == null)
            .Select(t => t.Name)
            .ToArray();
        Assert.Empty(missing);
    }

    [Fact]
    public void ConstantsMatchNative()
    {
        Type native = typeof(Box3D.Interop.Native);
        int compared = 0;
        foreach (FieldInfo field in native.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.Name.StartsWith("B3_", StringComparison.Ordinal)))
        {
            string name = string.Concat(field.Name.Substring(3).Split('_').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
            MemberInfo managed = typeof(Constants).GetMember(name).FirstOrDefault();
            if (managed == null)
            {
                continue;
            }

            object expected = field.GetValue(null);
            object actual = managed is FieldInfo f ? f.GetValue(null) : ((PropertyInfo)managed).GetValue(null);
            Assert.Equal(expected, actual);
            compared++;
        }

        Assert.True(compared > 30, $"only {compared} constants compared");
    }
}
