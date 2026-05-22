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

    // Controls how much the lower/back body is narrowed.
    public float taper = 40f;       

    [Header("Lid")]
    // Lid height and curve smoothness.
    public float lidHeight = 90f;   
    public int lidSegments = 12;    

    [Header("Locker")]
    // Front lock plate size.
    public float lockerWidth = 50f;
    public float lockerHeight = 70f;

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
        float lockerHeight = 70f)
    {
        this.width = width;
        this.height = height;
        this.depth = depth;
        this.taper = taper;
        this.lidHeight = lidHeight;
        this.lidSegments = lidSegments;
        this.lockerWidth = lockerWidth;
        this.lockerHeight = lockerHeight;
    }

    public void ClampValues()
    {
        // Prevent zero or negative sizes.
        width = Mathf.Max(10f, width);
        height = Mathf.Max(10f, height);
        depth = Mathf.Max(10f, depth);
        lidHeight = Mathf.Max(10f, lidHeight);

        // Keep taper and lid detail within usable geometry limits.
        taper = Mathf.Clamp(taper, 0f, Mathf.Min(width * 0.45f, depth * 0.45f));
        lidSegments = Mathf.Clamp(lidSegments, 3, 64);

        // Keep the lock visible and valid.
        lockerWidth = Mathf.Max(5f, lockerWidth);
        lockerHeight = Mathf.Max(5f, lockerHeight);
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
            lockerHeight
        );
    }
}
