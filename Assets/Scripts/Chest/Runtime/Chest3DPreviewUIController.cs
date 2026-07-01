using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

// UI Toolkit 与 3D 宝箱预览系统之间的桥接控制器。
// 它负责：
// 1. 从 UIDocument 中查找按钮、DrawingArea 和右侧属性面板。
// 2. 响应 BOX / Basic Type / Editing / Preview the Texture 等 UI 操作。
// 3. 调用 Chest3DGenerator 生成或旋转 3D 宝箱。
// 4. 管理 RenderTexture，并把相机渲染结果显示到 DrawingArea 的背景图上。
//
// 注意：真正的宝箱 mesh 生成逻辑不在这里，而在 Chest3DGenerator / ChestMeshFactory 中。
// 本脚本只负责“UI 事件 -> 生成/渲染命令 -> DrawingArea 显示结果”的流程组织。
public class Chest3DPreviewUIController : MonoBehaviour
{
    [Header("UI References")]
    // 当前场景中的 UI Toolkit 文档。为空时会尝试从同一个 GameObject 上获取。
    [SerializeField] private UIDocument uiDocument;

    // 左侧 TypeToolbar 中整体 BOX 按钮的 name。点击后生成/刷新 3D 宝箱预览。
    [SerializeField] private string generateButtonName = "Btn_rectangle";

    // 右侧 Basic Type 区域中的 BOX 按钮 name。它和左侧 BOX 按钮共用同一个生成流程。
    [SerializeField] private string secondaryGenerateButtonName = "Btn_basicChestType";

    // 中间画布区 VisualElement 的 name。RenderTexture 会作为它的 backgroundImage 显示。
    [SerializeField] private string drawingAreaName = "DrawingArea";

    // DrawingArea 底部的 Editing 模式按钮。
    [SerializeField] private string editingModeButtonName = "Btn_editingMode";

    // DrawingArea 底部的 Preview the Texture 模式按钮。
    [SerializeField] private string textureModeButtonName = "Btn_generate";

    // 右侧 Inspector 中编辑模式参数面板的 name。
    [SerializeField] private string controlPanelName = "ControlPanel";

    // 右侧 Inspector 中纹理预览模式面板的 name。
    [SerializeField] private string texturePanelName = "TexturePanel";

    [Header("3D Preview")]
    // 3D 宝箱生成器。负责创建模型、维护旋转状态、暴露预览相机。
    [SerializeField] private Chest3DGenerator chestGenerator;

    // 旧字段 previewCamera 的序列化迁移兼容。
    [FormerlySerializedAs("previewCamera")]
    [SerializeField] private Camera editPreviewCamera;

    // 编辑模式相机和纹理预览相机分开管理，分别渲染不同 layer 上的宝箱。
    [SerializeField] private Camera texturePreviewCamera;

    // 纹理预览后处理。用于把 raw texture preview 进一步处理成线稿/描边效果。
    [SerializeField] private ChestTextureOutlinePostProcessor textureOutlinePostProcessor;

    // DrawingArea 尚未完成布局、拿不到有效宽高时使用的默认 RenderTexture 尺寸。
    [SerializeField] private int fallbackTextureWidth = 1024;
    [SerializeField] private int fallbackTextureHeight = 768;

    // 防止 UI 区域过小时创建过小的 RenderTexture，保证预览有基本清晰度。
    [SerializeField] private int minTextureSize = 256;

    // 编辑模式 RenderTexture 的抗锯齿采样数。纹理线稿模式通常使用 1，避免后处理采样混入额外边缘。
    [SerializeField] private int antiAliasing = 4;

    [Header("Editing Rotation")]
    // 鼠标拖拽像素到旋转角度的换算倍率。数值越大，宝箱旋转越灵敏。
    [SerializeField] private float dragRotationSensitivity = 0.3f;

    [Header("Runtime UI Styling")]
    // 是否在运行时覆盖生成按钮的样式。默认关闭，优先使用 UXML 中的设计。
    [SerializeField] private bool styleGenerateButton = false;
    [SerializeField] private string generateButtonText = "Generate";

