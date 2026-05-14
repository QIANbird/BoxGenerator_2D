using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class ImageAIService : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private AIServiceConfig config;

    [Header("Prompt Wrapping")]
    [SerializeField] private bool wrapUserPrompt = true;

    [TextArea(3, 6)]
    [SerializeField]
    private string promptPrefix =
        "请严格参考输入图中的黑色矩形线框，只在矩形内部生成以下材质：";

    [TextArea(3, 6)]
    [SerializeField]
    private string promptSuffix =
        "保持矩形外部为干净白色背景，不要改变矩形轮廓，不要在矩形外添加额外物体。生成结果应像一个被该材质填充的平面图形，而不是重新设计新的物体。";

    [Header("Debug")]
    [SerializeField] private bool saveRequestJsonForDebug = true;

    private void Awake()
    {
        if (config == null)
        {
            config = GetComponent<AIServiceConfig>();
        }
    }

    public IEnumerator GenerateRealResult(
        GenerationRequestData requestData,
        Action<string> onSuccess,
        Action<string> onError
    )
    {
        if (requestData == null)
        {
            onError?.Invoke("GenerationRequestData is null.");
            yield break;
        }

        if (config == null)
        {
            onError?.Invoke("AIServiceConfig is missing.");
            yield break;
        }

        string configError;
        if (!config.ValidateConfig(out configError))
        {
            onError?.Invoke($"AIServiceConfig is invalid: {configError}");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(requestData.Prompt))
        {
            onError?.Invoke("Prompt is empty.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(requestData.ReferenceImagePath) ||
            !File.Exists(requestData.ReferenceImagePath))
        {
            onError?.Invoke($"Reference image not found: {requestData.ReferenceImagePath}");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(requestData.ResultImagePath))
        {
            onError?.Invoke("Result image path is empty.");
            yield break;
        }

        config.ApplyDefaultsToRequest(requestData);

        string resultDirectory = Path.GetDirectoryName(requestData.ResultImagePath);
        if (!string.IsNullOrEmpty(resultDirectory))
        {
            Directory.CreateDirectory(resultDirectory);
        }

        string requestJson;
        try
        {
            requestJson = BuildDashScopeRequestJson(requestData);
        }
        catch (Exception e)
        {
            onError?.Invoke($"Failed to build DashScope request JSON: {e}");
            yield break;
        }

        if (saveRequestJsonForDebug)
        {
            TrySaveDebugRequestJson(requestData, requestJson);
        }

        byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);

        UnityWebRequest request = new UnityWebRequest(config.GenerationEndpointUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = config.RequestTimeoutSeconds;

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");

        string authorizationHeader = config.GetAuthorizationHeaderValue();
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
        {
            request.SetRequestHeader("Authorization", authorizationHeader);
        }

        if (config.LogRequestInfo)
        {
            Debug.Log("=== DashScope HTTP Request Start ===");
            Debug.Log($"Endpoint: {config.GenerationEndpointUrl}");
            Debug.Log($"Model: {requestData.ModelName}");
            Debug.Log($"Reference: {requestData.ReferenceImagePath}");
            Debug.Log($"Result Path: {requestData.ResultImagePath}");
            Debug.Log($"Size: {config.GetDashScopeSizeString()}");
            Debug.Log($"User Prompt: {requestData.Prompt}");
        }

        yield return request.SendWebRequest();

        bool requestFailed =
            request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.ProtocolError ||
            request.result == UnityWebRequest.Result.DataProcessingError;

        string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";

        if (requestFailed)
        {
            string errorMessage =
                $"DashScope API request failed.\n" +
                $"Result: {request.result}\n" +
                $"Error: {request.error}\n" +
                $"Response: {responseText}";

            request.Dispose();
            onError?.Invoke(errorMessage);
            yield break;
        }

        if (config.LogRequestInfo)
        {
            Debug.Log("=== DashScope HTTP Response ===");
            Debug.Log(responseText);
        }

        request.Dispose();

        string imageUrl;
        string parseError;

        bool parsed = TryExtractDashScopeImageUrl(responseText, out imageUrl, out parseError);

        if (!parsed)
        {
            onError?.Invoke($"Failed to parse DashScope response: {parseError}\nRaw response: {responseText}");
            yield break;
        }

        yield return StartCoroutine(DownloadImageFromUrl(
            imageUrl,
            requestData.ResultImagePath,
            onSuccess,
            onError
        ));
    }

    private string BuildDashScopeRequestJson(GenerationRequestData requestData)
    {
        // [API适配点 1]
        // DashScope 当前这版先不用 mask。
        // 如果以后换成支持 mask 的 API，可以在这里把 requestData.MaskImagePath 也转成 base64 后加入 JSON。
        string referenceImageDataUrl = ConvertImageFileToBase64DataUrl(requestData.ReferenceImagePath);

        string finalPrompt = BuildFinalPrompt(requestData.Prompt);

        string model = string.IsNullOrWhiteSpace(requestData.ModelName)
            ? config.ModelName
            : requestData.ModelName;

        StringBuilder json = new StringBuilder();

        json.Append("{");

        json.Append("\"model\":\"");
        json.Append(EscapeJson(model));
        json.Append("\",");

        json.Append("\"input\":{");
        json.Append("\"messages\":[");
        json.Append("{");
        json.Append("\"role\":\"user\",");
        json.Append("\"content\":[");

        json.Append("{");
        json.Append("\"image\":\"");
        json.Append(EscapeJson(referenceImageDataUrl));
        json.Append("\"");
        json.Append("},");

        json.Append("{");
        json.Append("\"text\":\"");
        json.Append(EscapeJson(finalPrompt));
        json.Append("\"");
        json.Append("}");

        json.Append("]");
        json.Append("}");
        json.Append("]");
        json.Append("},");

        json.Append("\"parameters\":{");

        json.Append("\"n\":");
        json.Append(config.NumberOfImages);
        json.Append(",");

        json.Append("\"size\":\"");
        json.Append(EscapeJson(config.GetDashScopeSizeString()));
        json.Append("\",");

        json.Append("\"prompt_extend\":");
        json.Append(config.PromptExtend ? "true" : "false");
        json.Append(",");

        json.Append("\"watermark\":");
        json.Append(config.Watermark ? "true" : "false");

        if (!string.IsNullOrWhiteSpace(config.NegativePrompt))
        {
            json.Append(",");
            json.Append("\"negative_prompt\":\"");
            json.Append(EscapeJson(config.NegativePrompt));
            json.Append("\"");
        }

        // [API适配点 2]
        // seed 是否支持取决于具体模型/接口文档。
        // 如果 DashScope 返回“不支持 seed”之类错误，可以把下面这段注释掉。
        if (config.Seed >= 0)
        {
            json.Append(",");
            json.Append("\"seed\":");
            json.Append(config.Seed);
        }

        // [API适配点 3]
        // 暂时不发送 Steps / GuidanceScale。
        // 因为 DashScope 示例中不一定支持这两个字段。
        // 如果公司 API 文档明确支持，再加到这里。

        json.Append("}");

        json.Append("}");

        return json.ToString();
    }

    private string BuildFinalPrompt(string userPrompt)
    {
        if (!wrapUserPrompt)
        {
            return userPrompt;
        }

        return $"{promptPrefix}{userPrompt}{promptSuffix}";
    }

    private string ConvertImageFileToBase64DataUrl(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new ArgumentException("Image path is empty.");
        }

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Image file not found.", imagePath);
        }

        byte[] imageBytes = File.ReadAllBytes(imagePath);
        string base64 = Convert.ToBase64String(imageBytes);

        // [API适配点 4]
        // 这里默认 Unity 导出的是 png。
        // 如果以后导出 jpg，要改成 data:image/jpeg;base64,
        return $"data:image/png;base64,{base64}";
    }

    private IEnumerator DownloadImageFromUrl(
        string imageUrl,
        string savePath,
        Action<string> onSuccess,
        Action<string> onError
    )
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            onError?.Invoke("Image URL is empty.");
            yield break;
        }

        if (config.LogRequestInfo)
        {
            Debug.Log($"Downloading generated image from: {imageUrl}");
        }

        UnityWebRequest imageRequest = UnityWebRequest.Get(imageUrl);
        imageRequest.timeout = config.RequestTimeoutSeconds;

        yield return imageRequest.SendWebRequest();

        bool failed =
            imageRequest.result == UnityWebRequest.Result.ConnectionError ||
            imageRequest.result == UnityWebRequest.Result.ProtocolError ||
            imageRequest.result == UnityWebRequest.Result.DataProcessingError;

        if (failed)
        {
            string errorMessage =
                $"Failed to download generated image.\n" +
                $"URL: {imageUrl}\n" +
                $"Error: {imageRequest.error}";

            imageRequest.Dispose();
            onError?.Invoke(errorMessage);
            yield break;
        }

        try
        {
            byte[] imageBytes = imageRequest.downloadHandler.data;

            string directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(savePath, imageBytes);

            imageRequest.Dispose();
            onSuccess?.Invoke(savePath);
        }
        catch (Exception e)
        {
            imageRequest.Dispose();
            onError?.Invoke($"Failed to save downloaded image: {e}");
        }
    }

    private bool TryExtractDashScopeImageUrl(
        string responseText,
        out string imageUrl,
        out string errorMessage
    )
    {
        imageUrl = "";
        errorMessage = "";

        if (string.IsNullOrWhiteSpace(responseText))
        {
            errorMessage = "Response text is empty.";
            return false;
        }

        try
        {
            DashScopeResponse response = JsonUtility.FromJson<DashScopeResponse>(responseText);

            if (response == null)
            {
                errorMessage = "JsonUtility returned null response.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(response.code))
            {
                errorMessage = $"DashScope returned error code: {response.code}, message: {response.message}";
                return false;
            }

            if (response.output == null)
            {
                errorMessage = "Response output is null.";
                return false;
            }

            if (response.output.choices == null || response.output.choices.Length == 0)
            {
                errorMessage = "Response output.choices is empty.";
                return false;
            }

            for (int i = 0; i < response.output.choices.Length; i++)
            {
                DashScopeChoice choice = response.output.choices[i];

                if (choice == null || choice.message == null || choice.message.content == null)
                {
                    continue;
                }

                for (int j = 0; j < choice.message.content.Length; j++)
                {
                    DashScopeContentItem item = choice.message.content[j];

                    if (item == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(item.image))
                    {
                        imageUrl = item.image;
                        return true;
                    }
                }
            }

            errorMessage = "No image URL found in output.choices[].message.content[].image.";
            return false;
        }
        catch (Exception e)
        {
            errorMessage = $"Failed to parse DashScope response JSON: {e}";
            return false;
        }
    }

    private void TrySaveDebugRequestJson(GenerationRequestData requestData, string requestJson)
    {
        try
        {
            string baseDirectory = Path.GetDirectoryName(requestData.ResultImagePath);

            if (string.IsNullOrEmpty(baseDirectory))
            {
                return;
            }

            string debugDirectory = Path.Combine(baseDirectory, "../DebugRequests");
            debugDirectory = Path.GetFullPath(debugDirectory);

            Directory.CreateDirectory(debugDirectory);

            string fileName = string.IsNullOrWhiteSpace(requestData.RequestId)
                ? $"dashscope_request_{DateTime.Now:yyyyMMdd_HHmmss_fff}.json"
                : $"dashscope_request_{requestData.RequestId}.json";

            string debugPath = Path.Combine(debugDirectory, fileName);
            File.WriteAllText(debugPath, requestJson, Encoding.UTF8);

            if (config.LogRequestInfo)
            {
                Debug.Log($"DashScope request JSON saved: {debugPath}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to save DashScope request JSON: {e}");
        }
    }

    private string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        StringBuilder sb = new StringBuilder();

        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (c < 32)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    [Serializable]
    private class DashScopeResponse
    {
        public string request_id;
        public string code;
        public string message;
        public DashScopeOutput output;
    }

    [Serializable]
    private class DashScopeOutput
    {
        public DashScopeChoice[] choices;
    }

    [Serializable]
    private class DashScopeChoice
    {
        public DashScopeMessage message;
    }

    [Serializable]
    private class DashScopeMessage
    {
        public DashScopeContentItem[] content;
    }

    [Serializable]
    private class DashScopeContentItem
    {
        public string image;
        public string text;
    }
}