using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebThuMuaPheLieu.helpper;
using WebThuMuaPheLieu.Models;
using WebThuMuaPheLieu.Services;
using WebThuMuaPheLieu.ViewModels;

namespace WebThuMuaPheLieu.Controllers
{
    public class HomeController : Controller
    {
        private const int HomeProductCount = 16;

        private static readonly TimeSpan HomeProductsCacheDuration = TimeSpan.FromMinutes(10);

        private readonly AppDbContext _context;
        private readonly IBannerInjectHelper _bannerInjectHelper;
        private readonly IContactInfoHelper _contactInfoHelper;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            AppDbContext context,
            IBannerInjectHelper bannerInjectHelper,
            IContactInfoHelper contactInfoHelper,
            IMemoryCache memoryCache,
            ILogger<HomeController> logger)
        {
            _context = context;
            _bannerInjectHelper = bannerInjectHelper;
            _contactInfoHelper = contactInfoHelper;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        // =========================================================
        // HOME
        // =========================================================
        [OutputCache(PolicyName = "Home")]
        public async Task<IActionResult> Index()
        {
            var stopwatch = Stopwatch.StartNew();

            // -----------------------------------------------------
            // Banner
            // -----------------------------------------------------
            var banners =
                await _bannerInjectHelper.GetActiveBannersAsync();

            // -----------------------------------------------------
            // Contact information
            // -----------------------------------------------------
            var contactInfo =
                await _contactInfoHelper.GetContactInfoAsync();

            // -----------------------------------------------------
            // Products
            //
            // Cache snapshot 10 phút + giới hạn số lượng thay vì
            // load toàn bộ sản phẩm active mỗi request.
            // -----------------------------------------------------
            var products = await _memoryCache.GetOrCreateAsync(SiteCacheKeys.HomeProducts, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = HomeProductsCacheDuration;

                var rows = await _context.Products
                    .AsNoTracking()
                    .Where(p => p.Status == "active")
                    .OrderByDescending(p => p.IsFeatured)
                    .ThenByDescending(p => p.CreatedAt)
                    .Take(HomeProductCount)
                    .Select(p => new
                    {
                        p.Id,
                        p.Name,
                        p.Slug,
                        p.PrimaryImage,
                        p.ShortDescription,
                        p.Description,
                        p.PriceLabel,
                        p.PriceValue,
                        p.Unit,
                        p.IsFeatured,
                        ProductImages = p.ProductImages
                            .OrderBy(image => image.OrderIndex ?? int.MaxValue)
                            .ThenBy(image => image.Id)
                            .Take(4)
                            .Select(image => image.ImageUrl)
                            .ToList(),
                        CategoryName = p.Category == null ? null : p.Category.Name
                    })
                    .ToListAsync();

                return rows
                    .Select(product =>
                    {
                        var productImages = BuildProductImageList(product.PrimaryImage, product.ProductImages);

                        return new ProductCardViewModel
                        {
                            Id = product.Id,
                            Slug = product.Slug,
                            Name = product.Name ?? string.Empty,
                            CategoryName = product.CategoryName,
                            CategoryIcon = GetCategoryIcon(product.CategoryName),
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
            }) ?? [];

            stopwatch.Stop();
            _logger.LogDebug(
                "Home.Index loaded: banners={BannerCount}, products={ProductCount}, elapsed={ElapsedMs}ms",
                banners.Count,
                products.Count,
                stopwatch.ElapsedMilliseconds);

            // -----------------------------------------------------
            // Home ViewModel
            // -----------------------------------------------------
            var viewModel = new HomeIndexViewModel
            {
                Banner = banners.FirstOrDefault(),
                ContactInfo = contactInfo,
                Products = products
            };

            return View(viewModel);
        }

        // =========================================================
        // PRODUCTS
        // =========================================================
        public IActionResult Products()
        {
            return RedirectToAction("Index", "Product");
        }

        // =========================================================
        // FEATURED PRODUCTS
        // =========================================================
        public IActionResult FeaturedProducts()
        {
            return RedirectToAction("Index", "Product");
        }

        [HttpGet("/Home/Detail/{id:int}")]
        public async Task<IActionResult> LegacyProductDetail(int id, string? slug)
        {
            var normalizedSlug = slug?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedSlug))
            {
                normalizedSlug = await _context.Products
                    .AsNoTracking()
                    .Where(product => product.Id == id && product.Status == "active")
                    .Select(product => product.Slug)
                    .FirstOrDefaultAsync();
            }

            if (string.IsNullOrWhiteSpace(normalizedSlug))
            {
                return RedirectPermanent(Url.Action("Index", "Product") ?? "/Product");
            }

            var canonicalPath = Url.RouteUrl("product-detail-seo", new { slug = normalizedSlug }) ?? $"/san-pham/{normalizedSlug}";
            return RedirectPermanent(canonicalPath);
        }

        // =========================================================
        // HOME LAZY SECTIONS
        // =========================================================
        [HttpGet("/Home/LazySection/{sectionKey}")]
        [OutputCache(PolicyName = "HomeLazySection")]
        public IActionResult LazySection(string sectionKey)
        {
            if (string.IsNullOrWhiteSpace(sectionKey))
            {
                return NotFound();
            }

            return sectionKey.Trim().ToLowerInvariant() switch
            {
                "recent-news" => ViewComponent("RecentNewsSectionComponent"),
                "scrap-services" => ViewComponent("ScrapPurchaseServicesSectionComponent"),
                _ => NotFound()
            };
        }

        // =========================================================
        // PRODUCT DETAIL
        // =========================================================
        [OutputCache(PolicyName = "ProductDetail")]
        public async Task<IActionResult> Detail(
            string? slug,
            int? id)
        {
            var normalizedSlug = slug?.Trim();

            var requestPath =
                Request.Path.Value?.TrimEnd('/')
                ?? string.Empty;

            var isLegacyDetailPath =
                requestPath.StartsWith(
                    "/Home/Detail",
                    StringComparison.OrdinalIgnoreCase);

            // -----------------------------------------------------
            // Legacy URL -> SEO URL
            // -----------------------------------------------------
            if (isLegacyDetailPath &&
                !string.IsNullOrWhiteSpace(normalizedSlug))
            {
                var canonicalPath =
                    Url.RouteUrl(
                        "product-detail-seo",
                        new
                        {
                            slug = normalizedSlug
                        })
                    ?? $"/san-pham/{normalizedSlug}";

                return RedirectPermanent(canonicalPath);
            }

            // -----------------------------------------------------
            // Không có slug và id
            // -----------------------------------------------------
            if (string.IsNullOrWhiteSpace(slug) &&
                id == null)
            {
                return NotFound();
            }

            Product? product = null;

            // -----------------------------------------------------
            // Tìm theo ID
            // -----------------------------------------------------
            if (id != null)
            {
                product = await _context.Products
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p =>
                        p.Id == id &&
                        p.Status == "active");
            }

            // -----------------------------------------------------
            // Nếu không tìm được theo ID -> tìm theo slug
            // -----------------------------------------------------
            if (product == null &&
                !string.IsNullOrWhiteSpace(slug))
            {
                product = await _context.Products
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p =>
                        p.Slug == slug &&
                        p.Status == "active");
            }

