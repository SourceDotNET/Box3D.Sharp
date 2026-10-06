using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;
using static Box3D.Tests.TestScene;

namespace Box3D.Tests;

public sealed class DynamicTreeTests
{
    private static readonly Vector3[] Centers = { new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(20, 0, 0) };

    private static AABB Box(Vector3 center)
    {
        return new AABB { lowerBound = center - new Vector3(0.5f), upperBound = center + new Vector3(0.5f) };
    }

    private struct Collector : ITreeQueryHandler
    {
        public List<(int ProxyId, ulong UserData)> Found;

        public bool OnTreeQuery(int proxyId, ulong userData)
        {
            Found.Add((proxyId, userData));
            return true;
        }
    }

    private struct Closest : ITreeQueryClosestHandler
    {
        public Vector3 Point;
        public int Calls;
        public ulong Best;
        public float BestDistance;

        public float OnTreeQueryClosest(float distanceSqrMin, int proxyId, ulong userData)
        {
            Calls++;
            float d = Vector3.DistanceSquared(Point, Centers[(int)userData - 100]);
            if (d < BestDistance)
            {
                BestDistance = d;
                Best = userData;
            }

            return d;
        }
    }

    private struct RayCounter : ITreeRayCastHandler
    {
        public int Calls;

        public float OnTreeRayCast(in RayCastInput input, int proxyId, ulong userData)
        {
            Calls++;
            return 1;
        }
    }

    private struct BoxCounter : ITreeBoxCastHandler
    {
        public int Calls;

        public float OnTreeBoxCast(in BoxCastInput input, int proxyId, ulong userData)
        {
            Calls++;
            return 1;
        }
    }

    private struct RayArguments : ITreeRayCastHandler
    {
        public List<int> Ids;
        public List<ulong> UserData;
        public List<float> MaxFractions;

        public float OnTreeRayCast(in RayCastInput input, int proxyId, ulong userData)
        {
            Ids.Add(proxyId);
            UserData.Add(userData);
            MaxFractions.Add(input.maxFraction);
            return input.maxFraction;
        }
    }

    private static int[] Populate(DynamicTree tree)
    {
        var ids = new int[Centers.Length];
        for (int i = 0; i < Centers.Length; ++i)
        {
            ids[i] = tree.CreateProxy(Box(Centers[i]), 1UL << i, (ulong)(100 + i));
        }

        return ids;
    }

    [Fact]
    public void ProxiesCanBeCreatedQueriedMovedAndDestroyed()
    {
        using DynamicTree tree = DynamicTree.Create(16);
        int[] ids = Populate(tree);

        Assert.Equal(3, tree.ProxyCount);
        Assert.True(tree.Height >= 1);
        Assert.True(tree.ByteCount > 0);
        Assert.Equal(2UL, tree.GetCategoryBits(ids[1]));
        AABB root = tree.RootBounds;
        Assert.True(root.lowerBound.X <= -0.5f && root.upperBound.X >= 20.5f);
        tree.Validate();

        var collector = new Collector { Found = new List<(int, ulong)>() };
        tree.Query(Box(new Vector3(10, 0, 0)), ulong.MaxValue, false, ref collector);
        Assert.Single(collector.Found);
        Assert.Equal((ids[1], 101UL), collector.Found[0]);

        var everything = new Collector { Found = new List<(int, ulong)>() };
        tree.Query(new AABB { lowerBound = new Vector3(-50), upperBound = new Vector3(50) }, 4, false, ref everything);
        Assert.Single(everything.Found);
        Assert.Equal(102UL, everything.Found[0].UserData);

        tree.MoveProxy(ids[0], Box(new Vector3(0, 30, 0)));
        var moved = new Collector { Found = new List<(int, ulong)>() };
        tree.Query(Box(new Vector3(0, 30, 0)), ulong.MaxValue, false, ref moved);
        Assert.Single(moved.Found);
        Assert.Equal(ids[0], moved.Found[0].ProxyId);

        tree.SetCategoryBits(ids[2], 8);
        Assert.Equal(8UL, tree.GetCategoryBits(ids[2]));

        tree.DestroyProxy(ids[1]);
        Assert.Equal(2, tree.ProxyCount);
        var empty = new Collector { Found = new List<(int, ulong)>() };
        tree.Query(Box(new Vector3(10, 0, 0)), ulong.MaxValue, false, ref empty);
        Assert.Empty(empty.Found);

        tree.Rebuild(true);
        tree.Validate();
        Assert.Equal(2, tree.ProxyCount);
    }

    [Fact]
    public void QueryClosestReportsNearestProxy()
    {
        using DynamicTree tree = DynamicTree.Create(16);
        Populate(tree);

        var closest = new Closest { Point = new Vector3(12, 1, 0), BestDistance = float.MaxValue };
        float minDistanceSqr = float.MaxValue;
        tree.QueryClosest(closest.Point, ulong.MaxValue, false, ref closest, ref minDistanceSqr);

        Assert.True(closest.Calls >= 1);
        Assert.Equal(101UL, closest.Best);
        Near(5, minDistanceSqr, 1e-4f);
    }

    [Fact]
    public void RayCastAndBoxCastVisitProxiesAlongPath()
    {
        using DynamicTree tree = DynamicTree.Create(16);
        Populate(tree);

        var ray = new RayCastInput { origin = new Vector3(-5, 0, 0), translation = new Vector3(30, 0, 0), maxFraction = 1 };
        var rays = new RayCounter();
        TreeStats stats = tree.RayCast(ray, ulong.MaxValue, false, ref rays);
        Assert.Equal(3, rays.Calls);
        Assert.True(stats.leafVisits >= 3);

        var filtered = new RayCounter();
        tree.RayCast(ray, 2, false, ref filtered);
        Assert.Equal(1, filtered.Calls);

        var boxCast = new BoxCastInput { box = Box(new Vector3(-5, 0, 0)), translation = new Vector3(30, 0, 0), maxFraction = 1 };
        var boxes = new BoxCounter();
        tree.BoxCast(boxCast, ulong.MaxValue, false, ref boxes);
        Assert.Equal(3, boxes.Calls);

        var miss = new RayCounter();
        tree.RayCast(new RayCastInput { origin = new Vector3(-5, 5, 0), translation = new Vector3(30, 0, 0), maxFraction = 1 }, ulong.MaxValue, false, ref miss);
        Assert.Equal(0, miss.Calls);
    }

    [Fact]
    public void RayCastHandlerReceivesInputAndProxyId()
    {
        using DynamicTree tree = DynamicTree.Create(16);
        int[] ids = Populate(tree);

        var ray = new RayCastInput { origin = new Vector3(-5, 0, 0), translation = new Vector3(30, 0, 0), maxFraction = 1 };
        var arguments = new RayArguments { Ids = new List<int>(), UserData = new List<ulong>(), MaxFractions = new List<float>() };
        tree.RayCast(ray, ulong.MaxValue, false, ref arguments);

        Assert.Equal(3, arguments.Ids.Count);
        Assert.Contains(ids[0], arguments.Ids);
        Assert.Contains(100UL, arguments.UserData);
        Assert.All(arguments.MaxFractions, f => Assert.Equal(1, f));
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        DynamicTree tree = DynamicTree.Create(4);
        tree.CreateProxy(Box(Vector3.Zero), 1, 1);
        tree.Dispose();
        tree.Dispose();
    }
}
