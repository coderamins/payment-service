using PaymentService.Domain.Entities;

namespace PaymentService.Application.Abstractions;

public interface IPaymentTransactionRepository
{
    Task AddAsync(
        PaymentTransaction transaction, 
        CancellationToken cancellationToken = default);

    Task<PaymentTransaction?> GetByIdAsync(
        long id, 
        CancellationToken cancellationToken = default);

    Task<PaymentTransaction?> GetByOrderIdAsync(
        long orderId, CancellationToken 
        cancellationToken = default);

    Task UpdateAsync(
        PaymentTransaction transaction, 
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
