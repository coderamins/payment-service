using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;

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
        => _context.PaymentTransaction.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<PaymentTransaction?> GetByOrderIdAsync(
        long orderId, CancellationToken cancellationToken = default)
        => _context.PaymentTransaction
            .Where(t => t.OrderId == orderId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task UpdateAsync(PaymentTransaction transaction, 
        CancellationToken cancellationToken = default)
    {
        _context.PaymentTransaction.Update(transaction);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
