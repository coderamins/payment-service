using PaymentService.Domain.Entities;

namespace PaymentService.Application.Abstractions;

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(string idempotencyKey, string requestPath, CancellationToken cancellationToken = default);

    Task SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken = default);
}
