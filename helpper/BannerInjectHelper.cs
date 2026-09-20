using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebThuMuaPheLieu.Models;
using WebThuMuaPheLieu.Services;

namespace WebThuMuaPheLieu.helpper;

public class BannerInjectHelper : IBannerInjectHelper
{
    private const string TitleSeparator = "|||";
    private static readonly TimeSpan BannerCacheDuration = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _context;
    private readonly IMemoryCache _memoryCache;

    public BannerInjectHelper(AppDbContext context, IMemoryCache memoryCache)
    {
        _context = context;
        _memoryCache = memoryCache;
    }

    public async Task<IReadOnlyList<BannerInjectSettings>> GetActiveBannersAsync()
    {
        return await _memoryCache.GetOrCreateAsync(SiteCacheKeys.ActiveBanners, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = BannerCacheDuration;

            var banners = await _context.Banners
                .AsNoTracking()
                .Where(item => item.Status != null && item.Status.ToLower() == "active")
                .OrderBy(item => item.OrderIndex)
                .ThenBy(item => item.Id)
                .Select(item => new BannerInjectSettings
                {
                    Id = item.Id,
                    Title = item.Title ?? string.Empty,
                    Subtitle = item.Subtitle ?? string.Empty,
                    SellText = item.SellText ?? string.Empty,
                    ButtonPrimaryText = item.ButtonPrimaryText ?? string.Empty,
                    ButtonPrimaryLink = item.ButtonPrimaryLink ?? string.Empty,
                    ButtonSecondaryText = item.ButtonSecondaryText ?? string.Empty,
                    ButtonSecondaryLink = item.ButtonSecondaryLink ?? string.Empty,
                    OrderIndex = item.OrderIndex ?? 0,
                    Status = item.Status ?? string.Empty,
                    Images = item.BannerImages
                        .OrderBy(image => image.OrderIndex)
                        .ThenBy(image => image.Id)
                        .Select(image => new BannerInjectImageSettings
                        {
                            Id = image.Id,
                            ImageUrl = image.ImageUrl ?? string.Empty,
                            Caption = image.Caption ?? string.Empty,
                            OrderIndex = image.OrderIndex ?? 0
                        })
                        .ToList()
                })
                .ToListAsync();

            return (IReadOnlyList<BannerInjectSettings>)banners.Select(ApplyTitleLines).ToList();
        }) ?? [];
    }

    private static BannerInjectSettings ApplyTitleLines(BannerInjectSettings banner)
    {
        var title = banner.Title ?? string.Empty;
        var titleLines = title.Split(TitleSeparator, StringSplitOptions.None)
            .Select(line => line.Trim())
            .ToArray();

        banner.TitleLine1 = titleLines.Length > 0 ? titleLines[0] : string.Empty;
        banner.TitleLine2 = titleLines.Length > 1 ? titleLines[1] : string.Empty;
        banner.TitleLine3 = titleLines.Length > 2 ? titleLines[2] : string.Empty;
        banner.TitleLine4 = titleLines.Length > 3 ? titleLines[3] : string.Empty;

        return banner;
    }
}
