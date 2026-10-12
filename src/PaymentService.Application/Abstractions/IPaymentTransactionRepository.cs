using PaymentService.Domain.Entities;

namespace PaymentService.Application.Abstractions;

public interface IPaymentTransactionRepository
{
    Task AddAsync(
        PaymentTransaction transaction, 
        CancellationToken cancellationToken = default);

    Task<PaymentTransaction?> GetByIdAsync(
        Guid id, 
        CancellationToken cancellationToken = default);

    Task<PaymentTransaction?> GetByOrderIdAsync(
        string orderId, CancellationToken 
        cancellationToken = default);

    Task UpdateAsync(
        PaymentTransaction transaction, 
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
