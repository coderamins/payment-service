namespace PaymentService.Domain.Entities;

public class OutboxMessage
{
    public Guid OutboxMessageId { get; private set; }

    public string EventType { get; private set; } = default!;

    public string PayloadJson { get; private set; } = default!;

    public DateTime CreatedAt { get; private set; }

    public DateTime? PublishedAt { get; private set; }

    public int RetryCount { get; private set; }

    public string? LastError { get; private set; }

    private OutboxMessage() { } 

    public static OutboxMessage Create(string eventType, string payloadJson)
    {
        return new OutboxMessage
        {
            OutboxMessageId = Guid.NewGuid(),
            EventType = eventType,
            PayloadJson = payloadJson,
            CreatedAt = DateTime.UtcNow,
            RetryCount = 0
        };
    }

    public void MarkPublished()
    {
        PublishedAt = DateTime.UtcNow;
    }

    public void MarkFailedAttempt(string error)
    {
        RetryCount++;
        LastError = error;
    }
}