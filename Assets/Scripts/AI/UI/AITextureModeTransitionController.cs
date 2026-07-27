using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
[DisallowMultipleComponent]
public sealed class AITextureModeTransitionController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private AITextureGenerationCoordinator generationCoordinator;
    [SerializeField] private Chest3DPreviewUIController previewController;
    [SerializeField] private ChestParameterPanelController parameterPanelController;

    [Header("UXML Element Names")]
    [SerializeField] private string overlayName =
        "AITextureEditingConfirmOverlay";
    [SerializeField] private string confirmButtonName =
        "AITextureEditingConfirmButton";
    [SerializeField] private string cancelButtonName =
        "AITextureEditingCancelButton";

    private VisualElement root;
    private VisualElement overlay;
    private Button confirmButton;
    private Button cancelButton;
    private bool isConfirmVisible;
    private bool isBound;

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

        if (previewController == null)
        {
            previewController = GetComponent<Chest3DPreviewUIController>();
        }

        if (parameterPanelController == null)
        {
            parameterPanelController =
                GetComponent<ChestParameterPanelController>();
        }
    }

    private void BindUI()
    {
        if (isBound)
        {
            return;
        }

        if (uiDocument == null ||
            generationCoordinator == null ||
            previewController == null ||
            parameterPanelController == null)
        {
            Debug.LogError(
                "AITextureModeTransitionController references are incomplete.",
                this);
            return;
        }

        root = uiDocument.rootVisualElement;
        overlay = root.Q<VisualElement>(overlayName);
        confirmButton = root.Q<Button>(confirmButtonName);
        cancelButton = root.Q<Button>(cancelButtonName);

        if (overlay == null || confirmButton == null || cancelButton == null)
        {
            Debug.LogError(
                "One or more Editing confirmation elements are missing.",
                this);
            return;
        }

        overlay.pickingMode = PickingMode.Position;
        confirmButton.clicked += OnConfirmClicked;
        cancelButton.clicked += OnCancelClicked;
        previewController.EditingModeRequested += OnEditingModeRequested;
        parameterPanelController.ParametersChanged += OnParametersChanged;
        generationCoordinator.ValidResultInvalidated += OnResultInvalidated;
        root.RegisterCallback<KeyDownEvent>(
            OnRootKeyDown,
            TrickleDown.TrickleDown);

        isBound = true;
        HideConfirmation();
    }

    private void UnbindUI()
    {
        if (!isBound)
        {
            return;
        }

        confirmButton.clicked -= OnConfirmClicked;
        cancelButton.clicked -= OnCancelClicked;
        previewController.EditingModeRequested -= OnEditingModeRequested;
        parameterPanelController.ParametersChanged -= OnParametersChanged;
        generationCoordinator.ValidResultInvalidated -= OnResultInvalidated;
        root.UnregisterCallback<KeyDownEvent>(
            OnRootKeyDown,
            TrickleDown.TrickleDown);

        isBound = false;
    }

    private void OnEditingModeRequested()
    {
        if (generationCoordinator.IsGenerating ||
            previewController.CurrentPreviewMode == ChestPreviewMode.Edit)
        {
            return;
        }

        if (generationCoordinator.HasValidResult)
        {
            ShowConfirmation();
            return;
        }

        previewController.EnterEditingMode();
    }

    private void ShowConfirmation()
    {
        isConfirmVisible = true;
        overlay.style.display = DisplayStyle.Flex;
        overlay.BringToFront();
        cancelButton.schedule.Execute(cancelButton.Focus);
    }

    private void HideConfirmation()
    {
        isConfirmVisible = false;

        if (overlay != null)
        {
            overlay.style.display = DisplayStyle.None;
        }
    }

    private void OnConfirmClicked()
    {
        if (!isConfirmVisible)
        {
            return;
        }

        HideConfirmation();
        previewController.EnterEditingMode();
    }

    private void OnCancelClicked()
    {
        HideConfirmation();
    }

    private void OnParametersChanged()
    {
        if (generationCoordinator.HasValidResult)
        {
            generationCoordinator.ClearSuccessfulResult();
        }
    }

    private void OnResultInvalidated()
    {
        HideConfirmation();
    }

    private void OnRootKeyDown(KeyDownEvent keyEvent)
    {
        if (!isConfirmVisible)
        {
            return;
        }

        if (keyEvent.keyCode == KeyCode.Escape)
        {
            OnCancelClicked();
            keyEvent.StopImmediatePropagation();
            return;
        }

        if (keyEvent.keyCode == KeyCode.Return ||
            keyEvent.keyCode == KeyCode.KeypadEnter)
        {
            OnConfirmClicked();
            keyEvent.StopImmediatePropagation();
            return;
        }

        if (keyEvent.keyCode == KeyCode.Tab)
        {
            cancelButton.Focus();
            keyEvent.StopImmediatePropagation();
        }
    }
}
