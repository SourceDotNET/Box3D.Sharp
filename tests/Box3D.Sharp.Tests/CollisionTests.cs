using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using static Box3D.Tests.TestScene;

namespace Box3D.Tests;

public sealed class CollisionTests
{
    private struct TriangleCounter : IMeshQueryHandler
    {
        public List<int> Triangles;

        public bool OnMeshQuery(Vector3 a, Vector3 b, Vector3 c, int triangleIndex)
        {
            Triangles.Add(triangleIndex);
            return true;
        }
    }

    private static ShapeProxy Point(Vector3 p, float radius)
    {
        return new ShapeProxy { points = new[] { p }, radius = radius };
    }

    private static Span<LocalManifoldPoint> Points()
    {
        return new LocalManifoldPoint[B3.MaxManifoldPoints];
    }

    [Fact]
    public void CollideSpheres()
    {
        Span<LocalManifoldPoint> points = Points();
        var sphere = new Sphere { radius = 1 };
        LocalManifold manifold = B3.CollideSpheres(points, sphere, sphere, At(1.5f, 0, 0));
        Assert.Equal(1, manifold.pointCount);
        Near(new Vector3(1, 0, 0), manifold.normal, 1e-4f);
        Near(-0.5f, points[0].separation, 1e-4f);

        LocalManifold apart = B3.CollideSpheres(points, sphere, sphere, At(5, 0, 0));
        Assert.Equal(0, apart.pointCount);
    }

    [Fact]
    public void CollideCapsulesAndCapsuleSphere()
    {
        Span<LocalManifoldPoint> points = Points();
        var capsule = new Capsule { center1 = new Vector3(-1, 0, 0), center2 = new Vector3(1, 0, 0), radius = 0.5f };

        LocalManifold capsules = B3.CollideCapsules(points, capsule, capsule, At(0, 0.9f, 0));
        Assert.True(capsules.pointCount >= 1);
        Near(new Vector3(0, 1, 0), capsules.normal, 1e-4f);
        Near(-0.1f, points[0].separation, 1e-3f);

        LocalManifold sphere = B3.CollideCapsuleAndSphere(points, capsule, new Sphere { radius = 0.5f }, At(0, 0.9f, 0));
        Assert.Equal(1, sphere.pointCount);
        Near(-0.1f, points[0].separation, 1e-3f);
    }

    [Fact]
    public void CollideHullsWithSpheresCapsulesAndHulls()
    {
        Span<LocalManifoldPoint> points = Points();
        BoxHull box = BoxHull.Make(1, 1, 1);
        BoxHull small = BoxHull.Make(0.5f, 0.5f, 0.5f);

        var simplex = new SimplexCache();
        LocalManifold sphere = B3.CollideHullAndSphere(points, box, new Sphere { radius = 0.5f }, At(0, 1.4f, 0), ref simplex);
        Assert.Equal(1, sphere.pointCount);
        Near(new Vector3(0, 1, 0), sphere.normal, 1e-3f);
        Near(-0.1f, points[0].separation, 1e-3f);

        simplex = new SimplexCache();
        var capsule = new Capsule { center1 = new Vector3(-0.5f, 0, 0), center2 = new Vector3(0.5f, 0, 0), radius = 0.5f };
        LocalManifold capsuleManifold = B3.CollideHullAndCapsule(points, box, capsule, At(0, 1.4f, 0), ref simplex);
        Assert.True(capsuleManifold.pointCount >= 1);
        Near(new Vector3(0, 1, 0), capsuleManifold.normal, 1e-3f);

        var sat = new SATCache();
        LocalManifold hulls = B3.CollideHulls(points, box, small, At(0, 1.4f, 0), ref sat);
        Assert.Equal(4, hulls.pointCount);
        Near(new Vector3(0, 1, 0), hulls.normal, 1e-3f);
        for (int i = 0; i < hulls.pointCount; ++i)
        {
            Near(-0.1f, points[i].separation, 1e-3f);
        }

        HullData cube = HullData.Create(CubePoints(0.5f), 8);
        try
        {
            sat = new SATCache();
            LocalManifold mixed = B3.CollideHulls(points, box, cube, At(0, 1.4f, 0), ref sat);
            Assert.Equal(4, mixed.pointCount);

            sat = new SATCache();
            LocalManifold apart = B3.CollideHulls(points, box, cube, At(0, 5, 0), ref sat);
            Assert.Equal(0, apart.pointCount);
        }
        finally
        {
            cube.Destroy();
        }
    }

