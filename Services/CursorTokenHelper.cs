using System.Text;
using System.Text.Json;

namespace WebThuMuaPheLieu.Services;

public static class CursorTokenHelper
{
    public static string Encode<T>(T payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryDecode<T>(string? token, out T? payload) where T : class
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(token));
            payload = JsonSerializer.Deserialize<T>(json);
            return payload is not null;
        }
        catch
        {
            payload = null;
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string token)
    {
        var base64 = token.Replace('-', '+').Replace('_', '/');

        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        return Convert.FromBase64String(base64);
    }
}