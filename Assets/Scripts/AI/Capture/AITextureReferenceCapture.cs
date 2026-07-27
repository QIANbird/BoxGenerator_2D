using System;
using UnityEngine;
using UnityEngine.UIElements;

public enum AITextureCaptureSizeMode
{
    MatchPreviewCanvas,
    FixedOutputSize
}

[Serializable]
public sealed class AITextureCaptureSizeSettings
{
    public AITextureCaptureSizeMode mode =
        AITextureCaptureSizeMode.MatchPreviewCanvas;

    [Min(1)] public int fixedWidth = 1024;
    [Min(1)] public int fixedHeight = 768;
    [Min(1)] public int minimumDimension = 256;
    [Min(1)] public int maximumDimension = 2048;

    public bool TryResolve(
        int previewWidth,
        int previewHeight,
        out int outputWidth,
        out int outputHeight,
        out string errorMessage)
    {
        outputWidth = 0;
        outputHeight = 0;

        if (previewWidth <= 0 || previewHeight <= 0)
        {
            errorMessage = "Preview canvas size is not available yet.";
            return false;
        }

        if (mode == AITextureCaptureSizeMode.FixedOutputSize)
        {
            outputWidth = fixedWidth;
            outputHeight = fixedHeight;
        }
        else
        {
            int safeMinimum = Mathf.Max(1, minimumDimension);
            int safeMaximum = Mathf.Max(safeMinimum, maximumDimension);
            float scale = 1f;
            int longestDimension = Mathf.Max(previewWidth, previewHeight);
            int shortestDimension = Mathf.Min(previewWidth, previewHeight);

            if (longestDimension > safeMaximum)
            {
                scale = safeMaximum / (float)longestDimension;
            }

            if (shortestDimension * scale < safeMinimum)
            {
                scale = safeMinimum / (float)shortestDimension;
            }

            if (longestDimension * scale > safeMaximum)
            {
                scale = safeMaximum / (float)longestDimension;
            }

            outputWidth = Mathf.Max(1, Mathf.RoundToInt(previewWidth * scale));
            outputHeight = Mathf.Max(1, Mathf.RoundToInt(previewHeight * scale));
        }

        if (outputWidth <= 0 || outputHeight <= 0)
        {
            errorMessage = "Resolved capture size is invalid.";
            return false;
        }

        errorMessage = "";
        return true;
    }
}

public sealed class AITextureReferenceSnapshot
{
    public AITextureImageData image;
    public ChestLatentParams chestParameters;
    public Vector3 previewEulerAngles;
    public int previewCanvasWidth;
    public int previewCanvasHeight;
    public int outputWidth;
    public int outputHeight;
}

