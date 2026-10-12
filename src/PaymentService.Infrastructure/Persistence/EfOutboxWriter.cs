using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;
using System.Text.Json;

namespace PaymentService.Infrastructure.Persistence;

public class EfOutboxWriter : IOutboxWriter
{
    private readonly PaymentDbContext _context;

    public EfOutboxWriter(PaymentDbContext context)
    {
        _context = context;
    }

    public void Add(string eventType, object payload)
    {
        var payloadJson = JsonSerializer.Serialize(payload);
        var message = OutboxMessage.Create(eventType, payloadJson);

        _context.OutboxMessage.Add(message);
    }
}