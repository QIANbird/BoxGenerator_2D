using UnityEngine;
using UnityEngine.UIElements;

// Chest3DPreviewUIController 是 UI Toolkit 和 3D 预览之间的桥接层。
//
// UI Toolkit 的 VisualElement 不能直接承载一个 3D 场景对象，
// 所以这里采用“摄像机 -> RenderTexture -> DrawingArea 背景图”的方案：
// 1. 用户点击 UXML 里的生成按钮。
// 2. 脚本调用 Chest3DGenerator.Generate() 生成宝箱模型。
// 3. previewCamera 把模型渲染到 RenderTexture。
// 4. DrawingArea 使用这个 RenderTexture 作为 backgroundImage。
//
// 这个脚本只负责 UI 事件和预览贴图管理，不负责生成 Mesh，也不负责 AI 请求。
[RequireComponent(typeof(UIDocument))]
public class Chest3DPreviewUIController : MonoBehaviour
{
    [Header("UI References")]
    // 承载 UILayout.uxml 的 UIDocument。
    [SerializeField] private UIDocument uiDocument;

    // UXML 中触发 3D 宝箱生成的按钮名称。
    // 当前复用旧按钮 Btn_rectangle。
    [SerializeField] private string generateButtonName = "Btn_rectangle";

    // UXML 中显示 3D 预览画面的区域名称。
    [SerializeField] private string drawingAreaName = "DrawingArea";

    [Header("3D Preview")]
    // 负责实际生成 Body / Lid / Locker 的运行时生成器。
    [SerializeField] private Chest3DGenerator chestGenerator;

    // 专门用于渲染到 RenderTexture 的预览相机。
    // 注意不要使用负责 Game 视图 Display 的主相机作为 targetTexture 相机。
    [SerializeField] private Camera previewCamera;

    // DrawingArea 尺寸还没完成布局时使用的备用贴图尺寸。
    [SerializeField] private int fallbackTextureWidth = 1024;
    [SerializeField] private int fallbackTextureHeight = 768;

    // 防止窗口太小时创建过小的 RenderTexture。
    [SerializeField] private int minTextureSize = 256;

    // RenderTexture 抗锯齿等级。
    [SerializeField] private int antiAliasing = 4;

    [Header("Runtime UI Styling")]
    // 当前 UILayout 里 Btn_rectangle 的宽高为 0%，所以运行时把它修成可点击按钮。
    [SerializeField] private bool styleGenerateButton = true;
    [SerializeField] private string generateButtonText = "Generate";

    // 运行时缓存的 UI 元素引用。
    private Button generateButton;
    private VisualElement drawingArea;

    // 用来承接 3D 相机画面的运行时贴图。
    private RenderTexture previewTexture;

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

    // 自动补齐可推断的引用，减少场景手动拖拽成本。
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

    // 从 UIDocument 中查找按钮和 DrawingArea，并注册事件。
    private void BindUI()
    {
        if (uiDocument == null)
        {
            Debug.LogError("Chest3DPreviewUIController requires a UIDocument.");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        generateButton = root.Q<Button>(generateButtonName);
        drawingArea = root.Q<VisualElement>(drawingAreaName);

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
        ConfigureGenerateButton();

        generateButton.clicked += OnGenerateClicked;
        drawingArea.RegisterCallback<GeometryChangedEvent>(OnDrawingAreaGeometryChanged);
    }

    // 移除事件绑定，并断开相机和 RenderTexture 的连接。
    private void UnbindUI()
    {
        if (generateButton != null)
        {
            generateButton.clicked -= OnGenerateClicked;
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

    // 配置 DrawingArea 的预览显示方式。
    // backgroundSize 使用 Contain，保证宝箱画面完整显示在 UI 区域内。
    private void ConfigureDrawingArea()
    {
        drawingArea.style.overflow = Overflow.Hidden;
        drawingArea.style.backgroundColor = new Color(1f, 1f, 1f, 1f);
        drawingArea.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        drawingArea.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
        drawingArea.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
    }

    // 只在运行时修正旧按钮的可见性，不直接改 UXML，避免影响旧原型场景。
    private void ConfigureGenerateButton()
    {
        if (!styleGenerateButton)
        {
            return;
        }

        generateButton.text = generateButtonText;
        generateButton.style.width = 82f;
        generateButton.style.height = 28f;
        generateButton.style.marginLeft = 8f;
        generateButton.style.marginTop = 8f;
        generateButton.style.fontSize = 11f;
    }

    // 用户点击 Generate 后的主流程：
    // 先确保 RenderTexture 存在，再生成模型，最后强制预览相机渲染一帧。
    private void OnGenerateClicked()
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
    }

    // DrawingArea 尺寸变化时，同步更新 RenderTexture 尺寸。
    // 如果还没有生成过预览贴图，则不主动创建，保持初始画布为空。
    private void OnDrawingAreaGeometryChanged(GeometryChangedEvent evt)
    {
        if (evt.newRect.width <= 0f || evt.newRect.height <= 0f)
        {
            return;
        }

        if (previewTexture != null)
        {
            EnsurePreviewTexture();
        }
    }

    // 根据 DrawingArea 当前尺寸创建或复用 RenderTexture。
    // 同时把 previewCamera.targetTexture 指向这张贴图。
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

    // 把 RenderTexture 设置成 DrawingArea 的背景图。
    private void ApplyTextureToDrawingArea()
    {
        if (drawingArea == null || previewTexture == null)
        {
            return;
        }

        drawingArea.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(previewTexture));
    }

    // 释放运行时创建的 RenderTexture，避免反复进入 Play 模式或切场景时泄漏。
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
