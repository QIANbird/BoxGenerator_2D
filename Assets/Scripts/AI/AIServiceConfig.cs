using UnityEngine;

public class AIServiceConfig : MonoBehaviour
{
    // HTTP API 配置
    [Header("DashScope HTTP API Settings")]
    [SerializeField]
    private string _generationEndpointUrl = "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation"; 
    // 生成服务的 API 端点 URL

    [SerializeField] private string _apiKey = ""; // API 密钥，用于身份验证
    [SerializeField] private string _authorizationScheme = "Bearer"; // 授权方案（如 Bearer）

    // 模型相关配置
    [Header("Model Settings")]
    [SerializeField] private string _modelName = "qwen-image-2.0-pro"; // 使用的模型名称
    [SerializeField] private int _requestTimeoutSeconds = 120; // 请求超时时间（秒）

    // 输出图像配置
    [Header("Output Image Settings")]
    [SerializeField] private int _outputWidth = 512; // 输出图像宽度
    [SerializeField] private int _outputHeight = 512; // 输出图像高度
    [SerializeField] private int _numberOfImages = 1; // 输出图像数量

    // DashScope 生成参数
    [Header("DashScope Generation Parameters")]
    [TextArea(2, 4)]
    [SerializeField] private string _negativePrompt = ""; // 负面提示，用于生成时排除的内容

    [SerializeField] private bool _promptExtend = true; // 是否扩展提示
    [SerializeField] private bool _watermark = false; // 是否添加水印

    // 其他可选参数
    [Header("Optional Parameters Reserved For Other APIs")]
    [SerializeField] private int _seed = -1; // 随机种子
    [SerializeField] private int _steps = 0; // 生成步骤数
    [SerializeField] private float _guidanceScale = 0f; // 指导比例

    // 调试选项
    [Header("Debug")]
    [SerializeField] private bool _logRequestInfo = true; // 是否记录请求信息

    // 公共属性，用于外部访问私有字段
    public string GenerationEndpointUrl => _generationEndpointUrl; // 获取生成服务的 API 端点 URL
    public string ApiKey => _apiKey; // 获取 API 密钥
    public string AuthorizationScheme => _authorizationScheme; // 获取授权方案

    public string ModelName => _modelName; // 获取模型名称
    public int RequestTimeoutSeconds => _requestTimeoutSeconds; // 获取请求超时时间

    public int OutputWidth => _outputWidth; // 获取输出图像宽度
    public int OutputHeight => _outputHeight; // 获取输出图像高度
    public int NumberOfImages => _numberOfImages; // 获取输出图像数量

    public string NegativePrompt => _negativePrompt; // 获取负面提示
    public bool PromptExtend => _promptExtend; // 获取是否扩展提示
    public bool Watermark => _watermark; // 获取是否添加水印

    public int Seed => _seed; // 获取随机种子
    public int Steps => _steps; // 获取生成步骤数
    public float GuidanceScale => _guidanceScale; // 获取指导比例

    public bool LogRequestInfo => _logRequestInfo; // 获取是否记录请求信息

    /// <summary>
    /// 检查是否设置了 API 密钥
    /// </summary>
    public bool HasApiKey()
    {
        return !string.IsNullOrWhiteSpace(_apiKey);
    }

    /// <summary>
    /// 获取授权头的值
    /// </summary>
    public string GetAuthorizationHeaderValue()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return "";
        }

        if (string.IsNullOrWhiteSpace(_authorizationScheme))
        {
            return _apiKey;
        }

        return $"{_authorizationScheme} {_apiKey}";
    }

    /// <summary>
    /// 获取 DashScope API 所需的图像尺寸字符串
    /// </summary>
    public string GetDashScopeSizeString()
    {
        return $"{_outputWidth}*{_outputHeight}";
    }

    /// <summary>
    /// 验证配置是否有效
    /// </summary>
    public bool ValidateConfig(out string errorMessage)
    {
        errorMessage = "";

        // 检查生成服务的 API 端点 URL 是否为空
        if (string.IsNullOrWhiteSpace(_generationEndpointUrl))
        {
            errorMessage = "Generation endpoint URL is empty.";
            return false;
        }

        // 检查 URL 是否以 http:// 或 https:// 开头
        if (!_generationEndpointUrl.StartsWith("http://") &&
            !_generationEndpointUrl.StartsWith("https://"))
        {
            errorMessage = "Generation endpoint URL must start with http:// or https://.";
            return false;
        }

        // 检查 API 密钥是否为空
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            errorMessage = "API key is empty. Please fill it in AIServiceConfig.";
            return false;
        }

        // 检查模型名称是否为空
        if (string.IsNullOrWhiteSpace(_modelName))
        {
            errorMessage = "Model name is empty.";
            return false;
        }

        // 检查请求超时时间是否大于 0
        if (_requestTimeoutSeconds <= 0)
        {
            errorMessage = "Request timeout must be greater than 0.";
            return false;
        }

        // 检查输出图像的宽度和高度是否大于 0
        if (_outputWidth <= 0 || _outputHeight <= 0)
        {
            errorMessage = "Output width and height must be greater than 0.";
            return false;
        }

        // 检查生成的图像数量是否大于 0
        if (_numberOfImages <= 0)
        {
            errorMessage = "Number of images must be greater than 0.";
            return false;
        }

        return true; // 配置有效
    }

    /// <summary>
    /// 将默认配置应用到生成请求数据
    /// </summary>
    public void ApplyDefaultsToRequest(GenerationRequestData requestData)
    {
        if (requestData == null)
        {
            return;
        }

        // 如果请求数据中未设置模型名称，使用默认模型名称
        if (string.IsNullOrWhiteSpace(requestData.ModelName))
        {
            requestData.ModelName = _modelName;
        }

        // 如果请求数据中未设置输出宽度和高度，使用默认值
        if (requestData.OutputWidth <= 0)
        {
            requestData.OutputWidth = _outputWidth;
        }

        if (requestData.OutputHeight <= 0)
        {
            requestData.OutputHeight = _outputHeight;
        }

        // 应用其他默认参数
        requestData.Seed = _seed;
        requestData.Steps = _steps;
        requestData.GuidanceScale = _guidanceScale;
    }

    /// <summary>
    /// 在编辑器中验证配置的有效性
    /// </summary>
    private void OnValidate()
    {
        // 确保某些配置值不小于 1
        _requestTimeoutSeconds = Mathf.Max(1, _requestTimeoutSeconds);
        _outputWidth = Mathf.Max(1, _outputWidth);
        _outputHeight = Mathf.Max(1, _outputHeight);
        _numberOfImages = Mathf.Max(1, _numberOfImages);
    }
}