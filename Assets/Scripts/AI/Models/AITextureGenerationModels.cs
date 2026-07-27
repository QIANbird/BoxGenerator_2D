using System;
using UnityEngine;

public enum AITextureGenerationStatus
{
    Idle,
    Generating,
    Succeeded,
    Failed,
    Cancelled
}

public enum AITextureGenerationErrorType
{
    None,
    Validation,
    ServiceUnavailable,
    Failed,
    Timeout,
    Cancelled,
    InvalidResponse,
    Unexpected
}

[Serializable]
public sealed class AITextureImageData
{
    public string sourceName = "";
    public string mimeType = "image/png";
    public int width;
    public int height;

    // Image bytes are runtime data. Keeping them out of Unity serialization avoids
    // putting large byte arrays into scenes and prefabs.
    [NonSerialized] public byte[] bytes;

    public bool HasData
    {
        get
        {
            return bytes != null &&
                   bytes.Length > 0 &&
                   width > 0 &&
                   height > 0;
        }
    }

    public AITextureImageData()
    {
    }

    public AITextureImageData(
        byte[] imageBytes,
        int imageWidth,
        int imageHeight,
        string imageMimeType,
        string imageSourceName = "")
    {
        bytes = imageBytes;
        width = imageWidth;
        height = imageHeight;
        mimeType = string.IsNullOrWhiteSpace(imageMimeType) ? "image/png" : imageMimeType;
        sourceName = imageSourceName ?? "";
    }

    public AITextureImageData Clone()
    {
        byte[] bytesCopy = null;

        if (bytes != null)
        {
            bytesCopy = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, bytesCopy, 0, bytes.Length);
        }

        return new AITextureImageData(
            bytesCopy,
            width,
            height,
            mimeType,
            sourceName
        );
    }
}

[Serializable]
public sealed class AITextureStyleSelection
{
    public string id = "";
    public string displayName = "";

    [TextArea(2, 5)]
    public string promptSuffix = "";

    public bool IsSelected
    {
        get
        {
            return !string.IsNullOrWhiteSpace(id) &&
                   !string.Equals(id, "none", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(id, "no_style", StringComparison.OrdinalIgnoreCase);
        }
    }

    public AITextureStyleSelection Clone()
    {
        return new AITextureStyleSelection
        {
            id = id,
            displayName = displayName,
            promptSuffix = promptSuffix
        };
    }
}

[Serializable]
public sealed class AITextureGenerationInputState
{
    [TextArea(3, 8)]
    public string userPrompt = "";

    public AITextureStyleSelection selectedStyle = new AITextureStyleSelection();

    [NonSerialized] public AITextureImageData uploadedReferenceImage;

    public bool HasUserInput
    {
        get
        {
            return !string.IsNullOrWhiteSpace(userPrompt) ||
                   (selectedStyle != null && selectedStyle.IsSelected) ||
                   (uploadedReferenceImage != null && uploadedReferenceImage.HasData);
        }
    }

    public AITextureGenerationInputState Clone()
    {
        return new AITextureGenerationInputState
        {
            userPrompt = userPrompt,
            selectedStyle = selectedStyle != null
                ? selectedStyle.Clone()
                : new AITextureStyleSelection(),
            uploadedReferenceImage = uploadedReferenceImage != null
                ? uploadedReferenceImage.Clone()
                : null
        };
    }

    public void Clear()
    {
        userPrompt = "";
        selectedStyle = new AITextureStyleSelection();
        uploadedReferenceImage = null;
    }
}

[Serializable]
public sealed class AITextureGenerationRequest
{
    public string requestId = "";
    public string createdAtUtc = "";

    [TextArea(3, 8)]
    public string userPrompt = "";

    public string styleId = "";
    public string stylePrompt = "";

    [TextArea(4, 10)]
    public string systemPrompt = "";

    [TextArea(4, 12)]
    public string finalPrompt = "";

    [NonSerialized] public AITextureImageData baseShapeImage;
    [NonSerialized] public AITextureImageData uploadedReferenceImage;

    public ChestLatentParams chestParameters;
    public Vector3 previewEulerAngles;

    public int inputWidth;
    public int inputHeight;
    public int outputWidth;
    public int outputHeight;

