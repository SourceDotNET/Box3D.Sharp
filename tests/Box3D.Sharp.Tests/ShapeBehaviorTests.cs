using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using static Box3D.Tests.TestScene;

namespace Box3D.Tests;

public sealed class ShapeBehaviorTests
{
    private struct ChildCollector : ICompoundQueryHandler
    {
        public List<int> Children;

        public bool OnCompoundQuery(CompoundData compound, int childIndex)
        {
            Children.Add(childIndex);
            return true;
        }
    }

    private static void DropBallAndExpectRest(TestScene scene, Vector3 start, float restY)
    {
        Body ball = scene.CreateBall(start).Body;
        scene.Step(240);
        Near(restY, ball.Position.Y, 0.06f);
        Assert.True(ball.LinearVelocity.Length() < 0.1f);
    }

    [Fact]
    public void SphereShapeCreateQueryDestroy()
    {
        using var scene = new TestScene();
        Shape shape = scene.CreateBall(new Vector3(0, 3, 0));
        Body body = shape.Body;

        Assert.True(shape.IsValid);
        Assert.True(shape.Exists());
        Assert.Equal(ShapeType.Sphere, shape.Type);
        Assert.Equal(scene.World, shape.World);
        Near(0.5f, shape.Sphere.radius, 1e-6f);
        Near(2.5f, shape.AABB.lowerBound.Y, 0.05f);

        CastOutput hit = shape.RayCast(new Vector3(0, 10, 0), new Vector3(0, -20, 0));
        Assert.True(hit.hit);
        Near(new Vector3(0, 3.5f, 0), hit.point, 1e-3f);
        Near(new Vector3(0, 1, 0), hit.normal, 1e-3f);

        shape.Sphere = new Sphere { radius = 1 };
        Near(1, shape.Sphere.radius, 1e-6f);

        shape.Friction = 0.25f;
        Near(0.25f, shape.Friction, 1e-6f);

        shape.Destroy(true);
        Assert.False(shape.Exists());
        Assert.Equal(0, body.ShapeCount);
    }

    [Fact]
    public void CapsuleShapeHasExpectedMassAndBounds()
    {
        using var scene = new TestScene();
        Body body = scene.CreateBody(BodyType.Dynamic, Vector3.Zero);
        var capsule = new Capsule { center1 = new Vector3(-1, 0, 0), center2 = new Vector3(1, 0, 0), radius = 0.5f };
        Shape shape = Shape.CreateCapsule(body, ShapeDef.Default, capsule);

        Assert.Equal(ShapeType.Capsule, shape.Type);
        Near(0.5f, shape.Capsule.radius, 1e-6f);
        Near(new Vector3(1, 0, 0), shape.Capsule.center2, 1e-6f);

        float expected = 1000 * (MathF.PI * 0.25f * 2 + 4.0f / 3.0f * MathF.PI * 0.125f);
        Near(expected, body.Mass, expected * 1e-3f);
        Near(new Vector3(-1.5f, -0.5f, -0.5f), shape.AABB.lowerBound, 0.05f);
        Near(new Vector3(1.5f, 0.5f, 0.5f), shape.AABB.upperBound, 0.05f);

        shape.Destroy(true);
        Assert.False(shape.Exists());
    }

    [Fact]
    public void BoxHullShapeHasBoxMassAndRestsOnGround()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        Body body = scene.CreateBody(BodyType.Dynamic, new Vector3(0, 3, 0));
        Shape shape = Shape.CreateHull(body, ShapeDef.Default, BoxHull.Make(1, 0.5f, 2));

        Assert.Equal(ShapeType.Hull, shape.Type);
        Near(8000, body.Mass, 1);
        HullData hull = shape.Hull;
        Assert.False(hull.IsNull);
        Assert.Equal(8, hull.VertexCount);
        Assert.Equal(6, hull.FaceCount);