    // 运行时缓存的 UI 元素引用。OnEnable/BindUI 时从 UIDocument.rootVisualElement 查询。
    private Button generateButton;
    private Button secondaryGenerateButton;
    private Button editingModeButton;
    private Button textureModeButton;
    private VisualElement drawingArea;
    private VisualElement controlPanel;
    private VisualElement texturePanel;

    // 编辑模式最终显示到 DrawingArea 的 RenderTexture。
    private RenderTexture editPreviewTexture;

    // 纹理预览模式最终显示到 DrawingArea 的 RenderTexture。
    private RenderTexture texturePreviewTexture;

    // 纹理预览模式的原始相机渲染结果。后处理会读取它，再输出到 texturePreviewTexture。
    private RenderTexture textureRawPreviewTexture;

    // 当前 DrawingArea 显示的模式。默认进入编辑模式。
    private ChestPreviewMode currentPreviewMode = ChestPreviewMode.Edit;

    // 是否已经至少渲染过一次预览。用于避免 UI 刚布局时无意义地刷新。
    private bool hasRenderedPreview;

    // 以下字段用于追踪编辑模式下的鼠标拖拽旋转。
    private bool isDraggingPreview;
    private int activePreviewPointerId = -1;
    private Vector2 lastPreviewPointerPosition;

    private void OnEnable()
    {
        // UI Toolkit 的 visual tree 在 OnEnable 时已经可访问，适合做元素查询和事件绑定。
        ResolveReferences();
        BindUI();
    }

    private void OnDisable()
    {
        // 禁用或热重载时解绑事件，避免重新进入 Play Mode 后重复注册回调。
        UnbindUI();
    }

    private void OnDestroy()
    {
        // RenderTexture 是运行时资源，脚本销毁时需要主动释放。
        ReleasePreviewTextures();
    }

    private void ResolveReferences()
    {
        // 优先使用 Inspector 中配置的引用；没有配置时尝试自动查找，降低场景配置成本。
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        // 生成器通常在 ChestRuntimeRoot 上，也可能通过 Inspector 手动拖入。
        if (chestGenerator == null)
        {
            chestGenerator = FindFirstObjectByType<Chest3DGenerator>();
        }

        // 从生成器读取编辑预览相机。生成器内部也会做自己的引用兜底。
        if (editPreviewCamera == null && chestGenerator != null)
        {
            editPreviewCamera = chestGenerator.EditPreviewCamera;
        }

        // 从生成器读取纹理预览相机；如果生成器没有场景相机，它可能会运行时创建一台。
        if (texturePreviewCamera == null && chestGenerator != null)
        {
            texturePreviewCamera = chestGenerator.TexturePreviewCamera;
        }

        // 最后退回主相机，避免编辑预览相机为空。
        if (editPreviewCamera == null)
        {
            editPreviewCamera = Camera.main;
        }

        // 纹理模式需要后处理器；未配置时优先复用同对象已有组件。
        if (textureOutlinePostProcessor == null)
        {
            textureOutlinePostProcessor = GetComponent<ChestTextureOutlinePostProcessor>();
        }

        // 如果场景没有挂后处理器，则运行时添加一个，保证 Preview the Texture 流程可用。
        if (textureOutlinePostProcessor == null)
        {
            textureOutlinePostProcessor = gameObject.AddComponent<ChestTextureOutlinePostProcessor>();
        }
    }

