using System;
using UnityEngine;

[Serializable]
public class ChestLatentParams
{
    public const float MinPositiveSize = 1f;
    public const float MinWidth = 10f;
    public const float MaxWidth = 600f;
    public const float MaxSize = 600f;
    public const float MinLockerWidth = 5f;
    public const int MinLidSegments = 4;
    public const int MaxLidSegments = 64;

    private const float GeometryClearance = 1f;

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
        // Prevent zero/negative accessory sizes first because body dimensions depend on them.
        lockerWidth = Mathf.Clamp(lockerWidth, MinLockerWidth, MaxWidth);
        lockerHeight = Mathf.Clamp(lockerHeight, MinPositiveSize, MaxSize);
        lockerDepth = Mathf.Clamp(lockerDepth, MinPositiveSize, MaxSize);

        // Body must be able to contain the lock plate visually.
        width = Mathf.Clamp(width, Mathf.Max(MinWidth, lockerWidth), MaxWidth);
        height = Mathf.Clamp(height, Mathf.Max(MinPositiveSize, lockerHeight * 0.5f), MaxSize);
        depth = Mathf.Clamp(depth, MinPositiveSize, MaxSize);
        lidHeight = Mathf.Clamp(lidHeight, MinPositiveSize, MaxSize);

        // Keep shell thickness usable for future inner/outer mesh generation.
        float maxBodyThickness = Mathf.Max(MinPositiveSize, Mathf.Min(width, height, depth) * 0.45f);
        float maxLidThickness = Mathf.Max(MinPositiveSize, Mathf.Min(width, lidHeight, depth) * 0.45f);
        bodyThickness = Mathf.Clamp(bodyThickness, MinPositiveSize, maxBodyThickness);
        lidThickness = Mathf.Clamp(lidThickness, MinPositiveSize, maxLidThickness);

        // Keep taper and lid detail within usable geometry limits.
        float maxTaperByWidth = Mathf.Max(0f, width - GeometryClearance);
        float maxTaperByDepth = Mathf.Max(0f, depth * 0.5f - GeometryClearance);
        taper = Mathf.Clamp(taper, 0f, Mathf.Min(maxTaperByWidth, maxTaperByDepth));
        lidSegments = Mathf.Clamp(lidSegments, MinLidSegments, MaxLidSegments);

        // Keep the lock visible and valid.
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
