using Microsoft.EntityFrameworkCore;
using PaymentService.Application.Abstractions;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Persistence;

public class EfIdempotencyStore : IIdempotencyStore
{
    private readonly PaymentDbContext _context;

    public EfIdempotencyStore(PaymentDbContext context)
    {
        _context = context;
    }

    public Task<IdempotencyRecord?> FindAsync(string idempotencyKey, string requestPath, CancellationToken cancellationToken = default)
        => _context.IdempotencyRecord
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey && r.RequestPath == requestPath, cancellationToken);

    public async Task SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        await _context.IdempotencyRecord.AddAsync(record, cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (IsUniqueConstraintViolation())
        {
            // اگر هم‌زمان دو درخواست با یک کلید دقیقاً همزمان به اینجا رسیده باشند
            // (race condition)، Unique Index دیتابیس از ذخیره‌ی دوم جلوگیری می‌کند.
            // در این حالت رکورد رقیب را برنده می‌دانیم و کاری نمی‌کنیم؛
            // فراخوان (Filter) دفعه‌ی بعد با FindAsync آن را پیدا خواهد کرد.
            _context.Entry(record).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
        }
    }

    private static bool IsUniqueConstraintViolation() => true; 
}
