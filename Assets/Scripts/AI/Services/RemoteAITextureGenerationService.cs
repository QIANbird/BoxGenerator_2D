using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public sealed class RemoteAITextureGenerationService :
    MonoBehaviour,
    IAITextureGenerationService
{
    private const int MaximumGatewayImageBytes = 20 * 1024 * 1024;

    [Header("Gateway")]
    [Tooltip("Only configure the application Gateway URL here. Never enter an Alibaba API key.")]
    [SerializeField] private string gatewayBaseUrl = "http://127.0.0.1:5088";

    [Header("Timing")]
    [Min(0.25f)]
    [SerializeField] private float pollIntervalSeconds = 1f;

    [Min(10)]
    [SerializeField] private int requestTimeoutSeconds = 45;

    [Min(30)]
    [SerializeField] private int totalGenerationTimeoutSeconds = 360;

    [Header("Image Transport")]
    [Range(75, 100)]
    [SerializeField] private int inputJpegQuality = 95;

    [Header("Debug")]
    [Tooltip("Logs IDs and statuses only. Prompts, images and credentials are never logged.")]
    [SerializeField] private bool logRequestLifecycle = true;

    private readonly HashSet<string> cancelledRequestIds =
        new HashSet<string>(StringComparer.Ordinal);

    private UnityWebRequest activeTransport;
    private string activeTransportRequestId = "";

    public string ServiceName => "Box Generator AI Gateway";

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

        if (!TryGetGatewayUrl(out string baseUrl, out string urlError))
        {
            onCompleted?.Invoke(AITextureGenerationResult.Failure(
                request.requestId,
                AITextureGenerationErrorType.Validation,
                urlError,
                startedAtUtc
            ));
            yield break;
        }

        cancelledRequestIds.Remove(request.requestId);

        byte[] baseShapeJpeg;
        byte[] styleReferenceJpeg = null;

        try
        {
            baseShapeJpeg = ConvertToOpaqueJpeg(
                request.baseShapeImage,
                inputJpegQuality);

            if (request.uploadedReferenceImage != null &&
                request.uploadedReferenceImage.HasData)
            {
                styleReferenceJpeg = ConvertToOpaqueJpeg(
                    request.uploadedReferenceImage,
                    inputJpegQuality);
            }
        }
        catch (Exception exception)
        {
            onCompleted?.Invoke(AITextureGenerationResult.Failure(
                request.requestId,
                AITextureGenerationErrorType.Validation,
                $"Failed to prepare an input image: {exception.Message}",
                startedAtUtc
            ));
            yield break;
        }

        if (baseShapeJpeg.Length > MaximumGatewayImageBytes ||
            (styleReferenceJpeg != null &&
             styleReferenceJpeg.Length > MaximumGatewayImageBytes))
        {
            onCompleted?.Invoke(AITextureGenerationResult.Failure(
                request.requestId,
                AITextureGenerationErrorType.Validation,
                "A normalized input image exceeds the 20 MB gateway limit.",
                startedAtUtc
            ));
            yield break;
        }

        List<IMultipartFormSection> form = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("requestId", request.requestId),
            new MultipartFormDataSection("prompt", request.finalPrompt),
            new MultipartFormDataSection("styleId", request.styleId ?? ""),
            new MultipartFormDataSection(
                "targetWidth",
                request.outputWidth.ToString()),
            new MultipartFormDataSection(
                "targetHeight",
                request.outputHeight.ToString()),
            new MultipartFormFileSection(
                "baseShapeImage",
                baseShapeJpeg,
                "editing-base-shape.jpg",
                "image/jpeg")
        };

        if (styleReferenceJpeg != null)
        {
            form.Add(new MultipartFormFileSection(
                "styleReferenceImage",
                styleReferenceJpeg,
                "style-reference.jpg",
                "image/jpeg"));
        }

        string createUrl = $"{baseUrl}/api/v1/generations";
        GatewayGenerationResponse gatewayResponse = null;

        using (UnityWebRequest createRequest = UnityWebRequest.Post(createUrl, form))
        {
            ConfigureRequest(createRequest, requestTimeoutSeconds);
            SetActiveTransport(request.requestId, createRequest);

            if (logRequestLifecycle)
            {
                Debug.Log(
                    $"[{ServiceName}] Submit request {request.requestId}. " +
                    $"Target={request.outputWidth}x{request.outputHeight}; " +
                    $"StyleReference={styleReferenceJpeg != null}."
                );
            }

            yield return createRequest.SendWebRequest();
            ClearActiveTransport(createRequest);

            if (IsCancelled(request.requestId, cancellationToken))
            {
                yield break;
            }

            TryParseGatewayResponse(
                createRequest.downloadHandler != null
                    ? createRequest.downloadHandler.text
                    : "",
                out gatewayResponse);

            if (!IsSuccessful(createRequest))
            {
                CompleteTransportFailure(
                    request,
                    startedAtUtc,
                    createRequest,
                    gatewayResponse,
                    onCompleted);
                yield break;
            }

            if (gatewayResponse == null ||
                !string.Equals(
                    gatewayResponse.requestId,
                    request.requestId,
                    StringComparison.Ordinal))
            {
                onCompleted?.Invoke(AITextureGenerationResult.Failure(
                    request.requestId,
                    AITextureGenerationErrorType.InvalidResponse,
                    "Gateway returned an invalid generation response.",
                    startedAtUtc
                ));
                yield break;
            }
        }

        float generationStartedAt = Time.realtimeSinceStartup;

        while (!string.Equals(
                   gatewayResponse.status,
                   "succeeded",
                   StringComparison.OrdinalIgnoreCase))
        {
            if (IsCancelled(request.requestId, cancellationToken))
            {
                yield break;
            }

            if (IsFailedStatus(gatewayResponse.status))
            {
                onCompleted?.Invoke(CreateGatewayFailure(
                    request,
                    startedAtUtc,
                    gatewayResponse));
                yield break;
            }

            if (Time.realtimeSinceStartup - generationStartedAt >=
                totalGenerationTimeoutSeconds)
            {
                StartCoroutine(SendGatewayCancellation(
                    baseUrl,
                    request.requestId));

                onCompleted?.Invoke(AITextureGenerationResult.Failure(
                    request.requestId,
                    AITextureGenerationErrorType.Timeout,
                    "AI texture generation timed out.",
                    startedAtUtc
                ));
                yield break;
            }

            yield return new WaitForSecondsRealtime(
                Mathf.Max(0.25f, pollIntervalSeconds));

            string statusUrl =
                $"{baseUrl}/api/v1/generations/{UnityWebRequest.EscapeURL(request.requestId)}";

            using (UnityWebRequest statusRequest = UnityWebRequest.Get(statusUrl))
            {
                ConfigureRequest(statusRequest, requestTimeoutSeconds);
                SetActiveTransport(request.requestId, statusRequest);
                yield return statusRequest.SendWebRequest();
                ClearActiveTransport(statusRequest);

                if (IsCancelled(request.requestId, cancellationToken))
                {
                    yield break;
                }

                GatewayGenerationResponse polledResponse = null;
                TryParseGatewayResponse(
                    statusRequest.downloadHandler != null
                        ? statusRequest.downloadHandler.text
                        : "",
                    out polledResponse);

                if (!IsSuccessful(statusRequest))
                {
                    CompleteTransportFailure(
                        request,
                        startedAtUtc,
                        statusRequest,
                        polledResponse,
                        onCompleted);
                    yield break;
                }

                if (polledResponse == null ||
                    !string.Equals(
                        polledResponse.requestId,
                        request.requestId,
                        StringComparison.Ordinal))
                {
                    onCompleted?.Invoke(AITextureGenerationResult.Failure(
                        request.requestId,
                        AITextureGenerationErrorType.InvalidResponse,
                        "Gateway returned an invalid task status.",
                        startedAtUtc
                    ));
                    yield break;
                }

                gatewayResponse = polledResponse;
            }
        }

        if (!gatewayResponse.resultAvailable)
        {
            onCompleted?.Invoke(AITextureGenerationResult.Failure(
                request.requestId,
                AITextureGenerationErrorType.InvalidResponse,
                "Gateway reported success without an available image.",
                startedAtUtc
            ));
            yield break;
        }

        string resultUrl =
            $"{baseUrl}/api/v1/generations/{UnityWebRequest.EscapeURL(request.requestId)}/result";
        byte[] resultBytes;

        using (UnityWebRequest resultRequest = UnityWebRequest.Get(resultUrl))
        {
            ConfigureRequest(resultRequest, requestTimeoutSeconds);
            SetActiveTransport(request.requestId, resultRequest);
            yield return resultRequest.SendWebRequest();
            ClearActiveTransport(resultRequest);

            if (IsCancelled(request.requestId, cancellationToken))
            {
                yield break;
            }

            if (!IsSuccessful(resultRequest))
            {
                CompleteTransportFailure(
                    request,
                    startedAtUtc,
                    resultRequest,
                    null,
                    onCompleted);
                yield break;
            }

            resultBytes = resultRequest.downloadHandler != null
                ? resultRequest.downloadHandler.data
                : null;
        }

        AITextureImageData resultImage;

        try
        {
            resultImage = NormalizeResultImage(
                resultBytes,
                request.outputWidth,
                request.outputHeight);
        }
        catch (Exception exception)
        {
            onCompleted?.Invoke(AITextureGenerationResult.Failure(
                request.requestId,
                AITextureGenerationErrorType.InvalidResponse,
                $"Gateway result image is invalid: {exception.Message}",
                startedAtUtc
            ));
            yield break;
        }

        if (IsCancelled(request.requestId, cancellationToken))
        {
            yield break;
        }

        cancelledRequestIds.Remove(request.requestId);

        string metadata =
            $"service={ServiceName}; providerRequestId=" +
            $"{gatewayResponse.providerRequestId}; " +
            $"providerMetadata={gatewayResponse.providerMetadata}";

        if (logRequestLifecycle)
        {
            Debug.Log(
                $"[{ServiceName}] Request {request.requestId} succeeded."
            );
        }

        onCompleted?.Invoke(AITextureGenerationResult.Success(
            request.requestId,
            resultImage,
            startedAtUtc,
            metadata
        ));
    }

    public void Cancel(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        cancelledRequestIds.Add(requestId);

        if (string.Equals(
                activeTransportRequestId,
                requestId,
                StringComparison.Ordinal))
        {
            activeTransport?.Abort();
        }

        if (TryGetGatewayUrl(out string baseUrl, out _))
        {
            StartCoroutine(SendGatewayCancellation(baseUrl, requestId));
        }

        if (logRequestLifecycle)
        {
            Debug.Log($"[{ServiceName}] Cancel request {requestId}.");
        }
    }

    private IEnumerator SendGatewayCancellation(
        string baseUrl,
        string requestId)
    {
        string cancelUrl =
            $"{baseUrl}/api/v1/generations/{UnityWebRequest.EscapeURL(requestId)}";

        using (UnityWebRequest cancelRequest =
               UnityWebRequest.Delete(cancelUrl))
        {
            ConfigureRequest(cancelRequest, requestTimeoutSeconds);
            yield return cancelRequest.SendWebRequest();
        }
    }

    private void CompleteTransportFailure(
        AITextureGenerationRequest request,
        string startedAtUtc,
        UnityWebRequest webRequest,
        GatewayGenerationResponse gatewayResponse,
        Action<AITextureGenerationResult> onCompleted)
    {
        if (gatewayResponse != null &&
            (!string.IsNullOrWhiteSpace(gatewayResponse.errorMessage) ||
             IsFailedStatus(gatewayResponse.status)))
        {
            onCompleted?.Invoke(CreateGatewayFailure(
                request,
                startedAtUtc,
                gatewayResponse));
            return;
        }

        AITextureGenerationErrorType errorType =
            IsTimeoutError(webRequest)
                ? AITextureGenerationErrorType.Timeout
                : AITextureGenerationErrorType.ServiceUnavailable;

        string message = string.IsNullOrWhiteSpace(webRequest.error)
            ? $"Gateway request failed (HTTP {webRequest.responseCode})."
            : $"Gateway request failed: {webRequest.error}";

        onCompleted?.Invoke(AITextureGenerationResult.Failure(
            request.requestId,
            errorType,
            message,
            startedAtUtc
        ));
    }

    private static AITextureGenerationResult CreateGatewayFailure(
        AITextureGenerationRequest request,
        string startedAtUtc,
        GatewayGenerationResponse response)
    {
        AITextureGenerationErrorType errorType =
            string.Equals(
                response.errorCode,
                "validation_error",
                StringComparison.OrdinalIgnoreCase)
                ? AITextureGenerationErrorType.Validation
                : string.Equals(
                    response.errorCode,
                    "provider_timeout",
                    StringComparison.OrdinalIgnoreCase)
                    ? AITextureGenerationErrorType.Timeout
                    : AITextureGenerationErrorType.Failed;

        string message = string.IsNullOrWhiteSpace(response.errorMessage)
            ? "AI texture generation failed."
            : response.errorMessage;

        return AITextureGenerationResult.Failure(
            request.requestId,
            errorType,
            message,
            startedAtUtc
        );
    }

    private static bool IsFailedStatus(string status)
    {
        return string.Equals(
                   status,
                   "failed",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   status,
                   "cancelled",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   status,
                   "canceled",
                   StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCancelled(
        string requestId,
        AITextureGenerationCancellationToken cancellationToken)
    {
        return (cancellationToken != null &&
                cancellationToken.IsCancellationRequested) ||
               cancelledRequestIds.Contains(requestId);
    }

    private void SetActiveTransport(
        string requestId,
        UnityWebRequest webRequest)
    {
        activeTransportRequestId = requestId;
        activeTransport = webRequest;
    }

    private void ClearActiveTransport(UnityWebRequest webRequest)
    {
        if (!ReferenceEquals(activeTransport, webRequest))
        {
            return;
        }

        activeTransport = null;
        activeTransportRequestId = "";
    }

    private static void ConfigureRequest(
        UnityWebRequest request,
        int timeoutSeconds)
    {
        request.timeout = Mathf.Max(1, timeoutSeconds);
        request.SetRequestHeader("Accept", "application/json");
    }

    private static bool IsSuccessful(UnityWebRequest request)
    {
        return request.result == UnityWebRequest.Result.Success &&
               request.responseCode >= 200 &&
               request.responseCode < 300;
    }

    private static bool IsTimeoutError(UnityWebRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.error) &&
               request.error.IndexOf(
                   "timed out",
                   StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool TryGetGatewayUrl(
        out string normalizedBaseUrl,
        out string errorMessage)
    {
        normalizedBaseUrl = (gatewayBaseUrl ?? "").Trim().TrimEnd('/');

        if (!Uri.TryCreate(
                normalizedBaseUrl,
                UriKind.Absolute,
                out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            errorMessage = "Gateway URL must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        bool isLoopback = uri.IsLoopback;

        if (!isLoopback && uri.Scheme != Uri.UriSchemeHttps)
        {
            errorMessage =
                "A non-local Gateway must use HTTPS.";
            return false;
        }

        errorMessage = "";
        return true;
    }

    private static bool TryParseGatewayResponse(
        string json,
        out GatewayGenerationResponse response)
    {
        response = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            response = JsonUtility.FromJson<GatewayGenerationResponse>(json);
            return response != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static byte[] ConvertToOpaqueJpeg(
        AITextureImageData source,
        int quality)
    {
        if (source == null || !source.HasData)
        {
            throw new InvalidOperationException("Input image is empty.");
        }

        Texture2D decoded = new Texture2D(
            2,
            2,
            TextureFormat.RGBA32,
            false,
            false);
        Texture2D opaque = null;

        try
        {
            if (!decoded.LoadImage(source.bytes, false))
            {
                throw new InvalidOperationException(
                    "Input image could not be decoded.");
            }

            Color32[] pixels = decoded.GetPixels32();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 color = pixels[i];
                int alpha = color.a;
                int inverseAlpha = 255 - alpha;
                color.r = (byte)((color.r * alpha + 255 * inverseAlpha) / 255);
                color.g = (byte)((color.g * alpha + 255 * inverseAlpha) / 255);
                color.b = (byte)((color.b * alpha + 255 * inverseAlpha) / 255);
                color.a = 255;
                pixels[i] = color;
            }

            opaque = new Texture2D(
                decoded.width,
                decoded.height,
                TextureFormat.RGB24,
                false,
                false);
            opaque.SetPixels32(pixels);
            opaque.Apply(false, false);
            return opaque.EncodeToJPG(Mathf.Clamp(quality, 75, 100));
        }
        finally
        {
            DestroyRuntimeObject(decoded);
            DestroyRuntimeObject(opaque);
        }
    }

    private static AITextureImageData NormalizeResultImage(
        byte[] imageBytes,
        int targetWidth,
        int targetHeight)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            throw new InvalidOperationException("Result bytes are empty.");
        }

        Texture2D decoded = new Texture2D(
            2,
            2,
            TextureFormat.RGBA32,
            false,
            false);
        Texture2D normalized = null;

        try
        {
            if (!decoded.LoadImage(imageBytes, false))
            {
                throw new InvalidOperationException(
                    "Result image could not be decoded.");
            }

            if (decoded.width == targetWidth &&
                decoded.height == targetHeight)
            {
                return new AITextureImageData(
                    decoded.EncodeToPNG(),
                    targetWidth,
                    targetHeight,
                    "image/png",
                    "wan-result.png");
            }

            RenderTexture temporary = RenderTexture.GetTemporary(
                targetWidth,
                targetHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;

            try
            {
                Graphics.Blit(decoded, temporary);
                RenderTexture.active = temporary;
                normalized = new Texture2D(
                    targetWidth,
                    targetHeight,
                    TextureFormat.RGBA32,
                    false,
                    false);
                normalized.ReadPixels(
                    new Rect(0, 0, targetWidth, targetHeight),
                    0,
                    0,
                    false);
                normalized.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }

            return new AITextureImageData(
                normalized.EncodeToPNG(),
                targetWidth,
                targetHeight,
                "image/png",
                "wan-result-normalized.png");
        }
        finally
        {
            DestroyRuntimeObject(decoded);
            DestroyRuntimeObject(normalized);
        }
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
        gatewayBaseUrl = (gatewayBaseUrl ?? "").Trim();
        pollIntervalSeconds = Mathf.Max(0.25f, pollIntervalSeconds);
        requestTimeoutSeconds = Mathf.Max(10, requestTimeoutSeconds);
        totalGenerationTimeoutSeconds = Mathf.Max(
            30,
            totalGenerationTimeoutSeconds);
        inputJpegQuality = Mathf.Clamp(inputJpegQuality, 75, 100);
    }

    private void OnDisable()
    {
        activeTransport?.Abort();
        activeTransport = null;
        activeTransportRequestId = "";
        cancelledRequestIds.Clear();
    }

    [Serializable]
    private sealed class GatewayGenerationResponse
    {
        public string requestId = "";
        public string status = "";
        public bool resultAvailable = false;
        public string errorCode = "";
        public string errorMessage = "";
        public string providerRequestId = "";
        public string providerMetadata = "";
    }
}
