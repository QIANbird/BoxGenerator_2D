using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

// Bridges UI Toolkit and the 3D chest preview.
// The same DrawingArea is used for both modes:
// - Editing: colored low-poly preview.
// - Preview the Texture: black/white texture-line preview.
public class Chest3DPreviewUIController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private string generateButtonName = "Btn_rectangle";
    [SerializeField] private string secondaryGenerateButtonName = "Btn_basicChestType";
    [SerializeField] private string drawingAreaName = "DrawingArea";
    [SerializeField] private string editingModeButtonName = "Btn_editingMode";
    [SerializeField] private string textureModeButtonName = "Btn_generate";
    [SerializeField] private string controlPanelName = "ControlPanel";
    [SerializeField] private string texturePanelName = "TexturePanel";

    [Header("3D Preview")]
    [SerializeField] private Chest3DGenerator chestGenerator;

    [FormerlySerializedAs("previewCamera")]
    [SerializeField] private Camera editPreviewCamera;

    [SerializeField] private Camera texturePreviewCamera;

    [SerializeField] private int fallbackTextureWidth = 1024;
    [SerializeField] private int fallbackTextureHeight = 768;
    [SerializeField] private int minTextureSize = 256;
    [SerializeField] private int antiAliasing = 4;

    [Header("Runtime UI Styling")]
    [SerializeField] private bool styleGenerateButton = false;
    [SerializeField] private string generateButtonText = "Generate";

    private Button generateButton;
    private Button secondaryGenerateButton;
    private Button editingModeButton;
    private Button textureModeButton;
    private VisualElement drawingArea;
    private VisualElement controlPanel;
    private VisualElement texturePanel;

    private RenderTexture editPreviewTexture;
    private RenderTexture texturePreviewTexture;
    private ChestPreviewMode currentPreviewMode = ChestPreviewMode.Edit;
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
        ReleasePreviewTextures();
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

        if (editPreviewCamera == null && chestGenerator != null)
        {
            editPreviewCamera = chestGenerator.EditPreviewCamera;
        }

        if (texturePreviewCamera == null && chestGenerator != null)
        {
            texturePreviewCamera = chestGenerator.TexturePreviewCamera;
        }

        if (editPreviewCamera == null)
        {
            editPreviewCamera = Camera.main;
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

        DisconnectCameraTarget(editPreviewCamera, editPreviewTexture);
        DisconnectCameraTarget(texturePreviewCamera, texturePreviewTexture);
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

    private void ShowEditingPanel()
    {
        currentPreviewMode = ChestPreviewMode.Edit;
        SetPanelVisible(controlPanel, true);
        SetPanelVisible(texturePanel, false);

        if (hasRenderedPreview)
        {
            GenerateAndRenderPreview(ChestPreviewMode.Edit);
        }
    }

    private void ShowTexturePanel()
    {
        currentPreviewMode = ChestPreviewMode.TextureLine;
        SetPanelVisible(controlPanel, false);
        SetPanelVisible(texturePanel, true);
        GenerateAndRenderPreview(ChestPreviewMode.TextureLine);
    }

    private static void SetPanelVisible(VisualElement panel, bool visible)
    {
        if (panel == null)
        {
            return;
        }

        panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void GenerateAndRenderPreview()
    {
        GenerateAndRenderPreview(currentPreviewMode);
    }

    public void GenerateAndRenderPreview(ChestPreviewMode mode)
    {
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

        currentPreviewMode = mode;
        EnsurePreviewTexture(mode);
        chestGenerator.GenerateBoth();

        editPreviewCamera = chestGenerator.EditPreviewCamera;
        texturePreviewCamera = chestGenerator.TexturePreviewCamera;

        RenderPreview(mode);
        hasRenderedPreview = true;
    }

    private void OnDrawingAreaGeometryChanged(GeometryChangedEvent evt)
    {
        if (evt.newRect.width <= 0f || evt.newRect.height <= 0f || !hasRenderedPreview)
        {
            return;
        }

        EnsurePreviewTexture(currentPreviewMode);
        RenderPreview(currentPreviewMode);
    }

    private void EnsurePreviewTexture(ChestPreviewMode mode)
    {
        if (drawingArea == null)
        {
            return;
        }

        Camera camera = GetCameraForMode(mode);

        if (camera == null)
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

        RenderTexture texture = GetTextureForMode(mode);

        if (texture != null && texture.width == width && texture.height == height)
        {
            camera.targetTexture = texture;
            ApplyTextureToDrawingArea(texture);
            return;
        }

        ReleasePreviewTexture(mode);

        texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = mode == ChestPreviewMode.Edit ? "ChestEditPreviewTexture" : "ChestTextureLinePreviewTexture",
            antiAliasing = Mathf.Max(1, antiAliasing),
            useMipMap = false
        };

        texture.Create();
        SetTextureForMode(mode, texture);
        camera.targetTexture = texture;
        ApplyTextureToDrawingArea(texture);
    }

    private void RenderPreview(ChestPreviewMode mode)
    {
        Camera camera = GetCameraForMode(mode);
        RenderTexture texture = GetTextureForMode(mode);

        if (camera == null || texture == null)
        {
            return;
        }

        camera.targetTexture = texture;
        ApplyTextureToDrawingArea(texture);
        camera.Render();
    }

    private Camera GetCameraForMode(ChestPreviewMode mode)
    {
        return mode == ChestPreviewMode.TextureLine ? texturePreviewCamera : editPreviewCamera;
    }

    private RenderTexture GetTextureForMode(ChestPreviewMode mode)
    {
        return mode == ChestPreviewMode.TextureLine ? texturePreviewTexture : editPreviewTexture;
    }

    private void SetTextureForMode(ChestPreviewMode mode, RenderTexture texture)
    {
        if (mode == ChestPreviewMode.TextureLine)
        {
            texturePreviewTexture = texture;
            return;
        }

        editPreviewTexture = texture;
    }

    private void ApplyTextureToDrawingArea(RenderTexture texture)
    {
        if (drawingArea == null || texture == null)
        {
            return;
        }

        drawingArea.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(texture));
    }

    private void ReleasePreviewTextures()
    {
        ReleasePreviewTexture(ChestPreviewMode.Edit);
        ReleasePreviewTexture(ChestPreviewMode.TextureLine);
    }

    private void ReleasePreviewTexture(ChestPreviewMode mode)
    {
        Camera camera = GetCameraForMode(mode);
        RenderTexture texture = GetTextureForMode(mode);

        DisconnectCameraTarget(camera, texture);

        if (texture == null)
        {
            return;
        }

        texture.Release();

        if (Application.isPlaying)
        {
            Destroy(texture);
        }
        else
        {
            DestroyImmediate(texture);
        }

        SetTextureForMode(mode, null);
    }

    private static void DisconnectCameraTarget(Camera camera, RenderTexture texture)
    {
        if (camera != null && camera.targetTexture == texture)
        {
            camera.targetTexture = null;
        }
    }
}
