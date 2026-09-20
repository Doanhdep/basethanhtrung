using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace WebThuMuaPheLieu.Services;

public sealed class ProductImageProcessor : IProductImageProcessor
{
    private static readonly WebpEncoder OriginalEncoder = new()
    {
        Quality = 82,
        Method = WebpEncodingMethod.BestQuality,
        FileFormat = WebpFileFormatType.Lossy
    };

    private static readonly WebpEncoder MediumEncoder = new()
    {
        Quality = 78,
        Method = WebpEncodingMethod.Default,
        FileFormat = WebpFileFormatType.Lossy
    };

    private static readonly WebpEncoder ThumbnailEncoder = new()
    {
        Quality = 72,
        Method = WebpEncodingMethod.Default,
        FileFormat = WebpFileFormatType.Lossy
    };

    public async Task<ProcessedProductImageResult> ProcessAndSaveAsync(
        IFormFile imageFile,
        string outputDirectory,
        string outputFileNameWithoutExtension,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        await using var readStream = imageFile.OpenReadStream();
        using var image = await Image.LoadAsync(readStream, cancellationToken);

        var originalFileName = $"{outputFileNameWithoutExtension}.webp";
        var mediumFileName = $"{outputFileNameWithoutExtension}-md.webp";
        var thumbFileName = $"{outputFileNameWithoutExtension}-thumb.webp";

        var originalPhysicalPath = Path.Combine(outputDirectory, originalFileName);
        var mediumPhysicalPath = Path.Combine(outputDirectory, mediumFileName);
        var thumbPhysicalPath = Path.Combine(outputDirectory, thumbFileName);

        await SaveProductVariantsAsync(image, originalPhysicalPath, mediumPhysicalPath, thumbPhysicalPath, cancellationToken);

        var normalizedDirectory = outputDirectory.Replace('\\', '/');
        var marker = "/wwwroot/";
        var markerIndex = normalizedDirectory.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var relativeFolder = markerIndex >= 0
            ? normalizedDirectory[(markerIndex + marker.Length)..]
            : normalizedDirectory;

        var originalBytes = new FileInfo(originalPhysicalPath).Length;
        var mediumBytes = new FileInfo(mediumPhysicalPath).Length;
        var thumbBytes = new FileInfo(thumbPhysicalPath).Length;

        return new ProcessedProductImageResult
        {
            PhysicalPath = originalPhysicalPath,
            MediumPath = mediumPhysicalPath,
            ThumbnailPath = thumbPhysicalPath,
            RelativeUrl = $"/{relativeFolder.Trim('/')}/{originalFileName}".Replace('\\', '/'),
            MediumRelativeUrl = $"/{relativeFolder.Trim('/')}/{mediumFileName}".Replace('\\', '/'),
            ThumbnailRelativeUrl = $"/{relativeFolder.Trim('/')}/{thumbFileName}".Replace('\\', '/'),
            OriginalBytes = imageFile.Length,
            OptimizedBytes = originalBytes,
            MediumBytes = mediumBytes,
            ThumbnailBytes = thumbBytes,
            Width = image.Width,
            Height = image.Height
        };
    }

