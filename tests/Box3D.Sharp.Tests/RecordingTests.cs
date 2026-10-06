using System;
using System.IO;
using System.Numerics;
using Xunit;
using static Box3D.Tests.TestScene;

namespace Box3D.Tests;

public sealed class RecordingTests
{
    private static Vector3 RecordFall(Recording recording, int frames)
    {
        using var scene = new TestScene();
        scene.CreateGround();
        Body ball = scene.CreateBall(new Vector3(0, 3, 0)).Body;

        scene.World.StartRecording(recording);
        scene.Step(frames);
        scene.World.StopRecording();
        return ball.Position;
    }

    [Fact]
    public void RecordingCapturesStepsAndValidates()
    {
        Recording recording = Recording.Create(1 << 16);
        try
        {
            Assert.False(recording.IsNull);
            RecordFall(recording, 30);

            Assert.True(recording.Size > 0);
            Assert.Equal(recording.Size, recording.Data.Length);
            Assert.True(B3.ValidateReplay(recording.Data, 1));
        }
        finally
        {
            recording.Destroy();
        }
    }

    [Fact]
    public void PlayerReplaysRecordedFrames()
    {
        Recording recording = Recording.Create(1 << 16);
        try
        {
            Vector3 finalPosition = RecordFall(recording, 30);

            RecordPlayer player = RecordPlayer.Create(recording.Data, 1);
            try
            {
                Assert.False(player.IsNull);
                Assert.Equal(30, player.FrameCount);
                RecordPlayerInfo info = player.Info;
                Assert.Equal(30, info.frameCount);
                Near(TimeStep, info.timeStep, 1e-6f);
                Assert.Equal(SubSteps, info.subStepCount);
                Assert.True(player.WorldId.Exists());

                int steps = 0;
                while (player.StepFrame())
                {
                    steps++;
                }

                Assert.Equal(30, steps);
                Assert.True(player.IsAtEnd);
                Assert.False(player.HasDiverged);
                Assert.Equal(-1, player.DivergeFrame);

                Body replayed = Body.Null;
                for (int i = 0; i < player.BodyCount; ++i)
                {
                    Body body = player.GetBodyId(i);
                    if (body.Exists() && body.Type == BodyType.Dynamic)
                    {
                        replayed = body;
                    }
                }

                Assert.True(replayed.IsValid);
                Near(finalPosition, replayed.Position, 1e-5f);

                player.SeekFrame(10);
                Assert.Equal(10, player.Frame);
                player.Restart();
                Assert.Equal(0, player.Frame);
            }
            finally
            {
                player.Destroy();
            }
        }
        finally
        {
            recording.Destroy();
        }
    }

    [Fact]
    public void RecordingSavesAndLoadsFromFile()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "recording-test-" + Guid.NewGuid().ToString("N") + ".b3rec");
        try
        {
            Recording recording = Recording.Create(1 << 16);
            try
            {
                RecordFall(recording, 10);
                Assert.True(recording.SaveToFile(path));

                Recording loaded = Recording.LoadFromFile(path);
                try
                {
                    Assert.False(loaded.IsNull);
                    Assert.Equal(recording.Size, loaded.Size);
                    Assert.True(recording.Data.SequenceEqual(loaded.Data));
                }
                finally
                {
                    loaded.Destroy();
                }
            }
            finally
            {
                recording.Destroy();
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InvalidDataIsRejected()
    {
        byte[] garbage = new byte[64];
        Assert.False(B3.ValidateReplay(garbage, 1));
        RecordPlayer player = RecordPlayer.Create(garbage, 1);
        Assert.True(player.IsNull);
        player.Destroy();
    }
}
