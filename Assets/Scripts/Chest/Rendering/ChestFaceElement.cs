using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class ChestFaceElement : VisualElement
{
    // Polygon points already projected into 2D UI space.
    private List<Vector2> vertices2D;

    // Colors used to draw the face surface and outline.
    private Color fillColor;
    private Color strokeColor;

    // Creates one drawable chest face from 2D vertices and colors.
    public ChestFaceElement(List<Vector2> vertices2D, Color fillColor, Color strokeColor)
    {
        this.vertices2D = vertices2D;
        this.fillColor = fillColor;
        this.strokeColor = strokeColor;

        // UI Toolkit calls this when the element needs to be redrawn.
        generateVisualContent += OnGenerateVisualContent;
    }


    private void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        // A filled face needs at least three points.
        if (vertices2D == null || vertices2D.Count < 3)
            return;

        Painter2D painter = ctx.painter2D;

        // Set draw style before tracing the polygon.
        painter.fillColor = fillColor;
        painter.strokeColor = strokeColor;
        painter.lineWidth = 2f;

        // Trace the face boundary from the first point through all remaining points.
        painter.BeginPath();
        painter.MoveTo(vertices2D[0]);

        for (int i = 1; i < vertices2D.Count; i++)
        {
            painter.LineTo(vertices2D[i]);
        }

        // Close, fill, then outline the polygon.
        painter.ClosePath();
        painter.Fill();
        painter.Stroke();
    }
}
