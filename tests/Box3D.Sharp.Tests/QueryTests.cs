using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using static Box3D.Tests.TestScene;

namespace Box3D.Tests;

public sealed class QueryTests : IDisposable
{
    private readonly TestScene _scene;
    private readonly Shape[] _balls;
    private readonly Capsule _mover = new Capsule { center1 = new Vector3(0, -0.3f, 0), center2 = new Vector3(0, 0.3f, 0), radius = 0.3f };

    public QueryTests()
    {
        _scene = new TestScene();
        _scene.CreateGround();
        _balls = new Shape[3];
        for (int i = 0; i < _balls.Length; ++i)
        {
            _balls[i] = _scene.CreateBall(new Vector3(3 * i, 1, 0), 0.5f, BodyType.Static);
        }
    }

    public void Dispose()
    {
        _scene.Dispose();
    }

    private World World => _scene.World;

    private struct AllHits : ICastResultHandler
    {
        public List<Shape> Shapes;
        public List<Vector3> Points;

        public float OnCastResult(Shape shapeId, Vector3 point, Vector3 normal, float fraction, ulong userMaterialId, int triangleIndex, int childIndex)
        {
            Shapes.Add(shapeId);
            Points.Add(point);
            return 1;
        }
    }

    private struct ClosestHit : ICastResultHandler
    {
        public int Calls;
        public Shape Shape;
        public float Fraction;
        public Vector3 Normal;

        public float OnCastResult(Shape shapeId, Vector3 point, Vector3 normal, float fraction, ulong userMaterialId, int triangleIndex, int childIndex)
        {
            Calls++;
            Shape = shapeId;
            Fraction = fraction;
            Normal = normal;
            return fraction;
        }
    }

    private struct Throwing : ICastResultHandler
    {
        public int Calls;

        public float OnCastResult(Shape shapeId, Vector3 point, Vector3 normal, float fraction, ulong userMaterialId, int triangleIndex, int childIndex)
        {
            Calls++;
            throw new InvalidOperationException("from handler");
        }
    }

    private struct Overlaps : IOverlapResultHandler
    {
        public List<Shape> Shapes;

        public bool OnOverlapResult(Shape shapeId)
        {
            Shapes.Add(shapeId);
            return true;
        }
    }

    private struct ThrowingOverlap : IOverlapResultHandler
    {
        public bool OnOverlapResult(Shape shapeId)
        {
            throw new ArgumentException("overlap");
        }
    }

    private struct MoverFilter : IMoverFilterHandler
    {
        public bool Accept;
        public int Calls;

        public bool OnMoverFilter(Shape shapeId)
        {
            Calls++;
            return Accept;
        }
    }

    private struct Planes : IPlaneResultHandler
    {
        public List<Shape> Shapes;
        public List<PlaneResult> Results;

        public bool OnPlaneResult(Shape shapeId, ReadOnlySpan<PlaneResult> planes)
        {
            Shapes.Add(shapeId);
            Results.AddRange(planes.ToArray());
            return true;
        }
    }

    [Fact]
    public void CastRayVisitsEveryShapeAlongTheRay()
    {
        var hits = new AllHits { Shapes = new List<Shape>(), Points = new List<Vector3>() };
        World.CastRay(new Vector3(-5, 1, 0), new Vector3(20, 0, 0), QueryFilter.Default, ref hits);

        Assert.Equal(3, hits.Shapes.Count);
        foreach (Shape ball in _balls)
        {
            Assert.Contains(ball, hits.Shapes);
        }

        Near(new Vector3(-0.5f, 1, 0), hits.Points[hits.Shapes.IndexOf(_balls[0])], 1e-3f);
    }

    [Fact]
    public void CastRayHandlerStateIsVisibleAfterCall()
    {
        var closest = new ClosestHit { Fraction = -1 };
        World.CastRay(new Vector3(-5, 1, 0), new Vector3(20, 0, 0), QueryFilter.Default, ref closest);

        Assert.True(closest.Calls >= 1);
        Assert.Equal(_balls[0], closest.Shape);
        Near(4.5f / 20, closest.Fraction, 1e-4f);
        Near(new Vector3(-1, 0, 0), closest.Normal, 1e-3f);
    }

