using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public class ShapeReferenceExporter : MonoBehaviour
{
    [Header("Reference Image Colors")]
    [SerializeField] private Color32 backgroundColor = new Color32(255, 255, 255, 255); // 背景颜色
    [SerializeField] private Color32 lineColor = new Color32(0, 0, 0, 255); // 矩形边框颜色

    /// <summary>
    /// 导出矩形参考图像
    /// </summary>
    /// <param name="drawingArea">绘制区域</param>
    /// <param name="rectangleElement">矩形元素</param>
    /// <param name="outputPath">输出文件路径</param>
    /// <param name="outputWidth">输出图像宽度</param>
    /// <param name="outputHeight">输出图像高度</param>
    /// <param name="lineThickness">边框厚度</param>
    /// <param name="errorMessage">错误信息</param>
    /// <returns>是否导出成功</returns>
    public bool ExportRectangleReference(
        VisualElement drawingArea,
        VisualElement rectangleElement,
        string outputPath,
        int outputWidth,
        int outputHeight,
        float lineThickness,
        out string errorMessage
    )
    {
        errorMessage = "";

        // 验证输入参数
        if (drawingArea == null)
        {
            errorMessage = "DrawingArea is null.";
            return false;
        }

        if (rectangleElement == null)
        {
            errorMessage = "RectangleElement is null.";
            return false;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            errorMessage = "Output path is empty.";
            return false;
        }

        if (outputWidth <= 0 || outputHeight <= 0)
        {
            errorMessage = "Output image size is invalid.";
            return false;
        }

        try
        {
            // 获取绘制区域和矩形的边界
            Rect areaRect = drawingArea.worldBound;
            Rect rectangleRect = rectangleElement.worldBound;

            if (areaRect.width <= 0 || areaRect.height <= 0)
            {
                errorMessage = "DrawingArea size is invalid. Make sure the UI has been rendered.";
                return false;
            }

            if (rectangleRect.width <= 0 || rectangleRect.height <= 0)
            {
                errorMessage = "Rectangle size is invalid.";
                return false;
            }

            // 计算矩形在绘制区域中的位置和大小
            float rectLeftInArea = rectangleRect.xMin - areaRect.xMin;
            float rectTopInArea = rectangleRect.yMin - areaRect.yMin;
            float rectWidthInArea = rectangleRect.width;
            float rectHeightInArea = rectangleRect.height;

            // 计算缩放比例
            float scaleX = outputWidth / areaRect.width;
            float scaleY = outputHeight / areaRect.height;

            // 转换为输出图像的像素坐标
            int rectLeft = Mathf.RoundToInt(rectLeftInArea * scaleX);
            int rectTop = Mathf.RoundToInt(rectTopInArea * scaleY);
            int rectWidth = Mathf.RoundToInt(rectWidthInArea * scaleX);
            int rectHeight = Mathf.RoundToInt(rectHeightInArea * scaleY);

            // 限制矩形边界
            rectLeft = Mathf.Clamp(rectLeft, 0, outputWidth - 1);
            rectTop = Mathf.Clamp(rectTop, 0, outputHeight - 1);
            rectWidth = Mathf.Clamp(rectWidth, 1, outputWidth - rectLeft);
            rectHeight = Mathf.Clamp(rectHeight, 1, outputHeight - rectTop);

            int thickness = Mathf.Max(1, Mathf.RoundToInt(lineThickness)); // 确保边框厚度至少为 1

            // 创建纹理
            Texture2D texture = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false);

            // 填充背景颜色
            FillBackground(texture, outputWidth, outputHeight, backgroundColor);

            // 绘制矩形边框
            DrawRectangleOutline(
                texture,
                outputWidth,
                outputHeight,
                rectLeft,
                rectTop,
                rectWidth,
                rectHeight,
                thickness,
                lineColor
            );

            texture.Apply(); // 应用更改到纹理

            // 将纹理编码为 PNG 并保存到文件
            byte[] pngBytes = texture.EncodeToPNG();

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(outputPath, pngBytes);

            Destroy(texture); // 销毁纹理以释放内存

            Debug.Log($"Reference image exported: {outputPath}");
            return true;
        }
        catch (Exception e)
        {
            errorMessage = e.ToString();
            return false;
        }
    }

    /// <summary>
    /// 填充纹理的背景颜色
    /// </summary>
    private void FillBackground(Texture2D texture, int width, int height, Color32 color)
    {
        Color32[] pixels = new Color32[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        texture.SetPixels32(pixels);
    }

    /// <summary>
    /// 绘制矩形边框
    /// </summary>
    private void DrawRectangleOutline(
        Texture2D texture,
        int textureWidth,
        int textureHeight,
        int left,
        int top,
        int rectWidth,
        int rectHeight,
        int thickness,
        Color32 color
    )
    {
        int right = left + rectWidth - 1;
        int bottom = top + rectHeight - 1;

        for (int t = 0; t < thickness; t++)
        {
            int topY = top + t;
            int bottomY = bottom - t;

            DrawHorizontalLine(texture, textureWidth, textureHeight, left, right, topY, color);
            DrawHorizontalLine(texture, textureWidth, textureHeight, left, right, bottomY, color);

            int leftX = left + t;
            int rightX = right - t;

            DrawVerticalLine(texture, textureWidth, textureHeight, leftX, top, bottom, color);
            DrawVerticalLine(texture, textureWidth, textureHeight, rightX, top, bottom, color);
        }
    }

    /// <summary>
    /// 绘制水平线
    /// </summary>
    private void DrawHorizontalLine(
        Texture2D texture,
        int textureWidth,
        int textureHeight,
        int xStart,
        int xEnd,
        int yFromTop,
        Color32 color
    )
    {
        for (int x = xStart; x <= xEnd; x++)
        {
            SetPixelTopLeftOrigin(texture, textureWidth, textureHeight, x, yFromTop, color);
        }
    }

    /// <summary>
    /// 绘制垂直线
    /// </summary>
    private void DrawVerticalLine(
        Texture2D texture,
        int textureWidth,
        int textureHeight,
        int x,
        int yStartFromTop,
        int yEndFromTop,
        Color32 color
    )
    {
        for (int y = yStartFromTop; y <= yEndFromTop; y++)
        {
            SetPixelTopLeftOrigin(texture, textureWidth, textureHeight, x, y, color);
        }
    }

    /// <summary>
    /// 设置像素颜色（以左上角为原点）
    /// </summary>
    private void SetPixelTopLeftOrigin(
        Texture2D texture,
        int textureWidth,
        int textureHeight,
        int x,
        int yFromTop,
        Color32 color
    )
    {
        if (x < 0 || x >= textureWidth)
        {
            return;
        }

        if (yFromTop < 0 || yFromTop >= textureHeight)
        {
            return;
        }

        int textureY = textureHeight - 1 - yFromTop;
        texture.SetPixel(x, textureY, color);
    }
}   