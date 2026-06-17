using UnityEngine;
using UnityEngine.UIElements;

// Chest3DPreviewUIController 是 UI Toolkit 和 3D 宝箱预览之间的桥接层。
//
// UI Toolkit 的 VisualElement 本身不能直接显示一个 3D 场景对象，所以这里采用：
// 预览相机 Camera -> RenderTexture -> DrawingArea.backgroundImage 的显示方案。
//
// 本脚本只负责“UI 点击事件”和“预览贴图管理”：
// 1. 从 UIDocument 中找到 BOX 按钮、Basic Type 图标按钮和 DrawingArea。
// 2. 用户点击按钮后，调用 Chest3DGenerator.Generate() 生成 3D 宝箱。
// 3. 使用 previewCamera 把生成结果渲染到 RenderTexture。
// 4. 把 RenderTexture 设置为 DrawingArea 的背景图，让 3D 模型显示在 UI 画布中央。
//
// Mesh 的创建逻辑不在这里，而是在 Chest3DGenerator / ChestMeshFactory 中。
// AI 生图请求也不在这里，而是在后续的材质/AI UI 控制脚本中。
[RequireComponent(typeof(UIDocument))]
public class Chest3DPreviewUIController : MonoBehaviour
{
    [Header("UI References")]
    // 当前 GameObject 上承载 UXML 的 UIDocument。
    // 如果没有在 Inspector 手动赋值，ResolveReferences() 会自动 GetComponent。
    [SerializeField] private UIDocument uiDocument;

    // 主要生成入口：UILayout2.0 里左侧工具栏的 BOX 按钮。
    [SerializeField] private string generateButtonName = "Btn_rectangle";

    // 第二生成入口：UILayout2.0 里 Basic Type 区域的 BOX 图标按钮。
    // 这个按钮是可选的；如果 UXML 中不存在，不会阻断主流程。
    [SerializeField] private string secondaryGenerateButtonName = "Btn_basicChestType";

    // 用于显示 3D 预览画面的 UI 容器。
    // RenderTexture 最终会被设置到这个 VisualElement 的 backgroundImage 上。
    [SerializeField] private string drawingAreaName = "DrawingArea";

    // 底部模式切换按钮：Editing 显示编辑参数面板，Preview the Texture 显示纹理预览面板。
    [SerializeField] private string editingModeButtonName = "Btn_editingMode";
    [SerializeField] private string textureModeButtonName = "Btn_generate";
    [SerializeField] private string controlPanelName = "ControlPanel";
    [SerializeField] private string texturePanelName = "TexturePanel";

    [Header("3D Preview")]
    // 负责真正生成宝箱模型的运行时生成器。
    // 本脚本只调用 Generate()，不直接创建 Mesh。
    [SerializeField] private Chest3DGenerator chestGenerator;

    // 专门用于渲染 3D 宝箱到 RenderTexture 的相机。
    // 注意：它应该是 ChestPreviewCamera，而不是负责 Game 视图 Display 的 Main Camera。
    [SerializeField] private Camera previewCamera;

    // DrawingArea 还没有完成布局、读不到有效尺寸时使用的备用贴图尺寸。
    [SerializeField] private int fallbackTextureWidth = 1024;
    [SerializeField] private int fallbackTextureHeight = 768;

    // 防止窗口太小或布局异常时创建过小的 RenderTexture。
    [SerializeField] private int minTextureSize = 256;

    // RenderTexture 抗锯齿等级。低模白模预览一般 2 或 4 都够用。
    [SerializeField] private int antiAliasing = 4;

    [Header("Runtime UI Styling")]
    // 旧版 UILayout.uxml 里的 Btn_rectangle 宽高曾经是 0，所以需要运行时补样式。
    // 新版 UILayout2.0 已经在 UXML 里定义了按钮样式，因此默认关闭。
    [SerializeField] private bool styleGenerateButton = false;
    [SerializeField] private string generateButtonText = "Generate";

