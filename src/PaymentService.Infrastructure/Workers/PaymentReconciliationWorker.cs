using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Application.Events;
using PaymentService.Domain.Enums;

namespace PaymentService.Infrastructure.Workers;

public class PaymentReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CreatedTimout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan RedirectedTimout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan CallBackReceivedTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VerifiedWarnAfter= TimeSpan.FromMinutes(5);
    private static readonly TimeSpan VerifiedReverseAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ManualReviewAfter = TimeSpan.FromHours(24);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentReconciliationWorker> _logger;

    public PaymentReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch(Exception ex)
            {
                _logger.LogError(ex, "اجرای Reconciliation با خطا مواجه شد .");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPaymentTransactionRepository>();
        var gateways = scope.ServiceProvider.GetRequiredService<IPaymentGatewayFactory>();
        var outbox=scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        await ExpireAsync(repo, EPaymentStatus.Created, CreatedTimout, ct);
        await ExpireAsync(repo, EPaymentStatus.RedirectedToGateway, RedirectedTimout, ct);
        await ReconciliationCallBackReceivedAsync(repo, gateways, outbox, ct);
        await ReconciliationVerifiedAsync(repo,gateways,outbox,ct);
    }

    private async Task ReconciliationVerifiedAsync(
        IPaymentTransactionRepository repo, 
        IPaymentGatewayFactory gateways, 
        IOutboxWriter outbox, 
        CancellationToken ct)
    {
        var warn = await repo.GetStaleAsync(EPaymentStatus.Verified,
            DateTime.UtcNow - VerifiedWarnAfter, BatchSize, ct);
        foreach(var item in warn.Where(x=>(x.ModifiedAt ?? x.CreatedAt)>=DateTime.UtcNow-VerifiedReverseAfter)) {
            _logger
                .LogWarning("سفارش شماره {Id} بیشتر از 5 دقیقه در وضعیت تایید(Verified) مانده!", item.OrderId);
        }

        var stale = await repo.GetStaleAsync(EPaymentStatus.Verified,
            DateTime.UtcNow - VerifiedReverseAfter, BatchSize, ct);

        foreach (var item in stale)
        {
            try
            {
                item.Touch();
                await repo.UpdateAsync(item, ct);
                await repo.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                continue;
            }
            try
            {
                var rev =await gateways.Resolve(item.GatewayProviderId)
                    .ReverseAsync(item,ct);

                if(rev.IsSuccess)
                {
                    item.MarkReversed();
                    outbox.Add(
                        "PaymentFailed",
                        new PaymentFailedEvent
                        (
                            item.PaymentTransactionId,
                            item.OrderId,
                            item.AmountInRials,
                            item.GatewayProviderId.ToString(),
                            "Reconciliation",
                            "VerifiedNotSettledTimeout",
                            DateTime.UtcNow
                        ));

                    await repo.UpdateAsync(item, ct);
                    await repo.SaveChangesAsync(ct);

                    _logger.LogWarning(
                        "تراکنش {Id} در تسویه گیر کرده بود بنابرین برگشت داده شد.",
                        item.PaymentTransactionId);

                    continue;
                }

                _logger.LogError
                    ("Reconciliation برای تراکنش {Id} که در تسویه گیر کرده ناموفق بود",
                    item.PaymentTransactionId);
            }
            catch(Exception ex) when(ex is not OperationCanceledException)
            {
                _logger.LogError
                    ("Reconciliation برای تراکنش {Id} که در تسویه گیر کرده با خطا مواجه شد",
                    item.PaymentTransactionId);
            }
        }
    }

    private async Task ExpireAsync(
        IPaymentTransactionRepository repo,
        EPaymentStatus status,
        TimeSpan timeout, 
        CancellationToken ct)
    {
        var stale = await repo.GetStaleAsync(status, DateTime.UtcNow - timeout, BatchSize, ct);
        foreach(var tx in stale)
        {
            try
            {
                tx.MarkFailed("Expired");
                await repo.UpdateAsync(tx, ct);
                await repo.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
            }
        }
    }

    private async Task ReconciliationCallBackReceivedAsync(
        IPaymentTransactionRepository repo, 
        IPaymentGatewayFactory gateways, 
        IOutboxWriter outbox, 
        CancellationToken ct)
    {
        var stale=await repo.GetStaleAsync(
            EPaymentStatus.CallbackReceived,DateTime.UtcNow-CallBackReceivedTimeout,BatchSize, ct);

        foreach(var tx in stale)
        {
            try
            {
                tx.Touch();
                await repo.UpdateAsync(tx, ct);
                await repo.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                continue;
            }

            try
            {
                var gateway = gateways.Resolve(tx.GatewayProviderId);
                var rev = await gateway.ReverseAsync(tx, ct);

                if (rev.IsSuccess)
                {
                    tx.MarkReversed();
                    outbox.Add("PaymentFailed", new PaymentFailedEvent(
                        tx.PaymentTransactionId,
                        tx.OrderId, 
                        tx.AmountInRials,
                        tx.GatewayProviderId.ToString(),
                        "Reconciliation",
                        "CallbackReceivedTimeout", 
                        DateTime.UtcNow));

                    await repo.UpdateAsync(tx, ct);
                    await repo.SaveChangesAsync(ct);

                    _logger.LogWarning("تراکنش {Id} در CallbackReceived گیرکرده برگشت داده شد",
                        tx.PaymentTransactionId);

                    continue;
                }

                _logger.LogError("Reverse تراکنش {Id} در Reconciliation ناموفق بود :{Code} .",
                    tx.PaymentTransactionId, rev.ResultCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("Reverse تراکنش {Id} در Reconciliation خطا داد .",
                    tx.PaymentTransactionId);
            }

            // اگه بعد از 24 ساعت هنوز reverse نشده باید بررسی دستی صورت بگیره
            if(DateTime.UtcNow-tx.CreatedAt>ManualReviewAfter)
            {
                _logger.LogCritical("تراکنش {Id} نیاز به بررسی دستی دارد", tx.PaymentTransactionId);
                tx.MarkFailed("ManualReviewRequired");
                await repo.UpdateAsync(tx, ct);
                await repo.SaveChangesAsync(ct);
            }
        }
    }

}