            // -----------------------------------------------------
            // Không tìm thấy sản phẩm
            // -----------------------------------------------------
            if (product == null)
            {
                return NotFound();
            }

            // -----------------------------------------------------
            // Product phải có slug
            // -----------------------------------------------------
            if (string.IsNullOrWhiteSpace(product.Slug))
            {
                return NotFound();
            }

            // -----------------------------------------------------
            // Redirect canonical URL
            // -----------------------------------------------------
            if (string.IsNullOrWhiteSpace(slug) ||
                !string.Equals(
                    slug,
                    product.Slug,
                    StringComparison.OrdinalIgnoreCase))
            {
                var canonicalPath =
                    Url.RouteUrl(
                        "product-detail-seo",
                        new
                        {
                            slug = product.Slug
                        })
                    ?? $"/san-pham/{product.Slug}";

                return RedirectPermanent(canonicalPath);
            }

            // -----------------------------------------------------
            // Category
            // -----------------------------------------------------
            var category =
                await _context.ProductCategories
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c =>
                        c.Id == product.CategoryId);

            // -----------------------------------------------------
            // Images
            // -----------------------------------------------------
            var images =
                await _context.ProductImages
                    .AsNoTracking()
                    .Where(x =>
                        x.ProductId == product.Id)
                    .OrderBy(x => x.OrderIndex)
                    .ToListAsync();

            // -----------------------------------------------------
            // Price histories
            // -----------------------------------------------------
            var priceHistories =
                await _context.PriceHistories
                    .AsNoTracking()
                    .Where(x =>
                        x.ProductId == product.Id)
                    .OrderByDescending(
                        x => x.EffectiveDate)
                    .ToListAsync();

            // -----------------------------------------------------
            // ViewModel
            // -----------------------------------------------------
            var vm = new ProductDetailViewModel
            {
                Product = product,
                Category = category,
                Images = images,
                PriceHistories = priceHistories
            };

            return View(vm);
        }

        // =========================================================
        // ABOUT
        // =========================================================
        [OutputCache(PolicyName = "About")]
        public async Task<IActionResult> About()
        {
            var contactInfo =
                await _contactInfoHelper.GetContactInfoAsync();

            ViewData["Title"] = "Giới thiệu";
            ViewData["ContactInfo"] = contactInfo;

            return View();
        }

        // =========================================================
        // PRIVACY
        // =========================================================
        public IActionResult Privacy()
        {
            return View();
        }

        // =========================================================
        // ERROR
        // =========================================================
        [ResponseCache(
            Duration = 0,
            Location = ResponseCacheLocation.None,
            NoStore = true)]
        public IActionResult Error()
        {
            return View(
                new ErrorViewModel
                {
                    RequestId =
                        Activity.Current?.Id
                        ?? HttpContext.TraceIdentifier
                });
        }

        private static List<string> BuildProductImageList(string? primaryImage, IEnumerable<string?>? galleryImages = null)
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
    }
}
