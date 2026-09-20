using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebThuMuaPheLieu.Models;
using WebThuMuaPheLieu.Services;

namespace WebThuMuaPheLieu.Controllers;

public class ProductController : Controller
{
    private const int DefaultCursorLimit = 8;
    private const int MaxCursorLimit = 24;
    private static readonly TimeSpan ProductListCacheDuration = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _context;
    private readonly IMemoryCache _memoryCache;

    public ProductController(AppDbContext context, IMemoryCache memoryCache)
    {
        _context = context;
        _memoryCache = memoryCache;
    }

    [OutputCache(PolicyName = "ProductList")]
    public async Task<IActionResult> Index(string? category = null)
    {
        if (Request.Query.ContainsKey("page"))
        {
            var normalizedCategory = category?.Trim();
            var routeValues = string.IsNullOrWhiteSpace(normalizedCategory)
                ? null
                : new { category = normalizedCategory };

            var cleanUrl = Url.Action(nameof(Index), "Product", routeValues) ?? "/Product";
            return RedirectPermanent(cleanUrl);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            ViewData["Robots"] = "noindex,follow";
            ViewData["Canonical"] = Url.Action(nameof(Index), "Product", null, Request.Scheme);
        }

        var viewModel = await BuildProductIndexViewModelAsync(category);
        return View(viewModel);
    }

    [HttpGet]
    public Task<IActionResult> GetPagedProducts(int page = 1, string? category = null)
    {
        return GetProductsCursor(null, category, DefaultCursorLimit);
    }

    [HttpGet("/Product/GetProductsCursor")]
    [OutputCache(PolicyName = "ProductCursorApi")]
    public async Task<IActionResult> GetProductsCursor(string? cursor, string? category, int limit = DefaultCursorLimit)
    {
        var normalizedLimit = Math.Clamp(limit, 1, MaxCursorLimit);
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? string.Empty : category.Trim();

        var query = _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "active");

        if (!string.IsNullOrWhiteSpace(normalizedCategory))
        {
            var categoryId = await ResolveCategoryIdAsync(normalizedCategory);
            query = categoryId.HasValue
                ? query.Where(p => p.CategoryId == categoryId.Value)
                : query.Where(p => false);
        }

        CursorTokenHelper.TryDecode<ProductCursor>(cursor, out var decodedCursor);

        if (decodedCursor is not null)
        {
            query = query.Where(p =>
                (p.IsFeatured ?? false) == false && decodedCursor.IsFeatured == true
                || (p.IsFeatured ?? false) == decodedCursor.IsFeatured
                    && (string.Compare(p.Name ?? string.Empty, decodedCursor.Name) > 0
                        || (p.Name == decodedCursor.Name && p.Id > decodedCursor.Id)));
        }

