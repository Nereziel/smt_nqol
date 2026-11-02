using System;
using UnityEngine;

namespace SMT.PriceGun200;

internal static class CheckoutColliderGeometry
{
    internal static bool NeedsReplacement(Vector3 size, Vector3 scale)
    {
        return size.x < 0f || size.y < 0f || size.z < 0f || scale.x < 0f || scale.y < 0f || scale.z < 0f;
    }

    internal static bool TryVertices(Vector3 center, Vector3 size, out Vector3[] vertices)
    {
        vertices = Array.Empty<Vector3>();
        if (!Finite(center.x) || !Finite(center.y) || !Finite(center.z) ||
            !Finite(size.x) || !Finite(size.y) || !Finite(size.z) || size.x == 0f || size.y == 0f || size.z == 0f)
            return false;
        var half = new Vector3(Math.Abs(size.x) * 0.5f, Math.Abs(size.y) * 0.5f, Math.Abs(size.z) * 0.5f);
        vertices = new Vector3[8];
        for (var i = 0; i < 8; i++)
            vertices[i] = new Vector3(center.x + ((i & 1) == 0 ? -half.x : half.x),
                center.y + ((i & 2) == 0 ? -half.y : half.y), center.z + ((i & 4) == 0 ? -half.z : half.z));
        return true;
    }

    // Twelve outward-facing triangles, two per face of the original box.
    internal static readonly int[] Triangles = {
        0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6,
        0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5,
        0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3
    };

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
