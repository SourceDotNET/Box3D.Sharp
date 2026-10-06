using System;
using System.Numerics;
using Xunit;
using static Box3D.Tests.TestScene;

namespace Box3D.Tests;

public sealed class BodyBehaviorTests
{
    [Fact]
    public void BodyTypesAreReportedAndCanChange()
    {
        using var scene = new TestScene();
        Body staticBody = scene.CreateBody(BodyType.Static, Vector3.Zero);
        Body kinematic = scene.CreateBody(BodyType.Kinematic, Vector3.Zero);
        Body dynamic = scene.CreateBall(new Vector3(0, 5, 0)).Body;

        Assert.Equal(BodyType.Static, staticBody.Type);
        Assert.Equal(BodyType.Kinematic, kinematic.Type);
        Assert.Equal(BodyType.Dynamic, dynamic.Type);
        Assert.Equal(scene.World, dynamic.World);

        dynamic.Type = BodyType.Static;
        Assert.Equal(BodyType.Static, dynamic.Type);
        scene.Step(30);
        Near(5, dynamic.Position.Y, 1e-5f);
    }

    [Fact]
    public void SetTransformMovesBodyAndConvertsPoints()
    {
        using var scene = new TestScene();
        Body body = scene.CreateBody(BodyType.Static, Vector3.Zero);
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        body.SetTransform(new Vector3(1, 2, 3), rotation);

        Near(new Vector3(1, 2, 3), body.Position, 1e-5f);
        Assert.True(MathF.Abs(Quaternion.Dot(rotation, body.Rotation)) > 0.9999f);
        Near(new Vector3(1, 2, 3), body.Transform.p, 1e-5f);

        Vector3 world = body.GetWorldPoint(new Vector3(1, 0, 0));
        Near(new Vector3(1, 2, 2), world, 1e-4f);
        Near(new Vector3(1, 0, 0), body.GetLocalPoint(world), 1e-4f);
        Near(new Vector3(0, 0, -1), body.GetWorldVector(Vector3.UnitX), 1e-4f);
        Near(Vector3.UnitX, body.GetLocalVector(new Vector3(0, 0, -1)), 1e-4f);
    }

