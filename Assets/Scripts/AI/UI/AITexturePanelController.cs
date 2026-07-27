using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
[DisallowMultipleComponent]
public sealed class AITexturePanelController : MonoBehaviour
{
    private const string NoStyleId = "no_style";
    private const string SciFiStyleId = "hard_scifi";
    private const string CartoonStyleId = "cartoon";

    private const string SciFiPrompt =
        "硬核科幻风格，硬表面机械结构，工业金属材质，精密科技细节，高对比表面。";

    private const string CartoonPrompt =
        "卡通风格，简洁色块，柔和明快配色，清晰轮廓，适度手绘质感。";

    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private AITextureGenerationCoordinator generationCoordinator;
    [SerializeField] private AITextureGenerationRequestBuilder requestBuilder;
    [SerializeField] private Chest3DPreviewUIController previewController;

    [Header("Input Limits")]
    [SerializeField] private int maxPromptCharacters = 500;
    [SerializeField] private int maxImageFileSizeMegabytes = 10;

    [Header("UXML Element Names")]
    [SerializeField] private string texturePanelName = "TexturePanel";
    [SerializeField] private string uploadButtonName = "TextureUploadButton";
    [SerializeField] private string clearUploadButtonName = "TextureUploadClearButton";
    [SerializeField] private string uploadFileLabelName = "TextureUploadFileLabel";
    [SerializeField] private string styleButtonName = "TextureStyleButton";
    [SerializeField] private string stylePopupName = "TextureStylePopup";
    [SerializeField] private string stylePopupCloseButtonName = "TextureStylePopupClose";
    [SerializeField] private string noStyleButtonName = "TextureStyleNone";
    [SerializeField] private string sciFiStyleButtonName = "TextureStyleSciFi";
    [SerializeField] private string cartoonStyleButtonName = "TextureStyleCartoon";
    [SerializeField] private string promptInputName = "TexturePromptInput";
    [SerializeField] private string promptCounterName = "TexturePromptCounter";
    [SerializeField] private string generateButtonName = "TextureGenerateButton";
    [SerializeField] private string downloadButtonName = "TextureDownloadButton";
    [SerializeField] private string errorLabelName = "TextureErrorLabel";
    [SerializeField] private string statusLabelName = "TextureStatusLabel";

    private VisualElement root;
    private VisualElement texturePanel;
    private Button uploadButton;
    private Button clearUploadButton;
    private Label uploadFileLabel;
    private Button styleButton;
    private VisualElement stylePopup;
    private Button stylePopupCloseButton;
    private Button noStyleButton;
    private Button sciFiStyleButton;
    private Button cartoonStyleButton;
    private TextField promptInput;
    private Label promptCounter;
    private Button generateButton;
    private Button downloadButton;
    private Label errorLabel;
    private Label statusLabel;

    private Texture2D uploadedPreviewTexture;
    private bool downloadAvailable;
    private bool isBound;

    public event Action<AITextureGenerationInputState> GenerateRequested;
    public event Action DownloadRequested;

    public bool HasUploadedImage
    {
        get
        {
            AITextureGenerationInputState state = GetInputState();
            return state.uploadedReferenceImage != null &&
                   state.uploadedReferenceImage.HasData;
        }
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindUI();
    }

    private void OnDisable()
    {
        UnbindUI();
        DestroyUploadedPreviewTexture();
    }

    private void ResolveReferences()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (generationCoordinator == null)
        {
            generationCoordinator = GetComponent<AITextureGenerationCoordinator>();
        }

        if (requestBuilder == null)
        {
            requestBuilder = GetComponent<AITextureGenerationRequestBuilder>();
        }

