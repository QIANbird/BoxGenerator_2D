using BoxGenerator.AIGateway;
using Microsoft.AspNetCore.Http.Features;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(
    Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
    ?? "http://127.0.0.1:5088");

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = GatewayOptions.MaxRequestBytes;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = GatewayOptions.MaxRequestBytes;
});

GatewayOptions gatewayOptions = GatewayOptions.FromEnvironment();
builder.Services.AddSingleton(gatewayOptions);
builder.Services.AddSingleton<GenerationStore>();
builder.Services.AddSingleton<WanImageProvider>();
builder.Services.AddSingleton<TokenPlanGenerationService>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<TokenPlanGenerationService>());
builder.Services.AddHttpClient(
    WanImageProvider.ProviderClientName,
    client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient(
    WanImageProvider.ResultClientName,
    client => client.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddHostedService<GenerationCleanupService>();

WebApplication app = builder.Build();

app.MapGet("/health", (GatewayOptions options) => Results.Ok(new
{
    status = "ok",
    mode = options.Mode.ToString().ToLowerInvariant(),
    providerConfigured = options.IsProviderConfigured,
    provider = options.Mode == GatewayMode.TokenPlan
        ? "aliyun-token-plan"
        : options.Mode.ToString().ToLowerInvariant(),
    model = options.Model
}));

app.MapPost(
    "/api/v1/generations",
    async (
        HttpRequest httpRequest,
        GenerationStore store,
        GatewayOptions options,
        WanImageProvider provider,
        TokenPlanGenerationService tokenPlanService,
        CancellationToken cancellationToken) =>
    {
        GenerationSubmission? submission;

        try
        {
            submission = await GenerationSubmission.ReadAsync(
                httpRequest,
                cancellationToken);
        }
        catch (GatewayValidationException exception)
        {
            return Results.BadRequest(GatewayError.Validation(exception.Message));
        }

        if (store.TryGet(submission.RequestId, out GenerationJob? existing))
        {
            return Results.Json(
                existing!.ToResponse(),
                statusCode: StatusCodes.Status200OK);
        }

        GenerationJob job = store.Create(submission);

        if (options.Mode == GatewayMode.Mock)
        {
            // Mock mode exercises the complete HTTP path without a cloud key.
            // Returning the Editing image also preserves the requested composition.
            job.Succeed(
                submission.BaseShapeImage.Bytes,
                submission.BaseShapeImage.ContentType,
                "local-mock",
                "local-mock");

            return Results.Accepted(
                $"/api/v1/generations/{Uri.EscapeDataString(job.RequestId)}",
                job.ToResponse());
        }

        if (!options.IsProviderConfigured)
        {
            job.Fail(
                "gateway_not_configured",
                options.ConfigurationErrorMessage);

            return Results.Json(
                job.ToResponse(),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            if (options.Mode == GatewayMode.TokenPlan)
            {
                if (!tokenPlanService.Enqueue(job, submission))
                {
                    job.Fail(
                        "gateway_queue_unavailable",
                        "The Token Plan generation queue is unavailable.");

                    return Results.Json(
                        job.ToResponse(),
                        statusCode:
                            StatusCodes.Status503ServiceUnavailable);
                }

                return Results.Accepted(
                    $"/api/v1/generations/{Uri.EscapeDataString(job.RequestId)}",
                    job.ToResponse());
            }

            ProviderSubmissionResult providerResult =
                await provider.SubmitAsync(submission, cancellationToken);

            job.MarkSubmitted(
                providerResult.TaskId!,
                providerResult.ProviderRequestId);

            return Results.Accepted(
                $"/api/v1/generations/{Uri.EscapeDataString(job.RequestId)}",
                job.ToResponse());
        }
        catch (ProviderException exception)
        {
            job.Fail(exception.Code, exception.Message, exception.ProviderRequestId);

            return Results.Json(
                job.ToResponse(),
                statusCode: StatusCodes.Status502BadGateway);
        }
    });

app.MapGet(
    "/api/v1/generations/{requestId}",
    async (
        string requestId,
        GenerationStore store,
        GatewayOptions options,
        WanImageProvider provider,
        CancellationToken cancellationToken) =>
    {
        if (!store.TryGet(requestId, out GenerationJob? job))
        {
            return Results.NotFound(
                GatewayError.NotFound("Generation request was not found."));
        }

        if (options.Mode == GatewayMode.Wan &&
            job!.CanPollProvider)
        {
            await job.PollLock.WaitAsync(cancellationToken);

            try
            {
                if (job.CanPollProvider)
                {
                    ProviderTaskResult providerResult =
                        await provider.GetTaskAsync(
                            job.ProviderTaskId!,
                            cancellationToken);

                    switch (providerResult.Status)
                    {
                        case ProviderTaskStatus.Pending:
                            job.MarkPending(providerResult.ProviderRequestId);
                            break;
                        case ProviderTaskStatus.Running:
                            job.MarkRunning(providerResult.ProviderRequestId);
                            break;
                        case ProviderTaskStatus.Succeeded:
                            byte[] resultBytes =
                                await provider.DownloadResultAsync(
                                    providerResult.ResultUrl!,
                                    cancellationToken);
                            job.Succeed(
                                resultBytes,
                                "image/png",
                                providerResult.ProviderRequestId,
                                providerResult.UsageSize);
                            break;
                        case ProviderTaskStatus.Cancelled:
                            job.Cancel();
                            break;
                        case ProviderTaskStatus.Failed:
                            job.Fail(
                                providerResult.ErrorCode,
                                providerResult.ErrorMessage,
                                providerResult.ProviderRequestId);
                            break;
                    }
                }
            }
            catch (ProviderException exception)
            {
                job.Fail(
                    exception.Code,
                    exception.Message,
                    exception.ProviderRequestId);
            }
            finally
            {
                job.PollLock.Release();
            }
        }

        return Results.Ok(job!.ToResponse());
    });

app.MapGet(
    "/api/v1/generations/{requestId}/result",
    (string requestId, GenerationStore store) =>
    {
        if (!store.TryGet(requestId, out GenerationJob? job))
        {
            return Results.NotFound(
                GatewayError.NotFound("Generation request was not found."));
        }

        GenerationResultImage? result = job!.GetResult();

        if (result == null)
        {
            return Results.Conflict(
                GatewayError.InvalidState("The generation result is not available."));
        }

        return Results.File(
            result.Bytes,
            result.ContentType,
            $"ai-texture-{requestId}.png",
            enableRangeProcessing: false);
    });

app.MapDelete(
    "/api/v1/generations/{requestId}",
    (
        string requestId,
        GenerationStore store,
        TokenPlanGenerationService tokenPlanService) =>
    {
        if (!store.TryGet(requestId, out GenerationJob? job))
        {
            // DELETE is idempotent so cancellation remains safe if the POST was
            // interrupted before the client received its response.
            return Results.NoContent();
        }

        job!.Cancel();
        tokenPlanService.Cancel(requestId);
        return Results.NoContent();
    });

app.Run();
