using System.Collections.Concurrent;
using System.Threading.Channels;

namespace BoxGenerator.AIGateway;

public sealed class TokenPlanGenerationService : BackgroundService
{
    private readonly Channel<WorkItem> queue =
        Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<string, CancellationTokenSource>
        cancellations = new(StringComparer.Ordinal);
    private readonly WanImageProvider provider;
    private readonly ILogger<TokenPlanGenerationService> logger;

    public TokenPlanGenerationService(
        WanImageProvider provider,
        ILogger<TokenPlanGenerationService> logger)
    {
        this.provider = provider;
        this.logger = logger;
    }

    public bool Enqueue(
        GenerationJob job,
        GenerationSubmission submission)
    {
        CancellationTokenSource cancellation = new();

        if (!cancellations.TryAdd(job.RequestId, cancellation))
        {
            cancellation.Dispose();
            return false;
        }

        if (queue.Writer.TryWrite(new WorkItem(job, submission, cancellation)))
        {
            return true;
        }

        cancellations.TryRemove(job.RequestId, out _);
        cancellation.Dispose();
        return false;
    }

    public void Cancel(string requestId)
    {
        if (cancellations.TryGetValue(
            requestId,
            out CancellationTokenSource? cancellation))
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The worker completed between the dictionary lookup and
                // cancellation. The job has already reached a terminal state.
            }
        }
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await foreach (WorkItem item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken,
                    item.Cancellation.Token);

            try
            {
                item.Job.MarkRunning("");
                ProviderSubmissionResult result = await provider.SubmitAsync(
                    item.Submission,
                    linkedCancellation.Token);

                if (!result.IsCompleted)
                {
                    throw new ProviderException(
                        "invalid_provider_response",
                        "Token Plan returned an asynchronous task instead of an image.",
                        result.ProviderRequestId);
                }

                byte[] resultBytes = await provider.DownloadResultAsync(
                    result.ResultUrl!,
                    linkedCancellation.Token);
                item.Job.Succeed(
                    resultBytes,
                    "image/png",
                    result.ProviderRequestId,
                    result.UsageSize);
            }
            catch (OperationCanceledException)
                when (item.Cancellation.IsCancellationRequested)
            {
                item.Job.Cancel();
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ProviderException exception)
            {
                item.Job.Fail(
                    exception.Code,
                    exception.Message,
                    exception.ProviderRequestId);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Unexpected Token Plan generation error for request {RequestId}.",
                    item.Job.RequestId);
                item.Job.Fail(
                    "gateway_internal_error",
                    "The Token Plan generation failed inside the gateway.");
            }
            finally
            {
                cancellations.TryRemove(item.Job.RequestId, out _);
                item.Cancellation.Dispose();
            }
        }
    }

    private sealed record WorkItem(
        GenerationJob Job,
        GenerationSubmission Submission,
        CancellationTokenSource Cancellation);
}