    private void BindUI()
    {
        // UIDocument 是 UI Toolkit 运行时界面的入口，没有它无法查询 UXML 中的元素。
        if (uiDocument == null)
        {
            Debug.LogError("Chest3DPreviewUIController requires a UIDocument.");
            return;
        }

        // 按 UXML name 查询所有需要交互或显示/隐藏的节点。
        VisualElement root = uiDocument.rootVisualElement;
        generateButton = root.Q<Button>(generateButtonName);
        secondaryGenerateButton = root.Q<Button>(secondaryGenerateButtonName);
        editingModeButton = root.Q<Button>(editingModeButtonName);
        textureModeButton = root.Q<Button>(textureModeButtonName);
        drawingArea = root.Q<VisualElement>(drawingAreaName);
        controlPanel = root.Q<VisualElement>(controlPanelName);
        texturePanel = root.Q<VisualElement>(texturePanelName);

        // 左侧 BOX 按钮是基础生成入口，缺失时说明当前 UXML 不匹配本控制器配置。
        if (generateButton == null)
        {
            Debug.LogError($"Generate button not found: {generateButtonName}");
            return;
        }

        // DrawingArea 是 RenderTexture 显示和拖拽旋转输入的共同区域，缺失时不能继续。
        if (drawingArea == null)
        {
            Debug.LogError($"Drawing area not found: {drawingAreaName}");
            return;
        }

        // 初始化 DrawingArea 的背景显示策略，并根据配置可选覆盖生成按钮样式。
        ConfigureDrawingArea();
        ConfigureGenerateButton(generateButton);

        // 默认进入编辑模式，右侧显示参数面板。
        ShowEditingPanel();

        // 左侧 BOX 和右侧 Basic Type BOX 都触发同一套生成/渲染流程。
        generateButton.clicked += GenerateAndRenderPreview;

        if (secondaryGenerateButton != null)
        {
            secondaryGenerateButton.clicked += GenerateAndRenderPreview;
        }

        // 底部模式按钮只切换当前模式和右侧面板，同时根据需要刷新预览。
        if (editingModeButton != null)
        {
            editingModeButton.clicked += ShowEditingPanel;
        }

        if (textureModeButton != null)
        {
            textureModeButton.clicked += ShowTexturePanel;
        }

        // DrawingArea 尺寸变化时重建匹配尺寸的 RenderTexture。
        drawingArea.RegisterCallback<GeometryChangedEvent>(OnDrawingAreaGeometryChanged);

        // 编辑模式下，DrawingArea 也作为 3D 预览的拖拽旋转输入区域。
        drawingArea.RegisterCallback<PointerDownEvent>(OnDrawingAreaPointerDown);
        drawingArea.RegisterCallback<PointerMoveEvent>(OnDrawingAreaPointerMove);
        drawingArea.RegisterCallback<PointerUpEvent>(OnDrawingAreaPointerUp);
        drawingArea.RegisterCallback<PointerCancelEvent>(OnDrawingAreaPointerCancel);
    }

    private void UnbindUI()
    {
        // 与 BindUI 对称解绑，避免多次 OnEnable 后同一个点击触发多次生成。
        if (generateButton != null)
        {
            generateButton.clicked -= GenerateAndRenderPreview;
        }

        if (secondaryGenerateButton != null)
        {
            secondaryGenerateButton.clicked -= GenerateAndRenderPreview;
        }

        if (editingModeButton != null)
        {
            editingModeButton.clicked -= ShowEditingPanel;
        }

        if (textureModeButton != null)
        {
            textureModeButton.clicked -= ShowTexturePanel;
        }

        if (drawingArea != null)
        {
            // 如果脚本禁用时用户仍在拖拽，先释放 Pointer Capture。
            CancelPreviewDrag();
            drawingArea.UnregisterCallback<GeometryChangedEvent>(OnDrawingAreaGeometryChanged);
            drawingArea.UnregisterCallback<PointerDownEvent>(OnDrawingAreaPointerDown);
            drawingArea.UnregisterCallback<PointerMoveEvent>(OnDrawingAreaPointerMove);
            drawingArea.UnregisterCallback<PointerUpEvent>(OnDrawingAreaPointerUp);
            drawingArea.UnregisterCallback<PointerCancelEvent>(OnDrawingAreaPointerCancel);
        }

        // 解绑相机 targetTexture，避免相机继续持有之后将被释放的 RenderTexture。
        DisconnectCameraTarget(editPreviewCamera, editPreviewTexture);
        DisconnectCameraTarget(texturePreviewCamera, texturePreviewTexture);
        DisconnectCameraTarget(texturePreviewCamera, textureRawPreviewTexture);
    }

