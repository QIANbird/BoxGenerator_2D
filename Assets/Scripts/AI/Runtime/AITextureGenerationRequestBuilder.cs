using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AITextureGenerationRequestBuilder : MonoBehaviour
{
    private const string BaseSystemPrompt =
        "最后一张输入图片是彩色宝箱基本型参考图，必须严格参考。保持宝箱的观察视角、外轮廓、主要结构、" +
        "中心位置、画面占比和边界范围基本不变。不要随意放大、缩小、旋转、平移或裁切宝箱，" +
        "不要增加无关主体。只改变纹理、材质、颜色和表面细节。" +
        "输出画布宽高比必须与宝箱基本型参考图一致。";

    private const string UploadedImageConstraint =
        "除最后一张宝箱基本型图以外的输入图片仅作为纹理、配色与风格参考，" +
        "不得复制其主体结构或改变宝箱基本型。";

    [SerializeField] private AITextureReferenceCapture referenceCapture;

    public bool TryBuildRequest(
        AITextureGenerationInputState inputState,
        out AITextureGenerationRequest request,
        out string errorMessage)
    {
        request = null;

        if (inputState == null || !inputState.HasUserInput)
        {
            errorMessage =
                "Enter text, upload an image, or select a style.";
            return false;
        }

        ResolveReferences();

        if (referenceCapture == null)
        {
            errorMessage = "The Editing reference capture component is missing.";
            return false;
        }

        if (!referenceCapture.TryCapture(
                out AITextureReferenceSnapshot capture,
                out errorMessage))
        {
            return false;
        }

        AITextureStyleSelection style = inputState.selectedStyle;
        string userPrompt = (inputState.userPrompt ?? "").Trim();
        string styleId =
            style != null && style.IsSelected ? style.id ?? "" : "";
        string stylePrompt =
            style != null && style.IsSelected
                ? (style.promptSuffix ?? "").Trim()
                : "";
        bool hasUploadedImage =
            inputState.uploadedReferenceImage != null &&
            inputState.uploadedReferenceImage.HasData;
        string systemPrompt = hasUploadedImage
            ? $"{BaseSystemPrompt}{UploadedImageConstraint}"
            : BaseSystemPrompt;

        request = new AITextureGenerationRequest
        {
            userPrompt = userPrompt,
            styleId = styleId,
            stylePrompt = stylePrompt,
            systemPrompt = systemPrompt,
            finalPrompt = BuildFinalPrompt(
                systemPrompt,
                userPrompt,
                stylePrompt),
            baseShapeImage = capture.image,
            uploadedReferenceImage = hasUploadedImage
                ? inputState.uploadedReferenceImage.Clone()
                : null,
            chestParameters = capture.chestParameters,
            previewEulerAngles = capture.previewEulerAngles,
            inputWidth = capture.image.width,
            inputHeight = capture.image.height,
            outputWidth = capture.outputWidth,
            outputHeight = capture.outputHeight
        };

        errorMessage = "";
        return true;
    }

    private void ResolveReferences()
    {
        if (referenceCapture == null)
        {
            referenceCapture = GetComponent<AITextureReferenceCapture>();
        }
    }

    private static string BuildFinalPrompt(
        string systemPrompt,
        string userPrompt,
        string stylePrompt)
    {
        StringBuilder builder = new StringBuilder();
        AppendSection(builder, "系统构图约束", systemPrompt);
        AppendSection(builder, "用户纹理要求", userPrompt);
        AppendSection(builder, "预设风格要求", stylePrompt);
        return builder.ToString().Trim();
    }

    private static void AppendSection(
        StringBuilder builder,
        string title,
        string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
        }

        builder.Append(title);
        builder.AppendLine("：");
        builder.Append(content.Trim());
    }
}
