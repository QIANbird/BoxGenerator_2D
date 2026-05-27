using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ChestGeometryData
{
    public Dictionary<string, Vector3> points = new Dictionary<string, Vector3>();

   // 用于填充颜色的面
    public List<ChestFaceData> faces = new List<ChestFaceData>();
    // 用于单独绘制的可见轮廓线
    public List<ChestEdgeData> outlineEdges = new List<ChestEdgeData>();


    public void Clear()
    {
        points.Clear(); 
        faces.Clear(); 
        outlineEdges.Clear();
    }

    public void AddPoint(string pointName, Vector3 position)
    {
        points[pointName] = position; 
    }


    public Vector3 GetPoint(string pointName)
    {
        if (!points.TryGetValue(pointName, out Vector3 point))
        {
                        Debug.LogWarning($"Point not found: {pointName}"); 
            return Vector3.zero; 
        }

        return point; 
    }


    public void AddFace(string faceName, List<Vector3> vertices3D, int renderOrder)
    {
        faces.Add(new ChestFaceData(faceName, vertices3D, renderOrder)); 
    }

    public void AddFace(string faceName, List<string> vertexNames, int renderOrder)
    {
        List<Vector3> vertices3D = new List<Vector3>();

        foreach (string vertexName in vertexNames)
        {
            vertices3D.Add(GetPoint(vertexName));
        }

        faces.Add(new ChestFaceData(faceName, vertices3D, renderOrder, vertexNames));
    }
     // 添加轮廓线：可以是直线，也可以是多段折线
    public void AddOutlineEdge(string edgeName, List<Vector3> points3D, int renderOrder)
    {
        outlineEdges.Add(new ChestEdgeData(edgeName, points3D, renderOrder));
    }
}


[Serializable]
public class ChestFaceData
{
    public string faceName; 
    public List<Vector3> vertices3D; 
    public int renderOrder; 
    public List<string> vertexNames;


    public ChestFaceData(string faceName, List<Vector3> vertices3D, int renderOrder)
    {
        this.faceName = faceName;
        this.vertices3D = vertices3D;
        this.renderOrder = renderOrder;
        this.vertexNames = new List<string>();
    }

    public ChestFaceData(string faceName, List<Vector3> vertices3D, int renderOrder, List<string> vertexNames)
    {
        this.faceName = faceName;
        this.vertices3D = vertices3D;
        this.renderOrder = renderOrder;
        this.vertexNames = vertexNames;
    }
}

[Serializable]
public class ChestEdgeData
{
    public string edgeName;

    // 两个点表示直线，多个点表示折线/曲线轮廓
    public List<Vector3> points3D;

    public int renderOrder;

    public ChestEdgeData(string edgeName, List<Vector3> points3D, int renderOrder)
    {
        this.edgeName = edgeName;
        this.points3D = points3D;
        this.renderOrder = renderOrder;
    }
}