        scene.Step(240);
        Near(0.5f, body.Position.Y, 0.05f);
    }

    [Fact]
    public void HullDataShapeFromPoints()
    {
        HullData hull = HullData.Create(CubePoints(1), 8);
        try
        {
            using var scene = new TestScene();

            Assert.False(hull.IsNull);
            Assert.Equal(8, hull.VertexCount);
            Assert.Equal(6, hull.FaceCount);
            Near(8, hull.Volume, 1e-3f);
            Near(Vector3.Zero, hull.Center, 1e-4f);

            Assert.Equal(8, hull.Vertices.Length);
            Assert.Equal(8, hull.Points.Length);
            Assert.Equal(hull.EdgeCount, hull.Edges.Length);
            Assert.Equal(6, hull.Planes.Length);
            Assert.Equal(6, hull.Faces.Length);
            Assert.Equal(24, hull.SoaVertices.Length);
            Assert.Equal(24, hull.SoaNormals.Length);
            Assert.Equal(hull.EdgeCount / 2, hull.EdgeCosines.Length);
            for (int i = 0; i < 8; ++i)
            {
                Vector3 point = hull.Points[i];
                Near(Vector3.One, Vector3.Abs(point), 1e-5f);
                Assert.Equal(point.X, hull.SoaVertices[i]);
                Assert.Equal(point.Y, hull.SoaVertices[8 + i]);
                Assert.Equal(point.Z, hull.SoaVertices[16 + i]);
            }

            foreach (Plane plane in hull.Planes)
            {
                Near(1, plane.normal.Length(), 1e-5f);
                Near(1, plane.offset, 1e-5f);
            }

            foreach (float cosine in hull.EdgeCosines)
            {
                Near(0, cosine, 1e-5f);
            }

            foreach (HullHalfEdge edge in hull.Edges)
            {
                Assert.True(edge.origin < 8);
                Assert.True(edge.face < 6);
                Assert.Equal(edge, hull.Edges[hull.Edges[edge.twin].twin]);
            }

            Body body = scene.CreateBody(BodyType.Dynamic, new Vector3(0, 5, 0));
            Shape shape = Shape.CreateHull(body, ShapeDef.Default, hull);
            Assert.Equal(ShapeType.Hull, shape.Type);
            Near(8000, body.Mass, 1);
        }
        finally
        {
            hull.Destroy();
        }
    }

    [Fact]
    public void TransformedHullIsOffsetAndScaled()
    {
        HullData hull = HullData.Create(CubePoints(0.5f), 8);
        try
        {
            using var scene = new TestScene();
            Body body = scene.CreateBody(BodyType.Static, new Vector3(0, 10, 0));

            Shape boxShape = Shape.CreateTransformedHull(body, ShapeDef.Default, BoxHull.Make(0.5f, 0.5f, 0.5f), At(2, 0, 0), Vector3.One);
            Assert.Equal(ShapeType.Hull, boxShape.Type);
            AABB box = boxShape.AABB;
            Near(new Vector3(2, 10, 0), (box.lowerBound + box.upperBound) * 0.5f, 0.05f);

            Shape scaled = Shape.CreateTransformedHull(body, ShapeDef.Default, hull, At(-3, 0, 0), new Vector3(2, 1, 1));
            AABB scaledBox = scaled.AABB;
            Near(new Vector3(-3, 10, 0), (scaledBox.lowerBound + scaledBox.upperBound) * 0.5f, 0.05f);
            Near(2, scaledBox.upperBound.X - scaledBox.lowerBound.X, 0.1f);
            Near(1, scaledBox.upperBound.Y - scaledBox.lowerBound.Y, 0.1f);
        }
        finally
        {
            hull.Destroy();
        }
    }

    [Fact]
    public void MeshShapeSupportsResting()
    {
        MeshData mesh = MeshData.CreateGrid(10, 10, 1, 0, false);
        try
        {
            using var scene = new TestScene();

            Assert.False(mesh.IsNull);
            Assert.Equal(200, mesh.TriangleCount);
            Near(-5, mesh.Bounds.lowerBound.X, 1e-4f);
            Near(5, mesh.Bounds.upperBound.Z, 1e-4f);

            Body ground = scene.CreateBody(BodyType.Static, Vector3.Zero);
            Shape shape = Shape.CreateMesh(ground, ShapeDef.Default, mesh, Vector3.One);
            Assert.Equal(ShapeType.Mesh, shape.Type);
            Assert.Equal(mesh, shape.Mesh.data);

            DropBallAndExpectRest(scene, new Vector3(0.3f, 3, 0.2f), 0.5f);

            shape.Destroy(false);
            Assert.False(shape.Exists());
        }
        finally
        {
            mesh.Destroy();
        }
    }

    [Fact]
    public void MeshDataFromDefinitionAndBoxMesh()
    {
        var def = new MeshDef
        {
            vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, 0) },
            indices = new[] { 0, 1, 2, 0, 2, 3 },
        };
        MeshData quad = MeshData.Create(def, Span<int>.Empty);
        try
        {
            Assert.False(quad.IsNull);
            Assert.Equal(2, quad.TriangleCount);
            Assert.Equal(4, quad.VertexCount);
            Near(new Vector3(1, 0, 1), quad.Bounds.upperBound, 1e-5f);

            Assert.Equal(4, quad.Vertices.Length);
            Assert.Equal(2, quad.Triangles.Length);
            Assert.Equal(2, quad.MaterialIndices.Length);
            Assert.Equal(2, quad.Flags.Length);
            Assert.Equal(quad.NodeCount, quad.Nodes.Length);
            Assert.True(quad.Nodes.Length > 0);
            foreach (Vector3 vertex in def.vertices)
            {
                Assert.Contains(vertex, quad.Vertices.ToArray());
            }

            foreach (MeshTriangle triangle in quad.Triangles)
            {
                Assert.InRange(triangle.index1, 0, 3);
                Assert.InRange(triangle.index2, 0, 3);
                Assert.InRange(triangle.index3, 0, 3);
            }
        }
        finally
        {
            quad.Destroy();
        }

        MeshData box = MeshData.CreateBox(new Vector3(1, 2, 3), new Vector3(1, 1, 1), false);
        try
        {
            Assert.Equal(12, box.TriangleCount);
            Near(new Vector3(0, 1, 2), box.Bounds.lowerBound, 1e-5f);
            Near(new Vector3(2, 3, 4), box.Bounds.upperBound, 1e-5f);
            AABB moved = box.ComputeAABB(At(10, 0, 0), Vector3.One);
            Near(new Vector3(10, 1, 2), moved.lowerBound, 1e-4f);
        }
        finally
        {
            box.Destroy();
        }
    }

    [Fact]
    public void HeightFieldShapeSupportsResting()
    {
        HeightFieldData field = HeightFieldData.CreateGrid(11, 11, Vector3.One, false);
        try
        {
            using var scene = new TestScene();

            Assert.False(field.IsNull);
            Assert.Equal(11, field.ColumnCount);
            Assert.Equal(11, field.RowCount);
            Assert.Equal(121, field.CompressedHeights.Length);
            Assert.Equal(100, field.MaterialIndices.Length);
            Assert.Equal(200, field.Flags.Length);
            AABB bounds = field.Aabb;
            Vector3 center = (bounds.lowerBound + bounds.upperBound) * 0.5f;

            Body ground = scene.CreateBody(BodyType.Static, Vector3.Zero);
            Shape shape = Shape.CreateHeightField(ground, ShapeDef.Default, field);
            Assert.Equal(ShapeType.Height, shape.Type);
            Assert.Equal(field, shape.HeightField);

            DropBallAndExpectRest(scene, new Vector3(center.X + 0.3f, 3, center.Z + 0.2f), 0.5f);
        }
        finally
        {
            field.Destroy();
        }
    }

    [Fact]
    public void BakedCompoundShapeQueriesAndSimulates()
    {
        HullData hull = HullData.Create(CubePoints(0.5f), 8);
        try
        {
            var def = new CompoundDef
            {
                spheres = new[] { new CompoundSphereDef { sphere = new Sphere { center = new Vector3(-2, 0, 0), radius = 0.5f }, material = SurfaceMaterial.Default } },
                capsules = new[] { new CompoundCapsuleDef { capsule = new Capsule { center1 = new Vector3(-0.5f, 0, 0), center2 = new Vector3(0.5f, 0, 0), radius = 0.5f }, material = SurfaceMaterial.Default } },
                hulls = new[] { new CompoundHullDef { hull = hull, transform = At(2, 0, 0), material = SurfaceMaterial.Default } },
            };
            CompoundData compound = CompoundData.Create(def);
            try
            {
                using var scene = new TestScene();

                Assert.False(compound.IsNull);
                Assert.Equal(1, compound.SphereCount);
                Assert.Equal(1, compound.CapsuleCount);
                Assert.Equal(1, compound.HullCount);
                Near(0.5f, compound.GetSphere(0).sphere.radius, 1e-6f);
                Near(new Vector3(-2, 0, 0), compound.GetSphere(0).sphere.center, 1e-6f);
                Near(0.5f, compound.GetCapsule(0).capsule.radius, 1e-6f);
                Near(new Vector3(2, 0, 0), compound.GetHull(0).transform.p, 1e-6f);

                AABB aabb = compound.ComputeAABB(At(0, 0, 0));
                Near(-2.5f, aabb.lowerBound.X, 0.05f);
                Near(2.5f, aabb.upperBound.X, 0.05f);

                var collector = new ChildCollector { Children = new List<int>() };
                compound.Query(new AABB { lowerBound = new Vector3(-2.2f, -0.2f, -0.2f), upperBound = new Vector3(-1.8f, 0.2f, 0.2f) }, ref collector);
                Assert.Single(collector.Children);
                Assert.Equal(ShapeType.Sphere, compound.GetChild(collector.Children[0]).type);

                CastOutput ray = compound.RayCast(new RayCastInput { origin = new Vector3(-5, 0, 0), translation = new Vector3(10, 0, 0), maxFraction = 1 });
                Assert.True(ray.hit);
                Near(-2.5f, ray.point.X, 1e-3f);

                Body dynamic = scene.CreateBody(BodyType.Dynamic, new Vector3(0, 3, 0));
                Assert.False(Shape.CreateBakedCompound(dynamic, ShapeDef.Default, compound).IsValid);

                Body body = scene.CreateBody(BodyType.Static, Vector3.Zero);
                Shape shape = Shape.CreateBakedCompound(body, ShapeDef.Default, compound);
                Assert.True(shape.IsValid);
                Assert.Equal(ShapeType.Compound, shape.Type);
                Near(-2.5f, shape.AABB.lowerBound.X, 0.05f);

                DropBallAndExpectRest(scene, new Vector3(2.1f, 3, 0.1f), 1);
            }
            finally
            {
                compound.Destroy();
            }
        }
        finally
        {
            hull.Destroy();
        }
    }

    [Fact]
    public void BakedCompoundWithMeshChildIsHitByRayCast()
    {
        MeshData mesh = MeshData.CreateBox(Vector3.Zero, new Vector3(1, 1, 1), false);
        try
        {
            SurfaceMaterial material = SurfaceMaterial.Default;
            material.friction = 0.3f;
            var def = new CompoundDef
            {
                meshes = new[] { new CompoundMeshDef { meshData = mesh, transform = At(3, 0, 0), scale = Vector3.One, materials = new[] { material } } },
            };
            CompoundData compound = CompoundData.Create(def);
            try
            {
                Assert.False(compound.IsNull);
                Assert.Equal(1, compound.MeshCount);
                Assert.Equal(ShapeType.Mesh, compound.GetChild(0).type);
                CompoundMesh child = compound.GetMesh(0);
                Near(new Vector3(3, 0, 0), child.transform.p, 1e-6f);
                Assert.Equal(1, child.materialCount);
                Near(0.3f, compound.Materials[child.materialIndices[0]].friction, 1e-6f);

                using var scene = new TestScene();
                Body body = scene.CreateBody(BodyType.Static, new Vector3(0, 5, 0));
                Shape shape = Shape.CreateBakedCompound(body, ShapeDef.Default, compound);
                Assert.True(shape.IsValid);
                Assert.Equal(ShapeType.Compound, shape.Type);

                CastOutput hit = shape.RayCast(new Vector3(3, 10, 0), new Vector3(0, -10, 0));
                Assert.True(hit.hit);
                Near(new Vector3(3, 6, 0), hit.point, 1e-3f);
                Near(new Vector3(0, 1, 0), hit.normal, 1e-3f);

                CastOutput miss = shape.RayCast(new Vector3(0, 10, 0), new Vector3(0, -10, 0));
                Assert.False(miss.hit);
            }
            finally
            {
                compound.Destroy();
            }
        }
        finally
        {
            mesh.Destroy();
        }
    }

    [Fact]
    public void DataHandleDestroyReleasesNativeMemory()
    {
        long before = B3.ByteCount;

        HullData hull = HullData.CreateCylinder(2, 0.5f, 0, 12);
        Assert.False(hull.IsNull);
        Assert.True(B3.ByteCount > before);
        hull.Destroy();
        Assert.Equal(before, B3.ByteCount);

        MeshData mesh = MeshData.CreateTorus(8, 8, 2, 0.5f);
        Assert.True(mesh.TriangleCount > 0);
        Assert.True(B3.ByteCount > before);
        mesh.Destroy();
        Assert.Equal(before, B3.ByteCount);

        HeightFieldData field = HeightFieldData.CreateWave(9, 9, Vector3.One, 1, 1, false);
        Assert.True(B3.ByteCount > before);
        field.Destroy();
        Assert.Equal(before, B3.ByteCount);

        var def = new CompoundDef
        {
            spheres = new[] { new CompoundSphereDef { sphere = new Sphere { radius = 1 }, material = SurfaceMaterial.Default } },
        };
        CompoundData compound = CompoundData.Create(def);
        Assert.True(B3.ByteCount > before);
        compound.Destroy();
        Assert.Equal(before, B3.ByteCount);
    }

    [Fact]
    public void ShapeUserDataNameAndFilterRoundTrip()
    {
        using var scene = new TestScene();
        ShapeDef def = ShapeDef.Default;
        def.userData = (IntPtr)123;
        def.name = "ball";
        def.filter = new Filter { categoryBits = 2, maskBits = 5, groupIndex = -1 };
        Shape shape = scene.CreateBall(Vector3.Zero, def);

        Assert.Equal((IntPtr)123, shape.UserData);
        Assert.Equal("ball", shape.Name);
        Assert.Equal(2UL, shape.Filter.categoryBits);
        Assert.Equal(5UL, shape.Filter.maskBits);
        Assert.Equal(-1, shape.Filter.groupIndex);

        shape.UserData = (IntPtr)456;
        Assert.Equal((IntPtr)456, shape.UserData);
        shape.SetFilter(Filter.Default, true);
        Assert.Equal(ulong.MaxValue, shape.Filter.maskBits);
    }
}
