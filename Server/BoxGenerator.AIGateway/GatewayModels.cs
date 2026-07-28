using System.Globalization;

namespace BoxGenerator.AIGateway;

public sealed class GatewayValidationException : Exception
{
    public GatewayValidationException(string message)
        : base(message)
    {
    }
}

public sealed record UploadedImage(
    byte[] Bytes,
    string ContentType,
    string FileName);

public sealed record GenerationSubmission(
    string RequestId,
    string Prompt,
    string StyleId,
    int TargetWidth,
    int TargetHeight,
    UploadedImage BaseShapeImage,
    UploadedImage? StyleReferenceImage)
{
    public static async Task<GenerationSubmission> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            throw new GatewayValidationException(
                "Content-Type must be multipart/form-data.");
        }

        IFormCollection form = await request.ReadFormAsync(cancellationToken);
        string requestId = form["requestId"].ToString().Trim();
        string prompt = form["prompt"].ToString().Trim();
        string styleId = form["styleId"].ToString().Trim();

        if (requestId.Length == 0 ||
            requestId.Length > 64 ||
            requestId.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
        {
            throw new GatewayValidationException("requestId is invalid.");
        }

        if (prompt.Length == 0)
        {
            throw new GatewayValidationException("prompt is required.");
        }

        if (prompt.Length > 5000)
        {
            throw new GatewayValidationException(
                "prompt cannot exceed 5000 characters.");
        }

        if (styleId.Length > 64)
        {
            throw new GatewayValidationException(
                "styleId cannot exceed 64 characters.");
        }

        int targetWidth = ParseDimension(
            form["targetWidth"].ToString(),
            "targetWidth");
        int targetHeight = ParseDimension(
            form["targetHeight"].ToString(),
            "targetHeight");

        IFormFile? baseShapeFile = form.Files.GetFile("baseShapeImage");

        if (baseShapeFile == null)
        {
            throw new GatewayValidationException(
                "baseShapeImage is required.");
        }

        UploadedImage baseShape = await ReadJpegAsync(
            baseShapeFile,
            cancellationToken);

        IFormFile? styleFile = form.Files.GetFile("styleReferenceImage");
        UploadedImage? styleReference = styleFile == null
            ? null
            : await ReadJpegAsync(styleFile, cancellationToken);

        return new GenerationSubmission(
            requestId,
            prompt,
            styleId,
            targetWidth,
            targetHeight,
            baseShape,
            styleReference);
    }

    private static int ParseDimension(string value, string fieldName)
    {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int dimension) ||
            dimension < 240 ||
            dimension > 8000)
        {
            throw new GatewayValidationException(
                $"{fieldName} must be between 240 and 8000.");
        }

        return dimension;
    }

    private static async Task<UploadedImage> ReadJpegAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0 || file.Length > GatewayOptions.MaxImageBytes)
        {
            throw new GatewayValidationException(
                "Each image must be between 1 byte and 20 MB.");
        }

        if (!string.Equals(
                file.ContentType,
                "image/jpeg",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new GatewayValidationException(
                "Gateway input images must be normalized JPEG files.");
        }

        await using Stream stream = file.OpenReadStream();
        using MemoryStream memory = new((int)file.Length);
        await stream.CopyToAsync(memory, cancellationToken);
        byte[] bytes = memory.ToArray();

        if (bytes.Length < 4 ||
            bytes[0] != 0xFF ||
            bytes[1] != 0xD8 ||
            bytes[2] != 0xFF)
        {
            throw new GatewayValidationException(
                "An uploaded file is not a valid JPEG image.");
        }

        if (!TryReadJpegDimensions(bytes, out int width, out int height))
        {
            throw new GatewayValidationException(
                "An uploaded JPEG has invalid or unsupported dimensions.");
        }

        double aspectRatio = (double)width / height;

        if (width < 240 ||
            width > 8000 ||
            height < 240 ||
            height > 8000 ||
            aspectRatio < 1d / 8d ||
            aspectRatio > 8d)
        {
            throw new GatewayValidationException(
                "Each image must be 240-8000 pixels per side with an aspect ratio between 1:8 and 8:1.");
        }

        return new UploadedImage(
            bytes,
            "image/jpeg",
            Path.GetFileName(file.FileName));
    }

    private static bool TryReadJpegDimensions(
        byte[] bytes,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;
        int offset = 2;

        while (offset + 3 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            while (offset < bytes.Length && bytes[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= bytes.Length)
            {
                return false;
            }

            byte marker = bytes[offset++];

            if (marker == 0xD8 ||
                marker == 0xD9 ||
                marker == 0x01 ||
                marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (offset + 1 >= bytes.Length)
            {
                return false;
            }

            int segmentLength =
                (bytes[offset] << 8) |
                bytes[offset + 1];

            if (segmentLength < 2 ||
                offset + segmentLength > bytes.Length)
            {
                return false;
            }

            if (IsStartOfFrame(marker) && segmentLength >= 7)
            {
                height =
                    (bytes[offset + 3] << 8) |
                    bytes[offset + 4];
                width =
                    (bytes[offset + 5] << 8) |
                    bytes[offset + 6];
                return width > 0 && height > 0;
            }

            // Start-of-scan occurs after the frame header. If no supported SOF
            // marker was found before this point, do not scan compressed bytes.
            if (marker == 0xDA)
            {
                return false;
            }

            offset += segmentLength;
        }

        return false;
    }

    private static bool IsStartOfFrame(byte marker)
    {
        return marker is
            0xC0 or 0xC1 or 0xC2 or 0xC3 or
            0xC5 or 0xC6 or 0xC7 or
            0xC9 or 0xCA or 0xCB or
            0xCD or 0xCE or 0xCF;
    }
}

public sealed record GatewayError(
    string ErrorCode,
    string ErrorMessage)
{
    public static GatewayError Validation(string message) =>
        new("validation_error", message);

    public static GatewayError NotFound(string message) =>
        new("not_found", message);

    public static GatewayError InvalidState(string message) =>
        new("invalid_state", message);
}

public sealed record GenerationResponse(
    string RequestId,
    string Status,
    bool ResultAvailable,
    string ErrorCode,
    string ErrorMessage,
    string ProviderRequestId,
    string ProviderMetadata);

public sealed record GenerationResultImage(
    byte[] Bytes,
    string ContentType);
