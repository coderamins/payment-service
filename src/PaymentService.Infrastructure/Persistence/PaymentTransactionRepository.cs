using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Common.Exceptions;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Persistence;

public class PaymentTransactionRepository : IPaymentTransactionRepository
{
    private readonly PaymentDbContext _context;

    public PaymentTransactionRepository(PaymentDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(
        PaymentTransaction transaction,
        CancellationToken cancellationToken = default)
            => await _context.PaymentTransaction.AddAsync(transaction, cancellationToken);

    public Task<PaymentTransaction?> GetByIdAsync(
        long id, CancellationToken cancellationToken = default)
        => _context.PaymentTransaction.FirstOrDefaultAsync(t => t.PaymentTransactionId == id, cancellationToken);

    public Task<PaymentTransaction?> GetByOrderIdAsync(
        long orderId,
        EPaymentSubscriber paymentSubscriber,
        CancellationToken cancellationToken = default)
        => _context.PaymentTransaction
            .Where(t => t.OrderId == orderId && t.PaymentSubscriberId == paymentSubscriber)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<PaymentTransaction>> GetActivePayments(
        long orderId, EPaymentSubscriber paymentSubscriber,
        CancellationToken ct) => _context.PaymentTransaction
        .Where(t => t.OrderId == orderId && t.PaymentSubscriberId == paymentSubscriber &&
               (t.PaymentStatusId == EPaymentStatus.Created ||
                t.PaymentStatusId == EPaymentStatus.RedirectedToGateway ||
                t.PaymentStatusId == EPaymentStatus.CallbackReceived ||
                t.PaymentStatusId == EPaymentStatus.Verified ||
                t.PaymentStatusId == EPaymentStatus.Settled))
        .ToListAsync(ct);

    public Task<List<PaymentTransaction>> GetStaleAsync(
        EPaymentStatus status, DateTime olderThanUtc, int take, CancellationToken ct) =>
      _context.PaymentTransaction
        .Where(t => t.PaymentStatusId == status &&
                   (t.ModifiedAt ?? t.CreatedAt) < olderThanUtc)
        .OrderBy(t => t.CreatedAt)
        .Take(take)
        .ToListAsync(ct);

    public Task UpdateAsync(PaymentTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        _context.PaymentTransaction.Update(transaction);
        return Task.CompletedTask;
    }

    public Task<PaymentTransaction?> GetByCallbackKeyAsync(Guid key, CancellationToken ct) =>
    key == Guid.Empty
        ? Task.FromResult<PaymentTransaction?>(null)
        : _context.PaymentTransaction.FirstOrDefaultAsync(t => t.CallbackKey == key, ct);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("برای این سفارش پرداخت فعالی وجود دارد.");
        }
    }

}
