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

        AddLockerPoints(data, p);
        AddLockerFace(data);

        AddOutlineEdges(data, p);

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

    // 生成锁扣矩形点，放在箱体正面中心
    private void AddLockerPoints(ChestGeometryData data, ChestLatentParams p)
    {
        float lw = p.lockerWidth;
        float lh = p.lockerHeight;

        // 锁扣中心位置：x 居中，y 在箱体正面上半部分，z 稍微朝前
        float centerX = 0f;
        float centerY = 0f;
        float centerZ = p.lockerAnchorDepth;

        data.AddPoint("LC_TL", new Vector3(centerX - lw / 2f, centerY + lh / 2f, centerZ));
        data.AddPoint("LC_TR", new Vector3(centerX + lw / 2f, centerY + lh / 2f, centerZ));
        data.AddPoint("LC_BR", new Vector3(centerX + lw / 2f, centerY - lh / 2f, centerZ));
        data.AddPoint("LC_BL", new Vector3(centerX - lw / 2f, centerY - lh / 2f, centerZ));
    }

    // 统一添加可见轮廓线，避免每个 face 自己描边
    private void AddOutlineEdges(ChestGeometryData data, ChestLatentParams p)
    {
        AddBodyOutlineEdges(data);
        AddLidOutlineEdges(data, p);
        AddLockerOutlineEdges(data);
    }

    // 箱体外轮廓线
    private void AddBodyOutlineEdges(ChestGeometryData data)
    {
        // 正面轮廓
        data.AddOutlineEdge(
            "Body_front_outline",
            new List<Vector3>
            {
            data.GetPoint("BD_TL"),
            data.GetPoint("BD_TR"),
            data.GetPoint("BD_BR"),
            data.GetPoint("BD_BL"),
            data.GetPoint("BD_TL")
            },
            10
        );

        // 右侧面轮廓
        data.AddOutlineEdge(
            "Body_right_outline",
            new List<Vector3>
            {
            data.GetPoint("BD_TR"),
            data.GetPoint("BD_TR_1"),
            data.GetPoint("BD_BR_1"),
            data.GetPoint("BD_BR"),
            data.GetPoint("BD_TR")
            },
            10
        );
    }

    // 盖子外轮廓线：只画外边，不画 LidCurve_i 之间的内部线
    private void AddLidOutlineEdges(ChestGeometryData data, ChestLatentParams p)
    {
        int n = p.lidSegments;

        // 左侧半椭圆弧线
        List<Vector3> leftCurve = new List<Vector3>();
        for (int i = 0; i <= n; i++)
        {
            leftCurve.Add(data.GetPoint($"LI_L_{i}"));
        }

        data.AddOutlineEdge("Lid_left_curve_outline", leftCurve, 11);

        // 右侧半椭圆弧线
        List<Vector3> rightCurve = new List<Vector3>();
        for (int i = 0; i <= n; i++)
        {
            rightCurve.Add(data.GetPoint($"LI_R_{i}"));
        }

        data.AddOutlineEdge("Lid_right_curve_outline", rightCurve, 11);

        // 前底边
        data.AddOutlineEdge(
            "Lid_front_bottom_outline",
            new List<Vector3>
            {
            data.GetPoint("LI_L_0"),
            data.GetPoint("LI_R_0")
            },
            11
        );

        // 后底边
        data.AddOutlineEdge(
            "Lid_back_bottom_outline",
            new List<Vector3>
            {
            data.GetPoint($"LI_L_{n}"),
            data.GetPoint($"LI_R_{n}")
            },
            11
        );

        // 盖子最高处的可见顶线
        int mid = n / 2;

        data.AddOutlineEdge(
            "Lid_top_ridge_outline",
            new List<Vector3>
            {
            data.GetPoint($"LI_L_{mid}"),
            data.GetPoint($"LI_R_{mid}")
            },
            12
        );
    }

    // 锁扣外轮廓线
    private void AddLockerOutlineEdges(ChestGeometryData data)
    {
        data.AddOutlineEdge(
            "Locker_outline",
            new List<Vector3>
            {
            data.GetPoint("LC_TL"),
            data.GetPoint("LC_TR"),
            data.GetPoint("LC_BR"),
            data.GetPoint("LC_BL"),
            data.GetPoint("LC_TL")
            },
            20
        );
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
            0
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

    // 生成锁扣面，renderOrder 设高一点，保证在最上层
    private void AddLockerFace(ChestGeometryData data)
    {
        data.AddFace(
            "Locker",
            new List<Vector3>
            {
                data.GetPoint("LC_TL"),
                data.GetPoint("LC_TR"),
                data.GetPoint("LC_BR"),
                data.GetPoint("LC_BL")
             },
            10
        );
    }
}
