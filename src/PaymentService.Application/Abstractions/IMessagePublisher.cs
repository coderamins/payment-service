namespace PaymentService.Application.Abstractions;

public interface IMessagePublisher
{
    Task PublishAsync(
        string eventType,
        string payloadJson, 
        CancellationToken cancellationToken = default);
}