    public bool HasSelectedStyle
    {
        get
        {
            return !string.IsNullOrWhiteSpace(styleId) &&
                   !string.Equals(styleId, "none", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(styleId, "no_style", StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool HasUserInput
    {
        get
        {
            return !string.IsNullOrWhiteSpace(userPrompt) ||
                   HasSelectedStyle ||
                   (uploadedReferenceImage != null && uploadedReferenceImage.HasData);
        }
    }

    public void AssignNewIdentity()
    {
        requestId = Guid.NewGuid().ToString("N");
        createdAtUtc = DateTime.UtcNow.ToString("O");
    }

    public bool Validate(out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            errorMessage = "Request ID is empty.";
            return false;
        }

        if (baseShapeImage == null || !baseShapeImage.HasData)
        {
            errorMessage = "Editing base-shape image is missing or invalid.";
            return false;
        }

        if (!HasUserInput)
        {
            errorMessage = "At least one user input is required: text, uploaded image, or style.";
            return false;
        }

        if (inputWidth <= 0 || inputHeight <= 0)
        {
            errorMessage = "Input width and height must be greater than zero.";
            return false;
        }

        if (baseShapeImage.width != inputWidth ||
            baseShapeImage.height != inputHeight)
        {
            errorMessage =
                "Editing reference dimensions do not match the request input size.";
            return false;
        }

        if (outputWidth <= 0 || outputHeight <= 0)
        {
            errorMessage = "Output width and height must be greater than zero.";
            return false;
        }

        if (inputWidth != outputWidth || inputHeight != outputHeight)
        {
            errorMessage =
                "Editing reference and target output dimensions must match.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(finalPrompt))
        {
            errorMessage = "Final prompt is empty.";
            return false;
        }

        errorMessage = "";
        return true;
    }

    public AITextureGenerationRequest Clone()
    {
        return new AITextureGenerationRequest
        {
            requestId = requestId,
            createdAtUtc = createdAtUtc,
            userPrompt = userPrompt,
            styleId = styleId,
            stylePrompt = stylePrompt,
            systemPrompt = systemPrompt,
            finalPrompt = finalPrompt,
            baseShapeImage = baseShapeImage != null ? baseShapeImage.Clone() : null,
            uploadedReferenceImage = uploadedReferenceImage != null
                ? uploadedReferenceImage.Clone()
                : null,
            chestParameters = chestParameters != null ? chestParameters.Clone() : null,
            previewEulerAngles = previewEulerAngles,
            inputWidth = inputWidth,
            inputHeight = inputHeight,
            outputWidth = outputWidth,
            outputHeight = outputHeight
        };
    }
}

[Serializable]
public sealed class AITextureGenerationResult
{
    public string requestId = "";
    public AITextureGenerationStatus status = AITextureGenerationStatus.Idle;
    public AITextureGenerationErrorType errorType = AITextureGenerationErrorType.None;
    public string errorMessage = "";
    public string startedAtUtc = "";
    public string completedAtUtc = "";
    public string serviceMetadata = "";

    [NonSerialized] public AITextureImageData resultImage;

    public bool IsSuccess
    {
        get
        {
            return status == AITextureGenerationStatus.Succeeded &&
                   resultImage != null &&
                   resultImage.HasData;
        }
    }

    public static AITextureGenerationResult Success(
        string requestId,
        AITextureImageData resultImage,
        string startedAtUtc,
        string serviceMetadata = "")
    {
        return new AITextureGenerationResult
        {
            requestId = requestId,
            status = AITextureGenerationStatus.Succeeded,
            errorType = AITextureGenerationErrorType.None,
            resultImage = resultImage,
            startedAtUtc = startedAtUtc,
            completedAtUtc = DateTime.UtcNow.ToString("O"),
            serviceMetadata = serviceMetadata ?? ""
        };
    }

    public static AITextureGenerationResult Failure(
        string requestId,
        AITextureGenerationErrorType errorType,
        string errorMessage,
        string startedAtUtc)
    {
        return new AITextureGenerationResult
        {
            requestId = requestId,
            status = AITextureGenerationStatus.Failed,
            errorType = errorType,
            errorMessage = errorMessage ?? "AI texture generation failed.",
            startedAtUtc = startedAtUtc,
            completedAtUtc = DateTime.UtcNow.ToString("O")
        };
    }

    public static AITextureGenerationResult Cancelled(
        string requestId,
        string startedAtUtc)
    {
        return new AITextureGenerationResult
        {
            requestId = requestId,
            status = AITextureGenerationStatus.Cancelled,
            errorType = AITextureGenerationErrorType.Cancelled,
            errorMessage = "",
            startedAtUtc = startedAtUtc,
            completedAtUtc = DateTime.UtcNow.ToString("O")
        };
    }
}
