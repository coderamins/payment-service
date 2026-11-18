using PaymentService.Application.Common.Options;

namespace PaymentService.Application.Common;

public static class ReturnUrlValidator
{
    public static bool IsAllowed(string? returnUrl, PaymentCallbackOptions options)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || !Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
            return false;

        // javascript: و data: رد می‌شوند؛ http فقط برای لوکال
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp))
            return false;

        // https://allowed.com@evil.com
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;

        var host = uri.IdnHost;

        foreach (var entry in options.AllowedReturnHosts ?? Array.Empty<string>())
        {
            var allowed = entry?.Trim();
            if (string.IsNullOrEmpty(allowed)) continue;

            if (allowed.StartsWith("*.", StringComparison.Ordinal))
            {
                // *.example.com فقط زیردامنه‌ها
                if (host.EndsWith(allowed[1..], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}