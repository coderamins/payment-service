using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentService.Application.Abstractions;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Infrastructure.Messaging;

public class OutboxProcessorBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;
    private const int MaxRetryCount = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorBackgroundService> _logger;

    public OutboxProcessorBackgroundService(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "خطای غیرمنتظره در پردازش Outbox");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();

        var pendingMessages = await dbContext.OutboxMessage
            .Where(m => m.PublishedAtUtc == null && m.RetryCount < MaxRetryCount)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(stoppingToken);

        foreach (var message in pendingMessages)
        {
            try
            {
                await publisher.PublishAsync(message.EventType, message.PayloadJson, stoppingToken);
                message.MarkPublished();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "انتشار پیام {MessageId} از نوع {EventType} ناموفق بود", message.Id, message.EventType);
                message.MarkFailedAttempt(ex.Message);
            }
        }

        if (pendingMessages.Count > 0)
            await dbContext.SaveChangesAsync(stoppingToken);
    }
}