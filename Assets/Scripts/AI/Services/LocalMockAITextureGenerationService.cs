using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum MockAITextureGenerationOutcome
{
    Success,
    Failure,
    Timeout
}

[DisallowMultipleComponent]
public sealed class LocalMockAITextureGenerationService :
    MonoBehaviour,
    IAITextureGenerationService
{
    [Header("Mock Timing")]
    [SerializeField] private float delaySeconds = 2f;

    [Header("Mock Outcome")]
    [SerializeField] private MockAITextureGenerationOutcome outcome =
        MockAITextureGenerationOutcome.Success;

    [SerializeField] private string failureMessage =
        "Local mock AI texture generation failed.";

    [Header("Optional Success Image")]
    [Tooltip("If empty, the mock returns a copy of the Editing base-shape image.")]
    [SerializeField] private Texture2D mockResultTexture;

    [Header("Local Style Preview")]
    [Tooltip("Applies a lightweight local color treatment when a preset style is selected.")]
    [SerializeField] private bool previewSelectedStyle = true;

    [Range(0f, 1f)]
    [SerializeField] private float stylePreviewStrength = 0.55f;

    [Header("Debug")]
    [SerializeField] private bool logRequests = true;

    private readonly HashSet<string> cancelledRequestIds = new HashSet<string>();

    public string ServiceName => "Local Mock AI Texture Service";

    public IEnumerator Generate(
        AITextureGenerationRequest request,
        AITextureGenerationCancellationToken cancellationToken,
        Action<AITextureGenerationResult> onCompleted)
    {
        string startedAtUtc = DateTime.UtcNow.ToString("O");

        if (request == null)
        {
            onCompleted?.Invoke(AITextureGenerationResult.Failure(
                "",
                AITextureGenerationErrorType.Validation,
                "Generation request is null.",
                startedAtUtc
            ));
            yield break;
        }

        if (!request.Validate(out string validationError))
        {
            onCompleted?.Invoke(AITextureGenerationResult.Failure(
                request.requestId,
                AITextureGenerationErrorType.Validation,
                validationError,
                startedAtUtc
            ));
            yield break;
        }

        if (logRequests)
        {
            Debug.Log(
                $"[{ServiceName}] Start request {request.requestId}. " +
                $"Input={request.inputWidth}x{request.inputHeight}, " +
                $"Output={request.outputWidth}x{request.outputHeight}, " +
                $"View={request.previewEulerAngles}, " +
                $"Text={!string.IsNullOrWhiteSpace(request.userPrompt)}, " +
                $"Upload={request.uploadedReferenceImage != null && request.uploadedReferenceImage.HasData}, " +
                $"Style={request.HasSelectedStyle}"
            );
            Debug.Log(
                $"[{ServiceName}] Final prompt for {request.requestId}:\n" +
                request.finalPrompt
            );
        }

        float elapsed = 0f;
        float safeDelay = Mathf.Max(0f, delaySeconds);

        while (elapsed < safeDelay)
        {
            if (IsCancelled(request.requestId, cancellationToken))
            {
                CompleteCancellation(request.requestId, startedAtUtc, onCompleted);
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (IsCancelled(request.requestId, cancellationToken))
        {
            CompleteCancellation(request.requestId, startedAtUtc, onCompleted);
            yield break;
        }

        if (outcome == MockAITextureGenerationOutcome.Failure)
        {
            cancelledRequestIds.Remove(request.requestId);
            onCompleted?.Invoke(AITextureGenerationResult.Failure(
                request.requestId,
                AITextureGenerationErrorType.Failed,
                failureMessage,
                startedAtUtc
            ));
            yield break;
        }

        if (outcome == MockAITextureGenerationOutcome.Timeout)
        {
            cancelledRequestIds.Remove(request.requestId);
            onCompleted?.Invoke(AITextureGenerationResult.Failure(
                request.requestId,
                AITextureGenerationErrorType.Timeout,
                "Local mock AI texture generation timed out.",
                startedAtUtc
            ));
            yield break;
        }

        AITextureImageData resultImage;

        try
        {
            resultImage = CreateSuccessImage(request);
        }
        catch (Exception exception)
        {
            cancelledRequestIds.Remove(request.requestId);
            onCompleted?.Invoke(AITextureGenerationResult.Failure(
                request.requestId,
                AITextureGenerationErrorType.Unexpected,
                $"Failed to create local mock result: {exception.Message}",
                startedAtUtc
            ));
            yield break;
        }

        if (IsCancelled(request.requestId, cancellationToken))
        {
            CompleteCancellation(request.requestId, startedAtUtc, onCompleted);
            yield break;
        }

        cancelledRequestIds.Remove(request.requestId);

        onCompleted?.Invoke(AITextureGenerationResult.Success(
            request.requestId,
            resultImage,
            startedAtUtc,
            $"service={ServiceName}; outcome=success"
        ));
    }

    public void Cancel(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        cancelledRequestIds.Add(requestId);

        if (logRequests)
        {
            Debug.Log($"[{ServiceName}] Cancel request {requestId}.");
        }
    }

    private bool IsCancelled(
        string requestId,
        AITextureGenerationCancellationToken cancellationToken)
    {
        return (cancellationToken != null && cancellationToken.IsCancellationRequested) ||
               cancelledRequestIds.Contains(requestId);
    }

    private void CompleteCancellation(
        string requestId,
        string startedAtUtc,
        Action<AITextureGenerationResult> onCompleted)
    {
        cancelledRequestIds.Remove(requestId);
        onCompleted?.Invoke(AITextureGenerationResult.Cancelled(requestId, startedAtUtc));
    }

    private AITextureImageData CreateSuccessImage(AITextureGenerationRequest request)
    {
        if (mockResultTexture == null)
        {
            if (request.baseShapeImage.width == request.outputWidth &&
                request.baseShapeImage.height == request.outputHeight &&
                (!previewSelectedStyle || !request.HasSelectedStyle))
            {
                AITextureImageData baseImageCopy = request.baseShapeImage.Clone();
                baseImageCopy.sourceName = "mock_base_shape_copy.png";
                return baseImageCopy;
            }

            Texture2D decodedBaseImage = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D resizedBaseImage = null;

            try
            {
                if (!decodedBaseImage.LoadImage(request.baseShapeImage.bytes, false))
                {
                    throw new InvalidOperationException(
                        "Editing base-shape image could not be decoded."
                    );
                }

                Texture2D resultTexture = decodedBaseImage;

                if (decodedBaseImage.width != request.outputWidth ||
                    decodedBaseImage.height != request.outputHeight)
                {
                    resizedBaseImage = CreateReadableTexture(
                        decodedBaseImage,
                        request.outputWidth,
                        request.outputHeight
                    );
                    resultTexture = resizedBaseImage;
                }

                if (previewSelectedStyle && request.HasSelectedStyle)
                {
                    ApplySelectedStylePreview(
                        resultTexture,
                        request.styleId,
                        stylePreviewStrength);
                }

                return new AITextureImageData(
                    resultTexture.EncodeToPNG(),
                    request.outputWidth,
                    request.outputHeight,
                    "image/png",
                    request.HasSelectedStyle
                        ? $"mock_{request.styleId}_preview.png"
                        : "mock_base_shape_resized.png"
                );
            }
            finally
            {
                DestroyRuntimeObject(decodedBaseImage);
                DestroyRuntimeObject(resizedBaseImage);
            }
        }

        Texture2D resizedTexture = CreateReadableTexture(
            mockResultTexture,
            request.outputWidth,
            request.outputHeight
        );

        try
        {
            return new AITextureImageData(
                resizedTexture.EncodeToPNG(),
                request.outputWidth,
                request.outputHeight,
                "image/png",
                "mock_result.png"
            );
        }
        finally
        {
            DestroyRuntimeObject(resizedTexture);
        }
    }

    private static Texture2D CreateReadableTexture(
        Texture source,
        int width,
        int height)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(
            width,
            height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB
        );

        RenderTexture previousActive = RenderTexture.active;

        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;

            Texture2D result = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                false,
                false
            );

            result.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            result.Apply(false, false);
            return result;
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    private static void ApplySelectedStylePreview(
        Texture2D texture,
        string styleId,
        float strength)
    {
        if (texture == null || string.IsNullOrWhiteSpace(styleId))
        {
            return;
        }

        strength = Mathf.Clamp01(strength);
        Color32[] pixels = texture.GetPixels32();
        bool sciFi = string.Equals(
            styleId,
            "hard_scifi",
            StringComparison.OrdinalIgnoreCase);
        bool cartoon = string.Equals(
            styleId,
            "cartoon",
            StringComparison.OrdinalIgnoreCase);

        if (!sciFi && !cartoon)
        {
            return;
        }

        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 source = pixels[i];

            // Keep the white/transparent preview canvas untouched so the mock
            // treatment changes the chest surface without changing composition.
            if (source.a == 0 ||
                (source.r >= 245 && source.g >= 245 && source.b >= 245))
            {
                continue;
            }

            byte targetRed;
            byte targetGreen;
            byte targetBlue;

            if (sciFi)
            {
                float luminance =
                    source.r * 0.2126f +
                    source.g * 0.7152f +
                    source.b * 0.0722f;
                float contrast = Mathf.Clamp(
                    (luminance - 128f) * 1.18f + 128f,
                    0f,
                    255f);

                targetRed = (byte)Mathf.Clamp(
                    20f + contrast * 0.72f,
                    0f,
                    255f);
                targetGreen = (byte)Mathf.Clamp(
                    28f + contrast * 0.84f,
                    0f,
                    255f);
                targetBlue = (byte)Mathf.Clamp(
                    42f + contrast * 0.96f,
                    0f,
                    255f);
            }
            else
            {
                targetRed = QuantizeColor(source.r, 5);
                targetGreen = QuantizeColor(source.g, 5);
                targetBlue = QuantizeColor(source.b, 5);
            }

            pixels[i] = new Color32(
                LerpByte(source.r, targetRed, strength),
                LerpByte(source.g, targetGreen, strength),
                LerpByte(source.b, targetBlue, strength),
                source.a);
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
    }

    private static byte QuantizeColor(byte value, int levels)
    {
        int safeLevels = Mathf.Max(2, levels);
        float step = 255f / (safeLevels - 1);
        return (byte)Mathf.Clamp(
            Mathf.Round(value / step) * step,
            0f,
            255f);
    }

    private static byte LerpByte(byte from, byte to, float amount)
    {
        return (byte)Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(from, to, amount)),
            0,
            255);
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

    private void OnValidate()
    {
        delaySeconds = Mathf.Max(0f, delaySeconds);
        stylePreviewStrength = Mathf.Clamp01(stylePreviewStrength);
    }

    private void OnDisable()
    {
        cancelledRequestIds.Clear();
    }
}
