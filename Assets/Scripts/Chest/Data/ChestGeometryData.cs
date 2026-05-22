using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ChestGeometryData
{
    public Dictionary<string, Vector3> points = new Dictionary<string, Vector3>();

    public List<ChestFaceData> faces = new List<ChestFaceData>();


    public void Clear()
    {
        points.Clear(); 
        faces.Clear(); 
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