    [Fact]
    public void CastRayRespectsQueryFilter()
    {
        ShapeDef def = ShapeDef.Default;
        def.filter = new Filter { categoryBits = 4, maskBits = ulong.MaxValue };
        Shape tagged = _scene.CreateBall(new Vector3(9, 1, 0), def, 0.5f, BodyType.Static);

        var all = new AllHits { Shapes = new List<Shape>(), Points = new List<Vector3>() };
        World.CastRay(new Vector3(-5, 1, 0), new Vector3(20, 0, 0), QueryFilter.Default, ref all);
        Assert.Equal(4, all.Shapes.Count);
        Assert.Contains(tagged, all.Shapes);

        QueryFilter filter = QueryFilter.Default;
        filter.maskBits = ~4UL;
        var hits = new AllHits { Shapes = new List<Shape>(), Points = new List<Vector3>() };
        World.CastRay(new Vector3(-5, 1, 0), new Vector3(20, 0, 0), filter, ref hits);

        Assert.Equal(3, hits.Shapes.Count);
        Assert.DoesNotContain(tagged, hits.Shapes);
    }

    [Fact]
    public void CastRayClosestReturnsNearestHit()
    {
        RayResult result = World.CastRayClosest(new Vector3(10, 1, 0), new Vector3(-20, 0, 0), QueryFilter.Default);
        Assert.True(result.hit);
        Assert.Equal(_balls[2], result.shapeId);
        Near(new Vector3(6.5f, 1, 0), result.point, 1e-3f);

        RayResult miss = World.CastRayClosest(new Vector3(0, 5, 0), new Vector3(0, 10, 0), QueryFilter.Default);
        Assert.False(miss.hit);
    }

    [Fact]
    public void ExceptionInQueryHandlerIsRethrownAfterCall()
    {
        var throwing = new Throwing();
        var error = Assert.Throws<InvalidOperationException>(() => World.CastRay(new Vector3(-5, 1, 0), new Vector3(20, 0, 0), QueryFilter.Default, ref throwing));
        Assert.Equal("from handler", error.Message);
        Assert.Equal(1, throwing.Calls);

        var overlap = new ThrowingOverlap();
        Assert.Throws<ArgumentException>(() => World.OverlapAABB(new AABB { lowerBound = new Vector3(-1, 0, -1), upperBound = new Vector3(1, 2, 1) }, QueryFilter.Default, ref overlap));

        var hits = new AllHits { Shapes = new List<Shape>(), Points = new List<Vector3>() };
        World.CastRay(new Vector3(-5, 1, 0), new Vector3(20, 0, 0), QueryFilter.Default, ref hits);
        Assert.Equal(3, hits.Shapes.Count);
    }

    [Fact]
    public void OverlapAABBFindsShapesInBox()
    {
        var overlaps = new Overlaps { Shapes = new List<Shape>() };
        World.OverlapAABB(new AABB { lowerBound = new Vector3(2.5f, 0.6f, -0.2f), upperBound = new Vector3(3.5f, 1.4f, 0.2f) }, QueryFilter.Default, ref overlaps);

        Assert.Single(overlaps.Shapes);
        Assert.Equal(_balls[1], overlaps.Shapes[0]);
    }

    [Fact]
    public void OverlapShapeFindsTouchingShapes()
    {
        var proxy = new ShapeProxy { points = new[] { Vector3.Zero }, radius = 0.6f };
        var overlaps = new Overlaps { Shapes = new List<Shape>() };
        World.OverlapShape(new Vector3(6, 2, 0), proxy, QueryFilter.Default, ref overlaps);

        Assert.Single(overlaps.Shapes);
        Assert.Equal(_balls[2], overlaps.Shapes[0]);

        var none = new Overlaps { Shapes = new List<Shape>() };
        World.OverlapShape(new Vector3(1.5f, 3, 0), proxy, QueryFilter.Default, ref none);
        Assert.Empty(none.Shapes);
    }

    [Fact]
    public void CastShapeHitsFirstShape()
    {
        var proxy = new ShapeProxy { points = new[] { Vector3.Zero }, radius = 0.25f };
        var closest = new ClosestHit();
        World.CastShape(new Vector3(-5, 1, 0), proxy, new Vector3(20, 0, 0), QueryFilter.Default, ref closest);

        Assert.True(closest.Calls >= 1);
        Assert.Equal(_balls[0], closest.Shape);
        Near(4.25f / 20, closest.Fraction, 2e-3f);
    }

