using System.Security.Cryptography;
using System.Text;

namespace MarketLens.Core.Domain;

public static class LocalFetchCachePolicy
{
    public static string BuildCacheKey(string source, string url, params string[] varyBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var normalizedSource = source.Trim().ToLowerInvariant();
        var normalizedUrl = NormalizeUrl(url);
        var vary = string.Join('\n', varyBy.Select(v => v.Trim()).Where(v => v.Length > 0));
        var material = string.IsNullOrEmpty(vary)
            ? normalizedUrl
            : $"{normalizedUrl}\n{vary}";

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return $"{normalizedSource}:{hash}";
    }

    public static DateTime CalculateExpiresAt(
        DateTime fetchedAt,
        bool success,
        TimeSpan successTtl,
        TimeSpan negativeTtl)
    {
        if (successTtl < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(successTtl));
        if (negativeTtl < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(negativeTtl));

        return fetchedAt.Add(success ? successTtl : negativeTtl);
    }

    public static bool IsFresh(DateTime expiresAt, DateTime utcNow)
        => expiresAt > utcNow;

    private static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            ? uri.AbsoluteUri
            : trimmed;
    }
}
