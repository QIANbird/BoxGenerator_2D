using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;


public class ChestRenderer2D : MonoBehaviour
{
    [Header("UI References")]
    // UI document that contains the drawing canvas.
    [SerializeField] private UIDocument uiDocument;

    // Name of the VisualElement used as the drawing target.
    [SerializeField] private string canvasElementName = "drawingCanvas";

    [Header("Render Settings")]
    // Moves projected points into a visible area of the canvas.
    [SerializeField] private Vector2 canvasOffset = new Vector2(500f, 350f);
    [SerializeField] private Color outlineColor = Color.black;
    [SerializeField] private float outlineWidth = 2f;

    [Header("Debug")]
    // Logs face names and render order before drawing.
    [SerializeField] private bool logFaceData = true;

    private VisualElement canvasRoot;

    private void Awake()
    {
        // Use the UIDocument on this GameObject if none was assigned.
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        // Find the canvas element in the UI tree.
        canvasRoot = uiDocument.rootVisualElement.Q<VisualElement>(canvasElementName);

        if (canvasRoot == null)
        {
            Debug.LogError($"Canvas element not found: {canvasElementName}");
        }
    }

    public void Render(ChestGeometryData geometryData)
    {
        // Nothing to draw if the canvas or geometry data is missing.
        if (canvasRoot == null || geometryData == null)
            return;

        // Remove the previous chest before drawing the new one.
        canvasRoot.Clear();

        DrawFilledFaces(geometryData);
        DrawOutlineEdges(geometryData);
    }

    private void DrawFilledFaces(ChestGeometryData geometryData)
    {
        List<ChestFaceData> sortedFaces = geometryData.faces
            .OrderBy(face => face.renderOrder)
            .ToList();

        foreach (ChestFaceData face in sortedFaces)
        {
            DrawFace(face);
        }
    }
    private void DrawOutlineEdges(ChestGeometryData geometryData)
    {
        List<ChestEdgeData> sortedEdges = geometryData.outlineEdges
            .OrderBy(edge => edge.renderOrder)
            .ToList();

        foreach (ChestEdgeData edge in sortedEdges)
        {
            DrawEdge(edge);
        }
    }

  

    private void DrawFace(ChestFaceData face)
    {
        // Convert each 3D face vertex into 2D canvas space.
        List<Vector2> vertices2D = new List<Vector2>();

        foreach (Vector3 point3D in face.vertices3D)
        {
            Vector2 point2D = IsoProjector.ProjectWithOffset(point3D, canvasOffset);
            vertices2D.Add(point2D);
        }

        Color fillColor = GetFaceFillColor(face.faceName);

        // Create the UI element that draws the polygon face.
        ChestFaceElement faceElement = new ChestFaceElement(vertices2D, fillColor);

        canvasRoot.Add(faceElement);
    }

    /// 绘制单条轮廓线：只 Stroke，不 Fill。
    private void DrawEdge(ChestEdgeData edge)
    {
        List<Vector2> points2D = new List<Vector2>();

        foreach (Vector3 point3D in edge.points3D)
        {
            Vector2 point2D = IsoProjector.ProjectWithOffset(point3D, canvasOffset);
            points2D.Add(point2D);
        }

        ChestEdgeElement edgeElement = new ChestEdgeElement(points2D, outlineColor, outlineWidth);
        SetupFullCanvasElement(edgeElement);

        canvasRoot.Add(edgeElement);
    }

    /// 让绘制元素覆盖整个画布区域，实际图形位置由顶点坐标决定。
    private void SetupFullCanvasElement(VisualElement element)
    {
        element.style.position = Position.Absolute;
        element.style.left = 0;
        element.style.top = 0;
        element.style.width = Length.Percent(100);
        element.style.height = Length.Percent(100);
    }

    
    private Color GetFaceFillColor(string faceName)
    {
        // Slightly different colors make each visible side easier to read.
        switch (faceName)
        {
            case "Body_Top":
                return Hex("#edafb8");//深粉色

            case "Body_front":
                return Hex("#f7e1d7");//淡粉色

            case "Body_right":
                return Hex("#dedbd2");//灰色

            case "Body_left":
                return Hex("#b0c4b1");//浅绿色

            case "Body_bottom":
                return Hex("#4a5759");//深绿色
            case "Lid_left_side":
                return Hex("#83c5be");//薄荷色
            case "Lid_right_side":
                return Hex("#006d77");//深薄荷色
            case "Locker":
                return Hex("#ffe6a7");//金色
            default:
                return new Color(0.65f, 0.40f, 0.22f, 1f);
        }
    }
    private Color Hex(string hex)
{
    ColorUtility.TryParseHtmlString(hex, out var color);
    return color;
}
}
