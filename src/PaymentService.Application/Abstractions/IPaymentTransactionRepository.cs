using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Application.Abstractions;

public interface IPaymentTransactionRepository
{
    Task AddAsync(
        PaymentTransaction transaction, 
        CancellationToken cancellationToken = default);

    Task<List<PaymentTransaction>> GetActivePayments(
        long orderId,
        EPaymentSubscriber PaymentSubscriber,
        CancellationToken ct);

    Task<PaymentTransaction?> GetByCallbackKeyAsync(
    Guid key,
    CancellationToken ct);

    Task<PaymentTransaction?> GetByIdAsync(
        long id, 
        CancellationToken cancellationToken = default);

    Task<PaymentTransaction?> GetByOrderIdAsync(
        long orderId,
        EPaymentSubscriber paymentSubscriber,
        CancellationToken 
        cancellationToken = default);

    Task<List<PaymentTransaction>> GetStaleAsync(
        EPaymentStatus status, DateTime olderThanUtc, int take, CancellationToken ct);

    Task UpdateAsync(
        PaymentTransaction transaction, 
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);


}
