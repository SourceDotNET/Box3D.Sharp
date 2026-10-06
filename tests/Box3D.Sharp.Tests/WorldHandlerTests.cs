using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Xunit;
using static Box3D.Tests.TestScene;

namespace Box3D.Tests;

public sealed class WorldHandlerTests
{
    private sealed class FixedFilter : ICustomFilterHandler
    {
        public bool Result;
        public int Calls;

        public bool OnCustomFilter(Shape shapeIdA, Shape shapeIdB)
        {
            Interlocked.Increment(ref Calls);
            return Result;
        }
    }

    private sealed class PreSolve : IPreSolveHandler
    {
        public bool Result;
        public int Calls;
        public Shape Target;
        public bool SawTarget;
        public Vector3 Normal;

        public bool OnPreSolve(Shape shapeIdA, Shape shapeIdB, Vector3 point, Vector3 normal)
        {
            Interlocked.Increment(ref Calls);
            if (shapeIdA == Target || shapeIdB == Target)
            {
                SawTarget = true;
                Normal = normal;
            }

            return Result;
        }
    }

    private struct Drawer : IDebugDrawHandler
    {
        public int Bounds;
        public int Transforms;
        public int Strings;
        public List<IntPtr> UserShapes;
        public bool Throw;

        public void OnDrawShape(IntPtr userShape, Transform transform, HexColor color)
        {
            UserShapes?.Add(userShape);
        }

        public void OnDrawSegment(Vector3 p1, Vector3 p2, HexColor color)
        {
        }

        public void OnDrawTransform(Transform transform)
        {
            Transforms++;
        }

        public void OnDrawPoint(Vector3 p, float size, HexColor color)
        {
        }

        public void OnDrawSphere(Vector3 p, float radius, HexColor color, float alpha)
        {
        }

        public void OnDrawCapsule(Vector3 p1, Vector3 p2, float radius, HexColor color, float alpha)
        {
        }

        public void OnDrawBounds(AABB aabb, HexColor color)
        {
            Bounds++;
            if (Throw)
            {
                throw new InvalidOperationException("draw");
            }
        }

        public void OnDrawBox(Vector3 extents, Transform transform, HexColor color)
        {
        }

        public void OnDrawString(Vector3 p, string s, HexColor color)
        {
            Strings++;
        }
    }

    private sealed class DebugShapes : IDebugShapeHandler
    {
        public readonly Dictionary<Shape, IntPtr> Created = new Dictionary<Shape, IntPtr>();
        public readonly Dictionary<Shape, ShapeType> Types = new Dictionary<Shape, ShapeType>();
        public readonly List<IntPtr> Destroyed = new List<IntPtr>();
        public float SphereRadius;
        private int _next = 1000;

        public IntPtr OnCreateDebugShape(in DebugShape debugShape)
        {
            var handle = (IntPtr)_next++;
            Created.Add(debugShape.shapeId, handle);
            Types.Add(debugShape.shapeId, debugShape.type);
            if (debugShape.type == ShapeType.Sphere)
            {
                SphereRadius = debugShape.sphere.radius;
            }

            return handle;
        }

        public void OnDestroyDebugShape(IntPtr userShape)
        {
            Destroyed.Add(userShape);
        }
    }

    [Fact]
    public void RejectingCustomFilterLetsBodyFallThroughGround()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        ShapeDef def = ShapeDef.Default;
        def.enableCustomFiltering = true;
        Body body = scene.CreateBall(new Vector3(0, 1, 0), def).Body;

        var filter = new FixedFilter { Result = false };
        scene.World.SetCustomFilterHandler(filter);
        scene.Step(120);

