using System.Text.RegularExpressions;

namespace BoxGenerator.AIGateway;

public enum GatewayMode
{
    Mock,
    Wan,
    TokenPlan
}

public sealed class GatewayOptions
{
    public const long MaxImageBytes = 20L * 1024L * 1024L;
    public const long MaxRequestBytes = 45L * 1024L * 1024L;

    private static readonly Regex WorkspacePattern = new(
        "^[a-zA-Z0-9-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public GatewayMode Mode { get; private init; } = GatewayMode.Mock;
    public string ApiKey { get; private init; } = "";
    public string WorkspaceId { get; private init; } = "";
    public string Region { get; private init; } = "cn-beijing";
    public string Model { get; private init; } = "wan2.7-image-pro";
    public string OutputSize { get; private init; } = "1K";
    public TimeSpan JobTtl { get; private init; } = TimeSpan.FromHours(1);

    public bool IsProviderConfigured =>
        Mode == GatewayMode.Mock ||
        (Mode == GatewayMode.TokenPlan
            ? ApiKey.StartsWith("sk-sp-", StringComparison.Ordinal)
            : !string.IsNullOrWhiteSpace(ApiKey) &&
              WorkspacePattern.IsMatch(WorkspaceId) &&
              string.Equals(Region, "cn-beijing", StringComparison.Ordinal));

    public string ConfigurationErrorMessage => Mode switch
    {
        GatewayMode.TokenPlan =>
            "The Token Plan provider is not configured. Set TOKEN_PLAN_API_KEY to the sk-sp- key issued for the plan.",
        GatewayMode.Wan =>
            "The Wan workspace provider is not configured. Set DASHSCOPE_API_KEY, BAILIAN_WORKSPACE_ID, and BAILIAN_REGION.",
        _ => ""
    };

    public Uri ApiBaseUri
    {
        get
        {
            if (Mode == GatewayMode.TokenPlan)
            {
                return new Uri(
                    "https://token-plan.cn-beijing.maas.aliyuncs.com/api/v1/",
                    UriKind.Absolute);
            }

            if (!WorkspacePattern.IsMatch(WorkspaceId))
            {
                throw new InvalidOperationException(
                    "BAILIAN_WORKSPACE_ID contains invalid characters.");
            }

            return new Uri(
                $"https://{WorkspaceId}.{Region}.maas.aliyuncs.com/api/v1/",
                UriKind.Absolute);
        }
    }

    public static GatewayOptions FromEnvironment()
    {
        string modeValue = Read("AI_GATEWAY_MODE", "mock");
        GatewayMode mode = modeValue.ToLowerInvariant() switch
        {
            "mock" => GatewayMode.Mock,
            "wan" => GatewayMode.Wan,
            "tokenplan" => GatewayMode.TokenPlan,
            "token-plan" => GatewayMode.TokenPlan,
            _ => throw new InvalidOperationException(
                "AI_GATEWAY_MODE must be mock, wan, or tokenplan.")
        };

        string outputSize = Read(
            "AI_IMAGE_OUTPUT_SIZE",
            Read("BAILIAN_OUTPUT_SIZE", "1K")).ToUpperInvariant();

        if (outputSize != "1K" && outputSize != "2K")
        {
            throw new InvalidOperationException(
                "AI_IMAGE_OUTPUT_SIZE must be either 1K or 2K.");
        }

        return new GatewayOptions
        {
            Mode = mode,
            ApiKey = mode == GatewayMode.TokenPlan
                ? Read("TOKEN_PLAN_API_KEY", "")
                : Read("DASHSCOPE_API_KEY", ""),
            WorkspaceId = Read("BAILIAN_WORKSPACE_ID", ""),
            Region = Read("BAILIAN_REGION", "cn-beijing"),
            Model = "wan2.7-image-pro",
            OutputSize = outputSize
        };
    }

    private static string Read(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