    [Fact]
    public void CastMoverStopsAtShapesUnlessFiltered()
    {
        var accept = new MoverFilter { Accept = true };
        float fraction = World.CastMover(new Vector3(-5, 1, 0), _mover, new Vector3(10, 0, 0), QueryFilter.Default, ref accept);
        Assert.True(accept.Calls > 0);
        Assert.InRange(fraction, 0.35f, 0.43f);

        var reject = new MoverFilter { Accept = false };
        float unblocked = World.CastMover(new Vector3(-5, 1, 0), _mover, new Vector3(10, 0, 0), QueryFilter.Default, ref reject);
        Assert.True(reject.Calls > 0);
        Near(1, unblocked, 1e-6f);
    }

    [Fact]
    public void CollideMoverGathersPlanes()
    {
        var planes = new Planes { Shapes = new List<Shape>(), Results = new List<PlaneResult>() };
        World.CollideMover(new Vector3(0.6f, 1, 0), _mover, QueryFilter.Default, ref planes);

        Assert.Contains(_balls[0], planes.Shapes);
        Assert.NotEmpty(planes.Results);
        Assert.True(planes.Results[0].plane.normal.X > 0.5f);

        CollisionPlane[] collision = new CollisionPlane[planes.Results.Count];
        for (int i = 0; i < collision.Length; ++i)
        {
            collision[i] = new CollisionPlane { plane = planes.Results[i].plane, pushLimit = float.MaxValue, clipVelocity = true };
        }

        PlaneSolverResult solved = B3.SolvePlanes(Vector3.Zero, collision);
        Assert.True(solved.delta.X > 0);
    }

    [Fact]
    public void BodyLevelQueries()
    {
        Body body = _balls[0].Body;
        Transform transform = body.Transform;

        BodyCastResult ray = body.CastRay(new Vector3(-5, 1, 0), new Vector3(10, 0, 0), QueryFilter.Default, 1, transform);
        Assert.True(ray.hit);
        Assert.Equal(_balls[0], ray.shapeId);
        Near(-0.5f, ray.point.X, 1e-3f);

        BodyCastResult moved = body.CastRay(new Vector3(-5, 1, 0), new Vector3(10, 0, 0), QueryFilter.Default, 1, At(0, 10, 0));
        Assert.False(moved.hit);

        var proxy = new ShapeProxy { points = new[] { Vector3.Zero }, radius = 0.25f };
        BodyCastResult cast = body.CastShape(new Vector3(-5, 1, 0), proxy, new Vector3(10, 0, 0), QueryFilter.Default, 1, false, transform);
        Assert.True(cast.hit);
        Near(4.25f / 10, cast.fraction, 2e-3f);

        Assert.True(body.OverlapShape(new Vector3(0.3f, 1, 0), proxy, QueryFilter.Default, transform));
        Assert.False(body.OverlapShape(new Vector3(0, 3, 0), proxy, QueryFilter.Default, transform));

        Span<BodyPlaneResult> planes = new BodyPlaneResult[4];
        int count = body.CollideMover(planes, new Vector3(0.6f, 1, 0), _mover, QueryFilter.Default, transform);
        Assert.True(count >= 1);
        Assert.Equal(_balls[0], planes[0].shapeId);

        BodyTOIResult toi = body.TimeOfImpactMover(new Vector3(-5, 1, 0), _mover, new Vector3(10, 0, 0), QueryFilter.Default, transform, transform);
        Assert.Equal(_balls[0], toi.shapeId);
        Assert.InRange(toi.fraction, 0.35f, 0.43f);
    }

    [Fact]
    public void ShapeLevelRayCast()
    {
        CastOutput hit = _balls[1].RayCast(new Vector3(3, 5, 0), new Vector3(0, -10, 0));
        Assert.True(hit.hit);
        Near(new Vector3(3, 1.5f, 0), hit.point, 1e-3f);
        Near(0.35f, hit.fraction, 1e-4f);

        CastOutput miss = _balls[1].RayCast(new Vector3(0, 5, 0), new Vector3(0, -10, 0));
        Assert.False(miss.hit);

        Near(new Vector3(3, 1.5f, 0), _balls[1].GetClosestPoint(new Vector3(3, 4, 0)), 1e-3f);
    }
}
