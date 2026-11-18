using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Persistence;

public class PaymentDbContext : DbContext
{
    public DbSet<PaymentTransaction> PaymentTransaction => Set<PaymentTransaction>();
    public DbSet<IdempotencyRecord> IdempotencyRecord => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessage => Set<OutboxMessage>();

    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("IdempotencyRecord");
            entity.HasKey(r => r.IdempotencyRecordId);

            entity.Property(r => r.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(r => r.RequestPath).HasMaxLength(256).IsRequired();
            entity.Property(r => r.RequestBodyHash).HasMaxLength(64).IsRequired();
            entity.Property(r => r.ResponseBodyJson).HasMaxLength(4000).IsRequired();

            // ترکیب کلید + مسیر باید یکتا باشد تا هم‌زمانی دو درخواست با یک کلید
            // با خطای Unique Constraint در دیتابیس مدیریت شود (نه فقط در کد سرویس)
            entity.HasIndex(r => new { r.IdempotencyKey, r.RequestPath }).IsUnique();
        });

        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.ToTable("PaymentTransaction");
            entity.HasKey(t => t.PaymentTransactionId);
            entity.Property(t => t.PaymentTransactionId).UseIdentityColumn(seed: 100_000_000, increment: 1);

            entity.HasIndex(t => t.CallbackKey);
            entity.Property(t => t.OrderId).HasMaxLength(64).IsRequired();
            entity.Property(t => t.PaymentSubscriberId).HasColumnType("tinyint").IsRequired();
            entity.Property(t => t.GatewayRefId).HasMaxLength(128);
            entity.Property(t => t.GatewaySaleReferenceId).HasMaxLength(128);
            entity.Property(t => t.MaskedCardNumber).HasMaxLength(32);
            entity.Property(t => t.GatewayResultCode).HasMaxLength(16);
            entity.Property(t => t.FailureReason).HasMaxLength(512);
            entity.Property(t => t.ReturnUrl).HasMaxLength(2048).IsRequired();

            entity.Property(t => t.SplitsJson).HasMaxLength(2000);

            entity.Property(t => t.AmountInRials).HasColumnType("decimal(18,2)");

            entity.Property(t => t.GatewayProviderId).HasColumnType("tinyint").IsRequired();
            entity.Property(t => t.PaymentStatusId).HasColumnType("tinyint").IsRequired();

            entity.HasIndex(t => t.OrderId);
            entity.HasIndex(t => t.GatewayRefId);

            entity.Property(t => t.RowVersion).IsRowVersion();

            entity.HasIndex(t => new { t.PaymentSubscriberId, t.OrderId })
            .IsUnique()
            .HasDatabaseName("UX_PaymentTransaction_ActivePerOrder")
            .HasFilter("[PaymentStatusId] IN (0, 1, 2, 3, 4)"); 
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessage");
            entity.HasKey(x => x.OutboxMessageId);

            entity.Property(m => m.EventType).HasMaxLength(128).IsRequired();
            entity.Property(m=>m.LastError).HasMaxLength(1024);

            entity.HasIndex(x => x.CreatedAt);
        });


        modelBuilder.HasDefaultSchema("pay");
    }
}
