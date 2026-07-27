using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AITextureResultDisplayController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AITextureGenerationCoordinator generationCoordinator;
    [SerializeField] private Chest3DPreviewUIController previewController;
    [SerializeField] private AITexturePanelController panelController;

    private Texture2D displayedResultTexture;
    private string expectedRequestId = "";
    private int expectedWidth;
    private int expectedHeight;
    private bool isBound;

    public bool HasDisplayedResult => displayedResultTexture != null;
    public Texture2D DisplayedResultTexture => displayedResultTexture;

    private void OnEnable()
    {
        ResolveReferences();
        BindEvents();
        RestoreValidResult();
    }

    private void OnDisable()
    {
        UnbindEvents();
    }

    private void OnDestroy()
    {
        if (previewController != null)
        {
            previewController.ClearAITextureResult(false);
        }

        DestroyDisplayedTexture();
    }

    private void ResolveReferences()
    {
        if (generationCoordinator == null)
        {
            generationCoordinator =
                GetComponent<AITextureGenerationCoordinator>();
        }

        if (previewController == null)
        {
            previewController = GetComponent<Chest3DPreviewUIController>();
        }

        if (panelController == null)
        {
            panelController = GetComponent<AITexturePanelController>();
        }
    }

    private void BindEvents()
    {
        if (isBound || generationCoordinator == null)
        {
            return;
        }

        generationCoordinator.GenerationStarted += OnGenerationStarted;
        generationCoordinator.GenerationSucceeded += OnGenerationSucceeded;
        generationCoordinator.GenerationFailed += OnGenerationFailed;
        generationCoordinator.GenerationCancelled += OnGenerationCancelled;
        generationCoordinator.ValidResultInvalidated += OnResultInvalidated;
        isBound = true;
    }

    private void UnbindEvents()
    {
        if (!isBound || generationCoordinator == null)
        {
            return;
        }

        generationCoordinator.GenerationStarted -= OnGenerationStarted;
        generationCoordinator.GenerationSucceeded -= OnGenerationSucceeded;
        generationCoordinator.GenerationFailed -= OnGenerationFailed;
        generationCoordinator.GenerationCancelled -= OnGenerationCancelled;
        generationCoordinator.ValidResultInvalidated -= OnResultInvalidated;
        isBound = false;
    }

    private void RestoreValidResult()
    {
        if (generationCoordinator == null ||
            !generationCoordinator.HasValidResult ||
            generationCoordinator.LastSuccessfulResult == null)
        {
            return;
        }

        TryDisplayResult(
            generationCoordinator.LastSuccessfulResult,
            false,
            out _);
    }

    private void OnGenerationStarted(AITextureGenerationRequest request)
    {
        if (request == null)
        {
            expectedRequestId = "";
            expectedWidth = 0;
            expectedHeight = 0;
            return;
        }

        expectedRequestId = request.requestId ?? "";
        expectedWidth = request.outputWidth;
        expectedHeight = request.outputHeight;
        panelController?.HideError();
    }

    private void OnGenerationSucceeded(AITextureGenerationResult result)
    {
        if (!TryDisplayResult(result, true, out string errorMessage))
        {
            panelController?.ShowError(errorMessage);
            Debug.LogError($"[AI Texture] {errorMessage}", this);
        }

        ClearExpectedRequest();
    }

    private void OnGenerationFailed(AITextureGenerationResult result)
    {
        string message =
            result != null && !string.IsNullOrWhiteSpace(result.errorMessage)
                ? result.errorMessage
                : "AI texture generation failed.";

        panelController?.ShowError(message);
        ClearExpectedRequest();
    }

    private void OnGenerationCancelled(string requestId)
    {
        ClearExpectedRequest();
    }

    private void OnResultInvalidated()
    {
        if (previewController != null)
        {
            previewController.ClearAITextureResult(true);
        }

        DestroyDisplayedTexture();
    }

    private bool TryDisplayResult(
        AITextureGenerationResult result,
        bool requireExpectedRequest,
        out string errorMessage)
    {
        if (previewController == null)
        {
            errorMessage = "The chest preview controller is unavailable.";
            return false;
        }

        if (result == null || !result.IsSuccess)
        {
            errorMessage = "The AI service did not return a valid result image.";
            return false;
        }

        if (requireExpectedRequest &&
            !string.Equals(
                result.requestId,
                expectedRequestId,
                StringComparison.Ordinal))
        {
            errorMessage =
                "A stale AI texture result was ignored.";
            return false;
        }

        AITextureImageData imageData = result.resultImage;
        int requiredWidth = requireExpectedRequest
            ? expectedWidth
            : imageData.width;
        int requiredHeight = requireExpectedRequest
            ? expectedHeight
            : imageData.height;

        if (imageData.width != requiredWidth ||
            imageData.height != requiredHeight)
        {
            errorMessage =
                "The AI result dimensions do not match the requested canvas.";
            return false;
        }

        Texture2D decodedTexture = new Texture2D(
            2,
            2,
            TextureFormat.RGBA32,
            false,
            false)
        {
            name = $"AITextureResult_{result.requestId}"
        };

        try
        {
            if (!decodedTexture.LoadImage(imageData.bytes, false))
            {
                errorMessage = "The AI result image could not be decoded.";
                return false;
            }

            if (decodedTexture.width != requiredWidth ||
                decodedTexture.height != requiredHeight)
            {
                errorMessage =
                    "Decoded AI result dimensions do not match the requested canvas.";
                return false;
            }

            Texture2D previousTexture = displayedResultTexture;

            if (!previewController.DisplayAITextureResult(decodedTexture))
            {
                errorMessage = "The AI result could not be displayed.";
                return false;
            }

            displayedResultTexture = decodedTexture;
            decodedTexture = null;
            DestroyRuntimeObject(previousTexture);
            panelController?.HideError();
            errorMessage = "";
            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Unable to display the AI result image: {exception.Message}";
            return false;
        }
        finally
        {
            DestroyRuntimeObject(decodedTexture);
        }
    }

    private void ClearExpectedRequest()
    {
        expectedRequestId = "";
        expectedWidth = 0;
        expectedHeight = 0;
    }

    private void DestroyDisplayedTexture()
    {
        DestroyRuntimeObject(displayedResultTexture);
        displayedResultTexture = null;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
