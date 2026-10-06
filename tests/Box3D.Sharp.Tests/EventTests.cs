using System;
using System.Numerics;
using Xunit;
using static Box3D.Tests.TestScene;

namespace Box3D.Tests;

public sealed class EventTests
{
    private static bool Involves(Shape a, Shape b, Shape target)
    {
        return a == target || b == target;
    }

    [Fact]
    public void ContactBeginAndEndEventsAreReported()
    {
        using var scene = new TestScene();
        Shape ground = scene.CreateGround();
        ShapeDef def = ShapeDef.Default;
        def.enableContactEvents = true;
        Shape ball = scene.CreateBall(new Vector3(0, 2, 0), def);
        Assert.True(ball.AreContactEventsEnabled);

        Contact contact = Contact.Null;
        for (int i = 0; i < 120 && contact.IsNull; ++i)
        {
            scene.Step();
            ContactEvents events = scene.World.ContactEvents;
            Assert.Equal(events.beginCount, events.beginEvents.Length);
            foreach (ContactBeginTouchEvent begin in events.beginEvents)
            {
                if (Involves(begin.shapeIdA, begin.shapeIdB, ball))
                {
                    Assert.True(Involves(begin.shapeIdA, begin.shapeIdB, ground));
                    contact = begin.contactId;
                }
            }
        }

        Assert.True(contact.IsValid);
        Assert.True(contact.Exists());
        ContactData data = contact.Data;
        Assert.True(Involves(data.shapeIdA, data.shapeIdB, ball));
        Assert.True(data.manifoldCount >= 1);
        Assert.True(data.manifolds[0].pointCount >= 1);
        Manifold manifold = data.manifolds[0];
        Assert.Equal(manifold.pointCount, manifold.points.Length);

        Span<ContactData> bodyContacts = new ContactData[4];
        Assert.Equal(1, ball.Body.GetContactData(bodyContacts));
        Assert.Equal(contact, bodyContacts[0].contactId);

        Body body = ball.Body;
        body.SetTransform(new Vector3(0, 10, 0), Quaternion.Identity);
        body.LinearVelocity = Vector3.Zero;

        bool ended = false;
        for (int i = 0; i < 10 && !ended; ++i)
        {
            scene.Step();
            foreach (ContactEndTouchEvent end in scene.World.ContactEvents.endEvents)
            {
                ended |= Involves(end.shapeIdA, end.shapeIdB, ball);
            }
        }

        Assert.True(ended);
    }

    [Fact]
    public void HitEventsReportImpactSpeed()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        ShapeDef def = ShapeDef.Default;
        def.enableHitEvents = true;
        Shape ball = scene.CreateBall(new Vector3(0, 5, 0), def);

        ContactHitEvent? hit = null;
        for (int i = 0; i < 120 && hit == null; ++i)
        {
            scene.Step();
            foreach (ContactHitEvent e in scene.World.ContactEvents.hitEvents)
            {
                if (Involves(e.shapeIdA, e.shapeIdB, ball))
                {
                    hit = e;
                }
            }
        }

        Assert.NotNull(hit);
        Assert.True(hit.Value.approachSpeed > 5);
        Near(0, hit.Value.point.Y, 0.1f);
        Near(1, MathF.Abs(hit.Value.normal.Y), 1e-3f);
    }

    [Fact]
    public void SensorBeginAndEndEventsAreReported()
    {
        using var scene = new TestScene();
        scene.CreateGround();

        Body sensorBody = scene.CreateBody(BodyType.Static, new Vector3(0, 3, 0));
        ShapeDef sensorDef = ShapeDef.Default;
        sensorDef.isSensor = true;
        sensorDef.enableSensorEvents = true;
        Shape sensor = Shape.CreateHull(sensorBody, sensorDef, BoxHull.Make(2, 0.5f, 2));
        Assert.True(sensor.IsSensor);

        ShapeDef visitorDef = ShapeDef.Default;
        visitorDef.enableSensorEvents = true;
        Shape ball = scene.CreateBall(new Vector3(0, 6, 0), visitorDef);

        bool began = false;
        bool ended = false;
        bool seenInside = false;
        Span<Shape> visitors = stackalloc Shape[4];
        for (int i = 0; i < 180 && !ended; ++i)
        {
            scene.Step();
            SensorEvents events = scene.World.SensorEvents;
            foreach (SensorBeginTouchEvent begin in events.beginEvents)
            {
                began |= begin.sensorShapeId == sensor && begin.visitorShapeId == ball;
            }

            foreach (SensorEndTouchEvent end in events.endEvents)
            {
                ended |= end.sensorShapeId == sensor && end.visitorShapeId == ball;
            }

            if (began && !ended && !seenInside)
            {
                int count = sensor.GetSensorData(visitors);
                seenInside = count == 1 && visitors[0] == ball;
            }
        }

        Assert.True(began);
        Assert.True(seenInside);
        Assert.True(ended);
    }

    [Fact]
    public void BodyMoveEventsCarryTransformAndUserData()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        BodyDef def = BodyDef.Default;
        def.type = BodyType.Dynamic;
        def.position = new Vector3(0, 2, 0);
        def.userData = (IntPtr)5;
        Body body = Body.Create(scene.World, def);
        Shape.CreateSphere(body, ShapeDef.Default, new Sphere { radius = 0.5f });

        scene.Step();
        BodyEvents events = scene.World.BodyEvents;
        Assert.Equal(1, events.moveCount);
        BodyMoveEvent move = events.moveEvents[0];
        Assert.Equal(body, move.bodyId);
        Assert.Equal((IntPtr)5, move.userData);
        Near(body.Position, move.transform.p, 1e-5f);
        Assert.False(move.fellAsleep);

        bool fellAsleep = false;
        for (int i = 0; i < 400 && !fellAsleep; ++i)
        {
            scene.Step();
            foreach (BodyMoveEvent e in scene.World.BodyEvents.moveEvents)
            {
                fellAsleep |= e.bodyId == body && e.fellAsleep;
            }
        }

        Assert.True(fellAsleep);
        Assert.False(body.IsAwake);
    }

    [Fact]
    public void JointEventsFireWhenForceExceedsThreshold()
    {
        using var scene = new TestScene();
        Body ground = scene.CreateBody(BodyType.Static, Vector3.Zero);
        Body body = scene.CreateBall(new Vector3(1, 0, 0)).Body;

        var def = RevoluteJointDef.Default;
        def.@base.bodyIdA = ground;
        def.@base.bodyIdB = body;
        def.@base.localFrameB = At(-1, 0, 0);
        def.@base.forceThreshold = 0;
        def.@base.torqueThreshold = 0;
        def.@base.userData = (IntPtr)7;
        RevoluteJoint joint = RevoluteJoint.Create(scene.World, def);

        scene.Step();
        JointEvents events = scene.World.JointEvents;
        Assert.Equal(1, events.count);
        Assert.Equal((Joint)joint, events.jointEvents[0].jointId);
        Assert.Equal((IntPtr)7, events.jointEvents[0].userData);
    }

    [Fact]
    public void EventsAreNotReportedWhenDisabled()
    {
        using var scene = new TestScene();
        scene.CreateGround();
        scene.CreateBall(new Vector3(0, 2, 0));

        int begins = 0;
        int hits = 0;
        for (int i = 0; i < 120; ++i)
        {
            scene.Step();
            ContactEvents events = scene.World.ContactEvents;
            begins += events.beginCount;
            hits += events.hitCount;
        }

        Assert.Equal(0, begins);
        Assert.Equal(0, hits);
    }
}