    private void ConfigureDrawingArea()
    {
        // DrawingArea 本身不直接包含 3D 物体，而是显示 RenderTexture 作为背景图。
        // Contain + Center 可以让不同尺寸的 RenderTexture 稳定居中显示。
        drawingArea.style.overflow = Overflow.Hidden;
        drawingArea.style.backgroundColor = new Color(1f, 1f, 1f, 1f);
        drawingArea.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        drawingArea.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
        drawingArea.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
    }

    private void ConfigureGenerateButton(Button button)
    {
        // 旧版或调试用的运行时按钮样式覆盖入口。
        // 当前 UXML 已经有完整样式，所以默认 styleGenerateButton 为 false。
        if (!styleGenerateButton || button == null)
        {
            return;
        }

        button.text = generateButtonText;
        button.style.width = 82f;
        button.style.height = 28f;
        button.style.marginLeft = 8f;
        button.style.marginTop = 8f;
        button.style.fontSize = 11f;
    }

    private void ShowEditingPanel()
    {
        // 切换模式时停止拖拽，避免按住鼠标时模式切换导致 pointer 状态残留。
        CancelPreviewDrag();
        currentPreviewMode = ChestPreviewMode.Edit;//使用枚举值是为了更好地保护状态和维护可扩展性。
        SetPanelVisible(controlPanel, true);
        SetPanelVisible(texturePanel, false);

        // 如果已经生成过宝箱，则切回编辑模式时刷新一次彩色预览。
        if (hasRenderedPreview)
        {
            GenerateAndRenderPreview(ChestPreviewMode.Edit);
        }
    }

    private void ShowTexturePanel()
    {
        // 纹理模式不响应拖拽旋转输入，因此切换前先结束当前拖拽。
        CancelPreviewDrag();
        currentPreviewMode = ChestPreviewMode.TextureLine;
        SetPanelVisible(controlPanel, false);
        SetPanelVisible(texturePanel, true);

        // 进入纹理模式时立即生成/渲染线稿预览。
        GenerateAndRenderPreview(ChestPreviewMode.TextureLine);
    }

