using System;
using System.Numerics;
using Xunit;

namespace Box3D.Tests;

public sealed class CompoundBytesTests
{
    [Fact]
    public void CompoundRoundTripsThroughCallerOwnedBytes()
    {
        var spheres = new[] { new CompoundSphereDef { sphere = new Sphere { radius = 0.5f }, material = SurfaceMaterial.Default } };
        CompoundData original = CompoundData.Create(new CompoundDef { spheres = spheres });
        Assert.False(original.IsNull);

        ReadOnlySpan<byte> serialized = original.ConvertToBytes();
        byte[] buffer = GC.AllocateArray<byte>(serialized.Length, pinned: true);
        serialized.CopyTo(buffer);
        original.Destroy();

        CompoundData loaded = CompoundData.FromBytes(buffer);
        Assert.False(loaded.IsNull);
        Assert.Equal(1, loaded.SphereCount);

        World world = World.Create(WorldDef.Default);
        try
        {
            Body body = Body.Create(world, BodyDef.Default);
            Shape shape = Shape.CreateBakedCompound(body, ShapeDef.Default, loaded);
            Assert.True(shape.IsValid);

            RayResult hit = world.CastRayClosest(new Vector3(0, 5, 0), new Vector3(0, -10, 0), QueryFilter.Default);
            Assert.True(hit.hit);
            Assert.Equal(shape, hit.shapeId);
        }
        finally
        {
            world.Destroy();
        }

        GC.KeepAlive(buffer);
    }

    [Fact]
    public void CorruptBytesAreRejected()
    {
        byte[] buffer = GC.AllocateArray<byte>(64, pinned: true);
        Assert.True(CompoundData.FromBytes(buffer).IsNull);
    }
}
