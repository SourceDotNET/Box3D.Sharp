using System;
using System.Numerics;

namespace Box3D;

public interface ICustomFilterHandler
{
    bool OnCustomFilter(Shape shapeIdA, Shape shapeIdB);
}

public interface IPreSolveHandler
{
    bool OnPreSolve(Shape shapeIdA, Shape shapeIdB, Vector3 point, Vector3 normal);
}

public interface IAllocatorHandler
{
    IntPtr OnAlloc(ulong size, int alignment);

    void OnFree(IntPtr memory, ulong size);
}

public interface IAssertHandler
{
    int OnAssert(string condition, string fileName, int lineNumber);
}

public interface ILogHandler
{
    void OnLog(string message);
}

public interface IDebugShapeHandler
{
    IntPtr OnCreateDebugShape(in DebugShape debugShape);

    void OnDestroyDebugShape(IntPtr userShape);
}

public interface IDebugDrawHandler
{
    void OnDrawShape(IntPtr userShape, Transform transform, HexColor color);

    void OnDrawSegment(Vector3 p1, Vector3 p2, HexColor color);

    void OnDrawTransform(Transform transform);

    void OnDrawPoint(Vector3 p, float size, HexColor color);

    void OnDrawSphere(Vector3 p, float radius, HexColor color, float alpha);

    void OnDrawCapsule(Vector3 p1, Vector3 p2, float radius, HexColor color, float alpha);

    void OnDrawBounds(AABB aabb, HexColor color);

    void OnDrawBox(Vector3 extents, Transform transform, HexColor color);

    void OnDrawString(Vector3 p, string s, HexColor color);
}
