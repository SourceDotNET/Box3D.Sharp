using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Xunit;
using static Box3D.Tests.TestScene;

namespace Box3D.Tests;

public sealed class GlobalHandlerTests
{
    private sealed class LogSink : ILogHandler
    {
        public readonly List<string> Messages = new List<string>();

        public void OnLog(string message)
        {
            lock (Messages)
            {
                Messages.Add(message);
            }
        }
    }

    private sealed class AssertCounter : IAssertHandler
    {
        public int Calls;

        public int OnAssert(string condition, string fileName, int lineNumber)
        {
            Calls++;
            return 0;
        }
    }

    [Fact]
    public void LogHandlerReceivesNativeMessagesAndCanBeCleared()
    {
        var sink = new LogSink();
        B3.SetLogHandler(sink);
        try
        {
            using var scene = new TestScene();
            scene.CreateGround();
            scene.CreateBall(new Vector3(0, 1, 0));
            scene.World.DumpMemoryStats();
        }
        finally
        {
            B3.SetLogHandler(null);
        }

        Assert.NotEmpty(sink.Messages);
        Assert.Contains(sink.Messages, m => m.Contains("body ids"));
    }

    [Fact]
    public void ClearedLogHandlerPrintsToConsole()
    {
        var sink = new LogSink();
        TextWriter original = Console.Out;
        var output = new StringWriter();
        B3.SetLogHandler(sink);
        B3.SetLogHandler(null);
        try
        {
            Console.SetOut(output);
            using var scene = new TestScene();
            scene.CreateGround();
            scene.World.DumpMemoryStats();
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Empty(sink.Messages);
        string text = output.ToString();
        Assert.Contains("Box3D: ", text);
        Assert.Contains("body ids", text);
    }

    [Fact]
    public void AssertHandlerCanBeSetAndCleared()
    {
        var counter = new AssertCounter();
        B3.SetAssertHandler(counter);
        try
        {
            using var scene = new TestScene();
            scene.CreateGround();
            Body body = scene.CreateBall(new Vector3(0, 2, 0)).Body;
            scene.Step(120);
            Near(0.5f, body.Position.Y, 0.05f);
        }
        finally
        {
            B3.SetAssertHandler(null);
        }

        Assert.Equal(0, counter.Calls);
    }

    [Fact]
    public void ClearingAllocatorKeepsDefaultAllocation()
    {
        B3.SetAllocator(null);
        long before = B3.ByteCount;
        using (var scene = new TestScene())
        {
            scene.CreateGround();
            Body body = scene.CreateBall(new Vector3(0, 2, 0)).Body;
            Assert.True(B3.ByteCount > before);
            scene.Step(120);
            Near(0.5f, body.Position.Y, 0.05f);
        }

        Assert.Equal(before, B3.ByteCount);
    }
}
