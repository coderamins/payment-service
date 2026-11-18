namespace PaymentService.Infrastructure.Gateways.Pasargad;

/// <summary>
/// کش توکن Bearer پاسارگاد. باید Singleton باشد تا بین درخواست‌ها مشترک بماند
/// (در غیر این صورت برای هر پرداخت یک getToken جدید زده می‌شود).
/// </summary>
public sealed class PasargadTokenCache
{
    private sealed record Entry(string Token, DateTime ExpiresAtUtc);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Entry? _entry;

    public async Task<string> GetOrCreateAsync(
        Func<CancellationToken, Task<(string Token, DateTime ExpiresAtUtc)>> factory,
        TimeSpan refreshMargin,
        CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _entry);
        if (IsUsable(current, refreshMargin))
            return current!.Token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // شاید درخواست دیگری هم‌زمان توکن را تازه کرده باشد
            current = Volatile.Read(ref _entry);
            if (IsUsable(current, refreshMargin))
                return current!.Token;

            var (token, expiresAtUtc) = await factory(cancellationToken);
            Volatile.Write(ref _entry, new Entry(token, expiresAtUtc));
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// بعد از 401 صدا زده می‌شود. فقط اگر توکن ذخیره‌شده همان توکن ردشده باشد پاک می‌شود
    /// تا توکن تازه‌ای که درخواست دیگری گرفته است دور ریخته نشود.
    /// </summary>
    public async Task InvalidateAsync(string rejectedToken, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_entry is not null && _entry.Token == rejectedToken)
                _entry = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool IsUsable(Entry? entry, TimeSpan margin)
        => entry is not null && entry.ExpiresAtUtc - margin > DateTime.UtcNow;
}