    [Fact]
    public void CollideTriangles()
    {
        Span<LocalManifoldPoint> points = Points();
        Vector3[] triangle = { new Vector3(-2, 0, -2), new Vector3(-2, 0, 2), new Vector3(2, 0, 0) };

        LocalManifold sphere = B3.CollideTriangleAndSphere(points, triangle[0], triangle[1], triangle[2], new Sphere { center = new Vector3(0, 0.5f, 0), radius = 0.6f });
        Assert.Equal(1, sphere.pointCount);
        Near(-0.1f, points[0].separation, 1e-3f);

        var simplex = new SimplexCache();
        var capsule = new Capsule { center1 = new Vector3(-0.5f, 0.5f, 0), center2 = new Vector3(0.5f, 0.5f, 0), radius = 0.6f };
        LocalManifold capsuleManifold = B3.CollideTriangleAndCapsule(points, triangle[0], triangle[1], triangle[2], capsule, ref simplex);
        Assert.True(capsuleManifold.pointCount >= 1);
        Near(-0.1f, points[0].separation, 1e-3f);

        var sat = new SATCache();
        BoxHull box = BoxHull.MakeOffset(0.25f, 0.25f, 0.25f, new Vector3(0, 0.2f, 0));
        LocalManifold hull = B3.CollideTriangleAndHull(points, triangle[0], triangle[1], triangle[2], 0, box, ref sat, true);
        Assert.True(hull.pointCount >= 1);
        Near(1, MathF.Abs(hull.normal.Y), 1e-3f);
        Near(-0.05f, points[0].separation, 1e-3f);
    }

    [Fact]
    public void CollideWritesPointsIntoTheCallerBuffer()
    {
        Span<LocalManifoldPoint> points = Points();
        var sphere = new Sphere { radius = 1 };
        LocalManifold manifold = B3.CollideSpheres(points, sphere, sphere, At(1.5f, 0, 0));
        Assert.Equal(1, manifold.pointCount);
        Assert.True(manifold.points.IsEmpty);
        Near(-0.5f, points[0].separation, 1e-4f);
    }

    [Fact]
    public void ShapeDistanceMeasuresGap()
    {
        var input = new DistanceInput
        {
            proxyA = Point(Vector3.Zero, 0.5f),
            proxyB = Point(Vector3.Zero, 0.5f),
            transform = At(3, 0, 0),
            useRadii = true,
        };
        var cache = new SimplexCache();
        DistanceOutput output = B3.ShapeDistance(input, ref cache, Span<Simplex>.Empty);
        Near(2, output.distance, 1e-4f);
        Near(new Vector3(0.5f, 0, 0), output.pointA, 1e-4f);
        Near(new Vector3(2.5f, 0, 0), output.pointB, 1e-4f);

        input.useRadii = false;
        cache = new SimplexCache();
        Near(3, B3.ShapeDistance(input, ref cache, Span<Simplex>.Empty).distance, 1e-4f);

        Span<Simplex> simplexes = new Simplex[8];
        cache = new SimplexCache();
        DistanceOutput traced = B3.ShapeDistance(input, ref cache, simplexes);
        Assert.True(traced.simplexCount >= 1);
        Simplex simplex = simplexes[0];
        Assert.Equal(simplex.count, simplex.vertices.Length);
        Assert.Equal(1, cache.count);
        Assert.Equal(1, cache.indexA.Length);
        Assert.Equal(1, cache.indexB.Length);
        Assert.Equal(0, cache.indexA[0]);
        Assert.Equal(0, cache.indexB[0]);
    }

    [Fact]
    public void ShapeCastPairFindsImpactFraction()
    {
        var input = new ShapeCastPairInput
        {
            proxyA = Point(Vector3.Zero, 0.5f),
            proxyB = Point(Vector3.Zero, 0.5f),
            transform = At(5, 0, 0),
            translationB = new Vector3(-10, 0, 0),
            maxFraction = 1,
        };
        CastOutput output = B3.ShapeCast(input);
        Assert.True(output.hit);
        Near(0.4f, output.fraction, 2e-3f);

        input.translationB = new Vector3(0, 10, 0);
        Assert.False(B3.ShapeCast(input).hit);
    }

