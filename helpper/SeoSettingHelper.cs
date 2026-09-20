using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebThuMuaPheLieu.Models;
using WebThuMuaPheLieu.Services;

namespace WebThuMuaPheLieu.helpper;

public class SeoSettingHelper : ISeoSettingHelper
{
    private const string SeoPrefix = "seo.";
    private static readonly TimeSpan SeoCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly IReadOnlyDictionary<string, string> SeoKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["seo.meta_title"] = "MetaTitle",
        ["seo.meta_description"] = "MetaDescription",
        ["seo.keywords"] = "SeoKeywords",
        ["seo.og_title"] = "OgTitle",
        ["seo.og_image"] = "OgImage"
    };

    private readonly AppDbContext _context;
    private readonly IMemoryCache _memoryCache;

    public SeoSettingHelper(AppDbContext context, IMemoryCache memoryCache)
    {
        _context = context;
        _memoryCache = memoryCache;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSeoSettingsAsync()
    {
        return await _memoryCache.GetOrCreateAsync(SiteCacheKeys.SeoSettings, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SeoCacheDuration;

            var settings = await _context.SiteSettings
                .AsNoTracking()
                .Where(item => item.SettingKey != null && item.SettingKey.ToLower().StartsWith(SeoPrefix))
                .Select(item => new
                {
                    item.SettingKey,
                    item.SettingValue
                })
                .ToListAsync();

            return (IReadOnlyDictionary<string, string>)settings
                .Select(item => new
                {
                    Key = NormalizeKey(item.SettingKey),
                    Value = item.SettingValue ?? string.Empty
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .GroupBy(item => item.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Value,
                    StringComparer.OrdinalIgnoreCase);
        }) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> GetSeoSettingValueAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var normalizedKey = NormalizeKey(key);

        var settings = await GetSeoSettingsAsync();
        return settings.TryGetValue(normalizedKey, out var value) ? value : string.Empty;
    }

    public static string NormalizeKey(string? settingKey)
    {
        if (string.IsNullOrWhiteSpace(settingKey))
        {
            return string.Empty;
        }

        var normalized = settingKey.Trim();

        if (SeoKeyMap.TryGetValue(normalized, out var mappedKey))
        {
            return mappedKey;
        }

        if (normalized.StartsWith(SeoPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(SeoPrefix.Length);
        }

        return normalized.Replace('.', ' ').Replace('_', ' ').Trim();
    }
}