    public async Task<ProcessedProductImageResult?> ProcessExistingImageAsync(
        string sourceImagePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
        {
            return null;
        }

        var extension = Path.GetExtension(sourceImagePath);
        if (!ImagePathHelper.SupportedRasterImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (ImagePathHelper.IsGeneratedVariantFile(sourceImagePath))
        {
            return null;
        }

        var originalPhysicalPath = ImagePathHelper.BuildVariantPhysicalPath(sourceImagePath, SiteImageVariant.Original);
        var mediumPhysicalPath = ImagePathHelper.BuildVariantPhysicalPath(sourceImagePath, SiteImageVariant.Medium);
        var thumbPhysicalPath = ImagePathHelper.BuildVariantPhysicalPath(sourceImagePath, SiteImageVariant.Thumbnail);

        if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
        {
            originalPhysicalPath = sourceImagePath;
        }

        var needsOriginal = !File.Exists(originalPhysicalPath);
        var needsMedium = !File.Exists(mediumPhysicalPath);
        var needsThumb = !File.Exists(thumbPhysicalPath);

        if (!needsOriginal && !needsMedium && !needsThumb)
        {
            return new ProcessedProductImageResult
            {
                PhysicalPath = originalPhysicalPath,
                MediumPath = mediumPhysicalPath,
                ThumbnailPath = thumbPhysicalPath,
                RelativeUrl = BuildRelativeUrl(originalPhysicalPath),
                MediumRelativeUrl = BuildRelativeUrl(mediumPhysicalPath),
                ThumbnailRelativeUrl = BuildRelativeUrl(thumbPhysicalPath),
                OriginalBytes = new FileInfo(sourceImagePath).Length,
                OptimizedBytes = new FileInfo(originalPhysicalPath).Length,
                MediumBytes = new FileInfo(mediumPhysicalPath).Length,
                ThumbnailBytes = new FileInfo(thumbPhysicalPath).Length
            };
        }

        using var image = await Image.LoadAsync(sourceImagePath, cancellationToken);
        await SaveProductVariantsAsync(
            image,
            needsOriginal ? originalPhysicalPath : null,
            needsMedium ? mediumPhysicalPath : null,
            needsThumb ? thumbPhysicalPath : null,
            cancellationToken);

        return new ProcessedProductImageResult
        {
            PhysicalPath = originalPhysicalPath,
            MediumPath = mediumPhysicalPath,
            ThumbnailPath = thumbPhysicalPath,
            RelativeUrl = BuildRelativeUrl(originalPhysicalPath),
            MediumRelativeUrl = BuildRelativeUrl(mediumPhysicalPath),
            ThumbnailRelativeUrl = BuildRelativeUrl(thumbPhysicalPath),
            OriginalBytes = new FileInfo(sourceImagePath).Length,
            OptimizedBytes = File.Exists(originalPhysicalPath) ? new FileInfo(originalPhysicalPath).Length : 0,
            MediumBytes = File.Exists(mediumPhysicalPath) ? new FileInfo(mediumPhysicalPath).Length : 0,
            ThumbnailBytes = File.Exists(thumbPhysicalPath) ? new FileInfo(thumbPhysicalPath).Length : 0,
            Width = image.Width,
            Height = image.Height
        };
    }

    private static async Task SaveProductVariantsAsync(
        Image image,
        string? originalPhysicalPath,
        string? mediumPhysicalPath,
        string? thumbPhysicalPath,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(originalPhysicalPath))
        {
            using var original = image.Clone(ctx =>
                ResizeIfLarger(ctx, image.Width, image.Height, new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(1600, 1600),
                    Sampler = KnownResamplers.Lanczos3
                }));

            await original.SaveAsWebpAsync(originalPhysicalPath, OriginalEncoder, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(mediumPhysicalPath))
        {
            using var medium = image.Clone(ctx =>
                ResizeIfLarger(ctx, image.Width, image.Height, new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(960, 960),
                    Sampler = KnownResamplers.Lanczos3
                }));

            await medium.SaveAsWebpAsync(mediumPhysicalPath, MediumEncoder, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(thumbPhysicalPath))
        {
            using var thumb = image.Clone(ctx =>
                ResizeIfLarger(ctx, image.Width, image.Height, new ResizeOptions
                {
                    Mode = ResizeMode.Crop,
                    Size = new Size(640, 400),
                    Position = AnchorPositionMode.Center,
                    Sampler = KnownResamplers.Lanczos3
                }));

            await thumb.SaveAsWebpAsync(thumbPhysicalPath, ThumbnailEncoder, cancellationToken);
        }
    }

    private static IImageProcessingContext ResizeIfLarger(
        IImageProcessingContext context,
        int sourceWidth,
        int sourceHeight,
        ResizeOptions options)
    {
        var targetWidth = options.Size.Width;
        var targetHeight = options.Size.Height;

        if (sourceWidth <= targetWidth && sourceHeight <= targetHeight && options.Mode != ResizeMode.Crop)
        {
            return context;
        }

        return context.Resize(options);
    }

    private static string BuildRelativeUrl(string physicalPath)
    {
        var normalizedPath = physicalPath.Replace('\\', '/');
        var marker = "/wwwroot/";
        var markerIndex = normalizedPath.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return markerIndex >= 0
            ? $"/{normalizedPath[(markerIndex + marker.Length)..].TrimStart('/')}"
            : normalizedPath;
    }
}
