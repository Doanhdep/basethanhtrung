namespace WebThuMuaPheLieu.Services;

public enum SiteImageVariant
{
    Original,
    Medium,
    Thumbnail
}

public static class ImagePathHelper
{
    public const string DefaultProductImage = "/assets/images/no-image.png";
    public const string DefaultBlogImage = "/assets/img/blog/blog-1.jpg";
    public const string DefaultHeroImage = "/assets/img/satthep.jpg";
    public static readonly string[] SupportedRasterImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tiff", ".avif"];

    public static string NormalizeWebPath(string? imagePath, string fallback = DefaultProductImage)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return fallback;
        }

        var normalized = imagePath.Replace("\\", "/").Trim();

        if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            return $"/{normalized[2..]}";
        }

        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return normalized;
        }

        return $"/{normalized.TrimStart('/')}";
    }

    public static string ResolveVariantImage(
        string? imagePath,
        SiteImageVariant variant,
        string fallback = DefaultProductImage)
    {
        var normalized = NormalizeWebPath(imagePath, fallback);

        if (IsRemoteOrDataUri(normalized))
        {
            return normalized;
        }

        var extension = Path.GetExtension(normalized);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return normalized;
        }

        var extensionless = normalized[..^extension.Length];
        var candidates = new List<string>();

        if (variant == SiteImageVariant.Thumbnail)
        {
            candidates.Add($"{extensionless}-thumb.webp");
        }
        else if (variant == SiteImageVariant.Medium)
        {
            candidates.Add($"{extensionless}-md.webp");
        }

        if (!string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add($"{extensionless}.webp");
        }

        if (variant != SiteImageVariant.Original)
        {
            candidates.Add(normalized);
        }

        if (variant == SiteImageVariant.Original)
        {
            candidates.Add(string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : $"{extensionless}.webp");
            candidates.Add(normalized);
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (WebRootFileExists(candidate))
            {
                return candidate;
            }
        }

        return normalized;
    }

    public static IReadOnlyList<string> ResolveVariantSequence(
        IEnumerable<string?> imagePaths,
        SiteImageVariant variant,
        string fallback = DefaultProductImage)
    {
        var sequence = imagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolveVariantImage(path, variant, fallback))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sequence.Count == 0)
        {
            sequence.Add(ResolveVariantImage(fallback, variant, fallback));
        }

        return sequence;
    }

    public static string ToTildePath(string? imagePath, string fallback = DefaultProductImage)
    {
        var normalized = NormalizeWebPath(imagePath, fallback);

        if (IsRemoteOrDataUri(normalized))
        {
            return normalized;
        }

        return normalized.StartsWith("/", StringComparison.Ordinal)
            ? $"~{normalized}"
            : normalized;
    }

    public static bool WebRootFileExists(string? webPath)
    {
        if (string.IsNullOrWhiteSpace(webPath) || IsRemoteOrDataUri(webPath))
        {
            return false;
        }

        var normalized = NormalizeWebPath(webPath, string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var relativePath = normalized.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var physicalPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath);
        return File.Exists(physicalPath);
    }

    private static bool IsRemoteOrDataUri(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }
    public static string BuildVariantPhysicalPath(string sourceImagePath, SiteImageVariant variant)
    {
        var directory = Path.GetDirectoryName(sourceImagePath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourceImagePath);

        var suffix = variant switch
        {
            SiteImageVariant.Thumbnail => "-thumb.webp",
            SiteImageVariant.Medium => "-md.webp",
            _ => ".webp"
        };

        return Path.Combine(directory, $"{fileNameWithoutExtension}{suffix}");
    }

    public static bool IsGeneratedVariantFile(string filePath)
    {
        var fileNameNoExtension = Path.GetFileNameWithoutExtension(filePath);
        return fileNameNoExtension.EndsWith("-thumb", StringComparison.OrdinalIgnoreCase)
            || fileNameNoExtension.EndsWith("-md", StringComparison.OrdinalIgnoreCase);
    }
}
