using System.IO;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using WebThuMuaPheLieu.Services;
using WebThuMuaPheLieu.helpper;
using WebThuMuaPheLieu.Models;

var builder = WebApplication.CreateBuilder(args);

var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtection-Keys");
Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

builder.Services.AddMemoryCache();
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("BlogList", policy =>
    {
        policy.Tag(SiteCacheKeys.BlogOutputTag);
        policy.Expire(TimeSpan.FromMinutes(10));
        policy.SetVaryByQuery(new[] { "searchTerm", "categoryName", "page" });
    });

    options.AddPolicy("BlogDetail", policy =>
    {
        policy.Tag(SiteCacheKeys.BlogOutputTag);
        policy.Expire(TimeSpan.FromMinutes(30));
    });

    options.AddPolicy("BlogPagedApi", policy =>
    {
        policy.Tag(SiteCacheKeys.BlogOutputTag);
        policy.Expire(TimeSpan.FromMinutes(5));
        policy.SetVaryByQuery(new[] { "cursor", "limit", "searchTerm", "categoryName" });
    });

    options.AddPolicy("ProductCursorApi", policy =>
    {
        policy.Tag(SiteCacheKeys.ProductOutputTag);
        policy.Expire(TimeSpan.FromMinutes(5));
        policy.SetVaryByQuery(new[] { "cursor", "category", "limit" });
    });

    options.AddPolicy("ProductList", policy =>
    {
        policy.Tag(SiteCacheKeys.ProductOutputTag);
        policy.Expire(TimeSpan.FromMinutes(10));
        policy.SetVaryByQuery(new[] { "category", "page" });
    });

    options.AddPolicy("ProductDetail", policy =>
    {
        policy.Tag(SiteCacheKeys.ProductOutputTag);
        policy.Expire(TimeSpan.FromMinutes(30));
    });

    options.AddPolicy("Pricing", policy =>
    {
        policy.Tag(SiteCacheKeys.ProductOutputTag);
        policy.Expire(TimeSpan.FromMinutes(10));
    });

    options.AddPolicy("About", policy =>
    {
        policy.Tag(SiteCacheKeys.HomeOutputTag);
        policy.Expire(TimeSpan.FromMinutes(15));
    });

    options.AddPolicy("Contact", policy =>
    {
        policy.Tag(SiteCacheKeys.HomeOutputTag);
        policy.Expire(TimeSpan.FromMinutes(15));
    });
 
    options.AddPolicy("Home", policy =>
    {
        policy.Tag(SiteCacheKeys.HomeOutputTag);
        policy.Expire(TimeSpan.FromMinutes(15));
    });

    options.AddPolicy("HomeLazySection", policy =>
    {
        policy.Tag(SiteCacheKeys.HomeOutputTag);
        policy.Expire(TimeSpan.FromMinutes(15));
        policy.SetVaryByRouteValue(new[] { "sectionKey" });
    });
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

// Add services to the container.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin/login";
        options.Cookie.Name = "WebThuMuaPheLieu.AdminAuth";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddControllersWithViews();

var configuredConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Missing database connection string 'Default' or 'DefaultConnection'.");

var connectionStringBuilder = new SqlConnectionStringBuilder(configuredConnectionString);
if (connectionStringBuilder.MinPoolSize < 1)
{
    connectionStringBuilder.MinPoolSize = 1;
}

var defaultConnectionString = connectionStringBuilder.ConnectionString;

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(defaultConnectionString));

var databaseWarmupEnabled = builder.Configuration.GetValue("DatabaseWarmup:Enabled", !builder.Environment.IsDevelopment());
if (databaseWarmupEnabled)
{
    builder.Services.AddHostedService<DatabaseConnectionWarmupService>();
}

builder.Services.AddHttpClient();

var publicPageWarmupEnabled = builder.Configuration.GetValue("PublicPageWarmup:Enabled", !builder.Environment.IsDevelopment());
if (publicPageWarmupEnabled)
{
    builder.Services.AddHostedService<PublicPageWarmupService>();
}

builder.Services.AddScoped<IContactInfoHelper, ContactInfoHelper>();
builder.Services.AddScoped<IBannerInjectHelper, BannerInjectHelper>();
builder.Services.AddScoped<ISeoSettingHelper, SeoSettingHelper>();
builder.Services.AddScoped<ILayoutDataHelper, LayoutDataHelper>();
builder.Services.AddScoped<IStructuredDataService, StructuredDataService>();
builder.Services.AddSingleton<IBlogImageProcessor, BlogImageProcessor>();
builder.Services.AddSingleton<IProductImageProcessor, ProductImageProcessor>();
builder.Services.AddScoped<IBlogImageMigrationService, BlogImageMigrationService>();
builder.Services.AddScoped<IProductImageMigrationService, ProductImageMigrationService>();
builder.Services.AddHostedService<BlogImageMigrationHostedService>();
builder.Services.AddHostedService<ProductImageMigrationHostedService>();

var app = builder.Build();

if (args.Any(arg => string.Equals(arg, "--migrate-blog-images", StringComparison.OrdinalIgnoreCase)))
{
    using var scope = app.Services.CreateScope();
    var migrator = scope.ServiceProvider.GetRequiredService<IBlogImageMigrationService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("BlogImageMigrationCli");
    var result = await migrator.RunAsync();
    logger.LogInformation("Manual blog image migration completed: scanned={Scanned}, processed={Processed}, skipped={Skipped}", result.Scanned, result.Processed, result.Skipped);
    return;
}

