using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AITextureGenerationCoordinator : MonoBehaviour
{
    [Header("Service")]
    [Tooltip("Assign a MonoBehaviour that implements IAITextureGenerationService.")]
    [SerializeField] private MonoBehaviour serviceBehaviour;

    [Header("Runtime Input")]
    [SerializeField] private AITextureGenerationInputState inputState =
        new AITextureGenerationInputState();

    [Header("Debug")]
    [SerializeField] private bool logStateChanges = true;

    [SerializeField] private AITextureGenerationStatus status =
        AITextureGenerationStatus.Idle;

    [SerializeField] private string activeRequestId = "";
    [SerializeField] private string lastError = "";

    private IAITextureGenerationService service;
    private AITextureGenerationCancellationSource cancellationSource;
    private AITextureGenerationRequest activeRequest;
    private AITextureGenerationResult lastAttemptResult;
    private AITextureGenerationResult lastSuccessfulResult;
    private bool hasValidResult;

    public event Action<AITextureGenerationStatus> StatusChanged;
    public event Action<AITextureGenerationRequest> GenerationStarted;
    public event Action<AITextureGenerationResult> GenerationSucceeded;
    public event Action<AITextureGenerationResult> GenerationFailed;
    public event Action<string> GenerationCancelled;
    public event Action ValidResultInvalidated;

    public AITextureGenerationStatus Status => status;
    public bool IsGenerating => status == AITextureGenerationStatus.Generating;
    public string ActiveRequestId => activeRequestId;
    public string LastError => lastError;
    public bool HasValidResult => hasValidResult;
    public AITextureGenerationResult LastAttemptResult => lastAttemptResult;
    public AITextureGenerationResult LastSuccessfulResult => lastSuccessfulResult;
    public AITextureGenerationInputState InputState => inputState;

    public AITextureGenerationRequest ActiveRequest
    {
        get
        {
            return activeRequest != null ? activeRequest.Clone() : null;
        }
    }

    private void Awake()
    {
        ResolveService();
        EnsureInputState();
    }

    private void OnDisable()
    {
        if (IsGenerating)
        {
            CancelCurrentGeneration();
        }
    }

    public bool SetService(MonoBehaviour newServiceBehaviour, out string errorMessage)
    {
        if (IsGenerating)
        {
            errorMessage = "Cannot change AI service while generation is in progress.";
            return false;
        }

        if (newServiceBehaviour != null &&
            !(newServiceBehaviour is IAITextureGenerationService))
        {
            errorMessage =
                $"{newServiceBehaviour.GetType().Name} does not implement " +
                $"{nameof(IAITextureGenerationService)}.";
            return false;
        }

        serviceBehaviour = newServiceBehaviour;
        service = serviceBehaviour as IAITextureGenerationService;
        errorMessage = "";
        return true;
    }

    public void SetInputState(AITextureGenerationInputState newInputState)
    {
        inputState = newInputState != null
            ? newInputState.Clone()
            : new AITextureGenerationInputState();
    }

    public bool TryStartGeneration(
        AITextureGenerationRequest request,
        out string requestId,
        out string errorMessage)
    {
        requestId = "";

        if (IsGenerating)
        {
            errorMessage = "An AI texture generation request is already running.";
            return false;
        }

        ResolveService();

        if (service == null)
        {
            errorMessage =
                $"No component implementing {nameof(IAITextureGenerationService)} is configured.";
            return false;
        }

        if (request == null)
        {
            errorMessage = "Generation request is null.";
            return false;
        }

        AITextureGenerationRequest requestSnapshot = request.Clone();
        requestSnapshot.AssignNewIdentity();

        if (!requestSnapshot.Validate(out errorMessage))
        {
            return false;
        }

        requestId = requestSnapshot.requestId;
        activeRequestId = requestSnapshot.requestId;
        activeRequest = requestSnapshot;
        cancellationSource = new AITextureGenerationCancellationSource();
        lastError = "";

        SetStatus(AITextureGenerationStatus.Generating);
        GenerationStarted?.Invoke(requestSnapshot.Clone());

        StartCoroutine(RunGeneration(
            requestSnapshot,
            cancellationSource.Token
        ));

        errorMessage = "";
        return true;
    }

    public bool CancelCurrentGeneration()
    {
        if (!IsGenerating || string.IsNullOrWhiteSpace(activeRequestId))
        {
            return false;
        }

        string cancelledRequestId = activeRequestId;

        cancellationSource?.Cancel();
        service?.Cancel(cancelledRequestId);

        lastAttemptResult = AITextureGenerationResult.Cancelled(
            cancelledRequestId,
            activeRequest != null ? activeRequest.createdAtUtc : ""
        );

        activeRequestId = "";
        activeRequest = null;
        cancellationSource = null;
        lastError = "";

        SetStatus(AITextureGenerationStatus.Cancelled);
        GenerationCancelled?.Invoke(cancelledRequestId);

        if (logStateChanges)
        {
            Debug.Log($"[AI Texture] Request {cancelledRequestId} cancelled.");
        }

        return true;
    }

    public void InvalidateSuccessfulResult()
    {
        if (!hasValidResult)
        {
            return;
        }

        hasValidResult = false;
        ValidResultInvalidated?.Invoke();
    }

    public void ClearSuccessfulResult()
    {
        bool hadValidResult = hasValidResult;
        hasValidResult = false;
        lastSuccessfulResult = null;

        if (hadValidResult)
        {
            ValidResultInvalidated?.Invoke();
        }
    }

    public void ReturnToIdle()
    {
        if (IsGenerating)
        {
            return;
        }

        SetStatus(AITextureGenerationStatus.Idle);
        lastError = "";
    }

    private IEnumerator RunGeneration(
        AITextureGenerationRequest request,
        AITextureGenerationCancellationToken cancellationToken)
    {
        bool callbackReceived = false;

        void OnServiceCompleted(AITextureGenerationResult result)
        {
            callbackReceived = true;
            HandleServiceResult(request.requestId, cancellationToken, result);
        }

        IEnumerator serviceRoutine;

        try
        {
            serviceRoutine = service.Generate(
                request,
                cancellationToken,
                OnServiceCompleted
            );
        }
        catch (Exception exception)
        {
            CompleteUnexpectedFailure(request.requestId, request.createdAtUtc, exception);
            yield break;
        }

        if (serviceRoutine == null)
        {
            CompleteFailure(AITextureGenerationResult.Failure(
                request.requestId,
                AITextureGenerationErrorType.ServiceUnavailable,
                "AI texture service returned no generation routine.",
                request.createdAtUtc
            ));
            yield break;
        }

        while (true)
        {
            bool hasNext;
            object yieldedValue = null;

            try
            {
                hasNext = serviceRoutine.MoveNext();

                if (hasNext)
                {
                    yieldedValue = serviceRoutine.Current;
                }
            }
            catch (Exception exception)
            {
                CompleteUnexpectedFailure(request.requestId, request.createdAtUtc, exception);
                yield break;
            }

            if (!hasNext)
            {
                break;
            }

            yield return yieldedValue;
        }

        if (IsActiveRequest(request.requestId) &&
            !callbackReceived &&
            !cancellationToken.IsCancellationRequested)
        {
            CompleteFailure(AITextureGenerationResult.Failure(
                request.requestId,
                AITextureGenerationErrorType.InvalidResponse,
                "AI texture service completed without returning a result.",
                request.createdAtUtc
            ));
        }
    }

    private void HandleServiceResult(
        string expectedRequestId,
        AITextureGenerationCancellationToken cancellationToken,
        AITextureGenerationResult result)
    {
        // A cancelled or superseded request may still invoke a callback. It must
        // never change the state or overwrite a newer result.
        if (cancellationToken == null ||
            cancellationToken.IsCancellationRequested ||
            !IsActiveRequest(expectedRequestId))
        {
            return;
        }

        if (result == null)
        {
            CompleteFailure(AITextureGenerationResult.Failure(
                expectedRequestId,
                AITextureGenerationErrorType.InvalidResponse,
                "AI texture service returned a null result.",
                activeRequest != null ? activeRequest.createdAtUtc : ""
            ));
            return;
        }

        if (!string.Equals(
                result.requestId,
                expectedRequestId,
                StringComparison.Ordinal))
        {
            CompleteFailure(AITextureGenerationResult.Failure(
                expectedRequestId,
                AITextureGenerationErrorType.InvalidResponse,
                "AI texture service returned a result for a different request.",
                activeRequest != null ? activeRequest.createdAtUtc : ""
            ));
            return;
        }

        if (result.status == AITextureGenerationStatus.Cancelled)
        {
            CancelCurrentGeneration();
            return;
        }

        if (result.IsSuccess)
        {
            if (!TryValidateSuccessfulResult(result, out string resultError))
            {
                CompleteFailure(AITextureGenerationResult.Failure(
                    expectedRequestId,
                    AITextureGenerationErrorType.InvalidResponse,
                    resultError,
                    activeRequest != null ? activeRequest.createdAtUtc : ""
                ));
                return;
            }

            CompleteSuccess(result);
            return;
        }

        if (result.status == AITextureGenerationStatus.Succeeded)
        {
            CompleteFailure(AITextureGenerationResult.Failure(
                expectedRequestId,
                AITextureGenerationErrorType.InvalidResponse,
                "AI texture service reported success without a valid result image.",
                activeRequest != null ? activeRequest.createdAtUtc : ""
            ));
            return;
        }

        CompleteFailure(result);
    }

    private bool TryValidateSuccessfulResult(
        AITextureGenerationResult result,
        out string errorMessage)
    {
        if (activeRequest == null ||
            result == null ||
            result.resultImage == null)
        {
            errorMessage = "AI result image is missing.";
            return false;
        }

        AITextureImageData image = result.resultImage;

        if (image.width != activeRequest.outputWidth ||
            image.height != activeRequest.outputHeight)
        {
            errorMessage =
                "AI result dimensions do not match the requested output size.";
            return false;
        }

        Texture2D validationTexture = new Texture2D(
            2,
            2,
            TextureFormat.RGBA32,
            false,
            false);

        try
        {
            if (!validationTexture.LoadImage(image.bytes, false))
            {
                errorMessage = "AI result image bytes could not be decoded.";
                return false;
            }

            if (validationTexture.width != activeRequest.outputWidth ||
                validationTexture.height != activeRequest.outputHeight)
            {
                errorMessage =
                    "Decoded AI result dimensions do not match the requested output size.";
                return false;
            }
        }
        catch (Exception exception)
        {
            errorMessage =
                $"AI result image validation failed: {exception.Message}";
            return false;
        }
        finally
        {
            if (Application.isPlaying)
            {
                Destroy(validationTexture);
            }
            else
            {
                DestroyImmediate(validationTexture);
            }
        }

        errorMessage = "";
        return true;
    }

    private void CompleteSuccess(AITextureGenerationResult result)
    {
        if (!IsActiveRequest(result.requestId))
        {
            return;
        }

        lastAttemptResult = result;
        lastSuccessfulResult = result;
        hasValidResult = true;
        lastError = "";

        FinishActiveRequest();
        SetStatus(AITextureGenerationStatus.Succeeded);
        GenerationSucceeded?.Invoke(result);

        if (logStateChanges)
        {
            Debug.Log($"[AI Texture] Request {result.requestId} succeeded.");
        }
    }

    private void CompleteFailure(AITextureGenerationResult result)
    {
        if (result == null || !IsActiveRequest(result.requestId))
        {
            return;
        }

        lastAttemptResult = result;
        lastError = string.IsNullOrWhiteSpace(result.errorMessage)
            ? "AI texture generation failed."
            : result.errorMessage;

        FinishActiveRequest();
        SetStatus(AITextureGenerationStatus.Failed);
        GenerationFailed?.Invoke(result);

        if (logStateChanges)
        {
            Debug.LogWarning(
                $"[AI Texture] Request {result.requestId} failed: {lastError}"
            );
        }
    }

    private void CompleteUnexpectedFailure(
        string requestId,
        string startedAtUtc,
        Exception exception)
    {
        if (!IsActiveRequest(requestId))
        {
            return;
        }

        CompleteFailure(AITextureGenerationResult.Failure(
            requestId,
            AITextureGenerationErrorType.Unexpected,
            exception != null
                ? $"Unexpected AI service error: {exception.Message}"
                : "Unexpected AI service error.",
            startedAtUtc
        ));
    }

    private void FinishActiveRequest()
    {
        activeRequestId = "";
        activeRequest = null;
        cancellationSource = null;
    }

    private bool IsActiveRequest(string requestId)
    {
        return IsGenerating &&
               !string.IsNullOrWhiteSpace(requestId) &&
               string.Equals(activeRequestId, requestId, StringComparison.Ordinal);
    }

    private void SetStatus(AITextureGenerationStatus newStatus)
    {
        if (status == newStatus)
        {
            return;
        }

        status = newStatus;
        StatusChanged?.Invoke(status);
    }

    private void EnsureInputState()
    {
        if (inputState == null)
        {
            inputState = new AITextureGenerationInputState();
        }
    }

    private void ResolveService()
    {
        service = serviceBehaviour as IAITextureGenerationService;

        if (service != null)
        {
            return;
        }

        MonoBehaviour[] components = GetComponents<MonoBehaviour>();

        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is IAITextureGenerationService foundService)
            {
                serviceBehaviour = components[i];
                service = foundService;
                return;
            }
        }
    }

    private void OnValidate()
    {
        EnsureInputState();

        if (serviceBehaviour != null &&
            !(serviceBehaviour is IAITextureGenerationService))
        {
            Debug.LogWarning(
                $"{name}: Assigned AI service must implement " +
                $"{nameof(IAITextureGenerationService)}.",
                this
            );
        }
    }
}
