using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace Box3D.Tests;

public sealed class PropertyRoundTripTests : IDisposable
{
    private readonly World _world;
    private readonly Body _ground;
    private readonly Body _body;
    private readonly Shape _shape;

    public PropertyRoundTripTests()
    {
        _world = World.Create(WorldDef.Default);
        _ground = Body.Create(_world, BodyDef.Default);
        BodyDef def = BodyDef.Default;
        def.type = BodyType.Dynamic;
        def.position = new Vector3(0, 2, 0);
        _body = Body.Create(_world, def);
        _shape = Shape.CreateSphere(_body, ShapeDef.Default, new Sphere { radius = 0.5f });
        Shape.CreateSphere(_ground, ShapeDef.Default, new Sphere { radius = 0.5f });
    }

    public void Dispose()
    {
        _world.Destroy();
    }

    [Fact]
    public void WorldPropertiesRoundTrip() => RoundTrip(typeof(World), _world);

    [Fact]
    public void BodyPropertiesRoundTrip() => RoundTrip(typeof(Body), _body);

    [Fact]
    public void ShapePropertiesRoundTrip() => RoundTrip(typeof(Shape), _shape, "Capsule", "Hull", "Mesh", "HeightField");

    public static TheoryData<string> JointKinds()
    {
        var data = new TheoryData<string>();
        foreach (Type joint in Reflect.Safe.GetExportedTypes().Where(t => t.IsEnum && t.Name.EndsWith("Joint", StringComparison.Ordinal) && t != typeof(Joint)))
        {
            data.Add(joint.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(JointKinds))]
    public void JointPropertiesRoundTrip(string jointName)
    {
        Type extensions = Reflect.Safe.GetType("Box3D." + jointName + "Extensions");
        MethodInfo create = extensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "Create" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(World));
        Type defType = create.GetParameters()[1].ParameterType.GetElementType();
        object def = defType.GetProperty("Default").GetValue(null);
        FieldInfo baseField = defType.GetField("base");
        object jointDef = baseField.GetValue(def);
        jointDef.GetType().GetField("bodyIdA").SetValue(jointDef, _ground);
        jointDef.GetType().GetField("bodyIdB").SetValue(jointDef, _body);
        baseField.SetValue(def, jointDef);

        object joint = create.Invoke(null, new[] { _world, def });
        Assert.NotEqual(0UL, Convert.ToUInt64(joint));
        RoundTrip(joint.GetType(), joint);
        RoundTrip(typeof(Joint), Enum.ToObject(typeof(Joint), Convert.ToUInt64(joint)));
    }

    private static void RoundTrip(Type receiver, object instance, params string[] skip)
    {
        List<MethodInfo> methods = Reflect.ExtensionMethods(receiver).ToList();
        int checkedCount = 0;
        foreach (MethodInfo getter in methods.Where(m => m.Name.StartsWith("get_", StringComparison.Ordinal) && m.GetParameters().Length == 1))
        {
            string property = getter.Name.Substring(4);
            if (Array.IndexOf(skip, property) >= 0)
            {
                continue;
            }

            MethodInfo setter = methods.FirstOrDefault(m => m.Name == "set_" + property && m.GetParameters().Length == 2);
            object value = getter.Invoke(null, new[] { instance });
            if (setter == null)
            {
                continue;
            }

            setter.Invoke(null, new[] { instance, value });
            object again = getter.Invoke(null, new[] { instance });
            Assert.True(Equals(value, again), $"{receiver.Name}.{property}: set {value}, got {again}");
            checkedCount++;
        }

        Assert.True(checkedCount > 0, $"{receiver.Name} has no settable properties");
    }
}
