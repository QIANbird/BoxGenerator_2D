using System;
using System.Collections;
using System.Data.SqlTypes;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class MaterialGenerateManager : MonoBehaviour
{
    //定义 UI 元素名称、输出设置和模拟 AI 设置
    [Header("UXML Element Names")]
    [SerializeField] private string promptInputName = "PromptInput";
    [SerializeField] private string generateButtonName = "Btn_generate";
    [SerializeField] private string resultPreviewName = "ResultPreview";
    [SerializeField] private string errorLabelName = "ErrorLabel";
    [SerializeField] private string drawingAreaName = "DrawingArea";
    [SerializeField] private string rectangleElementName = "RectangleOutline";

    [Header("Export Settings")]
    [SerializeField] private int exportWidth = 512;
    [SerializeField] private int exportHeight = 512;
    [SerializeField] private float referenceLineThickness = 4f;

    [Header("Debug")]
    [SerializeField] private bool saveRequestJson = true;


    private TextField promptInput;
    private Button generateButton;
    private VisualElement resultPreview;
    private Label errorLabel;
    private VisualElement drawingArea;

    private ShapeReferenceExporter referenceExporter;

    // 用来导出 mask.png：黑底 + 白色实心矩形
    private ShapeMaskExporter maskExporter;
    // 用来调用真实云端 AI API
    private ImageAIService imageAIService;
    // 用来读取 API endpoint、key、model、output size 等配置
    private AIServiceConfig aiServiceConfig;

    private string rootOutputDirectory;
    private string promptDirectory;
    private string referenceDirectory;
    private string maskDirectory;
    private string resultDirectory;
    private string requestDirectory;

    private bool isGenerating = false;
    private string originalGenerateButtonText = "Generate";


    /// 在启用脚本时初始化 UI 元素、组件和输出目录
   
    private void OnEnable()
    {
        UIDocument uiDocument = GetComponent<UIDocument>();
        VisualElement root = uiDocument.rootVisualElement;

        promptInput = root.Q<TextField>(promptInputName);
        generateButton = root.Q<Button>(generateButtonName);
        resultPreview = root.Q<VisualElement>(resultPreviewName);
        errorLabel = root.Q<Label>(errorLabelName);
        drawingArea = root.Q<VisualElement>(drawingAreaName);

        // 检查所有必需的 UI 元素是否存在，并在缺失时输出错误日志
        if (promptInput == null)
        {
            Debug.LogError($"找不到 TextField：{promptInputName}。请检查 UI Builder 里的 Name。");
            return;
        }

        if (generateButton == null)
        {
            Debug.LogError($"找不到 Button：{generateButtonName}。请检查 UI Builder 里的 Name。");
            return;
        }

        if (resultPreview == null)
        {
            Debug.LogError($"找不到 ResultPreview：{resultPreviewName}。请检查 UI Builder 里的 Name。");
            return;
        }

        if (drawingArea == null)
        {
            Debug.LogError($"找不到 DrawingArea：{drawingAreaName}。请检查 UI Builder 里的 Name。");
            return;
        }

        if (errorLabel == null)
        {
            Debug.LogWarning($"找不到 ErrorLabel：{errorLabelName}。不会影响生成流程，但不会显示界面错误提示。");
        }

        // 获取或添加 ShapeReferenceExporter 组件
        referenceExporter = GetComponent<ShapeReferenceExporter>();
        if (referenceExporter == null)
        {
            referenceExporter = gameObject.AddComponent<ShapeReferenceExporter>();
        }
        // 获取或添加 ShapeMaskExporter 组件
        maskExporter = GetComponent<ShapeMaskExporter>();
        if (maskExporter == null)
        {
            maskExporter = gameObject.AddComponent<ShapeMaskExporter>();
        }
        // 获取或添加 ImageAIService 组件及AIconfig文档
        aiServiceConfig = GetComponent<AIServiceConfig>();
        if (aiServiceConfig == null)
        {
            aiServiceConfig = gameObject.AddComponent<AIServiceConfig>();
            Debug.LogWarning("当前 GameObject 上没有 AIServiceConfig，已自动添加。请在 Inspector 中填写真实 API 配置。");
        }
        imageAIService = GetComponent<ImageAIService>();
        if (imageAIService == null)
        {
            imageAIService = gameObject.AddComponent<ImageAIService>();
        }

        InitializeOutputDirectories();

        // 初始化输出目录结构
        InitializeOutputDirectories();

        // 存储原始按钮文本以便在生成过程中更新和恢复
        originalGenerateButtonText = generateButton.text;
        generateButton.clicked += OnGenerateClicked;

        // 确保初始状态下没有错误信息显示
        HideError();
        // 初始化结果预览区域的样式
        resultPreview.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        resultPreview.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
        resultPreview.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
    }

    /// <summary>
    /// 在禁用脚本时清理按钮点击事件
    /// </summary>
    private void OnDisable()
    {
        if (generateButton != null)
        {
            generateButton.clicked -= OnGenerateClicked;
        }
    }

    /// <summary>
    /// 初始化输出目录，用于存储生成的文件
    /// </summary>
    private void InitializeOutputDirectories()
    {
        rootOutputDirectory = Path.Combine(Application.persistentDataPath, "MaterialGeneratorDemo");
        promptDirectory = Path.Combine(rootOutputDirectory, "Prompts");
        referenceDirectory = Path.Combine(rootOutputDirectory, "References");
        resultDirectory = Path.Combine(rootOutputDirectory, "Results");
        maskDirectory = Path.Combine(rootOutputDirectory, "Masks");
        resultDirectory = Path.Combine(rootOutputDirectory, "Results");
        requestDirectory = Path.Combine(rootOutputDirectory, "Requests");


        Directory.CreateDirectory(rootOutputDirectory);
        Directory.CreateDirectory(promptDirectory);
        Directory.CreateDirectory(referenceDirectory);
        Directory.CreateDirectory(resultDirectory);

        Debug.Log($"本地生成文件目录：{rootOutputDirectory}");
    }

    /// <summary>
    /// 生成按钮点击事件的入口
    /// </summary>
    /// 当用户点击生成按钮时，如果当前没有正在生成的任务，则启动生成流程
    private void OnGenerateClicked()
    {
        if (isGenerating)
        {
            return;
        }

        StartCoroutine(GenerateRoutine());
    }

    /// <summary>
    /// 生成流程的核心逻辑，包括输入验证、文件保存、图像生成和加载
    /// </summary>
    private IEnumerator GenerateRoutine()
    {
        string prompt = promptInput.value == null ? "" : promptInput.value.Trim();

        if (string.IsNullOrWhiteSpace(prompt))
        {
            ShowError("Please enter a material prompt.");
            yield break;
        }

        VisualElement rectangleElement = drawingArea.Q<VisualElement>(rectangleElementName);

        if (rectangleElement == null)
        {
            ShowError("Please create a rectangle first.");
            yield break;
        }

        SetGeneratingState(true);
        HideError();

        string requestId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        // 使用 AIServiceConfig 中的输出尺寸配置，如果没有设置则使用默认值
        int outputWidth = aiServiceConfig.OutputWidth > 0 ? aiServiceConfig.OutputWidth : exportWidth;
        int outputHeight = aiServiceConfig.OutputHeight > 0 ? aiServiceConfig.OutputHeight : exportHeight;

        // 定义生成文件的路径，包括 prompt 文本、参考图像、mask 图像、结果图像和请求 JSON
        string promptPath = Path.Combine(promptDirectory, $"prompt_{requestId}.txt");
        string referenceImagePath = Path.Combine(referenceDirectory, $"reference_{requestId}.png");
        string maskImagePath = Path.Combine(maskDirectory, $"mask_{requestId}.png");
        string resultImagePath = Path.Combine(resultDirectory, $"result_{requestId}.png");
        string requestJsonPath = Path.Combine(requestDirectory, $"request_{requestId}.json");

        // 将 prompt 文本保存到本地文件，供后续调试和记录使用
        try
        {
            File.WriteAllText(promptPath, prompt, Encoding.UTF8);
            Debug.Log($"Prompt 已保存：{promptPath}");
        }
        catch (Exception e)
        {
            ShowError("Failed to save prompt.");
            Debug.LogError(e);
            SetGeneratingState(false);
            yield break;
        }
        // 导出参考图像，包含用户绘制的矩形区域，并保存到本地文件
        string exportReferenceError;//存储导出参考图像过程中可能出现的错误信息
        bool exportSuccess = referenceExporter.ExportRectangleReference(
            drawingArea,
            rectangleElement,
            referenceImagePath,
            outputWidth,
            outputHeight,
            referenceLineThickness,
            out exportReferenceError
        );

        if (!exportSuccess)
        {
            ShowError("Failed to export reference image.");
            Debug.LogError(exportReferenceError);
            SetGeneratingState(false);
            yield break;
        }

        // 导出 mask.png：黑底 + 白色实心矩形
        string exportMaskError;
        bool maskExportSuccess = maskExporter.ExportRectangleMask(
            drawingArea,
            rectangleElement,
            maskImagePath,
            outputWidth,
            outputHeight,
            out exportMaskError
        );

        if (!maskExportSuccess)
        {
            ShowError("Failed to export mask image.");
            Debug.LogError(exportMaskError);
            SetGeneratingState(false);
            yield break;
        }
        // 构建生成请求数据对象，包含所有必要的信息和参数，供后续调用 AI 服务使用
        GenerationRequestData requestData = new GenerationRequestData
        {
            Prompt = prompt,
            PromptTextPath = promptPath,
            ReferenceImagePath = referenceImagePath,
            MaskImagePath = maskImagePath,
            ResultImagePath = resultImagePath,
            OutputWidth = outputWidth,
            OutputHeight = outputHeight,

            ModelName = aiServiceConfig.ModelName,
            Seed = aiServiceConfig.Seed,
            Steps = aiServiceConfig.Steps,
            GuidanceScale = aiServiceConfig.GuidanceScale,

            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        // 保存一份request JSON，便于调试云API请求
        if (saveRequestJson)
        {
            try
            {
                string requestJson = JsonUtility.ToJson(requestData, true);
                File.WriteAllText(requestJsonPath, requestJson, Encoding.UTF8);
                Debug.Log($"Request JSON 已保存：{requestJsonPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Request JSON 保存失败，但不会中断生成流程：{e}");
            }
        }
        
        bool AICompleted = false;
        bool AISuccess = false;
        string AIError = "";
        string finalImagePath = "";


        // 调用 ImageAIService-GenerateRealResult 方法，传入请求数据和回调函数，等待生成完成
        yield return StartCoroutine(imageAIService.GenerateRealResult(
         requestData,
         onSuccess: path =>
         {
             AICompleted = true;
             AISuccess = true;
             finalImagePath = path;
         },
         onError: error =>
         {
             AICompleted = true;
             AISuccess = false;
             AIError = error;
         }
     ));

        if (!AICompleted || !AISuccess)
        {
            ShowError("Real AI generation failed.");
            Debug.LogError(AIError);
            SetGeneratingState(false);
            yield break;
        }

        // 生成完成后，加载生成的图像到结果预览区域，并更新 UI 状态
        bool previewLoaded = LoadImageToResultPreview(finalImagePath);

        if (!previewLoaded)
        {
            ShowError("Failed to load result image.");
            SetGeneratingState(false);
            yield break;
        }

        Debug.Log($"result 已显示：{finalImagePath}");

        SetGeneratingState(false);
    }

    /// 加载生成的图像到结果预览区域
    private bool LoadImageToResultPreview(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            Debug.LogError($"结果图片不存在：{imagePath}");
            return false;
        }

        try
        {
            byte[] imageBytes = File.ReadAllBytes(imagePath);

            Texture2D texture = new Texture2D(2, 2);
            bool loaded = texture.LoadImage(imageBytes);

            if (!loaded)
            {
                Debug.LogError("Texture2D.LoadImage 失败。");
                return false;
            }

            resultPreview.style.backgroundImage = new StyleBackground(texture);
            resultPreview.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            resultPreview.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            resultPreview.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            // resultPreview.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            return false;
        }
    }

    /// 设置生成状态（禁用按钮、更新按钮文本）

    private void SetGeneratingState(bool generating)
    {
        isGenerating = generating;

        if (generateButton == null)
        {
            return;
        }

        generateButton.SetEnabled(!generating);
        generateButton.text = generating ? "Generating..." : originalGenerateButtonText;
    }

   /// 显示错误信息
    private void ShowError(string message)
    {
        Debug.LogWarning(message);

        if (errorLabel == null)
        {
            return;
        }

        errorLabel.text = message;
        errorLabel.style.display = DisplayStyle.Flex;
    }

    /// 隐藏错误信息
    private void HideError()
    {
        if (errorLabel == null)
        {
            return;
        }

        errorLabel.text = "";
        errorLabel.style.display = DisplayStyle.None;
    }
}