    [Fact]
    public void TimeOfImpactAndSweep()
    {
        var still = new Sweep { q1 = Quaternion.Identity, q2 = Quaternion.Identity };
        var moving = new Sweep { c1 = new Vector3(5, 0, 0), c2 = new Vector3(-5, 0, 0), q1 = Quaternion.Identity, q2 = Quaternion.Identity };

        Transform middle = moving.GetTransform(0.5f);
        Near(Vector3.Zero, middle.p, 1e-5f);

        var input = new TOIInput
        {
            proxyA = Point(Vector3.Zero, 0.5f),
            proxyB = Point(Vector3.Zero, 0.5f),
            sweepA = still,
            sweepB = moving,
            maxFraction = 1,
        };
        TOIOutput output = B3.TimeOfImpact(input);
        Assert.Equal(TOIState.Hit, output.state);
        Assert.InRange(output.fraction, 0.35f, 0.41f);

        input.sweepB.c2 = new Vector3(5, 5, 0);
        Assert.Equal(TOIState.Separated, B3.TimeOfImpact(input).state);
    }

    [Fact]
    public void MassAndBoundsComputations()
    {
        var sphere = new Sphere { radius = 1 };
        Near(4.0f / 3.0f * MathF.PI, sphere.ComputeMass(1).mass, 1e-3f);
        AABB sphereBox = sphere.ComputeAABB(At(1, 2, 3));
        Near(new Vector3(0, 1, 2), sphereBox.lowerBound, 1e-5f);
        Near(new Vector3(2, 3, 4), sphereBox.upperBound, 1e-5f);
        Assert.True(sphereBox.IsValid);

        var capsule = new Capsule { center1 = new Vector3(-1, 0, 0), center2 = new Vector3(1, 0, 0), radius = 0.5f };
        Near(MathF.PI * 0.5f + 4.0f / 3.0f * MathF.PI * 0.125f, capsule.ComputeMass(1).mass, 1e-3f);
        AABB capsuleBox = capsule.ComputeAABB(At(0, 0, 0));
        Near(new Vector3(-1.5f, -0.5f, -0.5f), capsuleBox.lowerBound, 1e-5f);

        HullData hull = HullData.Create(CubePoints(1), 8);
        try
        {
            MassData hullMass = hull.ComputeMass(1);
            Near(8, hullMass.mass, 1e-3f);
            Near(Vector3.Zero, hullMass.center, 1e-4f);
            Near(8.0f * 2 / 3, hullMass.inertia.cx.X, 1e-2f);
            AABB hullBox = hull.ComputeAABB(At(10, 0, 0));
            Near(new Vector3(9, -1, -1), hullBox.lowerBound, 1e-4f);

            HullData clone = hull.CloneAndTransform(At(0, 5, 0), new Vector3(2, 2, 2));
            try
            {
                Near(new Vector3(0, 5, 0), clone.Center, 1e-3f);
                Near(64, clone.Volume, 1e-2f);
            }
            finally
            {
                clone.Destroy();
            }
        }
        finally
        {
            hull.Destroy();
        }
    }

