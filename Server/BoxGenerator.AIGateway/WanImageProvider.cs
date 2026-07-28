using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoxGenerator.AIGateway;

public enum ProviderTaskStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public sealed record ProviderSubmissionResult(
    string TaskId,
    string ProviderRequestId);

public sealed record ProviderTaskResult(
    ProviderTaskStatus Status,
    string? ResultUrl,
    string ProviderRequestId,
    string ErrorCode,
    string ErrorMessage,
    string UsageSize);

public sealed class ProviderException : Exception
{
    public ProviderException(
        string code,
        string message,
        string providerRequestId = "",
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = string.IsNullOrWhiteSpace(code) ? "provider_error" : code;
        ProviderRequestId = providerRequestId ?? "";
    }

    public string Code { get; }
    public string ProviderRequestId { get; }
}

public sealed class WanImageProvider
{
    public const string ProviderClientName = "wan-provider";
    public const string ResultClientName = "wan-result";

    private const int MaxResultBytes = 25 * 1024 * 1024;
    private readonly GatewayOptions options;
    private readonly IHttpClientFactory clientFactory;

    public WanImageProvider(
        GatewayOptions options,
        IHttpClientFactory clientFactory)
    {
        this.options = options;
        this.clientFactory = clientFactory;
    }

    public async Task<ProviderSubmissionResult> SubmitAsync(
        GenerationSubmission submission,
        CancellationToken cancellationToken)
    {
        List<Dictionary<string, string>> content = new();

        // Wan uses the last input image to determine output aspect ratio.
        // The optional style image therefore comes first and the Editing
        // base-shape reference is deliberately the final image.
        if (submission.StyleReferenceImage != null)
        {
            content.Add(ImageContent(submission.StyleReferenceImage));
        }

        content.Add(ImageContent(submission.BaseShapeImage));
        content.Add(new Dictionary<string, string>
        {
            ["text"] = submission.Prompt
        });

        object body = new
        {
            model = options.Model,
            input = new
            {
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content
                    }
                }
            },
            parameters = new
            {
                size = options.OutputSize,
                n = 1,
                watermark = false
            }
        };

        Uri endpoint = new(
            options.ApiBaseUri,
            "services/aigc/image-generation/generation");
        using HttpRequestMessage request = CreateProviderRequest(
            HttpMethod.Post,
            endpoint);
        request.Headers.Add("X-DashScope-Async", "enable");
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        WanResponse response = await SendAndParseAsync(
            request,
            cancellationToken);
        string taskId = response.Output?.TaskId ?? "";

        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new ProviderException(
                response.Code ?? "invalid_provider_response",
                response.Message ?? "Wan did not return a task ID.",
                response.RequestId ?? "");
        }

        return new ProviderSubmissionResult(
            taskId,
            response.RequestId ?? "");
    }

    public async Task<ProviderTaskResult> GetTaskAsync(
        string taskId,
        CancellationToken cancellationToken)
    {
        Uri endpoint = new(
            options.ApiBaseUri,
            $"tasks/{Uri.EscapeDataString(taskId)}");
        using HttpRequestMessage request = CreateProviderRequest(
            HttpMethod.Get,
            endpoint);
        WanResponse response = await SendAndParseAsync(
            request,
            cancellationToken);

        string taskStatus = response.Output?.TaskStatus ?? "";
        ProviderTaskStatus status = taskStatus.ToUpperInvariant() switch
        {
            "PENDING" => ProviderTaskStatus.Pending,
            "RUNNING" => ProviderTaskStatus.Running,
            "SUCCEEDED" => ProviderTaskStatus.Succeeded,
            "CANCELED" => ProviderTaskStatus.Cancelled,
            "FAILED" => ProviderTaskStatus.Failed,
            "UNKNOWN" => ProviderTaskStatus.Failed,
            _ => throw new ProviderException(
                "invalid_provider_response",
                $"Wan returned an unknown task status: {taskStatus}.",
                response.RequestId ?? "")
        };

        string? resultUrl = null;

        if (status == ProviderTaskStatus.Succeeded)
        {
            resultUrl = response.Output?
                .Choices?
                .SelectMany(choice =>
                    choice.Message?.Content ?? Array.Empty<WanContent>())
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Type,
                        "image",
                        StringComparison.OrdinalIgnoreCase))?
                .Image;

            if (string.IsNullOrWhiteSpace(resultUrl))
            {
                throw new ProviderException(
                    "invalid_provider_response",
                    "Wan reported success without an image URL.",
                    response.RequestId ?? "");
            }
        }

        WanTaskResult? failedSubtask = response.Output?
            .Results?
            .FirstOrDefault(result =>
                !string.IsNullOrWhiteSpace(result.Code) ||
                !string.IsNullOrWhiteSpace(result.Message));
        string errorCode =
            response.Output?.Code ??
            failedSubtask?.Code ??
            response.Code ??
            "";
        string errorMessage =
            response.Output?.Message ??
            failedSubtask?.Message ??
            response.Message ??
            "";

        if (status == ProviderTaskStatus.Failed &&
            string.IsNullOrWhiteSpace(errorMessage))
        {
            errorCode = "generation_failed";
            errorMessage = "Wan image generation failed.";
        }

        return new ProviderTaskResult(
            status,
            resultUrl,
            response.RequestId ?? "",
            errorCode,
            errorMessage,
            response.Usage?.Size ?? "");
    }

    public async Task<byte[]> DownloadResultAsync(
        string resultUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(resultUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            (!string.Equals(
                 uri.Host,
                 "aliyuncs.com",
                 StringComparison.OrdinalIgnoreCase) &&
             !uri.Host.EndsWith(
                 ".aliyuncs.com",
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new ProviderException(
                "invalid_result_url",
                "Wan returned an invalid result URL.");
        }

        HttpClient client = clientFactory.CreateClient(ResultClientName);
        using HttpResponseMessage response = await client.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException(
                "result_download_failed",
                $"Failed to download the Wan result (HTTP {(int)response.StatusCode}).");
        }

        long? contentLength = response.Content.Headers.ContentLength;

        if (contentLength > MaxResultBytes)
        {
            throw new ProviderException(
                "result_too_large",
                "The Wan result exceeded the gateway size limit.");
        }

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream memory = new();
        byte[] buffer = new byte[81920];
        int total = 0;

        while (true)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(0, buffer.Length),
                cancellationToken);

            if (read == 0)
            {
                break;
            }

            total += read;

            if (total > MaxResultBytes)
            {
                throw new ProviderException(
                    "result_too_large",
                    "The Wan result exceeded the gateway size limit.");
            }

            await memory.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }

        return memory.ToArray();
    }

    private async Task<WanResponse> SendAndParseAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            HttpClient client = clientFactory.CreateClient(ProviderClientName);
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            string json = await response.Content.ReadAsStringAsync(
                cancellationToken);

            WanResponse? payload;

            try
            {
                payload = JsonSerializer.Deserialize<WanResponse>(json);
            }
            catch (JsonException exception)
            {
                throw new ProviderException(
                    "invalid_provider_response",
                    $"Wan returned invalid JSON (HTTP {(int)response.StatusCode}).",
                    innerException: exception);
            }

            if (payload == null)
            {
                throw new ProviderException(
                    "invalid_provider_response",
                    "Wan returned an empty response.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(
                    payload.Code ?? $"http_{(int)response.StatusCode}",
                    payload.Message ??
                    $"Wan request failed (HTTP {(int)response.StatusCode}).",
                    payload.RequestId ?? "");
            }

            return payload;
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException(
                "provider_timeout",
                "The Wan request timed out.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ProviderException(
                "provider_unavailable",
                "The Wan service could not be reached.",
                innerException: exception);
        }
    }

    private HttpRequestMessage CreateProviderRequest(
        HttpMethod method,
        Uri endpoint)
    {
        HttpRequestMessage request = new(method, endpoint);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static Dictionary<string, string> ImageContent(
        UploadedImage image)
    {
        return new Dictionary<string, string>
        {
            ["image"] =
                $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Bytes)}"
        };
    }

    private sealed class WanResponse
    {
        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("output")]
        public WanOutput? Output { get; set; }

        [JsonPropertyName("usage")]
        public WanUsage? Usage { get; set; }
    }

    private sealed class WanOutput
    {
        [JsonPropertyName("task_id")]
        public string? TaskId { get; set; }

        [JsonPropertyName("task_status")]
        public string? TaskStatus { get; set; }

        [JsonPropertyName("choices")]
        public WanChoice[]? Choices { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("results")]
        public WanTaskResult[]? Results { get; set; }
    }

    private sealed class WanTaskResult
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private sealed class WanChoice
    {
        [JsonPropertyName("message")]
        public WanMessage? Message { get; set; }
    }

    private sealed class WanMessage
    {
        [JsonPropertyName("content")]
        public WanContent[]? Content { get; set; }
    }

    private sealed class WanContent
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("image")]
        public string? Image { get; set; }
    }

    private sealed class WanUsage
    {
        [JsonPropertyName("size")]
        public string? Size { get; set; }
    }
}
