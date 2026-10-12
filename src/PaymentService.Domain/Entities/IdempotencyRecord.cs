namespace PaymentService.Domain.Entities;

/// <summary>
/// رکورد نگهداری نتیجه‌ی یک درخواست بر اساس کلید Idempotency-Key.
/// </summary>
public class IdempotencyRecord
{
    public Guid Id { get; private set; }

    public string IdempotencyKey { get; private set; } = default!;

    public string RequestPath { get; private set; } = default!;

    public string RequestBodyHash { get; private set; } = default!;
    public int ResponseStatusCode { get; private set; }
    public string ResponseBodyJson { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }

    private IdempotencyRecord() { } // برای EF Core

    public static IdempotencyRecord Create(
        string idempotencyKey, string requestPath, string requestBodyHash,
        int responseStatusCode, string responseBodyJson)
    {
        return new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            RequestPath = requestPath,
            RequestBodyHash = requestBodyHash,
            ResponseStatusCode = responseStatusCode,
            ResponseBodyJson = responseBodyJson,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
