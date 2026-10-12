namespace PaymentService.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; private set; }

    public string EventType { get; private set; } = default!;

    public string PayloadJson { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? PublishedAtUtc { get; private set; }

    public int RetryCount { get; private set; }

    public string? LastError { get; private set; }

    private OutboxMessage() { } 

    public static OutboxMessage Create(string eventType, string payloadJson)
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            PayloadJson = payloadJson,
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };
    }

    public void MarkPublished()
    {
        PublishedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailedAttempt(string error)
    {
        RetryCount++;
        LastError = error;
    }
}