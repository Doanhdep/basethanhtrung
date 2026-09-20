using System.Globalization;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebThuMuaPheLieu.Models;

namespace WebThuMuaPheLieu.Controllers;

[Route("sitemap.xml")]
public class SitemapController : Controller
{
    private const string CanonicalBaseUrl = "https://phelieuthanhtrung.vn";
    private static readonly DateTime StaticPageLastModifiedUtc = new(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public SitemapController(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpGet]
    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Index()
    {
        var entries = new List<SitemapEntry>
        {
            BuildStaticEntry("/", 1.0m),
            BuildStaticEntry("/Home/About", 0.8m),
            BuildStaticEntry("/Product", 0.8m),
            BuildStaticEntry("/Pricing", 0.8m),
            BuildStaticEntry("/Blog", 0.8m),
            BuildStaticEntry("/Contact", 0.8m)
        };

        var products = await _context.Products
            .AsNoTracking()
            .Where(product => product.Status == "active" && product.Slug != null && product.Slug != string.Empty)
            .Select(product => new
            {
                product.Slug,
                product.UpdatedAt,
                product.CreatedAt
            })
            .ToListAsync();

        entries.AddRange(products.Select(product => new SitemapEntry(
            $"/san-pham/{product.Slug!.Trim()}",
            product.UpdatedAt ?? product.CreatedAt,
            0.8m)));

        var blogPosts = await _context.BlogPosts
            .AsNoTracking()
            .Where(post => post.Status == "published" && post.Slug != null && post.Slug != string.Empty)
            .Select(post => new
            {
                post.Slug,
                post.UpdatedAt,
                post.PublishedAt,
                post.CreatedAt
            })
            .ToListAsync();

        entries.AddRange(blogPosts.Select(post => new SitemapEntry(
            $"/blog/{post.Slug!.Trim()}",
            post.UpdatedAt ?? post.PublishedAt ?? post.CreatedAt,
            0.8m)));

        var baseUrl = ResolveCanonicalBaseUrl(_configuration);
        var xml = BuildSitemapXml(entries, baseUrl);
        return Content(xml, "application/xml", Encoding.UTF8);
    }

    private static SitemapEntry BuildStaticEntry(string path, decimal priority)
    {
        return new SitemapEntry(path, StaticPageLastModifiedUtc, priority);
    }

    private static string BuildSitemapXml(IEnumerable<SitemapEntry> entries, string baseUrl)
    {
        var settings = new XmlWriterSettings
        {
            Async = false,
            Encoding = Encoding.UTF8,
            Indent = true
        };

        using var stringWriter = new Utf8StringWriter();
        using var writer = XmlWriter.Create(stringWriter, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

        foreach (var entry in entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .GroupBy(entry => NormalizePath(entry.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteStartElement("url");
            writer.WriteElementString("loc", $"{baseUrl}{NormalizePath(entry.Path)}");

            if (entry.LastModified.HasValue)
            {
                writer.WriteElementString("lastmod", FormatLastModified(entry.LastModified.Value));
            }

            writer.WriteElementString("priority", entry.Priority.ToString("0.00", CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();

        return stringWriter.ToString();
    }

    private static string NormalizePath(string path)
    {
        return $"/{path.Trim().TrimStart('/')}";
    }

    private static string ResolveCanonicalBaseUrl(IConfiguration configuration)
    {
        var configuredBaseUrl = configuration["Seo:CanonicalBaseUrl"]
            ?? configuration["CanonicalBaseUrl"];

        return string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? CanonicalBaseUrl
            : configuredBaseUrl.Trim().TrimEnd('/');
    }

    private static string FormatLastModified(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();

        return utcValue.ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);
    }

    private sealed record SitemapEntry(string Path, DateTime? LastModified, decimal Priority);

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
