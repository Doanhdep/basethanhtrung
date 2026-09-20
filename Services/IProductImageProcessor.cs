using Microsoft.AspNetCore.Http;

namespace WebThuMuaPheLieu.Services;

public interface IProductImageProcessor
{
    Task<ProcessedProductImageResult> ProcessAndSaveAsync(
        IFormFile imageFile,
        string outputDirectory,
        string outputFileNameWithoutExtension,
        CancellationToken cancellationToken = default);

    Task<ProcessedProductImageResult?> ProcessExistingImageAsync(
        string sourceImagePath,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessedProductImageResult
{
    public string RelativeUrl { get; init; } = string.Empty;

    public string MediumRelativeUrl { get; init; } = string.Empty;

    public string ThumbnailRelativeUrl { get; init; } = string.Empty;

    public string PhysicalPath { get; init; } = string.Empty;

    public string MediumPath { get; init; } = string.Empty;

    public string ThumbnailPath { get; init; } = string.Empty;

    public long OriginalBytes { get; init; }

    public long OptimizedBytes { get; init; }

    public long MediumBytes { get; init; }

    public long ThumbnailBytes { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}

