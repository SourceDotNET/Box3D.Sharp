using System;
using System.Numerics;
using Xunit;
using static Box3D.Tests.TestScene;

namespace Box3D.Tests;

public sealed class JointBehaviorTests
{
    private static void Link(ref JointDef def, Body a, Body b)
    {
        def.bodyIdA = a;
        def.bodyIdB = b;
    }

    private static void AssertJoint(Joint joint, JointType type, Body a, Body b)
    {
        Assert.True(joint.IsValid);
        Assert.True(joint.Exists());
        Assert.Equal(type, joint.Type);
        Assert.Equal(a, joint.BodyA);
        Assert.Equal(b, joint.BodyB);
    }

    [Fact]
    public void EveryJointTypeCanBeCreatedSimulatedAndDestroyed()
    {
        using var scene = new TestScene();
        Body ground = scene.CreateBody(BodyType.Static, Vector3.Zero);
        var joints = new Joint[9];
        var bodies = new Body[9];
        for (int i = 0; i < bodies.Length; ++i)
        {
            bodies[i] = scene.CreateBall(new Vector3(3 * i, 0, 0)).Body;
        }

        var distance = DistanceJointDef.Default;
        Link(ref distance.@base, ground, bodies[0]);
        distance.length = 1;
        joints[0] = (Joint)DistanceJoint.Create(scene.World, distance);
        AssertJoint(joints[0], JointType.Distance, ground, bodies[0]);

        var filter = FilterJointDef.Default;
        Link(ref filter.@base, ground, bodies[1]);
        joints[1] = (Joint)FilterJoint.Create(scene.World, filter);
        AssertJoint(joints[1], JointType.Filter, ground, bodies[1]);

        var motor = MotorJointDef.Default;
        Link(ref motor.@base, ground, bodies[2]);
        joints[2] = (Joint)MotorJoint.Create(scene.World, motor);
        AssertJoint(joints[2], JointType.Motor, ground, bodies[2]);

        var parallel = ParallelJointDef.Default;
        Link(ref parallel.@base, ground, bodies[3]);
        joints[3] = (Joint)ParallelJoint.Create(scene.World, parallel);
        AssertJoint(joints[3], JointType.Parallel, ground, bodies[3]);

        var prismatic = PrismaticJointDef.Default;
        Link(ref prismatic.@base, ground, bodies[4]);
        joints[4] = (Joint)PrismaticJoint.Create(scene.World, prismatic);
        AssertJoint(joints[4], JointType.Prismatic, ground, bodies[4]);

        var revolute = RevoluteJointDef.Default;
        Link(ref revolute.@base, ground, bodies[5]);
        joints[5] = (Joint)RevoluteJoint.Create(scene.World, revolute);
        AssertJoint(joints[5], JointType.Revolute, ground, bodies[5]);

        var spherical = SphericalJointDef.Default;
        Link(ref spherical.@base, ground, bodies[6]);
        joints[6] = (Joint)SphericalJoint.Create(scene.World, spherical);
        AssertJoint(joints[6], JointType.Spherical, ground, bodies[6]);

        var weld = WeldJointDef.Default;
        Link(ref weld.@base, ground, bodies[7]);
        joints[7] = (Joint)WeldJoint.Create(scene.World, weld);
        AssertJoint(joints[7], JointType.Weld, ground, bodies[7]);

        var wheel = WheelJointDef.Default;
        Link(ref wheel.@base, ground, bodies[8]);
        joints[8] = (Joint)WheelJoint.Create(scene.World, wheel);
        AssertJoint(joints[8], JointType.Wheel, ground, bodies[8]);

        Assert.Equal(9, ground.JointCount);
        Assert.Equal(9, scene.World.Counters.jointCount);
        scene.Step(30);

        foreach (Joint joint in joints)
        {
            Assert.True(joint.Exists());
            joint.Destroy(true);
            Assert.False(joint.Exists());
        }

        Assert.Equal(0, ground.JointCount);
    }

    [Fact]
    public void RevoluteLimitStopsPendulum()
    {
        using var scene = new TestScene();
        Body ground = scene.CreateBody(BodyType.Static, Vector3.Zero);
        Body free = scene.CreateBall(new Vector3(1, 0, 0), 0.25f).Body;
        Body limited = scene.CreateBall(new Vector3(1, 0, 5), 0.25f).Body;

        var def = RevoluteJointDef.Default;
        Link(ref def.@base, ground, free);
        def.@base.localFrameB = At(-1, 0, 0);
        RevoluteJoint freeJoint = RevoluteJoint.Create(scene.World, def);

        def.@base.bodyIdB = limited;
        def.@base.localFrameA = At(0, 0, 5);
        def.enableLimit = true;
        def.lowerAngle = -0.25f;
        def.upperAngle = 0.25f;
        RevoluteJoint limitedJoint = RevoluteJoint.Create(scene.World, def);

        Assert.True(limitedJoint.IsLimitEnabled);
        Near(-0.25f, limitedJoint.LowerLimit, 1e-6f);
        Near(0.25f, limitedJoint.UpperLimit, 1e-6f);

        scene.Step(30);
        Assert.True(freeJoint.Angle < -0.6f);
        Assert.InRange(limitedJoint.Angle, -0.3f, 0.3f);
        Near(-0.25f, limitedJoint.Angle, 0.05f);
        Near(1, Vector3.Distance(limited.Position, new Vector3(0, 0, 5)), 0.02f);

        limitedJoint.SetLimits(-0.5f, 0.5f);
        Near(-0.5f, limitedJoint.LowerLimit, 1e-6f);
        scene.Step(60);
        Near(-0.5f, limitedJoint.Angle, 0.05f);
    }

