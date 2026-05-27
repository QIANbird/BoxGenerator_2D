using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// 单条轮廓线 UI 元素，只画线，不填充
public class ChestEdgeElement : VisualElement
{
    private readonly List<Vector2> points2D;
    private readonly Color strokeColor;
    private readonly float lineWidth;

    public ChestEdgeElement(List<Vector2> points2D, Color strokeColor, float lineWidth = 2f)
    {
        this.points2D = points2D;
        this.strokeColor = strokeColor;
        this.lineWidth = lineWidth;

        generateVisualContent += OnGenerateVisualContent;
    }

    // UI Toolkit 重绘时调用
    private void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        if (points2D == null || points2D.Count < 2)
            return;

        Painter2D painter = ctx.painter2D;

        painter.strokeColor = strokeColor;
        painter.lineWidth = lineWidth;

        painter.BeginPath();
        painter.MoveTo(points2D[0]);

        for (int i = 1; i < points2D.Count; i++)
        {
            painter.LineTo(points2D[i]);
        }

        painter.Stroke();
    }
}