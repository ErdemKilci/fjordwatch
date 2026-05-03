namespace FjordWatch.Infrastructure;

/// <summary>
/// Translate a <c>redis://</c> URL into the key-value form
/// StackExchange.Redis expects. Supports an optional database number passed
/// as the URL path (<c>redis://host:6379/0</c>).
/// </summary>
public static class RedisUrlConverter
{
    public static string ToConfigurationString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("redis URL is empty", nameof(raw));
        }

        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            return raw;
        }

        var uri = new Uri(raw);
        var host = uri.Host;
        var port = uri.IsDefaultPort ? 6379 : uri.Port;

        string? defaultDatabase = null;
        if (!string.IsNullOrEmpty(uri.AbsolutePath))
        {
            var path = uri.AbsolutePath.TrimStart('/');
            if (!string.IsNullOrEmpty(path) && int.TryParse(path, out _))
            {
                defaultDatabase = path;
            }
        }

        var parts = new List<string> { $"{host}:{port}" };

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var userPass = uri.UserInfo.Split(':', 2);
            if (userPass.Length == 2)
            {
                parts.Add($"user={Uri.UnescapeDataString(userPass[0])}");
                parts.Add($"password={Uri.UnescapeDataString(userPass[1])}");
            }
            else
            {
                parts.Add($"password={Uri.UnescapeDataString(userPass[0])}");
            }
        }

        if (defaultDatabase is not null)
        {
            parts.Add($"defaultDatabase={defaultDatabase}");
        }

        return string.Join(',', parts);
    }
}