    [Fact]
    public void DistanceJointKeepsLength()
    {
        using var scene = new TestScene();
        Body ground = scene.CreateBody(BodyType.Static, Vector3.Zero);
        Body body = scene.CreateBall(new Vector3(0, 3, 0), 0.25f).Body;

        var def = DistanceJointDef.Default;
        Link(ref def.@base, ground, body);
        def.@base.localFrameA = At(0, 5, 0);
        def.length = 2;
        DistanceJoint joint = DistanceJoint.Create(scene.World, def);
        Near(2, joint.Length, 1e-6f);

        body.ApplyLinearImpulseToCenter(new Vector3(body.Mass * 4, 0, 0), true);
        scene.Step(120);
        Near(2, joint.CurrentLength, 0.03f);
        Near(2, Vector3.Distance(body.Position, new Vector3(0, 5, 0)), 0.03f);

        joint.Length = 3;
        scene.Step(120);
        Near(3, joint.CurrentLength, 0.05f);
    }

    [Fact]
    public void RevoluteMotorDrivesAngularVelocity()
    {
        using var scene = new TestScene(Vector3.Zero);
        Body ground = scene.CreateBody(BodyType.Static, Vector3.Zero);
        Body body = scene.CreateBody(BodyType.Dynamic, Vector3.Zero);
        Shape.CreateHull(body, ShapeDef.Default, BoxHull.Make(1, 0.2f, 0.2f));

        var def = RevoluteJointDef.Default;
        Link(ref def.@base, ground, body);
        def.enableMotor = true;
        def.motorSpeed = 3;
        def.maxMotorTorque = 10000;
        RevoluteJoint joint = RevoluteJoint.Create(scene.World, def);

        Assert.True(joint.IsMotorEnabled);
        Near(3, joint.MotorSpeed, 1e-6f);
        scene.Step(30);
        Near(3, body.AngularVelocity.Z, 0.05f);
        Near(0, body.AngularVelocity.X, 1e-3f);
        Assert.True(joint.Angle > 1);

        joint.MotorSpeed = -2;
        scene.Step(30);
        Near(-2, body.AngularVelocity.Z, 0.05f);
    }

    [Fact]
    public void PrismaticLimitStopsSlide()
    {
        using var scene = new TestScene(new Vector3(-10, 0, 0));
        Body ground = scene.CreateBody(BodyType.Static, Vector3.Zero);
        Body body = scene.CreateBall(Vector3.Zero, 0.25f).Body;

        var def = PrismaticJointDef.Default;
        Link(ref def.@base, ground, body);
        def.enableLimit = true;
        def.lowerTranslation = -1;
        def.upperTranslation = 1;
        PrismaticJoint joint = PrismaticJoint.Create(scene.World, def);

        scene.Step(120);
        Near(-1, joint.Translation, 0.02f);
        Near(-1, body.Position.X, 0.02f);
    }

    [Fact]
    public void WeldJointHoldsBodyAgainstGravity()
    {
        using var scene = new TestScene();
        Body ground = scene.CreateBody(BodyType.Static, Vector3.Zero);
        Body body = scene.CreateBall(new Vector3(2, 0, 0)).Body;

        var def = WeldJointDef.Default;
        Link(ref def.@base, ground, body);
        def.@base.localFrameA = At(2, 0, 0);
        WeldJoint.Create(scene.World, def);

        scene.Step(60);
        Near(new Vector3(2, 0, 0), body.Position, 0.05f);
    }

    [Fact]
    public void JointUserDataAndSettingsRoundTrip()
    {
        using var scene = new TestScene();
        Body ground = scene.CreateBody(BodyType.Static, Vector3.Zero);
        Body body = scene.CreateBall(new Vector3(1, 0, 0)).Body;

        var def = SphericalJointDef.Default;
        Link(ref def.@base, ground, body);
        def.@base.userData = (IntPtr)99;
        def.@base.collideConnected = true;
        SphericalJoint spherical = SphericalJoint.Create(scene.World, def);
        Joint joint = (Joint)spherical;

        Assert.Equal((IntPtr)99, joint.UserData);
        Assert.Equal((IntPtr)99, spherical.UserData);
        Assert.True(joint.CollideConnected);
        joint.UserData = (IntPtr)100;
        Assert.Equal((IntPtr)100, spherical.UserData);

        spherical.SetTwistLimits(-0.3f, 0.4f);
        Near(-0.3f, spherical.LowerTwistLimit, 1e-6f);
        Near(0.4f, spherical.UpperTwistLimit, 1e-6f);

        joint.SetConstraintTuning(30, 1);
        joint.GetConstraintTuning(out float hertz, out float damping);
        Near(30, hertz, 1e-6f);
        Near(1, damping, 1e-6f);

        Span<Joint> attached = stackalloc Joint[2];
        Assert.Equal(1, body.GetJoints(attached));
        Assert.Equal(joint, attached[0]);
    }
}
