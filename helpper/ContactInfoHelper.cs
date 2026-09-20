using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WebThuMuaPheLieu.Models;
using WebThuMuaPheLieu.Services;

namespace WebThuMuaPheLieu.helpper;

public class ContactInfoHelper : IContactInfoHelper
{
    private static readonly TimeSpan ContactInfoCacheDuration = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _context;
    private readonly IMemoryCache _memoryCache;

    public ContactInfoHelper(AppDbContext context, IMemoryCache memoryCache)
    {
        _context = context;
        _memoryCache = memoryCache;
    }

    public async Task<ContactInfoSettings> GetContactInfoAsync()
    {
        var keys = new[]
        {
            "contact.phone",
            "contact.zalo",
            "contact.messenger",
            "contact.facebook",
            "contact.email",
            "contact.address",
            "contact.purchase_areas"
        };

        return await _memoryCache.GetOrCreateAsync(SiteCacheKeys.ContactInfo, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ContactInfoCacheDuration;

            var settings = await _context.SiteSettings
                .AsNoTracking()
                .Where(item => item.SettingKey != null && keys.Contains(item.SettingKey))
                .Select(item => new
                {
                    item.SettingKey,
                    item.SettingValue
                })
                .ToDictionaryAsync(item => item.SettingKey!, item => item.SettingValue ?? string.Empty);

            return new ContactInfoSettings
            {
                Phone = GetValue(settings, "contact.phone", "0974640626"),
                Zalo = NormalizeExternalLink(GetValue(settings, "contact.zalo", "zalo.me/0974640626")),
                Messenger = NormalizeExternalLink(GetValue(settings, "contact.messenger", "m.me/phelieupro")),
                Facebook = NormalizeExternalLink(GetValue(settings, "contact.facebook", "facebook.com/phelieupro")),
                Gmail = GetValue(settings, "contact.email", "contact@phelieupro.vn"),
                Address = GetValue(settings, "contact.address", "Quận 12, TP. Hồ Chí Minh"),
                PurchaseAreas = GetValue(settings, "contact.purchase_areas", string.Empty)
            };
        }) ?? new ContactInfoSettings
        {
            Phone = "0974640626",
            Zalo = "https://zalo.me/0974640626",
            Messenger = "https://m.me/phelieupro",
            Facebook = "https://facebook.com/phelieupro",
            Gmail = "contact@phelieupro.vn",
            Address = "Quận 12, TP. Hồ Chí Minh"
        };
    }

    private static string GetValue(IReadOnlyDictionary<string, string> settings, string key, string fallback)
    {
        return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static string NormalizeExternalLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "#";
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"https://{trimmed.TrimStart('/')}";
    }
}
