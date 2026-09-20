using System.Net;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using WebThuMuaPheLieu.Models;
using WebThuMuaPheLieu.Services;
using WebThuMuaPheLieu.helpper;

namespace WebThuMuaPheLieu.Controllers;

public class BlogController : Controller
{
    private const string UsefulSessionKeyPrefix = "blog-useful:";
    private static readonly TimeSpan UsefulCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan BlogCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly IReadOnlyDictionary<string, string> LegacyBlogSlugRedirects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["thu-mua-phe-lieu-quan-7-tphcm-24-7"] = "thu-mua-phe-lieu-tp-hcm",
        ["thanh-ly-phe-lieu-cong-trinh"] = "phe-lieu-thanh-trung-chia-se-cach-thanh-ly-phe-lieu-nhanh-va-hieu-qua",
        ["thu-mua-ionx-phe-lieu-gia-cao-uy-tin-tai-tphcm-24-7"] = "thu-mua-phe-lieu-gia-cao",
        ["thu-mua-phe-lieu-inox-tai-tay-ninh-uy-tin-24-7"] = "thu-mua-phe-lieu-gia-cao"
    };
    private readonly AppDbContext _context;
    private readonly IMemoryCache _memoryCache;
    private readonly IContactInfoHelper _contactInfoHelper;
    private readonly IBlogImageProcessor _blogImageProcessor;

    public BlogController(
        AppDbContext context,
        IMemoryCache memoryCache,
        IContactInfoHelper contactInfoHelper,
        IBlogImageProcessor blogImageProcessor)
    {
        _context = context;
        _memoryCache = memoryCache;
        _contactInfoHelper = contactInfoHelper;
        _blogImageProcessor = blogImageProcessor;
    }

    [OutputCache(PolicyName = "BlogList")]
    public async Task<IActionResult> Index(string? searchTerm, string? categoryName)
    {
        var normalizedSearchTerm = searchTerm?.Trim();
        var normalizedCategoryName = categoryName?.Trim();

        if (Request.Query.ContainsKey("page"))
        {
            object? routeValues = null;
            if (!string.IsNullOrWhiteSpace(normalizedSearchTerm) || !string.IsNullOrWhiteSpace(normalizedCategoryName))
            {
                routeValues = new
                {
                    searchTerm = string.IsNullOrWhiteSpace(normalizedSearchTerm) ? null : normalizedSearchTerm,
                    categoryName = string.IsNullOrWhiteSpace(normalizedCategoryName) ? null : normalizedCategoryName
                };
            }

            var cleanUrl = Url.Action(nameof(Index), "Blog", routeValues) ?? "/Blog";
            return RedirectPermanent(cleanUrl);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearchTerm) || !string.IsNullOrWhiteSpace(normalizedCategoryName))
        {
            ViewData["Robots"] = "noindex,follow";
            ViewData["Canonical"] = $"{Request.Scheme}://{Request.Host}/Blog";
        }

        var viewModel = await BuildBlogIndexViewModelAsync(searchTerm, categoryName);
        return View(viewModel);
    }

    [OutputCache(PolicyName = "BlogDetail")]
    public async Task<IActionResult> Detail(string slug)
    {
        var normalizedSlug = slug?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            return RedirectToAction(nameof(Index));
        }

        if (LegacyBlogSlugRedirects.TryGetValue(normalizedSlug, out var targetSlug))
        {
            var canonicalPath = Url.RouteUrl("blog-detail-seo", new { slug = targetSlug }) ?? $"/blog/{targetSlug}";
            return RedirectPermanent(canonicalPath);
        }

        var parsedPostId = int.TryParse(normalizedSlug, out var postIdValue)
            ? postIdValue
            : (int?)null;

        var post = await _context.BlogPosts
            .AsNoTracking()
            .Include(item => item.Author)
            .FirstOrDefaultAsync(item => item.Status == "published"
                && (((item.Slug ?? string.Empty) == normalizedSlug)
                    || (parsedPostId.HasValue && item.Id == parsedPostId.Value)));

        if (post is null)
        {
            return NotFound();
        }

        var categoryFilters = await BuildCategoryFiltersAsync();

        var postCategories = await _context.BlogPostCategories
            .AsNoTracking()
            .Where(postCategory => postCategory.PostId == post.Id && postCategory.CategoryId.HasValue)
            .Join(
                _context.BlogCategories.AsNoTracking(),
                postCategory => postCategory.CategoryId,
                blogCategory => (int?)blogCategory.Id,
                (postCategory, blogCategory) => new
                {
                    blogCategory.Id,
                    blogCategory.Name
                })
            .Distinct()
            .ToListAsync();

        var primaryCategoryName = postCategories
            .Select(item => item.Name)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Tin tức";

        var relatedCategoryIds = postCategories
            .Select(item => item.Id)
            .ToList();

        var relatedPostsQuery = _context.BlogPosts
            .AsNoTracking()
            .Where(item => item.Status == "published" && item.Id != post.Id);

        if (relatedCategoryIds.Count > 0)
        {
            var categoryPostIds = _context.BlogPostCategories
                .AsNoTracking()
                .Where(postCategory => postCategory.PostId.HasValue
                    && postCategory.CategoryId.HasValue
                    && relatedCategoryIds.Contains(postCategory.CategoryId.Value))
                .Select(postCategory => postCategory.PostId!.Value)
                .Distinct();

            relatedPostsQuery = relatedPostsQuery.Where(item => categoryPostIds.Contains(item.Id));
        }

        var relatedPosts = await relatedPostsQuery
            .OrderByDescending(item => item.PublishedAt ?? item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(4)
            .Select(item => new BlogPostListItem
            {
                Id = item.Id,
                Slug = item.Slug,
                Title = item.Title,
                Excerpt = item.Excerpt,
                CoverImage = item.CoverImage,
                PublishedAt = item.PublishedAt ?? item.CreatedAt,
                LikeCount = item.LikeCount ?? 0,
                AuthorName = item.Author != null ? item.Author.FullName ?? string.Empty : string.Empty
            })
            .ToListAsync();

        var relatedBlogCards = await BuildBlogCardsAsync(relatedPosts);
        var canonicalSlug = BuildBlogSlug(post.Slug, post.Id);
        var currentUrl = Url.RouteUrl("blog-detail-seo", new { slug = canonicalSlug }, Request.Scheme) ?? string.Empty;
        var rawGalleryImages = await _context.BlogImages
            .AsNoTracking()
            .Where(image => image.BlogId == post.Id)
            .OrderBy(image => image.OrderIndex ?? int.MaxValue)
            .ThenBy(image => image.Id)
            .Select(image => image.ImageUrl)
            .ToListAsync();

        var galleryImages = rawGalleryImages
            .Select(NormalizeBlogImagePath)
            .Where(imageUrl => !string.IsNullOrWhiteSpace(imageUrl))
            .Distinct()
            .ToList();

        var normalizedCoverImage = NormalizeBlogImagePath(post.CoverImage);

        if (!galleryImages.Any())
        {
            galleryImages.Add(normalizedCoverImage);
        }
        else if (!galleryImages.Any(imageUrl => string.Equals(imageUrl, normalizedCoverImage, StringComparison.OrdinalIgnoreCase)))
        {
            galleryImages.Insert(0, normalizedCoverImage);
        }

        var viewModel = new BlogDetailViewModel
        {
            Post = post,
            CoverImage = normalizedCoverImage,
            GalleryImages = galleryImages,
            PrimaryCategoryName = primaryCategoryName,
            AuthorName = ResolveBlogAuthorName(post.Author?.FullName, post.Content),
            CurrentUrl = currentUrl,
            RelatedPosts = relatedBlogCards,
            Categories = categoryFilters
        };

        return View(viewModel);
    }

    [HttpGet("/Blog/Detail")]
    public IActionResult LegacyDetail(string? slug)
    {
        var normalizedSlug = slug?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSlug))
        {
            return RedirectPermanent(Url.Action(nameof(Index), "Blog") ?? "/Blog");
        }

        if (LegacyBlogSlugRedirects.TryGetValue(normalizedSlug, out var targetSlug))
        {
            normalizedSlug = targetSlug;
        }

        var canonicalPath = Url.RouteUrl("blog-detail-seo", new { slug = normalizedSlug }) ?? $"/blog/{normalizedSlug}";
        return RedirectPermanent(canonicalPath);
    }

    [HttpGet("/Blog/GetPagedBlogs")]
    [OutputCache(PolicyName = "BlogPagedApi")]
    public Task<IActionResult> GetPagedBlogs(string? searchTerm, string? categoryName, int page = 1)
    {
        return GetBlogsCursor(null, searchTerm, categoryName, 6);
    }

    [HttpGet("/Blog/GetBlogsCursor")]
    [OutputCache(PolicyName = "BlogPagedApi")]
    public async Task<IActionResult> GetBlogsCursor(string? cursor, string? searchTerm, string? categoryName, int limit = 6)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 24);
        var normalizedSearchTerm = searchTerm?.Trim() ?? string.Empty;
        var selectedCategory = await ResolveBlogCategoryFilterAsync(categoryName);

        var query = _context.BlogPosts
            .AsNoTracking()
            .Where(post => post.Status == "published");

        if (selectedCategory is not null)
        {
            var categoryPostIds = _context.BlogPostCategories
                .AsNoTracking()
                .Where(postCategory => postCategory.PostId.HasValue
                    && postCategory.CategoryId == selectedCategory.Id)
                .Select(postCategory => postCategory.PostId!.Value);

            query = query.Where(post => categoryPostIds.Contains(post.Id));
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearchTerm))
        {
            var keywordPattern = $"%{normalizedSearchTerm}%";

            query = query.Where(post =>
                EF.Functions.Like(post.Title ?? string.Empty, keywordPattern)
                || EF.Functions.Like(post.Excerpt ?? string.Empty, keywordPattern));
        }

        CursorTokenHelper.TryDecode<BlogCursor>(cursor, out var decodedCursor);

        if (decodedCursor is not null)
        {
            var cursorSortDate = decodedCursor.SortDate;
            var cursorId = decodedCursor.Id;

            query = query.Where(post =>
                (post.PublishedAt ?? post.CreatedAt) < cursorSortDate
                || ((post.PublishedAt ?? post.CreatedAt) == cursorSortDate && post.Id < cursorId));
        }

        var pagedPosts = await query
            .OrderByDescending(post => post.PublishedAt ?? post.CreatedAt)
            .ThenByDescending(post => post.Id)
            .Take(normalizedLimit + 1)
            .Select(post => new BlogPostListItem
            {
                Id = post.Id,
                Slug = post.Slug,
                Title = post.Title,
                Excerpt = post.Excerpt,
                CoverImage = post.CoverImage,
                PublishedAt = post.PublishedAt ?? post.CreatedAt,
                LikeCount = post.LikeCount ?? 0,
                AuthorName = post.Author != null ? post.Author.FullName ?? string.Empty : string.Empty
            })
            .ToListAsync();

        var hasMore = pagedPosts.Count > normalizedLimit;
        var batch = pagedPosts.Take(normalizedLimit).ToList();

        string? nextCursor = null;
        if (hasMore && batch.Count > 0)
        {
            var lastPost = batch[^1];
            nextCursor = CursorTokenHelper.Encode(new BlogCursor
            {
                SortDate = lastPost.PublishedAt ?? DateTime.MinValue,
                Id = lastPost.Id
            });
        }

        var blogCards = await BuildBlogCardsAsync(batch);

        return Json(new
        {
            searchTerm = normalizedSearchTerm,
            selectedCategoryName = selectedCategory?.Name ?? string.Empty,
            selectedCategorySlug = selectedCategory?.Slug ?? string.Empty,
            selectedCategoryDisplayName = selectedCategory?.Name ?? string.Empty,
            posts = blogCards.Select(post => new
            {
                id = post.Id,
                slug = post.Slug,
                title = post.Title,
                summary = post.Summary,
                coverImage = post.CoverImage,
                imageSequence = post.ImageSequence,
                categoryName = post.CategoryName,
                publishedAt = post.PublishedAt?.ToString("dd/MM/yyyy"),
                likeCount = post.LikeCount,
                authorName = post.AuthorName,
                detailUrl = Url.RouteUrl("blog-detail-seo", new { slug = BuildBlogSlug(post.Slug, post.Id) }) ?? "/Blog"
            }),
            nextCursor = nextCursor,
            hasMore = hasMore,
            limit = normalizedLimit
        });
    }

    private sealed class BlogCursor
    {
        public DateTime SortDate { get; init; }

        public int Id { get; init; }
    }

    [HttpPost]
    public async Task<IActionResult> MarkUseful([FromForm] int id)
    {
        if (id <= 0)
        {
            return Json(new
            {
                success = false,
                message = "Bài viết không hợp lệ."
            });
        }

        if (TryGetUsefulCooldownRemaining(id, out var cooldownSecondsRemaining))
        {
            return Json(new
            {
                success = false,
                cooldownSecondsRemaining,
                message = BuildUsefulCooldownMessage(cooldownSecondsRemaining)
            });
        }

        var post = await _context.BlogPosts
            .FirstOrDefaultAsync(item => item.Id == id && item.Status == "published");

        if (post is null)
        {
            return Json(new
            {
                success = false,
                message = "Không tìm thấy bài viết."
            });
        }

        post.LikeCount = (post.LikeCount ?? 0) + 1;
        await _context.SaveChangesAsync();

        HttpContext.Session.SetString(
            BuildUsefulSessionKey(id),
            DateTimeOffset.UtcNow.Add(UsefulCooldown).ToString("O"));

        return Json(new
        {
            success = true,
            likeCount = post.LikeCount ?? 0,
            cooldownSecondsRemaining = (int)UsefulCooldown.TotalSeconds,
            message = "Đã ghi nhận đánh giá hữu ích."
        });
    }

    private async Task<BlogIndexViewModel> BuildBlogIndexViewModelAsync(string? searchTerm, string? categoryName)
    {
        const int pageSize = 6;

        var normalizedSearchTerm = searchTerm?.Trim() ?? string.Empty;
        var selectedCategory = await ResolveBlogCategoryFilterAsync(categoryName);

        var publishedPostsQuery = _context.BlogPosts
            .AsNoTracking()
            .Where(post => post.Status == "published");

        var categoryFilters = await BuildCategoryFiltersAsync();
        var contactInfo = await _contactInfoHelper.GetContactInfoAsync();

        var query = publishedPostsQuery;

        if (selectedCategory is not null)
        {
            var categoryPostIds = _context.BlogPostCategories
                .AsNoTracking()
                .Where(postCategory => postCategory.PostId.HasValue
                    && postCategory.CategoryId == selectedCategory.Id)
                .Select(postCategory => postCategory.PostId!.Value);

            query = query.Where(post => categoryPostIds.Contains(post.Id));
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearchTerm))
        {
            var keywordPattern = $"%{normalizedSearchTerm}%";

            query = query.Where(post =>
                EF.Functions.Like(post.Title ?? string.Empty, keywordPattern)
                || EF.Functions.Like(post.Excerpt ?? string.Empty, keywordPattern));
        }

        var pagedPosts = await query
            .OrderByDescending(post => post.PublishedAt ?? post.CreatedAt)
            .ThenByDescending(post => post.Id)
            .Take(pageSize + 1)
            .Select(post => new BlogPostListItem
            {
                Id = post.Id,
                Slug = post.Slug,
                Title = post.Title,
                Excerpt = post.Excerpt,
                CoverImage = post.CoverImage,
                PublishedAt = post.PublishedAt ?? post.CreatedAt,
                LikeCount = post.LikeCount ?? 0,
                AuthorName = post.Author != null ? post.Author.FullName ?? string.Empty : string.Empty
            })
            .ToListAsync();

        var hasMore = pagedPosts.Count > pageSize;
        var batchPosts = pagedPosts.Take(pageSize).ToList();

        var featuredPosts = await _memoryCache.GetOrCreateAsync(BlogCacheKeys.FeaturedPosts, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = BlogCacheDuration;

            return await publishedPostsQuery
                .OrderByDescending(post => post.LikeCount ?? 0)
                .ThenByDescending(post => post.PublishedAt ?? post.CreatedAt)
                .ThenByDescending(post => post.Id)
                .Take(5)
                .Select(post => new BlogPostListItem
                {
                    Id = post.Id,
                    Slug = post.Slug,
                    Title = post.Title,
                    Excerpt = post.Excerpt,
                    CoverImage = post.CoverImage,
                    PublishedAt = post.PublishedAt ?? post.CreatedAt,
                    LikeCount = post.LikeCount ?? 0,
                    AuthorName = post.Author != null ? post.Author.FullName ?? string.Empty : string.Empty
                })
                .ToListAsync();
        }) ?? [];

        var blogCards = await BuildBlogCardsAsync(batchPosts);
        var featuredBlogCards = await BuildBlogCardsAsync(featuredPosts);

        string? initialCursor = null;
        if (blogCards.Count > 0)
        {
            var lastPost = blogCards[^1];
            initialCursor = CursorTokenHelper.Encode(new BlogCursor
            {
                SortDate = lastPost.PublishedAt ?? DateTime.MinValue,
                Id = lastPost.Id
            });
        }

        var totalPublishedPosts = await _memoryCache.GetOrCreateAsync(BlogCacheKeys.TotalPublishedPosts, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = BlogCacheDuration;
            return await publishedPostsQuery.CountAsync();
        });

        return new BlogIndexViewModel
        {
            Posts = blogCards,
            FeaturedPosts = featuredBlogCards,
            Categories = categoryFilters,
            SearchTerm = normalizedSearchTerm,
            SelectedCategoryName = selectedCategory?.Name ?? string.Empty,
            SelectedCategorySlug = selectedCategory?.Slug ?? string.Empty,
            TotalPublishedPosts = totalPublishedPosts,
            InitialCursor = initialCursor,
            HasMore = hasMore,
            CurrentPage = 1,
            TotalPages = hasMore ? 2 : 1,
            PageSize = pageSize,
            TotalItems = blogCards.Count,
            ContactPhone = contactInfo.Phone
        };
    }

    private async Task<List<BlogCardViewModel>> BuildBlogCardsAsync(List<BlogPostListItem> posts)
    {
        if (posts.Count == 0)
        {
            return [];
        }

        var postIds = posts
            .Select(post => post.Id)
            .ToList();

        var categories = await _context.BlogPostCategories
            .AsNoTracking()
            .Where(postCategory => postCategory.PostId.HasValue && postIds.Contains(postCategory.PostId.Value))
            .Join(
                _context.BlogCategories.AsNoTracking(),
                postCategory => postCategory.CategoryId,
                blogCategory => (int?)blogCategory.Id,
                (postCategory, blogCategory) => new
                {
                    postCategory.PostId,
                    blogCategory.Name
                })
            .ToListAsync();

        var categoryByPostId = categories
            .Where(item => item.PostId.HasValue)
            .GroupBy(item => item.PostId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Tin tức");

        return posts
            .Select(post =>
            {
                categoryByPostId.TryGetValue(post.Id, out var postCategoryName);
                var coverImage = NormalizeBlogImagePath(post.CoverImage);
                var thumbnailImage = _blogImageProcessor.ResolveVariantImage(coverImage, BlogImageVariant.Thumbnail);
                var imageSequence = new[] { thumbnailImage, coverImage }
                    .Concat(BuildHoverImageSequenceFromCover(coverImage))
                    .Where(imageUrl => !string.IsNullOrWhiteSpace(imageUrl))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (imageSequence.Count == 0)
                {
                    imageSequence.Add(NormalizeBlogImagePath(null));
                }

                return new BlogCardViewModel
                {
                    Id = post.Id,
                    Slug = BuildBlogSlug(post.Slug, post.Id),
                    Title = post.Title ?? "Tin tức đang cập nhật",
                    Summary = BuildBlogSummary(post.Excerpt, post.Title),
                    CoverImage = imageSequence[0],
                    ImageSequence = imageSequence,
                    CategoryName = postCategoryName ?? "Tin tức",
                    PublishedAt = post.PublishedAt,
                    LikeCount = post.LikeCount,
                    AuthorName = post.AuthorName
                };
            })
            .ToList();
    }

    private async Task<List<BlogCategoryFilterViewModel>> BuildCategoryFiltersAsync(IQueryable<BlogPost>? publishedPostsQuery = null)
    {
        if (publishedPostsQuery is null)
        {
            return await _memoryCache.GetOrCreateAsync(BlogCacheKeys.CategoryFilters, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = BlogCacheDuration;
                return await BuildCategoryFiltersUncachedAsync();
            }) ?? [];
        }

        return await BuildCategoryFiltersUncachedAsync(publishedPostsQuery);
    }

    private async Task<List<BlogCategoryFilterViewModel>> BuildCategoryFiltersUncachedAsync(IQueryable<BlogPost>? publishedPostsQuery = null)
    {
        publishedPostsQuery ??= _context.BlogPosts
            .AsNoTracking()
            .Where(post => post.Status == "published");

        var categories = await _context.BlogCategories
            .AsNoTracking()
            .Select(category => new
            {
                category.Id,
                category.Name,
                category.Slug
            })
            .ToListAsync();

        var postCountsByCategory = await _context.BlogPostCategories
            .AsNoTracking()
            .Where(postCategory => postCategory.CategoryId.HasValue && postCategory.PostId.HasValue)
            .Join(
                publishedPostsQuery,
                postCategory => postCategory.PostId!.Value,
                post => post.Id,
                (postCategory, post) => new
                {
                    CategoryId = postCategory.CategoryId!.Value,
                    PostId = post.Id
                })
            .Distinct()
            .GroupBy(item => item.CategoryId)
            .Select(group => new
            {
                CategoryId = group.Key,
                PostCount = group.Count()
            })
            .ToDictionaryAsync(item => item.CategoryId, item => item.PostCount);

        return categories
            .Select(category => new BlogCategoryFilterViewModel
            {
                Name = category.Name ?? string.Empty,
                Slug = BuildCategorySlug(category.Slug, category.Name),
                PostCount = postCountsByCategory.TryGetValue(category.Id, out var postCount) ? postCount : 0
            })
            .Where(category => !string.IsNullOrWhiteSpace(category.Name))
            .OrderBy(category => category.Name)
            .ToList();
    }

    private async Task<BlogCategoryFilter?> ResolveBlogCategoryFilterAsync(string? categoryName)
    {
        var normalizedCategoryName = categoryName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCategoryName))
        {
            return null;
        }

        var categories = await _context.BlogCategories
            .AsNoTracking()
            .Select(category => new BlogCategoryFilter
            {
                Id = category.Id,
                Name = category.Name ?? string.Empty,
                Slug = BuildCategorySlug(category.Slug, category.Name)
            })
            .ToListAsync();

        var requestedSlug = GenerateSlug(normalizedCategoryName);

        return categories.FirstOrDefault(category =>
            string.Equals(category.Slug, normalizedCategoryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(category.Name, normalizedCategoryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(BuildCategorySlug(category.Slug, category.Name), requestedSlug, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildCategorySlug(string? slug, string? name)
    {
        return !string.IsNullOrWhiteSpace(slug)
            ? slug.Trim()
            : GenerateSlug(name);
    }

    private static string GenerateSlug(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        var normalized = source.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        var pendingDash = false;

        foreach (var character in normalized)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(character);
            if (unicodeCategory == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var current = character switch
            {
                'đ' => 'd',
                'Đ' => 'd',
                _ => char.ToLowerInvariant(character)
            };

            if (char.IsLetterOrDigit(current))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(current);
                pendingDash = false;
            }
            else
            {
                pendingDash = builder.Length > 0;
            }
        }

        return builder.ToString();
    }

    private bool TryGetUsefulCooldownRemaining(int postId, out int cooldownSecondsRemaining)
    {
        cooldownSecondsRemaining = 0;

        var sessionKey = BuildUsefulSessionKey(postId);
        var rawNextAvailableAt = HttpContext.Session.GetString(sessionKey);

        if (string.IsNullOrWhiteSpace(rawNextAvailableAt)
            || !DateTimeOffset.TryParse(rawNextAvailableAt, out var nextAvailableAt))
        {
            return false;
        }

        var remaining = nextAvailableAt - DateTimeOffset.UtcNow;

        if (remaining <= TimeSpan.Zero)
        {
            HttpContext.Session.Remove(sessionKey);
            return false;
        }

        cooldownSecondsRemaining = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        return true;
    }

    private static string BuildUsefulCooldownMessage(int cooldownSecondsRemaining)
    {
        var cooldownMinutesRemaining = Math.Max(1, (int)Math.Ceiling(cooldownSecondsRemaining / 60d));
        return $"Bạn chỉ có thể nhấn lại sau {cooldownMinutesRemaining} phút.";
    }

    private static string BuildUsefulSessionKey(int postId)
    {
        return $"{UsefulSessionKeyPrefix}{postId}";
    }

    private static string BuildBlogSummary(string? excerpt, string? content)
    {
        var summary = !string.IsNullOrWhiteSpace(excerpt)
            ? excerpt
            : content;

        if (string.IsNullOrWhiteSpace(summary))
        {
            return "Nội dung bài viết đang được cập nhật.";
        }

        summary = WebUtility.HtmlDecode(summary);
        summary = Regex.Replace(summary, "<.*?>", string.Empty).Trim();
        return summary.Length > 180 ? $"{summary[..177]}..." : summary;
    }

    private static string BuildBlogSlug(string? slug, int id)
    {
        return !string.IsNullOrWhiteSpace(slug)
            ? slug.Trim()
            : id.ToString();
    }

    private static string NormalizeBlogImagePath(string? coverImage)
    {
        if (string.IsNullOrWhiteSpace(coverImage))
        {
            return "/assets/img/blog/blog-1.jpg";
        }

        var normalized = coverImage.Replace("\\", "/").Trim();

        if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            return $"/{normalized[2..]}";
        }

        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return normalized;
        }

        return $"/{normalized.TrimStart('/')}";
    }

    private static List<string> BuildHoverImageSequenceFromCover(string coverImage)
    {
        if (string.IsNullOrWhiteSpace(coverImage))
        {
            return [];
        }

        var normalized = coverImage.Replace("\\", "/").Trim();
        var extension = Path.GetExtension(normalized);

        if (string.IsNullOrWhiteSpace(extension))
        {
            return [normalized];
        }

        var withoutExtension = normalized[..^extension.Length];
        var match = Regex.Match(withoutExtension, "^(.*?)(\\d+)$");

        if (!match.Success)
        {
            return [normalized];
        }

        var prefix = match.Groups[1].Value;
        var indexText = match.Groups[2].Value;

        if (!int.TryParse(indexText, out var startIndex) || startIndex <= 0)
        {
            return [normalized];
        }

        var sequence = new List<string>();
        var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

        for (var i = 0; i < 3; i++)
        {
            var candidateIndex = startIndex + i;
            var candidate = $"{prefix}{candidateIndex}{extension}";
            var relativePath = candidate.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var physicalPath = Path.Combine(webRootPath, relativePath);

            if (!System.IO.File.Exists(physicalPath))
            {
                break;
            }

            sequence.Add(candidate);
        }

        if (sequence.Count == 0)
        {
            sequence.Add(normalized);
        }

        return sequence;
    }

    private static string ResolveBlogAuthorName(string? authorFromAdmin, string? content)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            var match = Regex.Match(content, "^\\s*<!--AUTHOR:(.*?)-->", RegexOptions.Singleline);
            if (match.Success)
            {
                var decoded = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    return decoded;
                }
            }
        }

        return !string.IsNullOrWhiteSpace(authorFromAdmin) ? authorFromAdmin : string.Empty;
    }

    private sealed class BlogPostListItem
    {
        public int Id { get; init; }

        public string? Slug { get; init; }

        public string? Title { get; init; }

        public string? Excerpt { get; init; }

        public string? Content { get; init; }

        public string? CoverImage { get; init; }

        public DateTime? PublishedAt { get; init; }

        public int LikeCount { get; init; }

        public string AuthorName { get; init; } = string.Empty;
    }

    private sealed class BlogCategoryFilter
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Slug { get; init; } = string.Empty;
    }
}
