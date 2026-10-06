using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Box3D.Interop;

namespace Box3D;

public static unsafe partial class WorldExtensions
{
    extension(World world)
    {
        public void Draw<T>(in DebugDraw options, ref T drawer, ulong maskBits)
            where T : struct, IDebugDrawHandler
        {
            T state = drawer;
            b3DebugDraw raw = Unsafe.BitCast<DebugDraw, b3DebugDraw>(options);
            DrawContext context = DrawContext.Create<T>(Unsafe.AsPointer(ref state));
            DrawContext.Apply(ref raw, &context);
            Native.b3World_Draw(Unsafe.BitCast<World, b3WorldId>(world), &raw, maskBits);
            drawer = state;
            context.Base.ThrowIfFailed();
        }
    }
}

internal unsafe struct DrawContext
{
    public CallbackContext Base;
    public delegate*<void*, IntPtr, Transform, HexColor, void> Shape;
    public delegate*<void*, Vector3, Vector3, HexColor, void> Segment;
    public delegate*<void*, Transform, void> Frame;
    public delegate*<void*, Vector3, float, HexColor, void> Point;
    public delegate*<void*, Vector3, float, HexColor, float, void> Sphere;
    public delegate*<void*, Vector3, Vector3, float, HexColor, float, void> Capsule;
    public delegate*<void*, AABB, HexColor, void> Bounds;
    public delegate*<void*, Vector3, Transform, HexColor, void> Box;
    public delegate*<void*, Vector3, string, HexColor, void> Text;

    public static DrawContext Create<T>(void* state)
        where T : struct, IDebugDrawHandler
    {
        return new DrawContext
        {
            Base = new CallbackContext(null, state),
            Shape = &ShapeDispatch<T>,
            Segment = &SegmentDispatch<T>,
            Frame = &FrameDispatch<T>,
            Point = &PointDispatch<T>,
            Sphere = &SphereDispatch<T>,
            Capsule = &CapsuleDispatch<T>,
            Bounds = &BoundsDispatch<T>,
            Box = &BoxDispatch<T>,
            Text = &TextDispatch<T>,
        };
    }

    public static void Apply(ref b3DebugDraw raw, DrawContext* context)
    {
        raw.DrawShapeFcn = &ShapeThunk;
        raw.DrawSegmentFcn = &SegmentThunk;
        raw.DrawTransformFcn = &FrameThunk;
        raw.DrawPointFcn = &PointThunk;
        raw.DrawSphereFcn = &SphereThunk;
        raw.DrawCapsuleFcn = &CapsuleThunk;
        raw.DrawBoundsFcn = &BoundsThunk;
        raw.DrawBoxFcn = &BoxThunk;
        raw.DrawStringFcn = &TextThunk;
        raw.context = context;
    }

    private static void ShapeDispatch<T>(void* s, IntPtr userShape, Transform transform, HexColor color)
        where T : struct, IDebugDrawHandler => Unsafe.AsRef<T>(s).OnDrawShape(userShape, transform, color);

    private static void SegmentDispatch<T>(void* s, Vector3 p1, Vector3 p2, HexColor color)
        where T : struct, IDebugDrawHandler => Unsafe.AsRef<T>(s).OnDrawSegment(p1, p2, color);

    private static void FrameDispatch<T>(void* s, Transform transform)
        where T : struct, IDebugDrawHandler => Unsafe.AsRef<T>(s).OnDrawTransform(transform);

    private static void PointDispatch<T>(void* s, Vector3 p, float size, HexColor color)
        where T : struct, IDebugDrawHandler => Unsafe.AsRef<T>(s).OnDrawPoint(p, size, color);

    private static void SphereDispatch<T>(void* s, Vector3 p, float radius, HexColor color, float alpha)
        where T : struct, IDebugDrawHandler => Unsafe.AsRef<T>(s).OnDrawSphere(p, radius, color, alpha);

    private static void CapsuleDispatch<T>(void* s, Vector3 p1, Vector3 p2, float radius, HexColor color, float alpha)
        where T : struct, IDebugDrawHandler => Unsafe.AsRef<T>(s).OnDrawCapsule(p1, p2, radius, color, alpha);

    private static void BoundsDispatch<T>(void* s, AABB aabb, HexColor color)
        where T : struct, IDebugDrawHandler => Unsafe.AsRef<T>(s).OnDrawBounds(aabb, color);

    private static void BoxDispatch<T>(void* s, Vector3 extents, Transform transform, HexColor color)
        where T : struct, IDebugDrawHandler => Unsafe.AsRef<T>(s).OnDrawBox(extents, transform, color);

    private static void TextDispatch<T>(void* s, Vector3 p, string text, HexColor color)
        where T : struct, IDebugDrawHandler => Unsafe.AsRef<T>(s).OnDrawString(p, text, color);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ShapeThunk(void* userShape, b3Transform transform, b3HexColor color, void* context)
    {
        var c = (DrawContext*)context;
        try
        {
            c->Shape(c->Base.State, (IntPtr)userShape, Unsafe.BitCast<b3Transform, Transform>(transform), (HexColor)color);
        }
        catch (Exception exception)
        {
            c->Base.Fail(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void SegmentThunk(Vector3 p1, Vector3 p2, b3HexColor color, void* context)
    {
        var c = (DrawContext*)context;
        try
        {
            c->Segment(c->Base.State, p1, p2, (HexColor)color);
        }
        catch (Exception exception)
        {
            c->Base.Fail(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void FrameThunk(b3Transform transform, void* context)
    {
        var c = (DrawContext*)context;
        try
        {
            c->Frame(c->Base.State, Unsafe.BitCast<b3Transform, Transform>(transform));
        }
        catch (Exception exception)
        {
            c->Base.Fail(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void PointThunk(Vector3 p, float size, b3HexColor color, void* context)
    {
        var c = (DrawContext*)context;
        try
        {
            c->Point(c->Base.State, p, size, (HexColor)color);
        }
        catch (Exception exception)
        {
            c->Base.Fail(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void SphereThunk(Vector3 p, float radius, b3HexColor color, float alpha, void* context)
    {
        var c = (DrawContext*)context;
        try
        {
            c->Sphere(c->Base.State, p, radius, (HexColor)color, alpha);
        }
        catch (Exception exception)
        {
            c->Base.Fail(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void CapsuleThunk(Vector3 p1, Vector3 p2, float radius, b3HexColor color, float alpha, void* context)
    {
        var c = (DrawContext*)context;
        try
        {
            c->Capsule(c->Base.State, p1, p2, radius, (HexColor)color, alpha);
        }
        catch (Exception exception)
        {
            c->Base.Fail(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void BoundsThunk(b3AABB aabb, b3HexColor color, void* context)
    {
        var c = (DrawContext*)context;
        try
        {
            c->Bounds(c->Base.State, Unsafe.BitCast<b3AABB, AABB>(aabb), (HexColor)color);
        }
        catch (Exception exception)
        {
            c->Base.Fail(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void BoxThunk(Vector3 extents, b3Transform transform, b3HexColor color, void* context)
    {
        var c = (DrawContext*)context;
        try
        {
            c->Box(c->Base.State, extents, Unsafe.BitCast<b3Transform, Transform>(transform), (HexColor)color);
        }
        catch (Exception exception)
        {
            c->Base.Fail(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void TextThunk(Vector3 p, sbyte* text, b3HexColor color, void* context)
    {
        var c = (DrawContext*)context;
        try
        {
            c->Text(c->Base.State, p, InteropHelpers.ToManagedString(text), (HexColor)color);
        }
        catch (Exception exception)
        {
            c->Base.Fail(exception);
        }
    }
}
