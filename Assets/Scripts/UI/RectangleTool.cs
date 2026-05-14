using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class UIToolkitRectangleCreator : MonoBehaviour
{
    // UI 元素配置
    [Header("Element Names In UXML")]
    [SerializeField] private string rectangleButtonName = "Btn_rectangle"; // 矩形创建按钮的名称
    [SerializeField] private string drawingAreaName = "DrawingArea"; // 绘制区域的名称

    [Header("Rectangle Settings")]
    [SerializeField] private Vector2 initialSize = new Vector2(220f, 160f); // 矩形的初始大小
    [SerializeField] private float borderThickness = 3f; // 矩形边框的厚度
    [SerializeField] private Color borderColor = Color.black; // 矩形边框的颜色

    [Header("Resize Settings")]
    [SerializeField] private float handleSize = 12f; // 调整大小的拖拽手柄的大小
    [SerializeField] private float minWidth = 40f; // 矩形的最小宽度
    [SerializeField] private float minHeight = 40f; // 矩形的最小高度

    [SerializeField] private bool showDebugHandles = true; // 是否显示拖拽手柄的调试颜色

    private Button btnRectangle; // 矩形创建按钮
    private VisualElement drawingArea; // 绘制区域
    private VisualElement currentRectangle; // 当前创建的矩形

    private float rectLeft; // 矩形的左边位置
    private float rectTop; // 矩形的顶部位置
    private float rectWidth; // 矩形的宽度
    private float rectHeight; // 矩形的高度

    private bool isResizing = false; // 是否正在调整矩形大小
    private ResizeEdge activeEdge; // 当前正在调整的边
    private VisualElement activeHandle; // 当前活动的拖拽手柄
    private int activePointerId = -1; // 当前活动的指针 ID

    private Vector2 startMousePosition; // 开始拖拽时鼠标的位置
    private float startLeft; // 开始拖拽时矩形的左边位置
    private float startTop; // 开始拖拽时矩形的顶部位置
    private float startWidth; // 开始拖拽时矩形的宽度
    private float startHeight; // 开始拖拽时矩形的高度

    private enum ResizeEdge
    {
        Left,   // 左边
        Right,  // 右边
        Top,    // 上边
        Bottom  // 下边
    }

    /// <summary>
    /// 在脚本启用时初始化 UI 元素和事件。
    /// </summary>
    private void OnEnable()
    {
        UIDocument uiDocument = GetComponent<UIDocument>();
        VisualElement root = uiDocument.rootVisualElement;

        btnRectangle = root.Q<Button>(rectangleButtonName);
        drawingArea = root.Q<VisualElement>(drawingAreaName);

        if (btnRectangle == null)
        {
            Debug.LogError($"找不到按钮：{rectangleButtonName}。请检查 UI Builder 里的 Name。");
            return;
        }

        if (drawingArea == null)
        {
            Debug.LogError($"找不到画布区域：{drawingAreaName}。请检查 UI Builder 里的 Name。");
            return;
        }

        drawingArea.style.position = Position.Relative;
        drawingArea.style.overflow = Overflow.Hidden;

        btnRectangle.clicked += CreateRectangleOutline; // 绑定按钮点击事件
    }

    /// <summary>
    /// 在脚本禁用时移除事件绑定。
    /// </summary>
    private void OnDisable()
    {
        if (btnRectangle != null)
        {
            btnRectangle.clicked -= CreateRectangleOutline;
        }
    }

    /// <summary>
    /// 创建矩形轮廓并添加到绘制区域。
    /// </summary>
    private void CreateRectangleOutline()
    {
        // 如果已有矩形，先移除
        if (currentRectangle != null)
        {
            currentRectangle.RemoveFromHierarchy();
        }

        // 计算矩形的初始位置和大小
        float areaWidth = drawingArea.resolvedStyle.width;
        float areaHeight = drawingArea.resolvedStyle.height;

        rectWidth = initialSize.x;
        rectHeight = initialSize.y;
        rectLeft = Mathf.Max(0f, (areaWidth - rectWidth) / 2f);
        rectTop = Mathf.Max(0f, (areaHeight - rectHeight) / 2f);

        // 创建矩形元素
        currentRectangle = new VisualElement();
        currentRectangle.name = "RectangleOutline";
        currentRectangle.style.position = Position.Absolute;
        currentRectangle.style.backgroundColor = Color.clear;

        // 设置矩形边框样式
        currentRectangle.style.borderTopWidth = borderThickness;
        currentRectangle.style.borderBottomWidth = borderThickness;
        currentRectangle.style.borderLeftWidth = borderThickness;
        currentRectangle.style.borderRightWidth = borderThickness;

        currentRectangle.style.borderTopColor = borderColor;
        currentRectangle.style.borderBottomColor = borderColor;
        currentRectangle.style.borderLeftColor = borderColor;
        currentRectangle.style.borderRightColor = borderColor;

        ApplyRectangleStyle();

        // 添加拖拽手柄
        currentRectangle.Add(CreateResizeHandle("Handle_Top", ResizeEdge.Top));
        currentRectangle.Add(CreateResizeHandle("Handle_Bottom", ResizeEdge.Bottom));
        currentRectangle.Add(CreateResizeHandle("Handle_Left", ResizeEdge.Left));
        currentRectangle.Add(CreateResizeHandle("Handle_Right", ResizeEdge.Right));

        // 将矩形添加到绘制区域
        drawingArea.Add(currentRectangle);
    }

    /// <summary>
    /// 应用矩形的样式（位置和大小）。
    /// </summary>
    private void ApplyRectangleStyle()
    {
        if (currentRectangle == null)
        {
            return;
        }

        currentRectangle.style.left = rectLeft;
        currentRectangle.style.top = rectTop;
        currentRectangle.style.width = rectWidth;
        currentRectangle.style.height = rectHeight;
    }

    /// <summary>
    /// 创建拖拽手柄，用于调整矩形大小。
    /// </summary>
    private VisualElement CreateResizeHandle(string handleName, ResizeEdge edge)
    {
        VisualElement handle = new VisualElement();
        handle.name = handleName;
        handle.style.position = Position.Absolute;
        handle.pickingMode = PickingMode.Position;

        // 设置手柄的颜色（调试模式下显示颜色，否则透明）
        Color handleColor = showDebugHandles
            ? new Color(0f, 0.5f, 1f, 0.18f)
            : Color.clear;

        handle.style.backgroundColor = handleColor;

        // 根据边缘设置手柄的位置和大小
        switch (edge)
        {
            case ResizeEdge.Top:
                handle.style.left = 0;
                handle.style.right = 0;
                handle.style.top = 0;
                handle.style.height = handleSize;
                break;

            case ResizeEdge.Bottom:
                handle.style.left = 0;
                handle.style.right = 0;
                handle.style.bottom = 0;
                handle.style.height = handleSize;
                break;

            case ResizeEdge.Left:
                handle.style.left = 0;
                handle.style.top = 0;
                handle.style.bottom = 0;
                handle.style.width = handleSize;
                break;

            case ResizeEdge.Right:
                handle.style.right = 0;
                handle.style.top = 0;
                handle.style.bottom = 0;
                handle.style.width = handleSize;
                break;
        }

        // 注册事件回调，用于处理拖拽操作
        handle.RegisterCallback<PointerDownEvent>(evt => BeginResize(evt, edge, handle));
        handle.RegisterCallback<PointerMoveEvent>(UpdateResize);
        handle.RegisterCallback<PointerUpEvent>(EndResize);

        return handle;
    }

    /// <summary>
    /// 开始调整矩形大小。
    /// </summary>
    private void BeginResize(PointerDownEvent evt, ResizeEdge edge, VisualElement handle)
    {
        if (currentRectangle == null)
        {
            return;
        }

        isResizing = true;
        activeEdge = edge;
        activeHandle = handle;
        activePointerId = evt.pointerId;

        startMousePosition = GetMousePositionInDrawingArea(evt.position);

        startLeft = rectLeft;
        startTop = rectTop;
        startWidth = rectWidth;
        startHeight = rectHeight;

        handle.CapturePointer(evt.pointerId);

        evt.StopPropagation();
    }

    /// <summary>
    /// 更新矩形大小。
    /// </summary>
    private void UpdateResize(PointerMoveEvent evt)
    {
        if (!isResizing || activeHandle == null || evt.pointerId != activePointerId || !activeHandle.HasPointerCapture(activePointerId))
        {
            return;
        }

        Vector2 currentMousePosition = GetMousePositionInDrawingArea(evt.position);
        Vector2 delta = currentMousePosition - startMousePosition;

        float areaWidth = drawingArea.resolvedStyle.width;
        float areaHeight = drawingArea.resolvedStyle.height;

        float newLeft = startLeft;
        float newTop = startTop;
        float newWidth = startWidth;
        float newHeight = startHeight;

        // 根据拖拽的边缘调整矩形大小
        switch (activeEdge)
        {
            case ResizeEdge.Right:
                newWidth = Mathf.Clamp(startWidth + delta.x, minWidth, Mathf.Max(minWidth, areaWidth - startLeft));
                break;

            case ResizeEdge.Bottom:
                newHeight = Mathf.Clamp(startHeight + delta.y, minHeight, Mathf.Max(minHeight, areaHeight - startTop));
                break;

            case ResizeEdge.Left:
                float right = startLeft + startWidth;
                newLeft = Mathf.Clamp(startLeft + delta.x, 0f, right - minWidth);
                newWidth = right - newLeft;
                break;

            case ResizeEdge.Top:
                float bottom = startTop + startHeight;
                newTop = Mathf.Clamp(startTop + delta.y, 0f, bottom - minHeight);
                newHeight = bottom - newTop;
                break;
        }

        rectLeft = newLeft;
        rectTop = newTop;
        rectWidth = newWidth;
        rectHeight = newHeight;

        ApplyRectangleStyle();

        evt.StopPropagation();
    }

    /// <summary>
    /// 结束调整矩形大小。
    /// </summary>
    private void EndResize(PointerUpEvent evt)
    {
        if (!isResizing || evt.pointerId != activePointerId)
        {
            return;
        }

        if (activeHandle != null && activeHandle.HasPointerCapture(activePointerId))
        {
            activeHandle.ReleasePointer(activePointerId);
        }

        isResizing = false;
        activeHandle = null;
        activePointerId = -1;

        evt.StopPropagation();
    }

    /// <summary>
    /// 获取鼠标在绘制区域中的位置。
    /// </summary>
    private Vector2 GetMousePositionInDrawingArea(Vector3 pointerPosition)
    {
        Vector2 panelPosition = new Vector2(pointerPosition.x, pointerPosition.y);
        return drawingArea.WorldToLocal(panelPosition);
    }
}