using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebThuMuaPheLieu.Models;
using WebThuMuaPheLieu.ViewModels;

namespace WebThuMuaPheLieu.Services;

public class StructuredDataService : IStructuredDataService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly TimeSpan SiteContextCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    private readonly AppDbContext _context;
    private readonly IMemoryCache _memoryCache;
    private readonly IConfiguration _configuration;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public StructuredDataService(AppDbContext context, IMemoryCache memoryCache, IConfiguration configuration)
    {
        _context = context;
        _memoryCache = memoryCache;
        _configuration = configuration;
    }

    public string SerializeSchemas(IEnumerable<object> schemaNodes)
    {
        var nodes = schemaNodes?.Where(item => item is not null).ToList() ?? [];
        if (nodes.Count == 0)
        {
            return string.Empty;
        }

        var payload = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = nodes
        };

        return JsonSerializer.Serialize(payload, _jsonOptions);
    }

    public IEnumerable<object> BuildHomeSchemas(HttpContext httpContext)
    {
        var site = BuildSiteContext(httpContext);
        var org = BuildOrganizationNode(site);

        var webSite = Clean(new Dictionary<string, object?>
        {
            ["@type"] = "WebSite",
            ["@id"] = $"{site.BaseUrl}#website",
            ["url"] = site.BaseUrl,
            ["name"] = site.Name,
            ["description"] = site.Description,
            ["publisher"] = new Dictionary<string, object?> { ["@id"] = $"{site.BaseUrl}#organization" }
        });

        return [webSite, org];
    }

    public IEnumerable<object> BuildBlogListSchemas(HttpContext httpContext)
    {
        var site = BuildSiteContext(httpContext);
        var canonicalUrl = BuildAbsoluteUrl(httpContext, "/Blog");

        var collection = Clean(new Dictionary<string, object?>
        {
            ["@type"] = "CollectionPage",
            ["@id"] = $"{canonicalUrl}#collection",
            ["url"] = canonicalUrl,
            ["name"] = "Tin tức",
            ["description"] = "Danh sách bài viết tin tức phế liệu",
            ["isPartOf"] = new Dictionary<string, object?> { ["@id"] = $"{site.BaseUrl}#website" },
            ["breadcrumb"] = new Dictionary<string, object?> { ["@id"] = $"{canonicalUrl}#breadcrumb" }
        });

        var breadcrumb = BuildBreadcrumb(site.BaseUrl,
        [
            (site.BaseUrl, "Trang chủ"),
            ($"{site.BaseUrl}/Blog", "Blog")
        ], canonicalUrl);

        return [collection, breadcrumb];
    }

    public IEnumerable<object> BuildBlogDetailSchemas(HttpContext httpContext, WebThuMuaPheLieu.Models.BlogDetailViewModel model)
    {
        var site = BuildSiteContext(httpContext);
        var post = model.Post;
        var canonical = !string.IsNullOrWhiteSpace(model.CurrentUrl)
            ? model.CurrentUrl
            : BuildAbsoluteUrl(httpContext, httpContext.Request.Path + httpContext.Request.QueryString);

        var publishedAt = post.PublishedAt ?? post.CreatedAt;
        var modifiedAt = post.UpdatedAt ?? publishedAt;
        var imageUrls = (model.GalleryImages ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => ToAbsoluteUrl(site.BaseUrl, item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (imageUrls.Count == 0 && !string.IsNullOrWhiteSpace(model.CoverImage))
        {
            imageUrls.Add(ToAbsoluteUrl(site.BaseUrl, model.CoverImage));
        }

        var authorName = string.IsNullOrWhiteSpace(model.AuthorName) ? site.Name : model.AuthorName;
        var description = !string.IsNullOrWhiteSpace(post.Excerpt) ? post.Excerpt : StripHtml(post.Content);

        var article = Clean(new Dictionary<string, object?>
        {
            ["@type"] = "BlogPosting",
            ["@id"] = $"{canonical}#blogposting",
            ["headline"] = post.Title,
            ["description"] = Truncate(description, 320),
            ["image"] = imageUrls.Count > 0 ? imageUrls : null,
            ["author"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["@id"] = $"{site.BaseUrl}#organization",
                ["name"] = authorName,
                ["url"] = site.BaseUrl
            },
            ["publisher"] = new Dictionary<string, object?> { ["@id"] = $"{site.BaseUrl}#organization" },
            ["datePublished"] = ToVietnamIso8601(publishedAt),
            ["dateModified"] = ToVietnamIso8601(modifiedAt),
            ["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = canonical
            },
            ["articleSection"] = string.IsNullOrWhiteSpace(model.PrimaryCategoryName) ? null : model.PrimaryCategoryName,
            ["url"] = canonical
        });

        var breadcrumb = BuildBreadcrumb(site.BaseUrl,
        [
            (site.BaseUrl, "Trang chủ"),
            ($"{site.BaseUrl}/Blog", "Blog"),
            (canonical, post.Title ?? "Chi tiết bài viết")
        ], canonical);

        return [article, breadcrumb];
    }

    public IEnumerable<object> BuildProductListSchemas(HttpContext httpContext)
    {
        var site = BuildSiteContext(httpContext);
        var canonicalUrl = BuildAbsoluteUrl(httpContext, "/Product");

        var collection = Clean(new Dictionary<string, object?>
        {
            ["@type"] = "CollectionPage",
            ["@id"] = $"{canonicalUrl}#collection",
            ["url"] = canonicalUrl,
            ["name"] = "Mặt hàng",
            ["description"] = "Danh sách sản phẩm phế liệu thu mua",
            ["isPartOf"] = new Dictionary<string, object?> { ["@id"] = $"{site.BaseUrl}#website" },
            ["breadcrumb"] = new Dictionary<string, object?> { ["@id"] = $"{canonicalUrl}#breadcrumb" }
        });

        var breadcrumb = BuildBreadcrumb(site.BaseUrl,
        [
            (site.BaseUrl, "Trang chủ"),
            ($"{site.BaseUrl}/Product", "Mặt hàng")
        ], canonicalUrl);

        return [collection, breadcrumb];
    }

    public IEnumerable<object> BuildProductDetailSchemas(HttpContext httpContext, ProductDetailViewModel model)
    {
        var site = BuildSiteContext(httpContext);
        var productSlug = !string.IsNullOrWhiteSpace(model.Product.Slug)
            ? model.Product.Slug
            : model.Product.Id.ToString(CultureInfo.InvariantCulture);
        var canonical = BuildAbsoluteUrl(httpContext, $"/san-pham/{productSlug}");
        var product = model.Product;

        var imageUrls = (model.Images ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.ImageUrl))
            .OrderBy(item => item.OrderIndex)
            .Select(item => ToAbsoluteUrl(site.BaseUrl, item.ImageUrl!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (imageUrls.Count == 0 && !string.IsNullOrWhiteSpace(product.PrimaryImage))
        {
            imageUrls.Add(ToAbsoluteUrl(site.BaseUrl, product.PrimaryImage));
        }

        var offers = product.PriceValue.HasValue
            ? Clean(new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["priceCurrency"] = "VND",
                ["price"] = product.PriceValue.Value.ToString("0.##", CultureInfo.InvariantCulture),
                ["availability"] = "https://schema.org/InStock",
                ["url"] = canonical
            })
            : null;

        var productNode = Clean(new Dictionary<string, object?>
        {
            ["@type"] = "Product",
            ["@id"] = $"{canonical}#product",
            ["name"] = product.Name,
            ["image"] = imageUrls.Count > 0 ? imageUrls : null,
            ["description"] = !string.IsNullOrWhiteSpace(product.ShortDescription) ? product.ShortDescription : product.Description,
            ["sku"] = product.Id > 0 ? product.Id.ToString() : null,
            ["brand"] = new Dictionary<string, object?> { ["@type"] = "Brand", ["name"] = site.Name },
            ["offers"] = offers,
            ["isPartOf"] = new Dictionary<string, object?> { ["@id"] = $"{site.BaseUrl}#website" },
            ["breadcrumb"] = new Dictionary<string, object?> { ["@id"] = $"{canonical}#breadcrumb" }
        });

        var breadcrumb = BuildBreadcrumb(site.BaseUrl,
        [
            (site.BaseUrl, "Trang chủ"),
            ($"{site.BaseUrl}/Product", "Mặt hàng"),
            (canonical, product.Name ?? "Chi tiết sản phẩm")
        ], canonical);

        return [productNode, breadcrumb];
    }

    public IEnumerable<object> BuildAboutSchemas(HttpContext httpContext)
    {
        var site = BuildSiteContext(httpContext);
        var aboutPage = Clean(new Dictionary<string, object?>
        {
            ["@type"] = "AboutPage",
            ["@id"] = $"{BuildAbsoluteUrl(httpContext, httpContext.Request.Path)}#about",
            ["url"] = BuildAbsoluteUrl(httpContext, httpContext.Request.Path),
            ["name"] = "Giới thiệu",
            ["isPartOf"] = new Dictionary<string, object?> { ["@id"] = $"{site.BaseUrl}#website" }
        });

        return [aboutPage, BuildOrganizationNode(site)];
    }

    public IEnumerable<object> BuildContactSchemas(HttpContext httpContext, ContactPageViewModel model)
    {
        var site = BuildSiteContext(httpContext);
        var pageUrl = BuildAbsoluteUrl(httpContext, httpContext.Request.Path);

        var contactPage = Clean(new Dictionary<string, object?>
        {
            ["@type"] = "ContactPage",
            ["@id"] = $"{pageUrl}#contact",
            ["url"] = pageUrl,
            ["name"] = "Liên hệ",
            ["isPartOf"] = new Dictionary<string, object?> { ["@id"] = $"{site.BaseUrl}#website" }
        });

        var org = BuildOrganizationNode(site, model.Phone, model.Email, model.Address);
        return [contactPage, org];
    }

    private SiteContext BuildSiteContext(HttpContext httpContext)
    {
        var configuredBaseUrl = _configuration["Seo:CanonicalBaseUrl"]
            ?? _configuration["CanonicalBaseUrl"];
        var baseUrl = !string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? configuredBaseUrl.Trim().TrimEnd('/')
            : $"{httpContext.Request.Scheme}://{httpContext.Request.Host}".TrimEnd('/');
        var settings = _memoryCache.GetOrCreate(SiteCacheKeys.StructuredDataSettings, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SiteContextCacheDuration;

            return _context.SiteSettings
                .AsNoTracking()
                .Where(item => item.SettingKey != null)
                .Select(item => new SiteSettingSnapshot
                {
                    SettingKey = item.SettingKey,
                    SettingValue = item.SettingValue
                })
                .ToList();
        }) ?? [];

        string? GetByKey(string key) => settings
            .FirstOrDefault(item => string.Equals(item.SettingKey, key, StringComparison.OrdinalIgnoreCase))
            ?.SettingValue?
            .Trim();

        var name = GetByKey("seo.meta_title");
        var description = GetByKey("seo.meta_description");
        var logo = GetByKey("seo.og_image");

        var social = new[] { GetByKey("contact.facebook"), GetByKey("contact.zalo"), GetByKey("contact.messenger") }
            .Select(item => NormalizeSameAsUrl(item))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SiteContext
        {
            BaseUrl = baseUrl,
            Name = string.IsNullOrWhiteSpace(name) ? "Phế Liệu Thành Trung" : name,
            Description = description,
            LogoUrl = string.IsNullOrWhiteSpace(logo) ? null : ToAbsoluteUrl(baseUrl, logo),
            SocialLinks = social
        };
    }

    private static Dictionary<string, object?> BuildOrganizationNode(SiteContext site, string? phone = null, string? email = null, string? address = null)
    {
        return Clean(new Dictionary<string, object?>
        {
            ["@type"] = "Organization",
            ["@id"] = $"{site.BaseUrl}#organization",
            ["name"] = site.Name,
            ["url"] = site.BaseUrl,
            ["logo"] = site.LogoUrl,
            ["description"] = site.Description,
            ["sameAs"] = site.SocialLinks.Count > 0 ? site.SocialLinks : null,
            ["telephone"] = string.IsNullOrWhiteSpace(phone) ? null : phone,
            ["email"] = string.IsNullOrWhiteSpace(email) ? null : email,
            ["address"] = string.IsNullOrWhiteSpace(address)
                ? null
                : new Dictionary<string, object?> { ["@type"] = "PostalAddress", ["streetAddress"] = address }
        });
    }

    private static Dictionary<string, object?> BuildBreadcrumb(string baseUrl, IReadOnlyList<(string Url, string Name)> items, string? pageUrl = null)
    {
        var breadcrumbItems = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Url) && !string.IsNullOrWhiteSpace(item.Name))
            .Select((item, index) => Clean(new Dictionary<string, object?>
            {
                ["@type"] = "ListItem",
                ["position"] = index + 1,
                ["name"] = item.Name,
                ["item"] = item.Url
            }))
            .ToList();

        return Clean(new Dictionary<string, object?>
        {
            ["@type"] = "BreadcrumbList",
            ["@id"] = !string.IsNullOrWhiteSpace(pageUrl) ? $"{pageUrl}#breadcrumb" : $"{baseUrl}#breadcrumb",
            ["itemListElement"] = breadcrumbItems.Count > 0 ? breadcrumbItems : null
        });
    }

    private static string? NormalizeSameAsUrl(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var normalized = rawValue.Trim();
        if (normalized == "#")
        {
            return null;
        }

        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            normalized = $"https:{normalized}";
        }

        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"https://{normalized.TrimStart('/')}";
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static Dictionary<string, object?> Clean(Dictionary<string, object?> input)
    {
        var result = new Dictionary<string, object?>();

        foreach (var pair in input)
        {
            if (pair.Value is null)
            {
                continue;
            }

            if (pair.Value is string str && string.IsNullOrWhiteSpace(str))
            {
                continue;
            }

            if (pair.Value is IEnumerable<string> strList && !strList.Any())
            {
                continue;
            }

            if (pair.Value is IEnumerable<object> objList && !objList.Any())
            {
                continue;
            }

            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static string BuildAbsoluteUrl(HttpContext httpContext, string path)
    {
        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{path}";
    }

    private static string ToAbsoluteUrl(string baseUrl, string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            normalized = "/" + normalized[2..];
        }

        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized.TrimStart('/');
        }

        return $"{baseUrl}{normalized}";
    }

    private static string StripHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", string.Empty).Trim();
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static string? ToVietnamIso8601(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var dt = value.Value;
        DateTimeOffset dto;

        if (dt.Kind == DateTimeKind.Utc)
        {
            dto = new DateTimeOffset(dt, TimeSpan.Zero).ToOffset(VietnamOffset);
        }
        else if (dt.Kind == DateTimeKind.Local)
        {
            dto = TimeZoneInfo.ConvertTime(new DateTimeOffset(dt), VietnamTimeZone);
        }
        else
        {
            dto = new DateTimeOffset(dt, VietnamOffset);
        }

        return dto.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch
        {
            return TimeZoneInfo.CreateCustomTimeZone("UTC+07", VietnamOffset, "UTC+07", "UTC+07");
        }
    }

    private sealed class SiteSettingSnapshot
    {
        public string? SettingKey { get; init; }

        public string? SettingValue { get; init; }
    }

    private sealed class SiteContext
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? LogoUrl { get; set; }
        public List<string> SocialLinks { get; set; } = [];
    }
}