if (args.Any(arg => string.Equals(arg, "--migrate-product-images", StringComparison.OrdinalIgnoreCase)))
{
    using var scope = app.Services.CreateScope();
    var migrator = scope.ServiceProvider.GetRequiredService<IProductImageMigrationService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ProductImageMigrationCli");
    var result = await migrator.RunAsync();
    logger.LogInformation(
        "Manual product/static image migration completed: scanned={Scanned}, processed={Processed}, skipped={Skipped}, failed={Failed}",
        result.Scanned,
        result.Processed,
        result.Skipped,
        result.Failed);
    return;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseResponseCompression();
app.UseRouting();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

app.MapControllerRoute(
    name: "sitemap",
    pattern: "sitemap.xml",
    defaults: new { controller = "Sitemap", action = "Index" });

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var ext = Path.GetExtension(ctx.File.Name);
        var headers = ctx.Context.Response.Headers;

        if (string.Equals(ext, ".css", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".js", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".avif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".svg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".woff", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".woff2", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".ttf", StringComparison.OrdinalIgnoreCase))
        {
            headers.CacheControl = "public,max-age=2592000";
        }
    }
});

app.MapControllerRoute(
    name: "blog-mark-useful",
    pattern: "Blog/MarkUseful",
    defaults: new { controller = "Blog", action = "MarkUseful" });

app.MapControllerRoute(
    name: "product-detail",
    pattern: "Home/Detail/{slug?}/{id:int?}",
    defaults: new { controller = "Home", action = "Detail" });

app.MapControllerRoute(
    name: "product-detail-seo",
    pattern: "san-pham/{slug}",
    defaults: new { controller = "Home", action = "Detail" });

app.MapControllerRoute(
    name: "blog-paged-api",
    pattern: "Blog/GetPagedBlogs",
    defaults: new { controller = "Blog", action = "GetPagedBlogs" });

app.MapControllerRoute(
    name: "blog-detail-legacy",
    pattern: "Blog/Detail",
    defaults: new { controller = "Blog", action = "LegacyDetail" });

app.MapControllerRoute(
    name: "blog-detail-seo",
    pattern: "blog/{slug}",
    defaults: new { controller = "Blog", action = "Detail" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

internal sealed class DatabaseConnectionWarmupService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseConnectionWarmupService> _logger;

    public DatabaseConnectionWarmupService(IServiceProvider serviceProvider, ILogger<DatabaseConnectionWarmupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var entityCount = context.Model.GetEntityTypes().Count();
                await context.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

                var settings = scope.ServiceProvider.GetRequiredService<ISeoSettingHelper>();
                await settings.GetSeoSettingsAsync();

                var contactInfo = scope.ServiceProvider.GetRequiredService<IContactInfoHelper>();
                await contactInfo.GetContactInfoAsync();

                var bannerInject = scope.ServiceProvider.GetRequiredService<IBannerInjectHelper>();
                await bannerInject.GetActiveBannersAsync();

                var layoutData = scope.ServiceProvider.GetRequiredService<ILayoutDataHelper>();
                await layoutData.GetServiceMenuItemsAsync();
                await layoutData.GetFooterFeaturedProductsAsync();
                await layoutData.GetFooterTopServicePostsAsync();

                await context.Products
                    .AsNoTracking()
                    .Where(p => p.Status == "active")
                    .OrderByDescending(p => p.IsFeatured)
                    .ThenByDescending(p => p.CreatedAt)
                    .Take(16)
                    .Select(p => new { p.Id })
                    .ToListAsync(cancellationToken);

                _logger.LogDebug("Database warm-up completed for {EntityCount} EF entities.", entityCount);
            }
            catch (OperationCanceledException)
            {
                // Application is shutting down before warm-up finishes.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Database warm-up failed; first request will open the connection normally.");
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class PublicPageWarmupService : IHostedService
{
    private static readonly string[] WarmupPaths =
    {
        "/",
        "/Home/About",
        "/Contact",
        "/Product",
        "/Pricing",
        "/Blog",
        "/Home/LazySection/recent-news",
        "/Home/LazySection/scrap-services"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IServer _server;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PublicPageWarmupService> _logger;

    public PublicPageWarmupService(
        IHttpClientFactory httpClientFactory,
        IHostApplicationLifetime applicationLifetime,
        IServer server,
        IConfiguration configuration,
        ILogger<PublicPageWarmupService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _applicationLifetime = applicationLifetime;
        _server = server;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var enabled = _configuration.GetValue("PublicPageWarmup:Enabled", true);
        if (!enabled)
        {
            return Task.CompletedTask;
        }

        _applicationLifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(() => WarmupAsync(_applicationLifetime.ApplicationStopping), CancellationToken.None);
        });

        return Task.CompletedTask;
    }

    private async Task WarmupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            var baseUrl = ResolveBaseUrl();
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                _logger.LogDebug("Public page warm-up skipped because no base URL is available.");
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var client = _httpClientFactory.CreateClient();

            foreach (var path in WarmupPaths)
            {
                var url = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "PublicPageWarmup/1.0");

                try
                {
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, linkedToken.Token);
                    _ = await response.Content.ReadAsByteArrayAsync(linkedToken.Token);
                    _logger.LogDebug("Warm-up {Path} completed with status {StatusCode}.", path, (int)response.StatusCode);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Warm-up {Path} timed out.", path);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Warm-up {Path} failed.", path);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Application is stopping before warm-up finishes.
        }
    }

    private string? ResolveBaseUrl()
    {
        var configuredBaseUrl = _configuration["PublicPageWarmup:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            return configuredBaseUrl;
        }

        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses;
        return addresses?
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Replace("0.0.0.0", "localhost", StringComparison.OrdinalIgnoreCase)
                .Replace("[::]", "localhost", StringComparison.OrdinalIgnoreCase)
                .Replace("*", "localhost", StringComparison.OrdinalIgnoreCase)
                .TrimEnd('/'))
            .FirstOrDefault();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
