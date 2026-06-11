using UnityEngine;
using UnityEngine.UIElements;

// 连接 UI Toolkit 和 3D 预览的桥接脚本。
// 流程：点击 BOX -> Chest3DGenerator 生成模型 -> 预览相机渲染到 RenderTexture -> DrawingArea 显示贴图。
[RequireComponent(typeof(UIDocument))]
public class Chest3DPreviewUIController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private string generateButtonName = "Btn_rectangle";
    [SerializeField] private string secondaryGenerateButtonName = "Btn_basicChestType";
    [SerializeField] private string drawingAreaName = "DrawingArea";

    [Header("3D Preview")]
    [SerializeField] private Chest3DGenerator chestGenerator;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private int fallbackTextureWidth = 1024;
    [SerializeField] private int fallbackTextureHeight = 768;
    [SerializeField] private int minTextureSize = 256;
    [SerializeField] private int antiAliasing = 4;

    [Header("Runtime UI Styling")]
    [SerializeField] private bool styleGenerateButton = false;
    [SerializeField] private string generateButtonText = "Generate";

    private Button generateButton;
    private Button secondaryGenerateButton;
    private VisualElement drawingArea;
    private RenderTexture previewTexture;
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
        ConfigureGenerateButton(generateButton);

        generateButton.clicked += GenerateAndRenderPreview;

        if (secondaryGenerateButton != null)
        {
            secondaryGenerateButton.clicked += GenerateAndRenderPreview;
        }

        drawingArea.RegisterCallback<GeometryChangedEvent>(OnDrawingAreaGeometryChanged);
    }

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

        if (drawingArea != null)
        {
            drawingArea.UnregisterCallback<GeometryChangedEvent>(OnDrawingAreaGeometryChanged);
        }

        if (previewCamera != null && previewCamera.targetTexture == previewTexture)
        {
            previewCamera.targetTexture = null;
        }
    }

    private void ConfigureDrawingArea()
    {
        drawingArea.style.overflow = Overflow.Hidden;
        drawingArea.style.backgroundColor = new Color(1f, 1f, 1f, 1f);
        drawingArea.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        drawingArea.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
        drawingArea.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
    }

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

    private void ApplyTextureToDrawingArea()
    {
        if (drawingArea == null || previewTexture == null)
        {
            return;
        }

        drawingArea.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(previewTexture));
    }

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
