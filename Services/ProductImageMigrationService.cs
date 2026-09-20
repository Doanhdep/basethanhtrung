namespace WebThuMuaPheLieu.Services;

public interface IProductImageMigrationService
{
    Task<ProductImageMigrationResult> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class ProductImageMigrationService : IProductImageMigrationService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IProductImageProcessor _productImageProcessor;
    private readonly ILogger<ProductImageMigrationService> _logger;

    public ProductImageMigrationService(
        IWebHostEnvironment environment,
        IProductImageProcessor productImageProcessor,
        ILogger<ProductImageMigrationService> logger)
    {
        _environment = environment;
        _productImageProcessor = productImageProcessor;
        _logger = logger;
    }

    public async Task<ProductImageMigrationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var roots = BuildImageRoots();

        var files = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            .Where(path => ImagePathHelper.SupportedRasterImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !ImagePathHelper.IsGeneratedVariantFile(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var processed = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await _productImageProcessor.ProcessExistingImageAsync(file, cancellationToken);
                if (result is null)
                {
                    skipped++;
                }
                else
                {
                    processed++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Product/static image migration failed for file {FilePath}", file);
            }
        }

        return new ProductImageMigrationResult(
            files.Count,
            processed,
            skipped,
            failed,
            startedAt,
            DateTimeOffset.UtcNow);
    }

    private IReadOnlyList<string> BuildImageRoots()
    {
        var webRootPath = _environment.WebRootPath;

        return
        [
            Path.Combine(webRootPath, "assets", "images", "products"),
            Path.Combine(webRootPath, "assets", "images", "bannersandseos"),
            Path.Combine(webRootPath, "assets", "img")
        ];
    }
}

public sealed record ProductImageMigrationResult(
    int Scanned,
    int Processed,
    int Skipped,
    int Failed,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);