    [Fact]
    public void RayCastsOnIndividualShapes()
    {
        var input = new RayCastInput { origin = new Vector3(-5, 0, 0), translation = new Vector3(10, 0, 0), maxFraction = 1 };
        Assert.True(input.IsValidRay);

        CastOutput sphere = new Sphere { radius = 1 }.RayCast(input);
        Assert.True(sphere.hit);
        Near(0.4f, sphere.fraction, 1e-4f);
        Near(new Vector3(-1, 0, 0), sphere.normal, 1e-4f);

        var inside = new RayCastInput { origin = new Vector3(0.5f, 0, 0), translation = new Vector3(1, 0, 0), maxFraction = 1 };
        CastOutput hollow = new Sphere { radius = 1 }.RayCastHollow(inside);
        Assert.True(hollow.hit);
        Near(0.5f, hollow.fraction, 1e-4f);
        Near(new Vector3(1, 0, 0), hollow.point, 1e-4f);

        var capsule = new Capsule { center1 = new Vector3(0, -1, 0), center2 = new Vector3(0, 1, 0), radius = 0.5f };
        Near(0.45f, capsule.RayCast(input).fraction, 1e-4f);

        HullData hull = HullData.Create(CubePoints(1), 8);
        try
        {
            CastOutput hullHit = hull.RayCast(input);
            Assert.True(hullHit.hit);
            Near(0.4f, hullHit.fraction, 1e-4f);
        }
        finally
        {
            hull.Destroy();
        }

        var down = new RayCastInput { origin = new Vector3(0.3f, 5, 0.3f), translation = new Vector3(0, -10, 0), maxFraction = 1 };
        MeshData meshData = MeshData.CreateGrid(4, 4, 1, 0, false);
        try
        {
            var mesh = new Mesh { data = meshData, scale = Vector3.One };
            CastOutput meshHit = mesh.RayCast(down);
            Assert.True(meshHit.hit);
            Near(0.5f, meshHit.fraction, 1e-4f);
            Assert.True(meshHit.triangleIndex >= 0);
        }
        finally
        {
            meshData.Destroy();
        }

        HeightFieldData field = HeightFieldData.CreateGrid(5, 5, Vector3.One, false);
        try
        {
            var fieldDown = new RayCastInput { origin = new Vector3(1.3f, 5, 1.3f), translation = new Vector3(0, -10, 0), maxFraction = 1 };
            CastOutput fieldHit = field.RayCast(fieldDown);
            Assert.True(fieldHit.hit);
            Near(0.5f, fieldHit.fraction, 1e-3f);
        }
        finally
        {
            field.Destroy();
        }
    }

    [Fact]
    public void ShapeOverlapsAndShapeCasts()
    {
        var sphere = new Sphere { radius = 1 };
        Assert.True(sphere.Overlap(At(0, 0, 0), Point(new Vector3(1.2f, 0, 0), 0.3f)));
        Assert.False(sphere.Overlap(At(0, 0, 0), Point(new Vector3(3, 0, 0), 0.3f)));
        Assert.True(sphere.Overlap(At(3, 0, 0), Point(new Vector3(3, 0, 0), 0.3f)));

        var cast = new ShapeCastInput { proxy = Point(new Vector3(-5, 0, 0), 0.5f), translation = new Vector3(10, 0, 0), maxFraction = 1 };
        CastOutput sphereCast = sphere.ShapeCast(cast);
        Assert.True(sphereCast.hit);
        Near(0.35f, sphereCast.fraction, 2e-3f);

        HullData hull = HullData.Create(CubePoints(1), 8);
        try
        {
            Assert.True(hull.Overlap(At(0, 0, 0), Point(new Vector3(1.2f, 0, 0), 0.3f)));
            Near(0.35f, hull.ShapeCast(cast).fraction, 2e-3f);
        }
        finally
        {
            hull.Destroy();
        }
    }

    [Fact]
    public void MeshAndHeightFieldQueriesReportTriangles()
    {
        MeshData meshData = MeshData.CreateGrid(4, 4, 1, 0, false);
        try
        {
            var mesh = new Mesh { data = meshData, scale = Vector3.One };
            var counter = new TriangleCounter { Triangles = new List<int>() };
            mesh.Query(new AABB { lowerBound = new Vector3(0.2f, -0.1f, 0.2f), upperBound = new Vector3(0.8f, 0.1f, 0.8f) }, ref counter);
            Assert.InRange(counter.Triangles.Count, 1, 2);
        }
        finally
        {
            meshData.Destroy();
        }

        HeightFieldData field = HeightFieldData.CreateGrid(5, 5, Vector3.One, false);
        try
        {
            var fieldCounter = new TriangleCounter { Triangles = new List<int>() };
            field.Query(new AABB { lowerBound = new Vector3(1.2f, -0.1f, 1.2f), upperBound = new Vector3(1.8f, 0.1f, 1.8f) }, ref fieldCounter);
            Assert.InRange(fieldCounter.Triangles.Count, 1, 2);
        }
        finally
        {
            field.Destroy();
        }
    }