    private static void SetPanelVisible(VisualElement panel, bool visible)
    {
        // 通过 display 切换右侧面板，而不是销毁/重建 UI 树。
        if (panel == null)
        {
            return;
        }

        panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void GenerateAndRenderPreview()
    {
        // 对外的便捷入口：按当前模式刷新预览。
        // ChestParameterPanelController 参数变化后会调用这个无参版本。
        GenerateAndRenderPreview(currentPreviewMode);
    }

    public void GenerateAndRenderPreview(ChestPreviewMode mode)
    {
        // 生成并渲染预览的主入口。
        // 注意：这里会调用 chestGenerator.GenerateBoth()，因此适合参数变化、首次生成、模式切换等需要重建模型的场景。
        // 拖拽旋转不走这个方法，因为拖拽只改变角度，不需要重新生成 mesh。
        ResolveReferences();

        if (chestGenerator == null)
        {
            Debug.LogError("Chest3DGenerator is missing.");
            return;
        }

        Camera camera = GetCameraForMode(mode);

        if (camera == null)
        {
            Debug.LogError($"Preview camera is missing for mode: {mode}");
            return;
        }

        // 当前模式决定 DrawingArea 接下来显示哪一张 RenderTexture。
        currentPreviewMode = mode;

        // 先确保 RenderTexture 尺寸和目标相机绑定正确，再生成模型和渲染。
        EnsurePreviewTexture(mode);
        chestGenerator.GenerateBoth();

        // GenerateBoth 可能会运行时创建 texturePreviewCamera，因此生成后重新同步一次相机引用。
        editPreviewCamera = chestGenerator.EditPreviewCamera;
        texturePreviewCamera = chestGenerator.TexturePreviewCamera;

        // 手动调用 Camera.Render，并把结果显示到 DrawingArea。
        RenderPreview(mode);
        hasRenderedPreview = true;
    }

    private void OnDrawingAreaGeometryChanged(GeometryChangedEvent evt)
    {
        // UI Toolkit 完成布局、窗口变化或面板尺寸变化时会触发。
        // 如果已经有预览，就按新尺寸重建 RenderTexture 并重新渲染。
        if (evt.newRect.width <= 0f || evt.newRect.height <= 0f || !hasRenderedPreview)
        {
            return;
        }

        EnsurePreviewTexture(currentPreviewMode);
        RenderPreview(currentPreviewMode);
    }

    private void OnDrawingAreaPointerDown(PointerDownEvent evt)
    {
        // 只允许在编辑模式中用鼠标左键拖拽旋转宝箱。
        // 纹理预览模式下 DrawingArea 只负责显示结果，不处理旋转输入。
        if (currentPreviewMode != ChestPreviewMode.Edit ||
            evt.button != 0 ||
            IsPointerOverModeButton(evt))
        {
            return;
        }

        ResolveReferences();

        // 没有生成器时无法旋转模型，直接忽略输入。
        if (chestGenerator == null)
        {
            return;
        }

        // 记录当前活跃 pointer，并捕获它。
        // CapturePointer 后，即使鼠标移出 DrawingArea，也能继续收到移动/抬起事件。
        isDraggingPreview = true;
        activePreviewPointerId = evt.pointerId;
        lastPreviewPointerPosition = ToVector2(evt.position);
        drawingArea.CapturePointer(activePreviewPointerId);

        // 阻止事件继续冒泡给其他可能监听 DrawingArea 的工具脚本。
        evt.StopPropagation();
    }

    private void OnDrawingAreaPointerMove(PointerMoveEvent evt)
    {
        // 只有当前捕获的 pointer 才能驱动旋转，避免多指/其他 pointer 干扰拖拽状态。
        if (!isDraggingPreview ||
            evt.pointerId != activePreviewPointerId ||
            drawingArea == null ||
            !drawingArea.HasPointerCapture(activePreviewPointerId))
        {
            return;
        }

        Vector2 pointerPosition = ToVector2(evt.position);
        Vector2 delta = pointerPosition - lastPreviewPointerPosition;
        lastPreviewPointerPosition = pointerPosition;

        // 没有实际位移时不做任何渲染，减少无意义刷新。
        if (delta.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        // 将屏幕拖拽位移换算成 yaw / pitch 增量，并交给生成器维护旋转状态。
        // 当前映射：水平位移控制左右旋转，垂直位移取反后控制上下旋转。
        chestGenerator.RotatePreview(
            -delta.x * dragRotationSensitivity,
            -delta.y * dragRotationSensitivity);

        // 拖拽时不重新 GenerateBoth，只把已有模型旋转后重新渲染编辑模式 RenderTexture。
        RenderPreview(ChestPreviewMode.Edit);
        evt.StopPropagation();//是为了独占事件，防止它继续冒泡
    }

    private void OnDrawingAreaPointerUp(PointerUpEvent evt)
    {
        // 鼠标抬起，结束当前拖拽并释放 pointer capture。
        if (!isDraggingPreview || evt.pointerId != activePreviewPointerId)
        {
            return;
        }

        CancelPreviewDrag();
        evt.StopPropagation();
    }

    private void OnDrawingAreaPointerCancel(PointerCancelEvent evt)
    {
        // 系统取消 pointer 时也要清理拖拽状态，例如窗口失焦或输入被中断。
        if (!isDraggingPreview || evt.pointerId != activePreviewPointerId)
        {
            return;
        }

        CancelPreviewDrag();
        evt.StopPropagation();
    }

    private void CancelPreviewDrag()
    {
        // 统一的拖拽清理函数：释放捕获、重置状态。
        if (drawingArea != null &&
            activePreviewPointerId >= 0 &&
            drawingArea.HasPointerCapture(activePreviewPointerId))
        {
            drawingArea.ReleasePointer(activePreviewPointerId);
        }

        isDraggingPreview = false;
        activePreviewPointerId = -1;
    }

    private void EnsurePreviewTexture(ChestPreviewMode mode)
    {
        // 根据 DrawingArea 当前像素尺寸创建或复用 RenderTexture。
        // RenderTexture 尺寸要尽量匹配 UI 区域，避免预览模糊或拉伸。
        if (drawingArea == null)
        {
            return;
        }

        Camera camera = GetCameraForMode(mode);

        // 没有相机时无法建立 targetTexture。
        if (camera == null)
        {
            return;
        }

        // UI Toolkit 的 resolvedStyle 只有布局完成后才有有效值。
        int width = Mathf.RoundToInt(drawingArea.resolvedStyle.width);
        int height = Mathf.RoundToInt(drawingArea.resolvedStyle.height);

        // 如果还没完成布局，则使用 fallback 尺寸保证首次渲染可以进行。
        if (width <= 0)
        {
            width = fallbackTextureWidth;
        }

        if (height <= 0)
        {
            height = fallbackTextureHeight;
        }

        // 设定最小尺寸，防止窗口太小时产生质量过低的预览纹理。
        width = Mathf.Max(minTextureSize, width);
        height = Mathf.Max(minTextureSize, height);

        if (mode == ChestPreviewMode.TextureLine)
        {
            // 纹理线稿模式使用两张 RenderTexture：
            // 1. textureRawPreviewTexture：相机直接渲染出的原图。
            // 2. texturePreviewTexture：后处理后的最终结果，显示到 DrawingArea。
            EnsureRenderTexture(
                ref textureRawPreviewTexture,
                "ChestTextureLineRawPreviewTexture",
                width,
                height,
                24,
                1);

            // 纹理模式不使用 MSAA，避免后处理采样边缘时得到被抗锯齿混合过的颜色。
            EnsureRenderTexture(
                ref texturePreviewTexture,
                "ChestTextureLinePreviewTexture",
                width,
                height,
                24,
                1);

            // 相机先渲染到 raw 纹理，DrawingArea 显示的是最终后处理纹理。
            camera.targetTexture = textureRawPreviewTexture;
            ApplyTextureToDrawingArea(texturePreviewTexture);
            return;
        }

        // 编辑模式只需要一张 RenderTexture：相机直接渲染，DrawingArea 直接显示。
        EnsureRenderTexture(
            ref editPreviewTexture,
            "ChestEditPreviewTexture",
            width,
            height,
            24,
            Mathf.Max(1, antiAliasing));

        camera.targetTexture = editPreviewTexture;
        ApplyTextureToDrawingArea(editPreviewTexture);
    }

    private void RenderPreview(ChestPreviewMode mode)
    {
        // 手动驱动预览相机渲染。
        // 因为预览相机 enabled = false，所以不会进入 Unity 默认相机渲染流程。
        Camera camera = GetCameraForMode(mode);

        if (camera == null)
        {
            return;
        }

        if (mode == ChestPreviewMode.TextureLine)
        {
            // 纹理模式必须同时具备 raw 和 final 两张纹理。
            if (textureRawPreviewTexture == null || texturePreviewTexture == null)
            {
                return;
            }

            // 第一步：相机把 TextureLine layer 上的宝箱渲染进 raw 纹理。
            camera.targetTexture = textureRawPreviewTexture;
            camera.Render();

            // 先做一次基础拷贝，保证即使后处理不可用，final 纹理里也有画面。
            Graphics.Blit(textureRawPreviewTexture, texturePreviewTexture);

            if (textureOutlinePostProcessor != null)
            {
                // 第二步：后处理读取 raw 纹理和纹理模型根节点，输出线稿/描边后的 final 纹理。
                textureOutlinePostProcessor.Render(
                    camera,
                    chestGenerator != null ? chestGenerator.TextureGeneratedRoot : null,
                    textureRawPreviewTexture,
                    texturePreviewTexture);
            }
            else
            {
                // 兜底路径：没有后处理器时直接显示 raw 结果。
                Graphics.Blit(textureRawPreviewTexture, texturePreviewTexture);
            }

            // UI Toolkit 背景图引用 final 纹理。
            ApplyTextureToDrawingArea(texturePreviewTexture);
            return;
        }

        // 编辑模式：直接把 edit 相机渲染结果显示到 DrawingArea。
        if (editPreviewTexture == null)
        {
            return;
        }

        camera.targetTexture = editPreviewTexture;
        ApplyTextureToDrawingArea(editPreviewTexture);
        camera.Render();
    }

    private void EnsureRenderTexture(
        ref RenderTexture texture,
        string textureName,
        int width,
        int height,
        int depthBits,
        int antiAliasingSamples)
    {
        // 统一的 RenderTexture 创建/复用函数。
        // 如果已有纹理尺寸和抗锯齿设置都匹配，就直接复用，避免每帧分配。
        antiAliasingSamples = Mathf.Max(1, antiAliasingSamples);

        if (texture != null &&
            texture.width == width &&
            texture.height == height &&
            texture.antiAliasing == antiAliasingSamples)
        {
            return;
        }

        // 尺寸或 AA 不匹配时释放旧纹理，再创建新纹理。
        ReleaseRenderTexture(ref texture);

        texture = new RenderTexture(width, height, depthBits, RenderTextureFormat.ARGB32)
        {
            name = textureName,
            antiAliasing = antiAliasingSamples,
            useMipMap = false
        };

        // 显式 Create，确保后续 Camera.Render / Graphics.Blit 可以立即使用。
        texture.Create();
    }

    private Camera GetCameraForMode(ChestPreviewMode mode)
    {
        // 当前模式决定使用哪一台预览相机。
        return mode == ChestPreviewMode.TextureLine ? texturePreviewCamera : editPreviewCamera;
    }

    private void ApplyTextureToDrawingArea(RenderTexture texture)
    {
        // UI Toolkit Runtime 可以直接把 RenderTexture 包成 Background 显示在 VisualElement 背景上。
        if (drawingArea == null || texture == null)
        {
            return;
        }

        drawingArea.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(texture));
    }

    private bool IsPointerOverModeButton(EventBase evt)
    {
        // 模式按钮位于 DrawingArea 内部。
        // 如果不排除它们，点击 Editing / Preview the Texture 的那一下也可能被当作拖拽起点。
        VisualElement target = evt.target as VisualElement;//获取事件发生的UI元素
        return IsElementOrDescendant(target, editingModeButton) ||
            IsElementOrDescendant(target, textureModeButton);
    }

    private static bool IsElementOrDescendant(VisualElement element, VisualElement ancestor)
    {
        // 判断事件目标是否是某个按钮本身或按钮内部子元素。
        while (element != null)
        {
            if (element == ancestor)
            {
                return true;
            }

            element = element.parent;
        }

        return false;
    }

    private static Vector2 ToVector2(Vector3 value)
    {
        // PointerEvent.position 是 Vector3，但这里拖拽计算只需要屏幕平面上的 x/y。
        return new Vector2(value.x, value.y);
    }

    private void ReleasePreviewTextures()
    {
        // 统一释放当前脚本创建过的所有预览纹理。
        ReleasePreviewTexture(ChestPreviewMode.Edit);
        ReleasePreviewTexture(ChestPreviewMode.TextureLine);
    }

    private void ReleasePreviewTexture(ChestPreviewMode mode)
    {
        // 按模式释放对应纹理。纹理模式有 raw/final 两张。
        if (mode == ChestPreviewMode.TextureLine)
        {
            ReleaseRenderTexture(ref textureRawPreviewTexture);
            ReleaseRenderTexture(ref texturePreviewTexture);
            return;
        }

        ReleaseRenderTexture(ref editPreviewTexture);
    }

    private void ReleaseRenderTexture(ref RenderTexture texture)
    {
        // RenderTexture 是显式创建的图形资源，需要在尺寸变化或脚本销毁时释放。
        if (texture == null)
        {
            return;
        }

        // 释放前先断开相机引用，避免相机持有已释放纹理。
        DisconnectCameraTarget(editPreviewCamera, texture);
        DisconnectCameraTarget(texturePreviewCamera, texture);

        texture.Release();

        // Play 模式和编辑器非 Play 模式使用不同销毁 API。
        if (Application.isPlaying)
        {
            Destroy(texture);
        }
        else
        {
            DestroyImmediate(texture);
        }

        texture = null;
    }

    private static void DisconnectCameraTarget(Camera camera, RenderTexture texture)
    {
        // 只在相机当前 targetTexture 正好是这张纹理时才置空，避免误改其他绑定。
        if (camera != null && camera.targetTexture == texture)
        {
            camera.targetTexture = null;
        }
    }
}
