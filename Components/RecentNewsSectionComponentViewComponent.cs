using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebThuMuaPheLieu.Models;
using WebThuMuaPheLieu.Services;
using WebThuMuaPheLieu.ViewModels;

namespace WebThuMuaPheLieu.Components
{
    public class RecentNewsSectionComponentViewComponent : ViewComponent
    {
        private static readonly TimeSpan RecentNewsCacheDuration = TimeSpan.FromMinutes(5);

        private readonly AppDbContext _context;
        private readonly IMemoryCache _memoryCache;
        private readonly IBlogImageProcessor _blogImageProcessor;
        private readonly ILogger<RecentNewsSectionComponentViewComponent> _logger;

        public RecentNewsSectionComponentViewComponent(
            AppDbContext context,
            IMemoryCache memoryCache,
            IBlogImageProcessor blogImageProcessor,
            ILogger<RecentNewsSectionComponentViewComponent> logger)
        {
            _context = context;
            _memoryCache = memoryCache;
            _blogImageProcessor = blogImageProcessor;
            _logger = logger;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            var fromCache = true;

            var posts = await _memoryCache.GetOrCreateAsync(SiteCacheKeys.RecentNewsPosts, async entry =>
            {
                fromCache = false;
                entry.AbsoluteExpirationRelativeToNow = RecentNewsCacheDuration;

                return await _context.BlogPosts
                    .AsNoTracking()
                    .Where(post => post.Status == "published"
                        && !_context.BlogPostCategories
                            .AsNoTracking()
                            .Any(category => category.PostId == post.Id && category.CategoryId == 1))
                    .OrderByDescending(post => post.PublishedAt ?? post.CreatedAt)
                    .ThenByDescending(post => post.Id)
                    .Take(8)
                    .Select(post => new BlogCardViewModel
                    {
                        Id = post.Id,
                        Slug = !string.IsNullOrWhiteSpace(post.Slug) ? post.Slug : post.Id.ToString(),
                        Title = post.Title ?? "Tin tức đang cập nhật",
                        Summary = !string.IsNullOrWhiteSpace(post.Excerpt) ? post.Excerpt : (post.Title ?? string.Empty),
                        PublishedAt = post.PublishedAt ?? post.CreatedAt,
                        CoverImage = string.IsNullOrWhiteSpace(post.CoverImage) ? "/assets/img/blog/blog-1.jpg" : post.CoverImage,
                        ImageSequence = post.BlogImages
                            .OrderBy(image => image.OrderIndex ?? int.MaxValue)
                            .ThenBy(image => image.Id)
                            .Take(4)
                            .Select(image => image.ImageUrl ?? string.Empty)
                            .Where(imageUrl => imageUrl != string.Empty)
                            .ToList()
                    })
                    .ToListAsync();
            }) ?? [];

            foreach (var post in posts)
            {
                var imageSequence = new List<string>();

                if (!string.IsNullOrWhiteSpace(post.CoverImage))
                {
                    imageSequence.Add(post.CoverImage);
                }

                imageSequence.AddRange(post.ImageSequence);
                post.ImageSequence = imageSequence
                    .Where(imageUrl => !string.IsNullOrWhiteSpace(imageUrl))
                    .Select(NormalizeImagePath)
                    .Distinct(System.StringComparer.OrdinalIgnoreCase)
                    .ToList();

                post.CoverImage = post.ImageSequence.FirstOrDefault() ?? "/assets/img/blog/blog-1.jpg";
                post.CoverImage = _blogImageProcessor.ResolveVariantImage(post.CoverImage, BlogImageVariant.Thumbnail);
            }

            var viewModel = new RecentNewsSectionComponentViewModel
            {
                Posts = posts
            };

            stopwatch.Stop();
            _logger.LogDebug(
                "RecentNewsSectionComponent: posts={PostCount}, cache={CacheHit}, elapsed={ElapsedMs}ms",
                posts.Count,
                fromCache,
                stopwatch.ElapsedMilliseconds);

            return View(viewModel);
        }

        private static string NormalizeImagePath(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return "/assets/img/blog/blog-1.jpg";
            }

            var normalized = imagePath.Replace("\\", "/").Trim();

            if (normalized.StartsWith("~/"))
            {
                return $"/{normalized[2..]}";
            }

            if (normalized.StartsWith("http://") || normalized.StartsWith("https://") || normalized.StartsWith("/"))
            {
                return normalized;
            }

            return $"/{normalized.TrimStart('/')}";
        }
    }
}
