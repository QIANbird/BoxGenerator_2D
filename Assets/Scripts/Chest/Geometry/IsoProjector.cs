using UnityEngine;

public static class IsoProjector
{
    // Standard isometric projection angle.
    private const float DefaultAngleDegrees = 30f;

    // Projects a 3D point to 2D using the default isometric angle.
    public static Vector2 Project(Vector3 point3D)
    {
        return Project(point3D, DefaultAngleDegrees);
    }

    // Projects a 3D point to 2D using a custom isometric angle.
    public static Vector2 Project(Vector3 point3D, float angleDegrees)
    {
        // Convert degrees to radians because Mathf trig functions use radians.
        float theta = angleDegrees * Mathf.Deg2Rad;

        // Combine x and z into screen space, while y stays vertical.
        float screenX = (point3D.x + point3D.z) * Mathf.Cos(theta);
        float screenY = -point3D.y + (point3D.x - point3D.z) * Mathf.Sin(theta);

        return new Vector2(screenX, screenY);
    }

    // Projects then moves the result by a 2D screen offset.
    public static Vector2 ProjectWithOffset(Vector3 point3D, Vector2 offset)
    {
        return Project(point3D) + offset;
    }

    // Same as above, but with a custom projection angle.
    public static Vector2 ProjectWithOffset(Vector3 point3D, Vector2 offset, float angleDegrees)
    {
        return Project(point3D, angleDegrees) + offset;
    }
}
