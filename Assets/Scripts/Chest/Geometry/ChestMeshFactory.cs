using System.Collections.Generic;
using UnityEngine;

// ChestMeshFactory 是纯几何工厂：
// 只根据 ChestLatentParams 计算顶点、三角面和 UV，并返回 Unity Mesh。
// 它不创建 GameObject，不管理材质，不读取 UI，也不持有场景状态。
//
// 当前坐标约定：
// x = 宽度方向，中心为 0，左负右正。
// y = 高度方向，箱体顶部为 0，向上为正，箱体向下延伸为负。
// z = 深度方向，中心为 0，正面为负 z，背面为正 z。
public static class ChestMeshFactory
{
    // 生成箱体主体 body_face 的低模外壳。
    // 现在先生成一个带 taper 的外表面体块；bodyThickness 暂时保留给后续内壁、包边和开口结构使用。
    public static Mesh CreateBodyMesh(ChestLatentParams sourceParams)
    {
        ChestLatentParams p = GetValidParams(sourceParams);

        float halfWidth = p.width * 0.5f;
        float halfDepth = p.depth * 0.5f;

        // taper 让底部宽度和深度略微收窄，形成低模宝箱的斜侧面。
        float bottomHalfWidth = Mathf.Max(1f, (p.width - p.taper) * 0.5f);
        float frontBottomZ = -halfDepth + p.taper;
        float backBottomZ = halfDepth - p.taper;

        // 顶部四角位于 y = 0，也就是箱盖和箱体的连接平面。
        Vector3 topFrontLeft = new Vector3(-halfWidth, 0f, -halfDepth);
        Vector3 topFrontRight = new Vector3(halfWidth, 0f, -halfDepth);
        Vector3 topBackRight = new Vector3(halfWidth, 0f, halfDepth);
        Vector3 topBackLeft = new Vector3(-halfWidth, 0f, halfDepth);

        // 底部四角位于 y = -height，并受 taper 收窄。
        Vector3 bottomFrontLeft = new Vector3(-bottomHalfWidth, -p.height, frontBottomZ);
        Vector3 bottomFrontRight = new Vector3(bottomHalfWidth, -p.height, frontBottomZ);
        Vector3 bottomBackRight = new Vector3(bottomHalfWidth, -p.height, backBottomZ);
        Vector3 bottomBackLeft = new Vector3(-bottomHalfWidth, -p.height, backBottomZ);

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        // 每个面单独写入四个顶点，方便 Unity 为低模风格计算硬边法线。
        AddQuad(vertices, triangles, uvs, topFrontLeft, topFrontRight, bottomFrontRight, bottomFrontLeft);
        AddQuad(vertices, triangles, uvs, topFrontRight, topBackRight, bottomBackRight, bottomFrontRight);
        AddQuad(vertices, triangles, uvs, topBackRight, topBackLeft, bottomBackLeft, bottomBackRight);
        AddQuad(vertices, triangles, uvs, topBackLeft, topFrontLeft, bottomFrontLeft, bottomBackLeft);
        AddQuad(vertices, triangles, uvs, bottomFrontLeft, bottomFrontRight, bottomBackRight, bottomBackLeft);

        return BuildMesh("Chest_Body_Mesh", vertices, triangles, uvs);
    }

    // 生成箱盖 lid_face 的拱形低模网格。
    // 外弧决定可见盖子轮廓；内弧用 lidThickness 向内收缩，形成有厚度的壳体。
    // 左右端盖使用半圆扇形面封住，避免从侧面看到空洞。
    public static Mesh CreateLidMesh(ChestLatentParams sourceParams)
    {
        ChestLatentParams p = GetValidParams(sourceParams);

        float halfWidth = p.width * 0.5f;
        float outerRadiusZ = p.depth * 0.5f;
        float innerRadiusZ = Mathf.Max(1f, outerRadiusZ - p.lidThickness);
        float innerHeight = Mathf.Max(1f, p.lidHeight - p.lidThickness);
        int segmentCount = Mathf.Max(3, p.lidSegments);

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        Vector3[] leftOuter = new Vector3[segmentCount + 1];
        Vector3[] rightOuter = new Vector3[segmentCount + 1];
        Vector3[] leftInner = new Vector3[segmentCount + 1];
        Vector3[] rightInner = new Vector3[segmentCount + 1];

        // 采样半椭圆弧线：theta 从 0 到 PI。
        // z 使用 cos 控制前后位置，y 使用 sin 控制拱顶高度。
        for (int i = 0; i <= segmentCount; i++)
        {
            float theta = Mathf.PI * i / segmentCount;
            float sin = Mathf.Sin(theta);
            float cos = Mathf.Cos(theta);

            leftOuter[i] = new Vector3(-halfWidth, p.lidHeight * sin, -outerRadiusZ * cos);
            rightOuter[i] = new Vector3(halfWidth, p.lidHeight * sin, -outerRadiusZ * cos);
            leftInner[i] = new Vector3(-halfWidth, innerHeight * sin, -innerRadiusZ * cos);
            rightInner[i] = new Vector3(halfWidth, innerHeight * sin, -innerRadiusZ * cos);
        }

        for (int i = 0; i < segmentCount; i++)
        {
            // 外弧面：玩家主要看到的盖子曲面。
            AddQuad(vertices, triangles, uvs, leftOuter[i], leftOuter[i + 1], rightOuter[i + 1], rightOuter[i]);

            // 内弧面：让盖子看起来不是一张零厚度面片。
            AddQuad(vertices, triangles, uvs, rightInner[i], rightInner[i + 1], leftInner[i + 1], leftInner[i]);
        }

        // 左右半圆端盖，对应参考图里侧面的整块色面。
        AddLidSideCap(vertices, triangles, uvs, leftOuter, -halfWidth, false);
        AddLidSideCap(vertices, triangles, uvs, rightOuter, halfWidth, true);

        // 前下沿和后下沿的厚度面，把外弧和内弧连接起来。
        AddQuad(vertices, triangles, uvs, leftOuter[0], rightOuter[0], rightInner[0], leftInner[0]);
        AddQuad(vertices, triangles, uvs, rightOuter[segmentCount], leftOuter[segmentCount], leftInner[segmentCount], rightInner[segmentCount]);

        return BuildMesh("Chest_Lid_Mesh", vertices, triangles, uvs);
    }