    [Fact]
    public void KinematicBodyFollowsItsVelocity()
    {
        using var scene = new TestScene();
        Body body = scene.CreateBody(BodyType.Kinematic, Vector3.Zero);
        Shape.CreateSphere(body, ShapeDef.Default, new Sphere { radius = 0.5f });
        body.LinearVelocity = new Vector3(2, 0, 0);
        body.AngularVelocity = new Vector3(0, 1, 0);
        Assert.Equal(new Vector3(2, 0, 0), body.LinearVelocity);

        scene.Step(60);
        Near(new Vector3(2, 0, 0), body.Position, 0.01f);
        Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Identity, body.Rotation)) < 0.95f);
    }

    [Fact]
    public void LinearImpulseChangesVelocityByImpulseOverMass()
    {
        using var scene = new TestScene(Vector3.Zero);
        Body body = scene.CreateBall(Vector3.Zero).Body;
        float mass = body.Mass;

        body.ApplyLinearImpulseToCenter(new Vector3(mass * 3, 0, 0), true);
        Near(3, body.LinearVelocity.X, 1e-3f);

        scene.Step(60);
        Near(3, body.Position.X, 0.01f);
    }

    [Fact]
    public void ForceAcceleratesBody()
    {
        using var scene = new TestScene(Vector3.Zero);
        Body body = scene.CreateBall(Vector3.Zero).Body;
        float mass = body.Mass;

        for (int i = 0; i < 60; ++i)
        {
            body.ApplyForceToCenter(new Vector3(0, 0, mass * 10), true);
            scene.Step();
        }

        Near(10, body.LinearVelocity.Z, 0.01f);
        scene.Step(10);
        Near(10, body.LinearVelocity.Z, 0.01f);
    }

    [Fact]
    public void TorqueAndAngularImpulseSpinBody()
    {
        using var scene = new TestScene(Vector3.Zero);
        Body body = scene.CreateBall(Vector3.Zero).Body;

        body.ApplyAngularImpulse(new Vector3(0, 0, 1), true);
        Assert.True(body.AngularVelocity.Z > 0);

        body.ApplyTorque(new Vector3(0, 50, 0), true);
        scene.Step();
        Assert.True(body.AngularVelocity.Y > 0);

        Body other = scene.CreateBall(new Vector3(5, 0, 0)).Body;
        other.ApplyLinearImpulse(new Vector3(0, 10, 0), new Vector3(5.5f, 0, 0), true);
        Assert.True(other.AngularVelocity.Z > 0);
        Assert.True(other.LinearVelocity.Y > 0);

        Body pushed = scene.CreateBall(new Vector3(10, 0, 0)).Body;
        pushed.ApplyForce(new Vector3(0, 100, 0), new Vector3(10.5f, 0, 0), true);
        scene.Step();
        Assert.True(pushed.AngularVelocity.Z > 0);
    }

    [Fact]
    public void MassMatchesSphereFormulaAndCanBeOverridden()
    {
        using var scene = new TestScene();
        Shape shape = scene.CreateBall(Vector3.Zero, 0.5f);
        Body body = shape.Body;
        float expected = 1000 * 4.0f / 3.0f * MathF.PI * 0.125f;

        Near(expected, body.Mass, expected * 1e-4f);
        Near(1 / expected, body.InverseMass, 1e-7f);
        Near(expected, new Sphere { radius = 0.5f }.ComputeMass(1000).mass, expected * 1e-4f);
        Near(expected, shape.ComputeMassData().mass, expected * 1e-4f);

        shape.SetDensity(2000, true);
        Near(2000, shape.Density, 1e-3f);
        Near(2 * expected, body.Mass, expected * 1e-3f);

        MassData data = body.MassData;
        data.mass = 2;
        body.MassData = data;
        Near(2, body.Mass, 1e-5f);

        body.ApplyMassFromShapes();
        Near(2 * expected, body.Mass, expected * 1e-3f);
    }

    [Fact]
    public void RestingBodyFallsAsleepAndCanBeWoken()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        Body body = scene.CreateBall(new Vector3(0, 0.5f, 0)).Body;
        Assert.True(body.IsAwake);

        scene.Step(300);
        Assert.False(body.IsAwake);
        Assert.Equal(0, scene.World.AwakeBodyCount);

        body.IsAwake = true;
        Assert.True(body.IsAwake);
        Assert.Equal(1, scene.World.AwakeBodyCount);
    }

    [Fact]
    public void DisabledBodyDoesNotSimulate()
    {
        using var scene = new TestScene();
        Body body = scene.CreateBall(new Vector3(0, 5, 0)).Body;

        body.Disable();
        Assert.False(body.IsEnabled);
        scene.Step(30);
        Near(5, body.Position.Y, 1e-6f);

        body.Enable();
        Assert.True(body.IsEnabled);
        scene.Step(30);
        Assert.True(body.Position.Y < 4);
    }

    [Fact]
    public void MotionLocksPreventLockedTranslation()
    {
        using var scene = new TestScene();
        BodyDef def = BodyDef.Default;
        def.type = BodyType.Dynamic;
        def.position = new Vector3(0, 5, 0);
        def.motionLocks = new MotionLocks { linearY = true };
        Body body = Body.Create(scene.World, def);
        Shape.CreateSphere(body, ShapeDef.Default, new Sphere { radius = 0.5f });

        Assert.True(body.MotionLocks.linearY);
        body.ApplyLinearImpulseToCenter(new Vector3(body.Mass, 0, 0), true);
        scene.Step(60);
        Near(5, body.Position.Y, 1e-4f);
        Assert.True(body.Position.X > 0.5f);

        body.MotionLocks = new MotionLocks();
        Assert.False(body.MotionLocks.linearY);
        scene.Step(30);
        Assert.True(body.Position.Y < 4.5f);
    }

    [Fact]
    public void ZeroGravityScaleFloats()
    {
        using var scene = new TestScene();
        Body body = scene.CreateBall(new Vector3(0, 5, 0)).Body;
        body.GravityScale = 0;
        scene.Step(60);
        Near(5, body.Position.Y, 1e-5f);
    }

    [Fact]
    public void NameAndUserDataRoundTrip()
    {
        using var scene = new TestScene();
        BodyDef def = BodyDef.Default;
        def.name = "hero";
        def.userData = (IntPtr)42;
        Body body = Body.Create(scene.World, def);

        Assert.Equal("hero", body.Name);
        Assert.Equal((IntPtr)42, body.UserData);

        body.Name = "villain";
        body.UserData = (IntPtr)77;
        Assert.Equal("villain", body.Name);
        Assert.Equal((IntPtr)77, body.UserData);
    }

    [Fact]
    public void ShapesEnumerateAndBoundsCoverThem()
    {
        using var scene = new TestScene();
        Shape first = scene.CreateBall(new Vector3(0, 5, 0));
        Body body = first.Body;
        Shape second = Shape.CreateSphere(body, ShapeDef.Default, new Sphere { center = new Vector3(2, 0, 0), radius = 0.5f });

        Assert.Equal(2, body.ShapeCount);
        Span<Shape> shapes = stackalloc Shape[4];
        int count = body.GetShapes(shapes);
        Assert.Equal(2, count);
        Assert.Contains(first, shapes.Slice(0, count).ToArray());
        Assert.Contains(second, shapes.Slice(0, count).ToArray());

        AABB box = body.ComputeAABB();
        Near(new Vector3(-0.5f, 4.5f, -0.5f), box.lowerBound, 0.05f);
        Near(new Vector3(2.5f, 5.5f, 0.5f), box.upperBound, 0.05f);

        Body cube = scene.CreateBody(BodyType.Static, new Vector3(10, 5, 0));
        Shape.CreateHull(cube, ShapeDef.Default, BoxHull.Make(1, 1, 1));
        float distance = cube.GetClosestPoint(out Vector3 closest, new Vector3(10, 10, 0));
        Near(4, distance, 0.01f);
        Near(new Vector3(10, 6, 0), closest, 0.01f);
    }

    [Fact]
    public void DestroyInvalidatesBodyAndItsShapes()
    {
        using var scene = new TestScene();
        Shape shape = scene.CreateBall(Vector3.Zero);
        Body body = shape.Body;
        Assert.True(body.IsValid);
        Assert.True(body.Exists());

        body.Destroy();
        Assert.False(body.Exists());
        Assert.False(shape.Exists());
        Assert.False(Body.Null.IsValid);
    }
}
