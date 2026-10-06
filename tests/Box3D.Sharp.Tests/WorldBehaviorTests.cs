using System.Numerics;
using Xunit;
using static Box3D.Tests.TestScene;

namespace Box3D.Tests;

public sealed class WorldBehaviorTests
{
    [Fact]
    public void CreateAndDestroyTracksValidityAndWorldCount()
    {
        int before = B3.WorldCount;
        World world = World.Create(WorldDef.Default);
        try
        {
            Assert.True(world.IsValid);
            Assert.True(world.Exists());
            Assert.Equal(before + 1, B3.WorldCount);
        }
        finally
        {
            world.Destroy();
        }

        Assert.False(world.Exists());
        Assert.Equal(before, B3.WorldCount);
    }

    [Fact]
    public void GravityFromDefinitionIsUsedAndCanBeChanged()
    {
        using var scene = new TestScene(new Vector3(0, 0, -5));
        World world = scene.World;
        Assert.Equal(new Vector3(0, 0, -5), world.Gravity);

        Body body = scene.CreateBall(new Vector3(0, 0, 0)).Body;
        scene.Step(30);
        Assert.True(body.Position.Z < -0.5f);
        Near(0, body.Position.Y, 1e-4f);

        world.Gravity = new Vector3(3, 0, 0);
        Assert.Equal(new Vector3(3, 0, 0), world.Gravity);
        float x = body.Position.X;
        scene.Step(30);
        Assert.True(body.Position.X > x + 0.1f);
    }

    [Fact]
    public void FreeFallMatchesGravity()
    {
        using var scene = new TestScene();
        Body body = scene.CreateBall(new Vector3(0, 5, 0)).Body;
        scene.Step(60);
        Near(0, body.Position.Y, 0.3f);
        Near(-10, body.LinearVelocity.Y, 0.05f);
    }

    [Fact]
    public void BallFallsAndRestsOnGround()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        Body body = scene.CreateBall(new Vector3(0, 5, 0)).Body;

        scene.Step(30);
        float midway = body.Position.Y;
        Assert.InRange(midway, 0.6f, 4.9f);

        scene.Step(240);
        Near(0.5f, body.Position.Y, 0.05f);
        Assert.True(body.LinearVelocity.Length() < 0.05f);
    }

    [Fact]
    public void CountersReflectWorldContents()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        scene.CreateBall(new Vector3(0, 0.6f, 0));
        scene.CreateBall(new Vector3(3, 0.6f, 0));
        scene.Step(5);

        Counters counters = scene.World.Counters;
        Assert.Equal(3, counters.bodyCount);
        Assert.Equal(3, counters.shapeCount);
        Assert.True(counters.contactCount >= 2);
        Assert.Equal(2, scene.World.AwakeBodyCount);
    }

    [Fact]
    public void UserDataObjectRoundTrips()
    {
        using var scene = new TestScene();
        World world = scene.World;
        Assert.Null(world.UserData);

        var payload = new object();
        world.UserData = payload;
        Assert.Same(payload, world.UserData);

        world.UserData = "text";
        Assert.Equal("text", world.UserData);
    }

    [Fact]
    public void DisablingSleepKeepsBodiesAwake()
    {
        using var scene = new TestScene();
        World world = scene.World;
        world.IsSleepingEnabled = false;
        Assert.False(world.IsSleepingEnabled);

        scene.CreateGround();
        Body body = scene.CreateBall(new Vector3(0, 0.5f, 0)).Body;
        scene.Step(300);
        Assert.True(body.IsAwake);
        Assert.Equal(1, world.AwakeBodyCount);
    }

    [Fact]
    public void FrictionMixingRuleChangesSliding()
    {
        float SlideDistance(MixingRule rule)
        {
            WorldDef def = WorldDef.Default;
            def.frictionMixingRule = rule;
            World world = World.Create(def);
            try
            {
                BodyDef groundDef = BodyDef.Default;
                groundDef.position = new Vector3(0, -0.5f, 0);
                ShapeDef groundShape = ShapeDef.Default;
                groundShape.baseMaterial.friction = 0;
                Shape.CreateHull(Body.Create(world, groundDef), groundShape, BoxHull.Make(50, 0.5f, 50));

                BodyDef boxDef = BodyDef.Default;
                boxDef.type = BodyType.Dynamic;
                boxDef.position = new Vector3(0, 0.5f, 0);
                Body box = Body.Create(world, boxDef);
                ShapeDef boxShape = ShapeDef.Default;
                boxShape.baseMaterial.friction = 0.6f;
                Shape.CreateHull(box, boxShape, BoxHull.Make(0.5f, 0.5f, 0.5f));

                for (int i = 0; i < 10; ++i)
                {
                    world.Step(TimeStep, SubSteps);
                }

                box.LinearVelocity = new Vector3(5, 0, 0);
                for (int i = 0; i < 60; ++i)
                {
                    world.Step(TimeStep, SubSteps);
                }

                return box.Position.X;
            }
            finally
            {
                world.Destroy();
            }
        }

        Assert.Equal(MixingRule.GeometricMean, WorldDef.Default.frictionMixingRule);
        Assert.Equal(MixingRule.Maximum, WorldDef.Default.restitutionMixingRule);

        float minimum = SlideDistance(MixingRule.Minimum);
        float maximum = SlideDistance(MixingRule.Maximum);
        float geometricMean = SlideDistance(MixingRule.GeometricMean);
        Assert.True(minimum > 4.5f);
        Assert.True(maximum < 3);
        Near(minimum, geometricMean, 1e-4f);
    }

    [Fact]
    public void ExplosionPushesBodiesAway()
    {
        using var scene = new TestScene(Vector3.Zero);
        Body body = scene.CreateBall(new Vector3(1, 0, 0)).Body;

        ExplosionDef explosion = ExplosionDef.Default;
        explosion.position = Vector3.Zero;
        explosion.radius = 5;
        explosion.falloff = 1;
        explosion.impulsePerArea = 10;
        scene.World.Explode(explosion);
        scene.Step();

        Assert.True(body.LinearVelocity.X > 0.01f);
        Assert.True(body.Position.X > 1);
    }

    [Fact]
    public void ContinuousAndWarmStartingFlagsToggle()
    {
        using var scene = new TestScene();
        World world = scene.World;
        Assert.True(world.IsContinuousEnabled);
        world.IsContinuousEnabled = false;
        Assert.False(world.IsContinuousEnabled);

        world.IsWarmStartingEnabled = false;
        Assert.False(world.IsWarmStartingEnabled);
        world.IsWarmStartingEnabled = true;
        Assert.True(world.IsWarmStartingEnabled);
    }
}
