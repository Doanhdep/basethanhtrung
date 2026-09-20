using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebThuMuaPheLieu.Models;
using WebThuMuaPheLieu.Services;

namespace WebThuMuaPheLieu.helpper;

public class LayoutDataHelper : ILayoutDataHelper
{
    private static readonly TimeSpan LayoutCacheDuration = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _context;
    private readonly IMemoryCache _memoryCache;

    public LayoutDataHelper(AppDbContext context, IMemoryCache memoryCache)
    {
        _context = context;
        _memoryCache = memoryCache;
    }

    public async Task<IReadOnlyList<LayoutBlogLinkItem>> GetServiceMenuItemsAsync()
    {
        return await _memoryCache.GetOrCreateAsync(SiteCacheKeys.HeaderServiceMenuItems, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LayoutCacheDuration;

            return await _context.BlogPosts
                .AsNoTracking()
                .Where(post => post.Status == "published"
                    && _context.BlogPostCategories
                        .AsNoTracking()
                        .Any(category => category.PostId == post.Id && category.CategoryId == 1))
                .OrderByDescending(post => post.PublishedAt ?? post.CreatedAt)
                .ThenByDescending(post => post.Id)
                .Select(post => new LayoutBlogLinkItem
                {
                    Title = post.Title,
                    Slug = post.Slug,
                    Id = post.Id
                })
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IReadOnlyList<LayoutProductLinkItem>> GetFooterFeaturedProductsAsync()
    {
        return await _memoryCache.GetOrCreateAsync(SiteCacheKeys.FooterFeaturedProducts, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LayoutCacheDuration;

            return await _context.Products
                .AsNoTracking()
                .Where(product => product.Status == "active")
                .OrderByDescending(product => product.IsFeatured)
                .ThenByDescending(product => product.CreatedAt)
                .Take(5)
                .Select(product => new LayoutProductLinkItem
                {
                    Name = product.Name,
                    Slug = product.Slug,
                    Id = product.Id
                })
                .ToListAsync();
        }) ?? [];
    }

    public async Task<IReadOnlyList<LayoutBlogLinkItem>> GetFooterTopServicePostsAsync()
    {
        return await _memoryCache.GetOrCreateAsync(SiteCacheKeys.FooterTopServicePosts, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LayoutCacheDuration;

            var servicePostIds = _context.BlogPostCategories
                .AsNoTracking()
                .Where(mapping => mapping.PostId.HasValue
                    && mapping.Category != null
                    && ((mapping.Category.Name ?? string.Empty) == "Dịch vụ"
                        || (mapping.Category.Slug ?? string.Empty) == "dich-vu"
                        || (mapping.Category.Slug ?? string.Empty) == "service"))
                .Select(mapping => mapping.PostId!.Value)
                .Distinct();

            return await _context.BlogPosts
                .AsNoTracking()
                .Where(post => post.Status == "published" && servicePostIds.Contains(post.Id))
                .OrderByDescending(post => post.LikeCount ?? 0)
                .ThenByDescending(post => post.PublishedAt ?? post.CreatedAt)
                .Take(5)
                .Select(post => new LayoutBlogLinkItem
                {
                    Title = post.Title,
                    Slug = post.Slug,
                    Id = post.Id
                })
                .ToListAsync();
        }) ?? [];
    }
}
