using System.Collections.Concurrent;

namespace BoxGenerator.AIGateway;

public enum GenerationJobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public sealed class GenerationJob
{
    private readonly object syncRoot = new();
    private GenerationJobStatus status = GenerationJobStatus.Pending;
    private GenerationResultImage? result;
    private string errorCode = "";
    private string errorMessage = "";
    private string providerRequestId = "";
    private string providerMetadata = "";

    public GenerationJob(GenerationSubmission submission)
    {
        RequestId = submission.RequestId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string RequestId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public string? ProviderTaskId { get; private set; }
    public SemaphoreSlim PollLock { get; } = new(1, 1);

    public bool CanPollProvider
    {
        get
        {
            lock (syncRoot)
            {
                return !string.IsNullOrWhiteSpace(ProviderTaskId) &&
                       (status == GenerationJobStatus.Pending ||
                        status == GenerationJobStatus.Running);
            }
        }
    }

    public void MarkSubmitted(string providerTaskId, string requestId)
    {
        lock (syncRoot)
        {
            if (status == GenerationJobStatus.Cancelled)
            {
                return;
            }

            ProviderTaskId = providerTaskId;
            providerRequestId = requestId ?? "";
            status = GenerationJobStatus.Pending;
        }
    }

    public void MarkPending(string requestId)
    {
        UpdateInProgress(GenerationJobStatus.Pending, requestId);
    }

    public void MarkRunning(string requestId)
    {
        UpdateInProgress(GenerationJobStatus.Running, requestId);
    }

    public void Succeed(
        byte[] bytes,
        string contentType,
        string requestId,
        string metadata)
    {
        lock (syncRoot)
        {
            if (status == GenerationJobStatus.Cancelled)
            {
                return;
            }

            result = new GenerationResultImage(bytes, contentType);
            providerRequestId = requestId ?? "";
            providerMetadata = metadata ?? "";
            errorCode = "";
            errorMessage = "";
            status = GenerationJobStatus.Succeeded;
        }
    }

    public void Fail(
        string code,
        string message,
        string requestId = "")
    {
        lock (syncRoot)
        {
            if (status == GenerationJobStatus.Cancelled)
            {
                return;
            }

            result = null;
            errorCode = string.IsNullOrWhiteSpace(code)
                ? "provider_error"
                : code;
            errorMessage = string.IsNullOrWhiteSpace(message)
                ? "Image generation failed."
                : message;
            providerRequestId = requestId ?? "";
            status = GenerationJobStatus.Failed;
        }
    }

    public void Cancel()
    {
        lock (syncRoot)
        {
            result = null;
            errorCode = "";
            errorMessage = "";
            status = GenerationJobStatus.Cancelled;
        }
    }

    public GenerationResultImage? GetResult()
    {
        lock (syncRoot)
        {
            return status == GenerationJobStatus.Succeeded
                ? result
                : null;
        }
    }

    public GenerationResponse ToResponse()
    {
        lock (syncRoot)
        {
            return new GenerationResponse(
                RequestId,
                status.ToString().ToLowerInvariant(),
                status == GenerationJobStatus.Succeeded && result != null,
                errorCode,
                errorMessage,
                providerRequestId,
                providerMetadata);
        }
    }

    private void UpdateInProgress(
        GenerationJobStatus newStatus,
        string requestId)
    {
        lock (syncRoot)
        {
            if (status == GenerationJobStatus.Cancelled)
            {
                return;
            }

            status = newStatus;
            providerRequestId = requestId ?? "";
        }
    }
}

public sealed class GenerationStore
{
    private readonly ConcurrentDictionary<string, GenerationJob> jobs =
        new(StringComparer.Ordinal);

    public GenerationJob Create(GenerationSubmission submission)
    {
        GenerationJob job = new(submission);

        if (!jobs.TryAdd(submission.RequestId, job))
        {
            throw new InvalidOperationException(
                "A generation with the same request ID already exists.");
        }

        return job;
    }

    public bool TryGet(string requestId, out GenerationJob? job)
    {
        return jobs.TryGetValue(requestId, out job);
    }

    public void RemoveExpired(TimeSpan ttl)
    {
        DateTimeOffset threshold = DateTimeOffset.UtcNow - ttl;

        foreach ((string key, GenerationJob job) in jobs)
        {
            if (job.CreatedAtUtc < threshold)
            {
                jobs.TryRemove(key, out _);
            }
        }
    }
}

public sealed class GenerationCleanupService : BackgroundService
{
    private readonly GenerationStore store;
    private readonly GatewayOptions options;

    public GenerationCleanupService(
        GenerationStore store,
        GatewayOptions options)
    {
        this.store = store;
        this.options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            store.RemoveExpired(options.JobTtl);
        }
    }
}
