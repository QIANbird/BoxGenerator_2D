using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
[DisallowMultipleComponent]
public sealed class AITextureGenerationModalController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private AITextureGenerationCoordinator generationCoordinator;

    [Header("UXML Element Names")]
    [SerializeField] private string overlayName =
        "AITextureGenerationModalOverlay";
    [SerializeField] private string statusLabelName =
        "AITextureGenerationStatusLabel";
    [SerializeField] private string loadingIndicatorName =
        "AITextureGenerationLoadingIndicator";
    [SerializeField] private string cancelButtonName =
        "AITextureGenerationCancelButton";

    private VisualElement root;
    private VisualElement overlay;
    private Label statusLabel;
    private Label loadingIndicator;
    private Button cancelButton;
    private IVisualElementScheduledItem animationSchedule;
    private int animationFrame;
    private bool isBound;

    public bool IsModalVisible =>
        overlay != null &&
        overlay.resolvedStyle.display != DisplayStyle.None;

    private void OnEnable()
    {
        ResolveReferences();
        BindUI();
    }

    private void OnDisable()
    {
        UnbindUI();
    }

    private void ResolveReferences()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (generationCoordinator == null)
        {
            generationCoordinator =
                GetComponent<AITextureGenerationCoordinator>();
        }
    }

    private void BindUI()
    {
        if (isBound)
        {
            return;
        }

        if (uiDocument == null || generationCoordinator == null)
        {
            Debug.LogError(
                "AITextureGenerationModalController requires a UIDocument " +
                "and AITextureGenerationCoordinator.",
                this);
            return;
        }

        root = uiDocument.rootVisualElement;
        overlay = root.Q<VisualElement>(overlayName);
        statusLabel = root.Q<Label>(statusLabelName);
        loadingIndicator = root.Q<Label>(loadingIndicatorName);
        cancelButton = root.Q<Button>(cancelButtonName);

        if (overlay == null ||
            statusLabel == null ||
            loadingIndicator == null ||
            cancelButton == null)
        {
            Debug.LogError(
                "One or more AI generation modal elements are missing.",
                this);
            return;
        }

        overlay.pickingMode = PickingMode.Position;
        cancelButton.clicked += OnCancelClicked;
        root.RegisterCallback<KeyDownEvent>(
            OnRootKeyDown,
            TrickleDown.TrickleDown);
        generationCoordinator.StatusChanged += OnGenerationStatusChanged;

        animationSchedule = overlay.schedule
            .Execute(UpdateLoadingAnimation)
            .Every(320);
        animationSchedule.Pause();
        isBound = true;

        RefreshVisibility(generationCoordinator.Status);
    }

    private void UnbindUI()
    {
        if (!isBound)
        {
            return;
        }

        cancelButton.clicked -= OnCancelClicked;
        root.UnregisterCallback<KeyDownEvent>(
            OnRootKeyDown,
            TrickleDown.TrickleDown);
        generationCoordinator.StatusChanged -= OnGenerationStatusChanged;
        animationSchedule?.Pause();
        animationSchedule = null;
        isBound = false;
    }

    private void OnGenerationStatusChanged(
        AITextureGenerationStatus newStatus)
    {
        RefreshVisibility(newStatus);
    }

    private void RefreshVisibility(AITextureGenerationStatus status)
    {
        if (overlay == null)
        {
            return;
        }

        if (status == AITextureGenerationStatus.Generating)
        {
            ShowModal();
        }
        else
        {
            HideModal();
        }
    }

    private void ShowModal()
    {
        animationFrame = 0;
        statusLabel.text = "正在生成纹理，请稍候...";
        cancelButton.SetEnabled(true);
        overlay.style.display = DisplayStyle.Flex;
        overlay.BringToFront();
        UpdateLoadingAnimation();
        animationSchedule?.Resume();
        cancelButton.schedule.Execute(cancelButton.Focus);
    }

    private void HideModal()
    {
        animationSchedule?.Pause();
        overlay.style.display = DisplayStyle.None;
    }

    private void UpdateLoadingAnimation()
    {
        if (generationCoordinator == null ||
            !generationCoordinator.IsGenerating ||
            loadingIndicator == null)
        {
            return;
        }

        switch (animationFrame % 3)
        {
            case 0:
                loadingIndicator.text = "●  ○  ○";
                break;
            case 1:
                loadingIndicator.text = "○  ●  ○";
                break;
            default:
                loadingIndicator.text = "○  ○  ●";
                break;
        }

        animationFrame++;
    }

    private void OnCancelClicked()
    {
        if (generationCoordinator == null ||
            !generationCoordinator.IsGenerating)
        {
            return;
        }

        generationCoordinator.CancelCurrentGeneration();
    }

    private void OnRootKeyDown(KeyDownEvent keyEvent)
    {
        if (generationCoordinator == null ||
            !generationCoordinator.IsGenerating)
        {
            return;
        }

        if (keyEvent.keyCode == KeyCode.Escape)
        {
            OnCancelClicked();
            keyEvent.StopImmediatePropagation();
            return;
        }

        if (keyEvent.keyCode == KeyCode.Tab)
        {
            cancelButton?.Focus();
            keyEvent.StopImmediatePropagation();
        }
    }
}