[DisallowMultipleComponent]
public sealed class AITextureReferenceCapture : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Chest3DGenerator chestGenerator;
    [SerializeField] private Camera editingPreviewCamera;

    [Header("Preview Canvas")]
    [SerializeField] private string drawingAreaName = "DrawingArea";
    [SerializeField] private int fallbackCanvasWidth = 1024;
    [SerializeField] private int fallbackCanvasHeight = 768;

    [Header("Capture Size")]
    [SerializeField] private AITextureCaptureSizeSettings sizeSettings =
        new AITextureCaptureSizeSettings();

    [Header("Image")]
    [Range(1, 8)]
    [SerializeField] private int antiAliasing = 4;

    public AITextureCaptureSizeSettings SizeSettings => sizeSettings;

    public bool TryCapture(
        out AITextureReferenceSnapshot snapshot,
        out string errorMessage)
    {
        snapshot = null;
        ResolveReferences();

        if (chestGenerator == null)
        {
            errorMessage = "The 3D chest generator is unavailable.";
            return false;
        }

        if (!chestGenerator.HasGeneratedChest)
        {
            errorMessage = "Generate a chest preview before creating a texture.";
            return false;
        }

        if (editingPreviewCamera == null)
        {
            errorMessage = "The Editing preview camera is unavailable.";
            return false;
        }

        ChestLatentParams parameterSnapshot =
            chestGenerator.CreateParameterSnapshot();

        if (parameterSnapshot == null)
        {
            errorMessage = "The current chest parameters are unavailable.";
            return false;
        }

        ResolvePreviewCanvasSize(out int canvasWidth, out int canvasHeight);

        if (sizeSettings == null)
        {
            sizeSettings = new AITextureCaptureSizeSettings();
        }

        if (!sizeSettings.TryResolve(
                canvasWidth,
                canvasHeight,
                out int outputWidth,
                out int outputHeight,
                out errorMessage))
        {
            return false;
        }

        if (!TryRenderEditingImage(
                canvasWidth,
                canvasHeight,
                outputWidth,
                outputHeight,
                out AITextureImageData image,
                out errorMessage))
        {
            return false;
        }

        snapshot = new AITextureReferenceSnapshot
        {
            image = image,
            chestParameters = parameterSnapshot,
            previewEulerAngles = chestGenerator.PreviewEulerAngles,
            previewCanvasWidth = canvasWidth,
            previewCanvasHeight = canvasHeight,
            outputWidth = outputWidth,
            outputHeight = outputHeight
        };

        return true;
    }

    private void ResolveReferences()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (chestGenerator == null)
        {
            chestGenerator = FindFirstObjectByType<Chest3DGenerator>();
        }

        if (editingPreviewCamera == null && chestGenerator != null)
        {
            editingPreviewCamera = chestGenerator.EditPreviewCamera;
        }
    }

    private void ResolvePreviewCanvasSize(out int width, out int height)
    {
        width = 0;
        height = 0;

        if (uiDocument != null)
        {
            VisualElement drawingArea =
                uiDocument.rootVisualElement.Q<VisualElement>(drawingAreaName);

            if (drawingArea != null)
            {
                width = Mathf.RoundToInt(drawingArea.resolvedStyle.width);
                height = Mathf.RoundToInt(drawingArea.resolvedStyle.height);
            }
        }

        if (width <= 0)
        {
            width = Mathf.Max(1, fallbackCanvasWidth);
        }

        if (height <= 0)
        {
            height = Mathf.Max(1, fallbackCanvasHeight);
        }
    }

    private bool TryRenderEditingImage(
        int previewCanvasWidth,
        int previewCanvasHeight,
        int outputWidth,
        int outputHeight,
        out AITextureImageData image,
        out string errorMessage)
    {
        image = null;
        RenderTexture captureTexture = null;
        RenderTexture resolvedTexture = null;
        Texture2D readableTexture = null;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = editingPreviewCamera.targetTexture;
        Rect previousRect = editingPreviewCamera.rect;
        float previousAspect = editingPreviewCamera.aspect;

        try
        {
            int samples = NormalizeAntiAliasing(antiAliasing);
            captureTexture = new RenderTexture(
                outputWidth,
                outputHeight,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "AITextureEditingReferenceCapture",
                antiAliasing = samples,
                useMipMap = false,
                autoGenerateMips = false
            };

            resolvedTexture = new RenderTexture(
                outputWidth,
                outputHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "AITextureEditingReferenceResolved",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };

            captureTexture.Create();
            resolvedTexture.Create();

            RenderTexture.active = captureTexture;
            GL.Clear(true, true, editingPreviewCamera.backgroundColor);

            editingPreviewCamera.targetTexture = captureTexture;
            editingPreviewCamera.rect = CalculateContainedViewport(
                previewCanvasWidth,
                previewCanvasHeight,
                outputWidth,
                outputHeight);
            editingPreviewCamera.aspect =
                previewCanvasWidth / (float)previewCanvasHeight;
            editingPreviewCamera.Render();

            Graphics.Blit(captureTexture, resolvedTexture);
            RenderTexture.active = resolvedTexture;

            readableTexture = new Texture2D(
                outputWidth,
                outputHeight,
                TextureFormat.RGBA32,
                false,
                false);
            readableTexture.ReadPixels(
                new Rect(0, 0, outputWidth, outputHeight),
                0,
                0,
                false);
            readableTexture.Apply(false, false);

            byte[] pngBytes = readableTexture.EncodeToPNG();

            if (pngBytes == null || pngBytes.Length == 0)
            {
                errorMessage = "The Editing reference image could not be encoded.";
                return false;
            }

            image = new AITextureImageData(
                pngBytes,
                outputWidth,
                outputHeight,
                "image/png",
                "editing_base_shape.png");
            errorMessage = "";
            return true;
        }
        catch (Exception exception)
        {
            errorMessage =
                $"Unable to capture the Editing reference image: {exception.Message}";
            return false;
        }
        finally
        {
            editingPreviewCamera.targetTexture = previousTarget;
            editingPreviewCamera.rect = previousRect;
            editingPreviewCamera.aspect = previousAspect;
            RenderTexture.active = previousActive;
            DestroyRuntimeObject(readableTexture);
            ReleaseRenderTexture(captureTexture);
            ReleaseRenderTexture(resolvedTexture);
        }
    }

    private static Rect CalculateContainedViewport(
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        float sourceAspect = sourceWidth / (float)sourceHeight;
        float targetAspect = targetWidth / (float)targetHeight;

        if (Mathf.Approximately(sourceAspect, targetAspect))
        {
            return new Rect(0f, 0f, 1f, 1f);
        }

        if (targetAspect > sourceAspect)
        {
            float normalizedWidth = sourceAspect / targetAspect;
            return new Rect(
                (1f - normalizedWidth) * 0.5f,
                0f,
                normalizedWidth,
                1f);
        }

        float normalizedHeight = targetAspect / sourceAspect;
        return new Rect(
            0f,
            (1f - normalizedHeight) * 0.5f,
            1f,
            normalizedHeight);
    }

    private static int NormalizeAntiAliasing(int requestedSamples)
    {
        if (requestedSamples >= 8)
        {
            return 8;
        }

        if (requestedSamples >= 4)
        {
            return 4;
        }

        return requestedSamples >= 2 ? 2 : 1;
    }

    private static void ReleaseRenderTexture(RenderTexture texture)
    {
        if (texture == null)
        {
            return;
        }

        texture.Release();
        DestroyRuntimeObject(texture);
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void OnValidate()
    {
        fallbackCanvasWidth = Mathf.Max(1, fallbackCanvasWidth);
        fallbackCanvasHeight = Mathf.Max(1, fallbackCanvasHeight);
        antiAliasing = NormalizeAntiAliasing(antiAliasing);

        if (sizeSettings == null)
        {
            sizeSettings = new AITextureCaptureSizeSettings();
        }

        sizeSettings.fixedWidth = Mathf.Max(1, sizeSettings.fixedWidth);
        sizeSettings.fixedHeight = Mathf.Max(1, sizeSettings.fixedHeight);
        sizeSettings.minimumDimension =
            Mathf.Max(1, sizeSettings.minimumDimension);
        sizeSettings.maximumDimension = Mathf.Max(
            sizeSettings.minimumDimension,
            sizeSettings.maximumDimension);
    }
}
