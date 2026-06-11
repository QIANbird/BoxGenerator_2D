using System;
using UnityEngine;

[Serializable]
public class ChestLatentParams
{
    [Header("Body")]
    // Main chest body dimensions.
    public float width = 300f;
    public float height = 180f;
    public float depth = 140f;

    // Wall thickness reserved for 3D body mesh generation.
    public float bodyThickness = 18f;

    // Controls how much the lower/back body is narrowed.
    public float taper = 40f;

    [Header("Lid")]
    // Lid height and curve smoothness.
    public float lidHeight = 90f;
    public int lidSegments = 12;

    // Shell thickness reserved for 3D lid mesh generation.
    public float lidThickness = 18f;

    [Header("Locker")]
    // Front lock plate size.
    public float lockerWidth = 50f;
    public float lockerHeight = 70f;
    public float lockerDepth = 10f;

    // Local z/depth offset for the lock attachment anchor.
    // 0 places the anchor on the lid front lower edge.
    public float lockerAnchorDepth = 0f;

    // Keeps Unity serialization and default creation simple.
    public ChestLatentParams()
    {
    }

    // Allows creating a full parameter set from code.
    public ChestLatentParams(
        float width,
        float height,
        float depth,
        float taper,
        float lidHeight,
        int lidSegments = 12,
        float lockerWidth = 50f,
        float lockerHeight = 70f,
        float lockerDepth = 10f,
        float bodyThickness = 18f,
        float lidThickness = 18f,
        float lockerAnchorDepth = 0f)
    {
        this.width = width;
        this.height = height;
        this.depth = depth;
        this.taper = taper;
        this.lidHeight = lidHeight;
        this.lidSegments = lidSegments;
        this.lockerWidth = lockerWidth;
        this.lockerHeight = lockerHeight;
        this.lockerDepth = lockerDepth;
        this.bodyThickness = bodyThickness;
        this.lidThickness = lidThickness;
        this.lockerAnchorDepth = lockerAnchorDepth;
    }

    public void ClampValues()
    {
        // Prevent zero or negative sizes.
        width = Mathf.Max(10f, width);
        height = Mathf.Max(10f, height);
        depth = Mathf.Max(10f, depth);
        lidHeight = Mathf.Max(10f, lidHeight);

        // Keep shell thickness usable for future inner/outer mesh generation.
        float maxBodyThickness = Mathf.Min(width, height, depth) * 0.45f;
        float maxLidThickness = Mathf.Min(width, lidHeight, depth) * 0.45f;
        bodyThickness = Mathf.Clamp(bodyThickness, 1f, maxBodyThickness);
        lidThickness = Mathf.Clamp(lidThickness, 1f, maxLidThickness);

        // Keep taper and lid detail within usable geometry limits.
        taper = Mathf.Clamp(taper, 0f, Mathf.Min(width * 0.45f, depth * 0.45f));
        lidSegments = Mathf.Clamp(lidSegments, 3, 64);

        // Keep the lock visible and valid.
        lockerWidth = Mathf.Max(5f, lockerWidth);
        lockerHeight = Mathf.Max(5f, lockerHeight);
        lockerDepth = Mathf.Clamp(lockerDepth, 1f, Mathf.Min(width, depth) * 0.2f);

        // Current coordinate convention uses positive z as box depth.
        // A small negative value can place front accessories slightly outward.
        lockerAnchorDepth = Mathf.Clamp(lockerAnchorDepth, -bodyThickness, bodyThickness);
    }

    // Creates an independent copy so edits do not affect the original.
    public ChestLatentParams Clone()
    {
        return new ChestLatentParams(
            width,
            height,
            depth,
            taper,
            lidHeight,
            lidSegments,
            lockerWidth,
            lockerHeight,
            lockerDepth,
            bodyThickness,
            lidThickness,
            lockerAnchorDepth
        );
    }
}
