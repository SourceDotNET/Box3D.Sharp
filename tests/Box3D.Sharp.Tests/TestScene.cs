using System;
using System.Numerics;
using Xunit;

namespace Box3D.Tests;

internal sealed class TestScene : IDisposable
{
    public const float TimeStep = 1.0f / 60.0f;
    public const int SubSteps = 4;

    public readonly World World;

    public TestScene(Vector3? gravity = null, IDebugShapeHandler debugShapes = null)
    {
        WorldDef def = WorldDef.Default;
        if (gravity.HasValue)
        {
            def.gravity = gravity.Value;
        }

        World = Box3D.World.Create(def, debugShapes);
        Assert.True(World.IsValid);
    }

    public void Step(int count = 1)
    {
        World world = World;
        for (int i = 0; i < count; ++i)
        {
            world.Step(TimeStep, SubSteps);
        }
    }

    public Body CreateBody(BodyType type, Vector3 position)
    {
        BodyDef def = BodyDef.Default;
        def.type = type;
        def.position = position;
        return Body.Create(World, def);
    }

    public Shape CreateGround()
    {
        return CreateGround(ShapeDef.Default);
    }

    public Shape CreateGround(in ShapeDef def)
    {
        Body ground = CreateBody(BodyType.Static, new Vector3(0, -1, 0));
        return Shape.CreateHull(ground, def, BoxHull.Make(50, 1, 50));
    }

    public Shape CreateBall(Vector3 position, float radius = 0.5f, BodyType type = BodyType.Dynamic)
    {
        return CreateBall(position, ShapeDef.Default, radius, type);
    }

    public Shape CreateBall(Vector3 position, in ShapeDef def, float radius = 0.5f, BodyType type = BodyType.Dynamic)
    {
        Body body = CreateBody(type, position);
        return Shape.CreateSphere(body, def, new Sphere { radius = radius });
    }

    public void Dispose()
    {
        World.Destroy();
    }

    public static Transform At(Vector3 position)
    {
        return new Transform { p = position, q = Quaternion.Identity };
    }

    public static Transform At(float x, float y, float z)
    {
        return At(new Vector3(x, y, z));
    }

    public static void Near(float expected, float actual, float tolerance)
    {
        Assert.True(MathF.Abs(expected - actual) <= tolerance, $"expected {expected} +/- {tolerance}, got {actual}");
    }

    public static void Near(Vector3 expected, Vector3 actual, float tolerance)
    {
        Assert.True((expected - actual).Length() <= tolerance, $"expected {expected} +/- {tolerance}, got {actual}");
    }

    public static Vector3[] CubePoints(float h)
    {
        return new[]
        {
            new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(-h, h, -h),
            new Vector3(-h, -h, h), new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h),
        };
    }
}
