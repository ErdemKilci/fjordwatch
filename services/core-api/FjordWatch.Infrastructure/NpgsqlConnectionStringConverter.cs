using Npgsql;

namespace FjordWatch.Infrastructure;

/// <summary>
/// Convert <c>DATABASE_URL</c> values in the standard
/// <c>postgres://user:pass@host:port/db</c> form into the key-value
/// connection-string format Npgsql expects. Other services in this repo
/// (Rust ais-ingestion, Python anomaly-detection) consume the URL form
/// directly; centralizing the conversion here keeps the operator-facing env
/// var consistent across languages.
/// </summary>
public static class NpgsqlConnectionStringConverter
{
    public static string ToKeyValue(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("connection string is empty", nameof(raw));
        }

        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            // Already in key=value form.
            return raw;
        }

        var uri = new Uri(raw);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
        };

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length == 2)
            {
                builder.Password = Uri.UnescapeDataString(parts[1]);
            }
        }

        var query = uri.Query.TrimStart('?');
        if (!string.IsNullOrEmpty(query))
        {
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2)
                {
                    builder[kv[0]] = Uri.UnescapeDataString(kv[1]);
                }
            }
        }

        return builder.ConnectionString;
    }
}