    [Fact]
    public void PlaneSolverAndClipping()
    {
        CollisionPlane[] planes =
        {
            new CollisionPlane { plane = new Plane { normal = Vector3.UnitY, offset = 0 }, pushLimit = float.MaxValue, clipVelocity = true },
        };
        Assert.True(planes[0].plane.IsValid);

        PlaneSolverResult result = B3.SolvePlanes(new Vector3(1, -1, 0), planes);
        Near(1, result.delta.X, 1e-4f);
        Assert.InRange(result.delta.Y, -0.01f, 0.0001f);
        Assert.True(planes[0].push > 0.9f);

        Near(new Vector3(1, 0, 0), B3.ClipVector(new Vector3(1, -1, 0), planes), 1e-5f);
        Near(new Vector3(1, 1, 0), B3.ClipVector(new Vector3(1, 1, 0), planes), 1e-5f);
    }

    [Fact]
    public void MathExports()
    {
        Near(MathF.Atan2(1, 2), B3.Atan2(1, 2), 1e-3f);
        CosSin cs = B3.ComputeCosSin(0.5f);
        Near(MathF.Cos(0.5f), cs.cosine, 1e-3f);
        Near(MathF.Sin(0.5f), cs.sine, 1e-3f);

        Quaternion q = B3.ComputeQuatBetweenUnitVectors(Vector3.UnitX, Vector3.UnitY);
        Near(Vector3.UnitY, Vector3.Transform(Vector3.UnitX, q), 1e-4f);

        Matrix3 steiner = B3.Steiner(2, new Vector3(1, 2, 3));
        Near(2 * (4 + 9), steiner.cx.X, 1e-4f);
        Near(-2 * 1 * 2, steiner.cx.Y, 1e-4f);
        Assert.True(steiner.IsValid);

        Near(new Vector3(1, 0, 0), B3.PointToSegmentDistance(Vector3.Zero, new Vector3(2, 0, 0), new Vector3(1, 3, 0)), 1e-5f);

        SegmentDistanceResult segments = B3.SegmentDistance(new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, -1), new Vector3(0, 1, 1));
        Near(Vector3.Zero, segments.point1, 1e-5f);
        Near(new Vector3(0, 1, 0), segments.point2, 1e-5f);
        Near(0.5f, segments.fraction1, 1e-5f);

        SegmentDistanceResult lines = B3.LineDistance(Vector3.Zero, Vector3.UnitX, new Vector3(5, 2, 0), Vector3.UnitZ);
        Near(new Vector3(5, 0, 0), lines.point1, 1e-4f);

        Assert.True(B3.IsValidFloat(1));
        Assert.False(B3.IsValidFloat(float.NaN));
        Assert.False(B3.IsValidVec3(new Vector3(float.PositiveInfinity, 0, 0)));
        Assert.True(B3.IsValidQuat(Quaternion.Identity));
        Assert.False(B3.IsValidQuat(default));
        Assert.True(B3.IsValidPosition(new Vector3(1, 2, 3)));
        Assert.True(At(1, 2, 3).IsValid);
        Assert.False(new Transform().IsValid);

        var matrix = new Matrix3 { cx = Vector3.UnitX, cy = Vector3.UnitY, cz = Vector3.UnitZ };
        Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Identity, matrix.MakeQuatFromMatrix())) > 0.9999f);

        byte[] data = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        uint hash = B3.Hash(5381, data);
        Assert.Equal(hash, B3.Hash(5381, data));
        data[9] = 11;
        Assert.NotEqual(hash, B3.Hash(5381, data));

        Assert.False(B3.IsDoublePrecision);
        Assert.Equal(4, B3.MaxManifoldPoints);
        Version version = B3.Version;
        Assert.True(version.major > 0 || version.minor > 0);
        Assert.NotEqual(B3.GetGraphColor(0), B3.GetGraphColor(1));

        ulong start = B3.Ticks;
        B3.Sleep(2);
        Assert.True(B3.GetMilliseconds(start) > 0);
    }

    [Fact]
    public void ScaleBoxAppliesPostScale()
    {
        var halfWidths = new Vector3(1, 1, 1);
        Transform transform = At(1, 0, 0);
        B3.ScaleBox(ref halfWidths, ref transform, new Vector3(2, 3, 4), 0.01f);
        Near(new Vector3(2, 3, 4), halfWidths, 1e-4f);
        Near(new Vector3(2, 0, 0), transform.p, 1e-4f);
    }
}
