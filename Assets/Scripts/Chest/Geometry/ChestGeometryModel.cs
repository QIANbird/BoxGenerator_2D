using System.Collections.Generic;
using UnityEngine;

public class ChestGeometryModel
{
    // Builds chest geometry data from latent size parameters.
    public ChestGeometryData Build(ChestLatentParams p)
    {
        // Keep parameters in a valid range before creating points.
        p.ClampValues();

        ChestGeometryData data = new ChestGeometryData();

        AddBodyPoints(data, p);
        AddBodyFaces(data);

        AddLidPoints(data, p);
        AddLidFaces(data, p);

        return data;
    }

    private void AddBodyPoints(ChestGeometryData data, ChestLatentParams p)
    {
        // Main dimensions; taper narrows the lower/back part of the body.
        float w = p.width;
        float h = p.height;
        float d = p.depth;
        float t = p.taper;

        // Front four body corners.
        data.AddPoint("BD_TL", new Vector3(-w / 2f, 0f, 0f));
        data.AddPoint("BD_TR", new Vector3(w / 2f, 0f, 0f));
        data.AddPoint("BD_BL", new Vector3((t - w) / 2f, -h, t));
        data.AddPoint("BD_BR", new Vector3((w - t) / 2f, -h, t));

        // Back four body corners, extended along the z axis.
        data.AddPoint("BD_TL_1", new Vector3(-w / 2f, 0f, d));
        data.AddPoint("BD_TR_1", new Vector3(w / 2f, 0f, d));
        data.AddPoint("BD_BL_1", new Vector3((t - w) / 2f, -h, d - t));
        data.AddPoint("BD_BR_1", new Vector3((w - t) / 2f, -h, d - t));
        
    }
    
    // 生成盖子的左右半椭圆曲线点
    private void AddLidPoints(ChestGeometryData data, ChestLatentParams p)
    {
       float w = p.width;
       float d = p.depth;
       float a = p.lidHeight;
       int n = p.lidSegments;

       for (int i = 0; i <= n; i++)
       {
           float theta = Mathf.PI * i / n;

           float y = a * Mathf.Sin(theta);
           float z = d / 2f - (d / 2f) * Mathf.Cos(theta);

           Vector3 leftPoint = new Vector3(-w / 2f, y, z);
           Vector3 rightPoint = new Vector3(w / 2f, y, z);

           data.AddPoint($"LI_L_{i}", leftPoint);
           data.AddPoint($"LI_R_{i}", rightPoint);
       }
}

// 根据曲线点生成盖子分段曲面
private void AddLidFaces(ChestGeometryData data, ChestLatentParams p)
{
    int n = p.lidSegments;

    for (int i = 0; i < n; i++)
    {
        Vector3 left0 = data.GetPoint($"LI_L_{i}");
        Vector3 right0 = data.GetPoint($"LI_R_{i}");
        Vector3 right1 = data.GetPoint($"LI_R_{i + 1}");
        Vector3 left1 = data.GetPoint($"LI_L_{i + 1}");

        data.AddFace(
            $"LidCurve_{i}",
            new List<Vector3>
            {
                left0,
                right0,
                right1,
                left1
            },
            1
        );
    }
    AddLidLeftSideFace(data, p);
    AddLidRightSideFace(data, p);
}

// 生成盖子左侧半椭圆面
private void AddLidLeftSideFace(ChestGeometryData data, ChestLatentParams p)
{
    int n = p.lidSegments;
    List<Vector3> vertices = new List<Vector3>();

    // 沿曲线从前到后收集左侧曲线点
    for (int i = 0; i <= n; i++)
    {
        vertices.Add(data.GetPoint($"LI_L_{i}"));
    }

    // 半椭圆底边会由 ClosePath 自动闭合
    data.AddFace(
        "Lid_left_side",
        vertices,
        1
    );
}

// 生成盖子右侧半椭圆面
private void AddLidRightSideFace(ChestGeometryData data, ChestLatentParams p)
{
    int n = p.lidSegments;
    List<Vector3> vertices = new List<Vector3>();

    // 沿曲线从后到前收集右侧曲线点，保证面片顶点顺序更稳定
    for (int i = n; i >= 0; i--)
    {
        vertices.Add(data.GetPoint($"LI_R_{i}"));
    }

    // 半椭圆底边会由 ClosePath 自动闭合
    data.AddFace(
        "Lid_right_side",
        vertices,
        2
    );
}

    private void AddBodyFaces(ChestGeometryData data)
    {
        // Faces reuse named points; the final number is used by the renderer.
        data.AddFace(
            "Body_Top",
            new List<string>
            {
                "BD_TL",
                "BD_TR",
                "BD_TR_1",
                "BD_TL_1"
            },
            1
        );

        data.AddFace(
            "Body_front",
            new List<string>
            {
                "BD_TL",
                "BD_TR",
                "BD_BR",
                "BD_BL"
            },
            2
        );

        data.AddFace(
            "Body_right",
            new List<string>
            {
                "BD_TR",
                "BD_TR_1",
                "BD_BR_1",
                "BD_BR"
            },
            1
        );

        data.AddFace(
            "Body_left",
            new List<string>
            {   "BD_BL",
                "BD_BL_1",
                "BD_TL_1",
                "BD_TL"
            },
            1
        );

        data.AddFace(
            "Body_bottom",
            new List<string>
            {
                "BD_BL",
                "BD_BL_1",
                "BD_BR_1",
                "BD_BR"
            },
            0
        );

    }
}
