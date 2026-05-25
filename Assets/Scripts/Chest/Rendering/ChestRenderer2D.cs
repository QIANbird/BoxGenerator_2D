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

        // Draw lower renderOrder faces first so later faces appear on top.
        List<ChestFaceData> sortedFaces = geometryData.faces
            .OrderBy(face => face.renderOrder)
            .ToList();

        if (logFaceData)
        {
            LogFaceData(sortedFaces);
        }

        foreach (ChestFaceData face in sortedFaces)
        {
            DrawFace(face);
        }
    }

    private void LogFaceData(List<ChestFaceData> sortedFaces)
    {
        Debug.Log($"ChestFaceData count: {sortedFaces.Count}");

        for (int i = 0; i < sortedFaces.Count; i++)
        {
            ChestFaceData face = sortedFaces[i];

            Debug.Log(FormatFaceDebugMessage(face, i));
        }
    }

    private string FormatFaceDebugMessage(ChestFaceData face, int drawIndex)
    {
        StringBuilder message = new StringBuilder();
        int vertexCount = face.vertices3D != null ? face.vertices3D.Count : 0;

        message.AppendLine(
            $"[{drawIndex}] name: {face.faceName}, renderOrder: {face.renderOrder}, vertices: {vertexCount}"
        );

        for (int i = 0; i < vertexCount; i++)
        {
            string vertexName = GetVertexDebugName(face, i);
            Vector3 vertex = face.vertices3D[i];

            message.AppendLine(
                $"    {i}: {vertexName} = ({vertex.x:F1}, {vertex.y:F1}, {vertex.z:F1})"
            );
        }

        return message.ToString();
    }

    private string GetVertexDebugName(ChestFaceData face, int vertexIndex)
    {
        if (face.vertexNames == null || vertexIndex >= face.vertexNames.Count)
        {
            return "Unnamed";
        }

        return face.vertexNames[vertexIndex];
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
        Color strokeColor = Color.black;

        // Create the UI element that draws the polygon face.
        ChestFaceElement faceElement = new ChestFaceElement(vertices2D, fillColor, strokeColor);

        // Stretch the custom drawing element over the whole canvas.
        faceElement.style.position = Position.Absolute;
        faceElement.style.left = 0;
        faceElement.style.top = 0;
        faceElement.style.width = Length.Percent(100);
        faceElement.style.height = Length.Percent(100);

        canvasRoot.Add(faceElement);
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
