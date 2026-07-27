using System;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AITextureJpgDownloadController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AITexturePanelController panelController;
    [SerializeField] private Chest3DPreviewUIController previewController;

    [Header("JPG Export")]
    [Range(1, 100)]
    [SerializeField] private int jpegQuality = 95;
    [SerializeField] private string exportFolderName = "Exports";
    [SerializeField] private string fileNamePrefix = "chest_preview_";

    [Header("Debug")]
    [SerializeField] private bool logSavedPath = true;

    private bool isBound;

    public event Action<string> DownloadSucceeded;
    public event Action<string> DownloadFailed;

    public string LastSavedPath { get; private set; } = "";

    private void OnEnable()
    {
        ResolveReferences();
        BindEvents();
    }

    private void OnDisable()
    {
        UnbindEvents();
    }

    private void ResolveReferences()
    {
        if (panelController == null)
        {
            panelController = GetComponent<AITexturePanelController>();
        }

        if (previewController == null)
        {
            previewController = GetComponent<Chest3DPreviewUIController>();
        }
    }

    private void BindEvents()
    {
        if (isBound || panelController == null)
        {
            return;
        }

        panelController.DownloadRequested += OnDownloadRequested;
        isBound = true;
    }

    private void UnbindEvents()
    {
        if (!isBound || panelController == null)
        {
            return;
        }

        panelController.DownloadRequested -= OnDownloadRequested;
        isBound = false;
    }

    private void OnDownloadRequested()
    {
        if (TryExportCurrentPreview(out string savedPath, out string errorMessage))
        {
            LastSavedPath = savedPath;
            panelController?.ShowStatus($"JPG saved:\n{savedPath}");
            DownloadSucceeded?.Invoke(savedPath);

            if (logSavedPath)
            {
                Debug.Log($"[AI Texture] JPG saved to: {savedPath}", this);
            }

            return;
        }

        panelController?.ShowError(errorMessage);
        DownloadFailed?.Invoke(errorMessage);
        Debug.LogError($"[AI Texture] JPG export failed: {errorMessage}", this);
    }

    public bool TryExportCurrentPreview(
        out string savedPath,
        out string errorMessage)
    {
        savedPath = "";

        if (previewController == null)
        {
            errorMessage = "The chest preview controller is unavailable.";
            return false;
        }

        if (!previewController.TryCaptureCurrentPreviewImage(
                out Texture2D capturedImage,
                out errorMessage))
        {
            return false;
        }

        try
        {
            int quality = Mathf.Clamp(jpegQuality, 1, 100);
            byte[] jpgBytes = capturedImage.EncodeToJPG(quality);

            if (jpgBytes == null || jpgBytes.Length == 0)
            {
                errorMessage = "The current preview could not be encoded as JPG.";
                return false;
            }

            string persistentRoot = Application.persistentDataPath;

            if (string.IsNullOrWhiteSpace(persistentRoot))
            {
                errorMessage = "The application export directory is unavailable.";
                return false;
            }

            string safeFolderName = SanitizePathSegment(
                exportFolderName,
                "Exports");
            string safePrefix = SanitizePathSegment(
                fileNamePrefix,
                "chest_preview_");
            string exportDirectory = Path.Combine(
                persistentRoot,
                safeFolderName);

            Directory.CreateDirectory(exportDirectory);
            savedPath = CreateUniqueFilePath(exportDirectory, safePrefix);
            File.WriteAllBytes(savedPath, jpgBytes);

            if (!File.Exists(savedPath) || new FileInfo(savedPath).Length == 0)
            {
                errorMessage = "The JPG file was not written successfully.";
                savedPath = "";
                return false;
            }

            errorMessage = "";
            return true;
        }
        catch (Exception exception)
        {
            savedPath = "";
            errorMessage = $"Unable to save the JPG: {exception.Message}";
            return false;
        }
        finally
        {
            DestroyRuntimeObject(capturedImage);
        }
    }

    private static string CreateUniqueFilePath(
        string directory,
        string prefix)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string baseName = $"{prefix}{timestamp}";
        string candidate = Path.Combine(directory, $"{baseName}.jpg");
        int suffix = 1;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(
                directory,
                $"{baseName}_{suffix:000}.jpg");
            suffix++;
        }

        return candidate;
    }

    private static string SanitizePathSegment(
        string value,
        string fallback)
    {
        string source = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        StringBuilder builder = new StringBuilder(source.Length);

        for (int i = 0; i < source.Length; i++)
        {
            char character = source[i];
            bool invalid =
                character == Path.DirectorySeparatorChar ||
                character == Path.AltDirectorySeparatorChar ||
                Array.IndexOf(invalidCharacters, character) >= 0;

            if (!invalid)
            {
                builder.Append(character);
            }
        }

        string result = builder.ToString();

        if (string.IsNullOrWhiteSpace(result) ||
            string.Equals(result, ".", StringComparison.Ordinal) ||
            string.Equals(result, "..", StringComparison.Ordinal))
        {
            return fallback;
        }

        return result;
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
        jpegQuality = Mathf.Clamp(jpegQuality, 1, 100);
    }
}