        Assert.True(filter.Calls > 0);
        Assert.True(body.Position.Y < -2);
    }

    [Fact]
    public void AcceptingCustomFilterKeepsCollisionAndNullClears()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        ShapeDef def = ShapeDef.Default;
        def.enableCustomFiltering = true;
        Body body = scene.CreateBall(new Vector3(0, 1, 0), def).Body;

        var filter = new FixedFilter { Result = true };
        scene.World.SetCustomFilterHandler(filter);
        scene.Step(120);
        Assert.True(filter.Calls > 0);
        Near(0.5f, body.Position.Y, 0.05f);

        scene.World.SetCustomFilterHandler(null);
        int calls = filter.Calls;
        Body second = scene.CreateBall(new Vector3(5, 1, 0), def).Body;
        scene.Step(120);
        Assert.Equal(calls, filter.Calls);
        Near(0.5f, second.Position.Y, 0.05f);
    }

    [Fact]
    public void PreSolveHandlerIsInvokedForTouchingContacts()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        ShapeDef def = ShapeDef.Default;
        def.enablePreSolveEvents = true;
        Shape ball = scene.CreateBall(new Vector3(0, 2, 0), def);
        Assert.True(ball.ArePreSolveEventsEnabled);

        var handler = new PreSolve { Result = true, Target = ball };
        scene.World.SetPreSolveHandler(handler);
        scene.Step(120);

        Assert.True(handler.Calls > 0);
        Assert.True(handler.SawTarget);
        Near(1, MathF.Abs(handler.Normal.Y), 1e-3f);
        Near(0.5f, ball.Body.Position.Y, 0.05f);

        scene.World.SetPreSolveHandler(null);
        int calls = handler.Calls;
        scene.Step(10);
        Assert.Equal(calls, handler.Calls);
    }

    [Fact]
    public void PreSolveReturningFalseDisablesContact()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        ShapeDef def = ShapeDef.Default;
        def.enablePreSolveEvents = true;
        Shape ball = scene.CreateBall(new Vector3(0, 2, 0), def);

        var handler = new PreSolve { Result = false, Target = ball };
        scene.World.SetPreSolveHandler(handler);
        scene.Step(180);

        Assert.True(handler.Calls > 0);
        Assert.True(ball.Body.Position.Y < -2);
    }

    [Fact]
    public void DebugDrawHandlerReceivesCalls()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        BodyDef def = BodyDef.Default;
        def.type = BodyType.Dynamic;
        def.position = new Vector3(0, 2, 0);
        def.name = "ball";
        Body body = Body.Create(scene.World, def);
        Shape.CreateSphere(body, ShapeDef.Default, new Sphere { radius = 0.5f });
        scene.Step();

        DebugDraw options = DebugDraw.Default;
        options.drawBounds = true;
        options.drawMass = true;
        options.drawBodyNames = true;
        var drawer = new Drawer();
        scene.World.Draw(options, ref drawer, ulong.MaxValue);

        Assert.Equal(2, drawer.Bounds);
        Assert.True(drawer.Transforms >= 2);
        Assert.True(drawer.Strings >= 1);
    }

    [Fact]
    public void ExceptionInDebugDrawIsRethrown()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        DebugDraw options = DebugDraw.Default;
        options.drawBounds = true;
        var drawer = new Drawer { Throw = true };

        World world = scene.World;
        Assert.Throws<InvalidOperationException>(() => world.Draw(options, ref drawer, ulong.MaxValue));
        Assert.Equal(1, drawer.Bounds);
    }

    [Fact]
    public void DebugShapeHandlerCreatesAndDestroysUserShapes()
    {
        var handler = new DebugShapes();
        var scene = new TestScene(null, handler);
        Shape ground;
        Shape ball;
        try
        {
            ground = scene.CreateGround();
            ball = scene.CreateBall(new Vector3(0, 2, 0));
            Assert.Empty(handler.Created);

            DebugDraw options = DebugDraw.Default;
            options.drawShapes = true;
            var drawer = new Drawer { UserShapes = new List<IntPtr>() };
            scene.World.Draw(options, ref drawer, ulong.MaxValue);

            Assert.Equal(2, handler.Created.Count);
            Assert.Equal(ShapeType.Hull, handler.Types[ground]);
            Assert.Equal(ShapeType.Sphere, handler.Types[ball]);
            Near(0.5f, handler.SphereRadius, 1e-6f);
            Assert.Contains(handler.Created[ground], drawer.UserShapes);
            Assert.Contains(handler.Created[ball], drawer.UserShapes);

            scene.World.Draw(options, ref drawer, ulong.MaxValue);
            Assert.Equal(2, handler.Created.Count);
            Assert.Equal(4, drawer.UserShapes.Count);

            ball.Destroy(true);
            Assert.Equal(new[] { handler.Created[ball] }, handler.Destroyed);
        }
        finally
        {
            scene.Dispose();
        }

        Assert.Contains(handler.Created[ground], handler.Destroyed);
        Assert.Equal(2, handler.Destroyed.Count);
    }
}