    // 生成锁扣 locker 的低模薄立方体。
    // 锚点位于箱盖前下沿正中，锁扣从锚点向下延伸，并向正面外侧凸出 lockerDepth。
    public static Mesh CreateLockerMesh(ChestLatentParams sourceParams)
    {
        ChestLatentParams p = GetValidParams(sourceParams);

        float halfWidth = p.lockerWidth * 0.5f;
        float frontSurfaceZ = -p.depth * 0.5f + p.lockerAnchorDepth;
        float frontZ = frontSurfaceZ - p.lockerDepth;
        float backZ = frontSurfaceZ;
        float topY = 0f;
        float bottomY = -p.lockerHeight;

        Vector3 frontTopLeft = new Vector3(-halfWidth, topY, frontZ);
        Vector3 frontTopRight = new Vector3(halfWidth, topY, frontZ);
        Vector3 frontBottomRight = new Vector3(halfWidth, bottomY, frontZ);
        Vector3 frontBottomLeft = new Vector3(-halfWidth, bottomY, frontZ);

        Vector3 backTopLeft = new Vector3(-halfWidth, topY, backZ);
        Vector3 backTopRight = new Vector3(halfWidth, topY, backZ);
        Vector3 backBottomRight = new Vector3(halfWidth, bottomY, backZ);
        Vector3 backBottomLeft = new Vector3(-halfWidth, bottomY, backZ);

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        AddQuad(vertices, triangles, uvs, frontTopLeft, frontTopRight, frontBottomRight, frontBottomLeft);
        AddQuad(vertices, triangles, uvs, backTopRight, backTopLeft, backBottomLeft, backBottomRight);
        AddQuad(vertices, triangles, uvs, backTopLeft, frontTopLeft, frontBottomLeft, backBottomLeft);
        AddQuad(vertices, triangles, uvs, frontTopRight, backTopRight, backBottomRight, frontBottomRight);
        AddQuad(vertices, triangles, uvs, backTopLeft, backTopRight, frontTopRight, frontTopLeft);
        AddQuad(vertices, triangles, uvs, frontBottomLeft, frontBottomRight, backBottomRight, backBottomLeft);

        return BuildMesh("Chest_Locker_Mesh", vertices, triangles, uvs);
    }

    // 生成前统一复制并钳制参数，避免 MeshFactory 修改外部正在编辑的参数对象。
    private static ChestLatentParams GetValidParams(ChestLatentParams sourceParams)
    {
        ChestLatentParams p = sourceParams != null ? sourceParams.Clone() : new ChestLatentParams();
        p.ClampValues();
        return p;
    }

    // 为箱盖左右两端生成半圆扇形端盖。
    // facePositiveX 控制三角形绕序，保证左右端盖的法线分别朝外。
    private static void AddLidSideCap(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs,
        Vector3[] outerArc,
        float x,
        bool facePositiveX)
    {
        float maxAbsZ = 1f;
        float maxY = 1f;

        for (int i = 0; i < outerArc.Length; i++)
        {
            maxAbsZ = Mathf.Max(maxAbsZ, Mathf.Abs(outerArc[i].z));
            maxY = Mathf.Max(maxY, outerArc[i].y);
        }

        int centerIndex = vertices.Count;
        vertices.Add(new Vector3(x, 0f, 0f));
        uvs.Add(new Vector2(0.5f, 0f));

        for (int i = 0; i < outerArc.Length - 1; i++)
        {
            int a = vertices.Count;
            vertices.Add(outerArc[i]);
            uvs.Add(new Vector2(0.5f + outerArc[i].z / (maxAbsZ * 2f), outerArc[i].y / maxY));

            int b = vertices.Count;
            vertices.Add(outerArc[i + 1]);
            uvs.Add(new Vector2(0.5f + outerArc[i + 1].z / (maxAbsZ * 2f), outerArc[i + 1].y / maxY));

            if (facePositiveX)
            {
                triangles.Add(centerIndex);
                triangles.Add(a);
                triangles.Add(b);
            }
            else
            {
                triangles.Add(centerIndex);
                triangles.Add(b);
                triangles.Add(a);
            }
        }
    }

    // 写入一个四边形面。
    // Unity Mesh 的 triangle index 只能表示三角形，所以每个四边形拆成两个三角形。
    private static void AddQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d)
    {
        int start = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(0f, 1f));

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    // 把收集好的顶点数据转换成 Unity Mesh，并刷新法线和包围盒。
    private static Mesh BuildMesh(string meshName, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
    {
        Mesh mesh = new Mesh
        {
            name = meshName
        };

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}
