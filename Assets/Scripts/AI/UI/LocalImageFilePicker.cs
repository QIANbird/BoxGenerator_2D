using System;
using System.Runtime.InteropServices;
using System.Text;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class LocalImageFilePicker
{
    public static bool TryPickImage(out string selectedPath, out string errorMessage)
    {
        selectedPath = "";
        errorMessage = "";

#if UNITY_EDITOR
        selectedPath = EditorUtility.OpenFilePanelWithFilters(
            "Select a texture or style reference image",
            "",
            new[]
            {
                "Image Files", "png,jpg,jpeg",
                "PNG Files", "png",
                "JPEG Files", "jpg,jpeg"
            }
        );

        return !string.IsNullOrWhiteSpace(selectedPath);
#elif UNITY_STANDALONE_WIN
        return TryOpenWindowsImageDialog(out selectedPath, out errorMessage);
#else
        errorMessage =
            "Local image selection is currently implemented for the Unity Editor " +
            "and Windows standalone builds.";
        return false;
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnNoChangeDirectory = 0x00000008;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class OpenFileName
    {
        public int structSize;
        public IntPtr owner;
        public IntPtr instance;
        public string filter;
        public string customFilter;
        public int maxCustomFilter;
        public int filterIndex;
        public StringBuilder file;
        public int maxFile;
        public StringBuilder fileTitle;
        public int maxFileTitle;
        public string initialDirectory;
        public string title;
        public int flags;
        public short fileOffset;
        public short fileExtension;
        public string defaultExtension;
        public IntPtr customData;
        public IntPtr hook;
        public string templateName;
        public IntPtr reservedPointer;
        public int reservedInt;
        public int extendedFlags;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileName([In, Out] OpenFileName openFileName);

    [DllImport("comdlg32.dll")]
    private static extern int CommDlgExtendedError();

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    private static bool TryOpenWindowsImageDialog(
        out string selectedPath,
        out string errorMessage)
    {
        selectedPath = "";
        errorMessage = "";

        OpenFileName dialog = new OpenFileName
        {
            structSize = Marshal.SizeOf<OpenFileName>(),
            owner = GetActiveWindow(),
            filter =
                "Image Files\0*.png;*.jpg;*.jpeg\0" +
                "PNG Files\0*.png\0" +
                "JPEG Files\0*.jpg;*.jpeg\0" +
                "All Files\0*.*\0\0",
            filterIndex = 1,
            file = new StringBuilder(4096),
            maxFile = 4096,
            fileTitle = new StringBuilder(512),
            maxFileTitle = 512,
            title = "Select a texture or style reference image",
            defaultExtension = "png",
            flags = OfnPathMustExist | OfnFileMustExist | OfnNoChangeDirectory
        };

        try
        {
            if (!GetOpenFileName(dialog))
            {
                // A zero extended error means the user closed the dialog.
                int nativeError = CommDlgExtendedError();

                if (nativeError != 0)
                {
                    errorMessage =
                        $"The Windows image picker failed with error {nativeError}.";
                }

                return false;
            }

            selectedPath = dialog.file.ToString();
            return !string.IsNullOrWhiteSpace(selectedPath);
        }
        catch (Exception exception)
        {
            errorMessage = $"Unable to open the image picker: {exception.Message}";
            return false;
        }
    }
#endif
}