    // 运行时缓存的 UI 元素引用，避免每次点击都重新 Q 查询。
    private Button generateButton;
    private Button secondaryGenerateButton;
    private Button editingModeButton;
    private Button textureModeButton;
    private VisualElement drawingArea;
    private VisualElement controlPanel;
    private VisualElement texturePanel;

    // 承接 previewCamera 渲染结果的运行时贴图。
    // 生命周期由本脚本管理：创建、复用、尺寸变化时重建、销毁时释放。
    private RenderTexture previewTexture;

    // 标记是否已经生成并渲染过一次。
    // DrawingArea 尺寸变化时，如果已经有过预览，就重新渲染一帧，避免贴图变空。
    private bool hasRenderedPreview;

    private void OnEnable()
    {
        ResolveReferences();
        BindUI();
    }

    private void OnDisable()
    {
        UnbindUI();
    }

    private void OnDestroy()
    {
        ReleasePreviewTexture();
    }

    // 自动补齐 Inspector 中没有手动拖拽的引用。
    // 这样原型阶段可以少做一些场景配置，但正式阶段仍建议在 Inspector 显式指定。
    private void ResolveReferences()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (chestGenerator == null)
        {
            chestGenerator = FindFirstObjectByType<Chest3DGenerator>();
        }

        if (previewCamera == null)
        {
            previewCamera = Camera.main;
        }
    }

    // 从 UXML 实例化后的 UI 树中查找控件，并注册点击/布局变化事件。
    // 这里依赖的是 UXML 的 name，而不是场景层级中的 GameObject 名。
    private void BindUI()
    {
        if (uiDocument == null)
        {
            Debug.LogError("Chest3DPreviewUIController requires a UIDocument.");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        generateButton = root.Q<Button>(generateButtonName);
        secondaryGenerateButton = root.Q<Button>(secondaryGenerateButtonName);
        editingModeButton = root.Q<Button>(editingModeButtonName);
        textureModeButton = root.Q<Button>(textureModeButtonName);
        drawingArea = root.Q<VisualElement>(drawingAreaName);
        controlPanel = root.Q<VisualElement>(controlPanelName);
        texturePanel = root.Q<VisualElement>(texturePanelName);

        if (generateButton == null)
        {
            Debug.LogError($"Generate button not found: {generateButtonName}");
            return;
        }

        if (drawingArea == null)
        {
            Debug.LogError($"Drawing area not found: {drawingAreaName}");
            return;
        }

        ConfigureDrawingArea();
        ConfigureGenerateButton(generateButton);
        ShowEditingPanel();

        generateButton.clicked += GenerateAndRenderPreview;

        if (secondaryGenerateButton != null)
        {
            secondaryGenerateButton.clicked += GenerateAndRenderPreview;
        }

        if (editingModeButton != null)
        {
            editingModeButton.clicked += ShowEditingPanel;
        }

        if (textureModeButton != null)
        {
            textureModeButton.clicked += ShowTexturePanel;
        }

        drawingArea.RegisterCallback<GeometryChangedEvent>(OnDrawingAreaGeometryChanged);
    }

    // 解除事件绑定，并断开相机与 RenderTexture 的连接。
    // 这可以避免脚本反复启用/禁用时重复注册事件，也避免相机引用已释放贴图。
    private void UnbindUI()
    {
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
            drawingArea.UnregisterCallback<GeometryChangedEvent>(OnDrawingAreaGeometryChanged);
        }

        if (previewCamera != null && previewCamera.targetTexture == previewTexture)
        {
            previewCamera.targetTexture = null;
        }
    }

    // 配置 DrawingArea 的显示方式。
    // backgroundSize 使用 Contain，保证宝箱完整显示在 UI 区域内，而不是被裁切。
    private void ConfigureDrawingArea()
    {
        drawingArea.style.overflow = Overflow.Hidden;
        drawingArea.style.backgroundColor = new Color(1f, 1f, 1f, 1f);
        drawingArea.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        drawingArea.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
        drawingArea.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
    }

    // 兼容旧 UI 的按钮样式修正入口。
    // 在 UILayout2.0 中 styleGenerateButton 默认关闭，因此 BOX 按钮会保持 UXML 里的设计。
    private void ConfigureGenerateButton(Button button)
    {
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

    // 切回编辑态：右侧显示 Basic Type + Params，隐藏 Texture。
    private void ShowEditingPanel()
    {
        SetPanelVisible(controlPanel, true);
        SetPanelVisible(texturePanel, false);
    }

    // 切到纹理预览态：右侧只显示 Texture 面板。
    // 之后如果要加 AI 栏，可以继续放在 TexturePanel 内部或扩展成新的状态。
    private void ShowTexturePanel()
    {
        SetPanelVisible(controlPanel, false);
        SetPanelVisible(texturePanel, true);
    }

    private static void SetPanelVisible(VisualElement panel, bool visible)
    {
        if (panel == null)
        {
            return;
        }

        panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // 对外公开的“生成并刷新预览”入口。
    // 当前由 BOX 按钮点击触发；后续参数面板实时调整时，也可以直接调用这个方法刷新画面。
    public void GenerateAndRenderPreview()
    {
        if (chestGenerator == null)
        {
            Debug.LogError("Chest3DGenerator is missing.");
            return;
        }

        if (previewCamera == null)
        {
            Debug.LogError("Preview camera is missing.");
            return;
        }

        EnsurePreviewTexture();
        chestGenerator.Generate();
        previewCamera.Render();
        hasRenderedPreview = true;
    }

    // DrawingArea 尺寸变化时同步 RenderTexture 尺寸。
    // 如果用户已经生成过宝箱，重建贴图后要补渲染一帧，保持 UI 上仍然可见。
    private void OnDrawingAreaGeometryChanged(GeometryChangedEvent evt)
    {
        if (evt.newRect.width <= 0f || evt.newRect.height <= 0f || previewTexture == null)
        {
            return;
        }

        EnsurePreviewTexture();

        if (hasRenderedPreview && previewCamera != null)
        {
            previewCamera.Render();
        }
    }

    // 确保 RenderTexture 存在且尺寸匹配当前 DrawingArea。
    // 如果尺寸没变，就复用旧贴图；尺寸变化时释放旧贴图并创建新贴图。
    private void EnsurePreviewTexture()
    {
        if (drawingArea == null || previewCamera == null)
        {
            return;
        }

        int width = Mathf.RoundToInt(drawingArea.resolvedStyle.width);
        int height = Mathf.RoundToInt(drawingArea.resolvedStyle.height);

        if (width <= 0)
        {
            width = fallbackTextureWidth;
        }

        if (height <= 0)
        {
            height = fallbackTextureHeight;
        }

        width = Mathf.Max(minTextureSize, width);
        height = Mathf.Max(minTextureSize, height);

        if (previewTexture != null && previewTexture.width == width && previewTexture.height == height)
        {
            previewCamera.targetTexture = previewTexture;
            ApplyTextureToDrawingArea();
            return;
        }

        ReleasePreviewTexture();

        previewTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = "Chest3DPreviewTexture",
            antiAliasing = Mathf.Max(1, antiAliasing),
            useMipMap = false
        };

        previewTexture.Create();
        previewCamera.targetTexture = previewTexture;
        ApplyTextureToDrawingArea();
    }

    // 把 RenderTexture 放到 DrawingArea 的 backgroundImage 上。
    // 因为 StageFooter 是 DrawingArea 的子元素，所以按钮会自然叠在预览图上方。
    private void ApplyTextureToDrawingArea()
    {
        if (drawingArea == null || previewTexture == null)
        {
            return;
        }

        drawingArea.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(previewTexture));
    }

    // 释放运行时创建的 RenderTexture，避免反复进入 Play Mode 后留下显存资源。
    private void ReleasePreviewTexture()
    {
        if (previewCamera != null && previewCamera.targetTexture == previewTexture)
        {
            previewCamera.targetTexture = null;
        }

        if (previewTexture == null)
        {
            return;
        }

        previewTexture.Release();

        if (Application.isPlaying)
        {
            Destroy(previewTexture);
        }
        else
        {
            DestroyImmediate(previewTexture);
        }

        previewTexture = null;
    }
}
