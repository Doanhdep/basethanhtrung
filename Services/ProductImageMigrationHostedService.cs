namespace WebThuMuaPheLieu.Services;

public sealed class ProductImageMigrationHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProductImageMigrationHostedService> _logger;
    private readonly IConfiguration _configuration;

    public ProductImageMigrationHostedService(
        IServiceProvider serviceProvider,
        ILogger<ProductImageMigrationHostedService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue<bool>("ProductImageMigration:RunOnStartup");
        if (!enabled)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);

        using var scope = _serviceProvider.CreateScope();
        var migrationService = scope.ServiceProvider.GetRequiredService<IProductImageMigrationService>();

        try
        {
            var result = await migrationService.RunAsync(stoppingToken);
            _logger.LogInformation(
                "Product/static image migration completed. Scanned={Scanned}, Processed={Processed}, Skipped={Skipped}, Failed={Failed}, DurationMs={DurationMs}",
                result.Scanned,
                result.Processed,
                result.Skipped,
                result.Failed,
                (result.FinishedAt - result.StartedAt).TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Product/static image migration canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Product/static image migration failed.");
        }
    }
}
