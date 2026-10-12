namespace PaymentService.Application.Abstractions;

public interface IOutboxWriter
{
    void Add(string eventType, object payload);
}