        var rows = await query
            .OrderByDescending(p => p.IsFeatured ?? false)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Take(normalizedLimit + 1)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.Name,
                p.IsFeatured,
                p.ShortDescription,
                p.Description,
                p.PriceLabel,
                p.PriceValue,
                p.Unit,
                p.PrimaryImage,
                ProductImages = p.ProductImages
                    .OrderBy(image => image.OrderIndex ?? int.MaxValue)
                    .ThenBy(image => image.Id)
                    .Take(4)
                    .Select(image => image.ImageUrl)
                    .ToList(),
                CategoryName = p.Category != null ? p.Category.Name : null
            })
            .ToListAsync();

        var hasMore = rows.Count > normalizedLimit;
        var batch = rows.Take(normalizedLimit).ToList();

        string? nextCursor = null;
        if (hasMore && batch.Count > 0)
        {
            var lastProduct = batch[^1];
            nextCursor = CursorTokenHelper.Encode(new ProductCursor
            {
                IsFeatured = lastProduct.IsFeatured ?? false,
                Name = lastProduct.Name ?? string.Empty,
                Id = lastProduct.Id
            });
        }

        var products = batch
            .Select(product =>
            {
                var productImages = BuildProductImageList(product.PrimaryImage, product.ProductImages);

                return new ProductCardViewModel
                {
                    Id = product.Id,
                    Slug = product.Slug,
                    CategoryIcon = GetCategoryIcon(product.CategoryName),
                    Name = product.Name ?? string.Empty,
                    CategoryName = product.CategoryName,
                    ShortDescription = product.ShortDescription,
                    Description = product.Description,
                    PriceLabel = product.PriceLabel,
                    PriceValue = product.PriceValue,
                    Unit = product.Unit,
                    PrimaryImage = productImages.FirstOrDefault() ?? ImagePathHelper.DefaultProductImage,
                    IsFeatured = product.IsFeatured ?? false,
                    Images = productImages
                };
            })
            .ToList();

        return Json(new
        {
            products = products.Select(product => new
            {
                id = product.Id,
                slug = product.Slug,
                name = product.Name,
                categoryName = product.CategoryName,
                categoryIcon = product.CategoryIcon,
                shortDescription = product.ShortDescription,
                description = product.Description,
                priceLabel = product.PriceLabel,
                unit = product.Unit,
                primaryImage = product.PrimaryImage,
                isFeatured = product.IsFeatured,
                images = product.Images
            }),
            nextCursor = nextCursor,
            hasMore = hasMore,
            limit = normalizedLimit
        });
    }

    private sealed class ProductCursor
    {
        public bool IsFeatured { get; init; }

        public string Name { get; init; } = string.Empty;

        public int Id { get; init; }
    }

    private async Task<ProductIndexViewModel> BuildProductIndexViewModelAsync(string? category)
    {
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? string.Empty : category.Trim();

        var query = _context.Products
            .AsNoTracking()
            .Where(p => p.Status == "active")
            .OrderByDescending(p => p.IsFeatured ?? false)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedCategory))
        {
            var categoryId = await ResolveCategoryIdAsync(normalizedCategory);
            query = categoryId.HasValue
                ? query.Where(p => p.CategoryId == categoryId.Value)
                : query.Where(p => false);
        }

        var pagedProductRows = await query
            .Take(DefaultCursorLimit + 1)
            .Select(product => new
            {
                product.Id,
                product.Slug,
                product.Name,
                product.IsFeatured,
                product.ShortDescription,
                product.Description,
                product.PriceLabel,
                product.PriceValue,
                product.Unit,
                product.PrimaryImage,
                ProductImages = product.ProductImages
                    .OrderBy(image => image.OrderIndex ?? int.MaxValue)
                    .ThenBy(image => image.Id)
                    .Take(4)
                    .Select(image => image.ImageUrl)
                    .ToList(),
                CategoryName = product.Category != null ? product.Category.Name : null
            })
            .ToListAsync();

        var hasMore = pagedProductRows.Count > DefaultCursorLimit;
        var batchRows = pagedProductRows.Take(DefaultCursorLimit).ToList();

        var allCategoryNames = await _memoryCache.GetOrCreateAsync(SiteCacheKeys.ProductCategoryNames, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ProductListCacheDuration;

            return await _context.Products
                .AsNoTracking()
                .Where(p => p.Status == "active" && p.Category != null && p.Category.Name != null)
                .Select(p => p.Category!.Name!)
                .Distinct()
                .OrderBy(name => name)
                .ToListAsync();
        }) ?? [];

        var pagedProducts = batchRows
            .Select(product =>
            {
                var productImages = BuildProductImageList(product.PrimaryImage, product.ProductImages);

                return new ProductCardViewModel
                {
                    Id = product.Id,
                    Slug = product.Slug,
                    CategoryIcon = GetCategoryIcon(product.CategoryName),
                    Name = product.Name ?? string.Empty,
                    CategoryName = product.CategoryName,
                    ShortDescription = product.ShortDescription,
                    Description = product.Description,
                    PriceLabel = product.PriceLabel,
                    PriceValue = product.PriceValue,
                    Unit = product.Unit,
                    PrimaryImage = productImages.FirstOrDefault() ?? ImagePathHelper.DefaultProductImage,
                    IsFeatured = product.IsFeatured ?? false,
                    Images = productImages
                };
            })
            .ToList();

        string? initialCursor = null;
        if (pagedProducts.Count > 0)
        {
            var lastProduct = pagedProducts[^1];
            initialCursor = CursorTokenHelper.Encode(new ProductCursor
            {
                IsFeatured = lastProduct.IsFeatured,
                Name = lastProduct.Name,
                Id = lastProduct.Id
            });
        }

        return new ProductIndexViewModel
        {
            Products = pagedProducts,
            CategoryNames = allCategoryNames,
            SelectedCategory = normalizedCategory,
            InitialCursor = initialCursor,
            HasMore = hasMore,
            CurrentPage = 1,
            TotalPages = hasMore ? 2 : 1,
            PageSize = DefaultCursorLimit,
            TotalItems = pagedProducts.Count
        };
    }

    private async Task<int?> ResolveCategoryIdAsync(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return null;
        }

        return await _context.ProductCategories
            .AsNoTracking()
            .Where(category => category.Name == categoryName)
            .Select(category => (int?)category.Id)
            .FirstOrDefaultAsync();
    }

    private List<string> BuildProductImageList(string? primaryImage, IEnumerable<string?>? galleryImages = null)
    {
        var sourceImages = new List<string?> { primaryImage };
        if (galleryImages is not null)
        {
            sourceImages.AddRange(galleryImages);
        }

        var images = ImagePathHelper.ResolveVariantSequence(
                sourceImages,
                SiteImageVariant.Thumbnail,
                ImagePathHelper.DefaultProductImage)
            .ToList();

        return images.Count > 0
            ? images
            : [ImagePathHelper.DefaultProductImage];
    }

    private static string GetCategoryIcon(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return "box-seam";

        return categoryName.ToLower() switch
        {
            var c when c.Contains("sắt") || c.Contains("thép") || c.Contains("kim loại") => "building",
            var c when c.Contains("đồng") || c.Contains("nhôm") || c.Contains("inox") => "gem",
            var c when c.Contains("giấy") || c.Contains("carton") => "box-seam",
            var c when c.Contains("máy") || c.Contains("điện") || c.Contains("cáp") => "cpu",
            var c when c.Contains("xưởng") || c.Contains("công nghiệp") => "gear-wide-connected",
            var c when c.Contains("dân dụng") || c.Contains("gia đình") => "house-door-fill",
            var c when c.Contains("nhựa") => "recycle",
            _ => "box-seam"
        };
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