        if (previewController == null)
        {
            previewController = GetComponent<Chest3DPreviewUIController>();
        }
    }

    private void BindUI()
    {
        if (isBound)
        {
            return;
        }

        if (uiDocument == null)
        {
            Debug.LogError("AITexturePanelController requires a UIDocument.", this);
            return;
        }

        if (generationCoordinator == null)
        {
            Debug.LogError(
                "AITexturePanelController requires an AITextureGenerationCoordinator.",
                this
            );
            return;
        }

        root = uiDocument.rootVisualElement;
        texturePanel = root.Q<VisualElement>(texturePanelName);
        uploadButton = root.Q<Button>(uploadButtonName);
        clearUploadButton = root.Q<Button>(clearUploadButtonName);
        uploadFileLabel = root.Q<Label>(uploadFileLabelName);
        styleButton = root.Q<Button>(styleButtonName);
        stylePopup = root.Q<VisualElement>(stylePopupName);
        stylePopupCloseButton = root.Q<Button>(stylePopupCloseButtonName);
        noStyleButton = root.Q<Button>(noStyleButtonName);
        sciFiStyleButton = root.Q<Button>(sciFiStyleButtonName);
        cartoonStyleButton = root.Q<Button>(cartoonStyleButtonName);
        promptInput = root.Q<TextField>(promptInputName);
        promptCounter = root.Q<Label>(promptCounterName);
        generateButton = root.Q<Button>(generateButtonName);
        downloadButton = root.Q<Button>(downloadButtonName);
        errorLabel = root.Q<Label>(errorLabelName);
        statusLabel = root.Q<Label>(statusLabelName);

        if (!ValidateRequiredElements())
        {
            return;
        }

        maxPromptCharacters = Mathf.Max(1, maxPromptCharacters);
        maxImageFileSizeMegabytes = Mathf.Max(1, maxImageFileSizeMegabytes);
        promptInput.maxLength = maxPromptCharacters;
        promptInput.multiline = true;

        uploadButton.clicked += OnUploadClicked;
        clearUploadButton.clicked += OnClearUploadClicked;
        styleButton.clicked += ToggleStylePopup;
        stylePopupCloseButton.clicked += CloseStylePopup;
        noStyleButton.clicked += OnNoStyleClicked;
        sciFiStyleButton.clicked += OnSciFiStyleClicked;
        cartoonStyleButton.clicked += OnCartoonStyleClicked;
        promptInput.RegisterValueChangedCallback(OnPromptChanged);
        generateButton.clicked += OnGenerateClicked;
        downloadButton.clicked += OnDownloadClicked;
        root.RegisterCallback<PointerDownEvent>(
            OnRootPointerDown,
            TrickleDown.TrickleDown
        );

        generationCoordinator.StatusChanged += OnGenerationStatusChanged;

        if (previewController != null)
        {
            previewController.PreviewRendered += OnPreviewRendered;
            downloadAvailable = previewController.HasRenderedPreview;
        }

        isBound = true;

        RestoreInputStateToUI();
        CloseStylePopup();
        HideError();
        HideStatus();
        RefreshControlStates();
    }

    private void UnbindUI()
    {
        if (!isBound)
        {
            return;
        }

        uploadButton.clicked -= OnUploadClicked;
        clearUploadButton.clicked -= OnClearUploadClicked;
        styleButton.clicked -= ToggleStylePopup;
        stylePopupCloseButton.clicked -= CloseStylePopup;
        noStyleButton.clicked -= OnNoStyleClicked;
        sciFiStyleButton.clicked -= OnSciFiStyleClicked;
        cartoonStyleButton.clicked -= OnCartoonStyleClicked;
        promptInput.UnregisterValueChangedCallback(OnPromptChanged);
        generateButton.clicked -= OnGenerateClicked;
        downloadButton.clicked -= OnDownloadClicked;
        root.UnregisterCallback<PointerDownEvent>(
            OnRootPointerDown,
            TrickleDown.TrickleDown
        );

        generationCoordinator.StatusChanged -= OnGenerationStatusChanged;

        if (previewController != null)
        {
            previewController.PreviewRendered -= OnPreviewRendered;
        }

        isBound = false;
    }

    private bool ValidateRequiredElements()
    {
        bool valid = true;

        valid &= RequireElement(texturePanel, texturePanelName);
        valid &= RequireElement(uploadButton, uploadButtonName);
        valid &= RequireElement(clearUploadButton, clearUploadButtonName);
        valid &= RequireElement(uploadFileLabel, uploadFileLabelName);
        valid &= RequireElement(styleButton, styleButtonName);
        valid &= RequireElement(stylePopup, stylePopupName);
        valid &= RequireElement(stylePopupCloseButton, stylePopupCloseButtonName);
        valid &= RequireElement(noStyleButton, noStyleButtonName);
        valid &= RequireElement(sciFiStyleButton, sciFiStyleButtonName);
        valid &= RequireElement(cartoonStyleButton, cartoonStyleButtonName);
        valid &= RequireElement(promptInput, promptInputName);
        valid &= RequireElement(promptCounter, promptCounterName);
        valid &= RequireElement(generateButton, generateButtonName);
        valid &= RequireElement(downloadButton, downloadButtonName);
        valid &= RequireElement(errorLabel, errorLabelName);
        valid &= RequireElement(statusLabel, statusLabelName);

        return valid;
    }

    private bool RequireElement(VisualElement element, string elementName)
    {
        if (element != null)
        {
            return true;
        }

        Debug.LogError($"Texture Panel element not found: {elementName}", this);
        return false;
    }

    private void OnUploadClicked()
    {
        HideError();

        if (!LocalImageFilePicker.TryPickImage(
                out string selectedPath,
                out string pickerError))
        {
            if (!string.IsNullOrWhiteSpace(pickerError))
            {
                ShowError(pickerError);
            }

            return;
        }

        TryLoadUploadedImage(selectedPath);
    }

    private void TryLoadUploadedImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            ShowError("The selected image file does not exist.");
            return;
        }

        string extension = Path.GetExtension(imagePath).ToLowerInvariant();

        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
        {
            ShowError("Supported image formats are PNG, JPG, and JPEG.");
            return;
        }

        try
        {
            FileInfo fileInfo = new FileInfo(imagePath);
            long maximumBytes = maxImageFileSizeMegabytes * 1024L * 1024L;

            if (fileInfo.Length > maximumBytes)
            {
                ShowError(
                    $"Image must be {maxImageFileSizeMegabytes} MB or smaller."
                );
                return;
            }

            byte[] imageBytes = File.ReadAllBytes(imagePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!texture.LoadImage(imageBytes, false))
            {
                DestroyTexture(texture);
                ShowError("The selected image could not be decoded.");
                return;
            }

            DestroyUploadedPreviewTexture();
            uploadedPreviewTexture = texture;

            AITextureGenerationInputState state = GetInputState();
            state.uploadedReferenceImage = new AITextureImageData(
                imageBytes,
                texture.width,
                texture.height,
                extension == ".png" ? "image/png" : "image/jpeg",
                Path.GetFileName(imagePath)
            );

            generationCoordinator.SetInputState(state);
            ApplyUploadedImageToUI();
            HideError();
            RefreshControlStates();
        }
        catch (Exception exception)
        {
            ShowError($"Unable to load the selected image: {exception.Message}");
        }
    }

    private void OnClearUploadClicked()
    {
        AITextureGenerationInputState state = GetInputState();
        state.uploadedReferenceImage = null;
        generationCoordinator.SetInputState(state);

        DestroyUploadedPreviewTexture();
        ApplyUploadedImageToUI();
        HideError();
        RefreshControlStates();
    }

    private void ToggleStylePopup()
    {
        bool currentlyVisible =
            stylePopup.resolvedStyle.display != DisplayStyle.None;

        stylePopup.style.display = currentlyVisible
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }

    private void CloseStylePopup()
    {
        if (stylePopup != null)
        {
            stylePopup.style.display = DisplayStyle.None;
        }
    }

    private void OnNoStyleClicked()
    {
        SelectStyle(NoStyleId, "No Style", "");
    }

    private void OnSciFiStyleClicked()
    {
        SelectStyle(SciFiStyleId, "硬核科幻", SciFiPrompt);
    }

    private void OnCartoonStyleClicked()
    {
        SelectStyle(CartoonStyleId, "卡通", CartoonPrompt);
    }

    private void SelectStyle(string id, string displayName, string promptSuffix)
    {
        AITextureGenerationInputState state = GetInputState();
        state.selectedStyle = new AITextureStyleSelection
        {
            id = id,
            displayName = displayName,
            promptSuffix = promptSuffix
        };

        generationCoordinator.SetInputState(state);
        ApplySelectedStyleToUI();
        CloseStylePopup();
        HideError();
        RefreshControlStates();
    }

    private void OnPromptChanged(ChangeEvent<string> changeEvent)
    {
        AITextureGenerationInputState state = GetInputState();
        state.userPrompt = changeEvent.newValue ?? "";
        generationCoordinator.SetInputState(state);

        UpdatePromptCounter();
        HideError();
        RefreshControlStates();
    }

    private void OnGenerateClicked()
    {
        AITextureGenerationInputState state = GetInputState();

        if (!state.HasUserInput)
        {
            ShowError("Enter text, upload an image, or select a style.");
            RefreshControlStates();
            return;
        }

        if (generationCoordinator.IsGenerating)
        {
            ShowError("A texture generation request is already running.");
            RefreshControlStates();
            return;
        }

        if (requestBuilder == null)
        {
            ShowError("The texture generation request builder is unavailable.");
            return;
        }

        if (!requestBuilder.TryBuildRequest(
                state,
                out AITextureGenerationRequest request,
                out string buildError))
        {
            ShowError(buildError);
            RefreshControlStates();
            return;
        }

        HideError();
        GenerateRequested?.Invoke(state.Clone());

        if (!generationCoordinator.TryStartGeneration(
                request,
                out string requestId,
                out string startError))
        {
            ShowError(startError);
            RefreshControlStates();
            return;
        }

        Debug.Log(
            $"[AI Texture] Captured Editing reference and started request " +
            $"{requestId} at {request.outputWidth} x {request.outputHeight}.",
            this
        );
    }

    private void OnDownloadClicked()
    {
        if (!downloadAvailable)
        {
            ShowError("Generate or display a chest preview before downloading.");
            return;
        }

        HideError();
        HideStatus();
        DownloadRequested?.Invoke();

        if (DownloadRequested == null)
        {
            Debug.Log(
                "[AI Texture] Download is available. JPG export will be connected " +
                "in the download stage."
            );
        }
    }

    private void OnRootPointerDown(PointerDownEvent pointerEvent)
    {
        if (stylePopup == null ||
            stylePopup.resolvedStyle.display == DisplayStyle.None)
        {
            return;
        }

        VisualElement target = pointerEvent.target as VisualElement;

        if (IsElementOrDescendant(target, stylePopup) ||
            IsElementOrDescendant(target, styleButton))
        {
            return;
        }

        CloseStylePopup();
    }

    private static bool IsElementOrDescendant(
        VisualElement candidate,
        VisualElement ancestor)
    {
        VisualElement current = candidate;

        while (current != null)
        {
            if (current == ancestor)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void OnGenerationStatusChanged(AITextureGenerationStatus newStatus)
    {
        RefreshControlStates();
    }

    private void OnPreviewRendered()
    {
        downloadAvailable = true;
        RefreshControlStates();
    }

    public void SetDownloadAvailable(bool available)
    {
        downloadAvailable = available;
        RefreshControlStates();
    }

    public void ShowError(string message)
    {
        HideStatus();

        if (errorLabel == null)
        {
            Debug.LogWarning(message, this);
            return;
        }

        errorLabel.text = message ?? "";
        errorLabel.style.display = string.IsNullOrWhiteSpace(message)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }

    public void HideError()
    {
        if (errorLabel == null)
        {
            return;
        }

        errorLabel.text = "";
        errorLabel.style.display = DisplayStyle.None;
    }

    public void ShowStatus(string message)
    {
        HideError();

        if (statusLabel == null)
        {
            Debug.Log(message, this);
            return;
        }

        statusLabel.text = message ?? "";
        statusLabel.style.display = string.IsNullOrWhiteSpace(message)
            ? DisplayStyle.None
            : DisplayStyle.Flex;
    }

    public void HideStatus()
    {
        if (statusLabel == null)
        {
            return;
        }

        statusLabel.text = "";
        statusLabel.style.display = DisplayStyle.None;
    }

    private void RestoreInputStateToUI()
    {
        AITextureGenerationInputState state = GetInputState();
        promptInput.SetValueWithoutNotify(state.userPrompt ?? "");

        RestoreUploadedPreviewTexture(state.uploadedReferenceImage);
        ApplyUploadedImageToUI();
        ApplySelectedStyleToUI();
        UpdatePromptCounter();
    }

    private void RestoreUploadedPreviewTexture(AITextureImageData imageData)
    {
        DestroyUploadedPreviewTexture();

        if (imageData == null || !imageData.HasData)
        {
            return;
        }

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (texture.LoadImage(imageData.bytes, false))
        {
            uploadedPreviewTexture = texture;
        }
        else
        {
            DestroyTexture(texture);
        }
    }

    private void ApplyUploadedImageToUI()
    {
        AITextureGenerationInputState state = GetInputState();
        AITextureImageData imageData = state.uploadedReferenceImage;
        bool hasImage =
            imageData != null &&
            imageData.HasData &&
            uploadedPreviewTexture != null;

        if (hasImage)
        {
            uploadButton.style.backgroundImage =
                new StyleBackground(uploadedPreviewTexture);
        }
        else
        {
            uploadButton.style.backgroundImage = StyleKeyword.None;
        }

        uploadButton.text = hasImage ? "" : "点击上传图片";
        uploadFileLabel.text = hasImage
            ? $"{imageData.sourceName}  {imageData.width} x {imageData.height}"
            : "PNG / JPG / JPEG";
        clearUploadButton.style.display = hasImage
            ? DisplayStyle.Flex
            : DisplayStyle.None;
    }

    private void ApplySelectedStyleToUI()
    {
        AITextureStyleSelection style = GetInputState().selectedStyle;
        string displayName =
            style != null && style.IsSelected
                ? style.displayName
                : "No Style";

        styleButton.text = $"Reference Style: {displayName}";

        SetStyleButtonSelected(noStyleButton, style == null || !style.IsSelected);
        SetStyleButtonSelected(
            sciFiStyleButton,
            style != null && string.Equals(
                style.id,
                SciFiStyleId,
                StringComparison.Ordinal
            )
        );
        SetStyleButtonSelected(
            cartoonStyleButton,
            style != null && string.Equals(
                style.id,
                CartoonStyleId,
                StringComparison.Ordinal
            )
        );
    }

    private static void SetStyleButtonSelected(Button button, bool selected)
    {
        if (button == null)
        {
            return;
        }

        if (selected)
        {
            button.AddToClassList("texture-style-card--selected");
        }
        else
        {
            button.RemoveFromClassList("texture-style-card--selected");
        }
    }

    private void UpdatePromptCounter()
    {
        string value = promptInput.value ?? "";
        promptCounter.text = $"{value.Length}/{maxPromptCharacters}";
    }

    private void RefreshControlStates()
    {
        if (!isBound)
        {
            return;
        }

        bool generating = generationCoordinator.IsGenerating;
        bool hasInput = GetInputState().HasUserInput;

        uploadButton.SetEnabled(!generating);
        clearUploadButton.SetEnabled(!generating);
        styleButton.SetEnabled(!generating);
        promptInput.SetEnabled(!generating);
        generateButton.SetEnabled(!generating && hasInput);
        downloadButton.SetEnabled(!generating && downloadAvailable);
    }

    private AITextureGenerationInputState GetInputState()
    {
        if (generationCoordinator == null ||
            generationCoordinator.InputState == null)
        {
            return new AITextureGenerationInputState();
        }

        return generationCoordinator.InputState.Clone();
    }

    private void DestroyUploadedPreviewTexture()
    {
        DestroyTexture(uploadedPreviewTexture);
        uploadedPreviewTexture = null;
    }

    private static void DestroyTexture(Texture2D texture)
    {
        if (texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(texture);
        }
        else
        {
            DestroyImmediate(texture);
        }
    }

    private void OnValidate()
    {
        maxPromptCharacters = Mathf.Max(1, maxPromptCharacters);
        maxImageFileSizeMegabytes = Mathf.Max(1, maxImageFileSizeMegabytes);
    }
}
