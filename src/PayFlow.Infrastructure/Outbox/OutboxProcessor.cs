using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayFlow.Application.Interfaces;
using PayFlow.Application.IntegrationEvents;
using PayFlow.Domain.Aggregates.Payments.Events;

namespace PayFlow.Infrastructure.Outbox;

public class OutboxProcessor : BackgroundService
{
    private const int MaxRetries = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly OutboxOptions _options;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessor> logger,
        IOptions<OutboxOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error in OutboxProcessor");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
        }
    }

    internal async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();

        var messages = await store.LockNextBatchAsync(_options.BatchSize, ct);
        var publishedKeys = new List<string>(messages.Count);

        foreach (var message in messages)
        {
            var idempotencyKey = $"outbox:{message.EventType}:{message.Id}";

            try
            {
                if (await idempotency.HasBeenProcessedAsync(idempotencyKey, ct))
                {
                    message.ProcessedAt = DateTime.UtcNow;
                    continue;
                }

                var notification = ToIntegrationEvent(message);

                if (notification is not null)
                    await mediator.Publish(notification, ct);
                else if (!IsKnownEventType(message.EventType))
                    _logger.LogWarning("Unknown OutboxMessage event type {EventType} — add a mapping in ToIntegrationEvent", message.EventType);

                message.ProcessedAt = DateTime.UtcNow;
                publishedKeys.Add(idempotencyKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox message {MessageId} of type {EventType} (attempt {Attempt})",
                    message.Id, message.EventType, message.RetryCount + 1);

                message.RetryCount++;
                message.Error = ex.Message;

                if (message.RetryCount >= MaxRetries)
                {
                    _logger.LogError("Outbox message {MessageId} exceeded {MaxRetries} retries — dead-lettered", message.Id, MaxRetries);
                    message.ProcessedAt = DateTime.UtcNow;
                }
            }
        }

        // Persist ProcessedAt before writing to Redis — if DB fails, Redis is not set and next cycle retries
        await store.CommitAsync(ct);

        foreach (var key in publishedKeys)
        {
            try
            {
                await idempotency.MarkAsProcessedAsync(key, TimeSpan.FromHours(24), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to mark idempotency key {Key} in Redis — next cycle will retry safely via DB guard", key);
            }
        }
    }

    private static INotification? ToIntegrationEvent(OutboxMessage message) =>
        message.EventType switch
        {
            nameof(PaymentCreated) => JsonSerializer.Deserialize<PaymentCreated>(message.Payload) is { } e
                ? new PaymentCreatedIntegrationEvent(e.PaymentId) : null,

            nameof(PaymentApproved) => JsonSerializer.Deserialize<PaymentApproved>(message.Payload) is { } e
                ? new PaymentApprovedIntegrationEvent(e.PaymentId) : null,

            nameof(PaymentFailed) => JsonSerializer.Deserialize<PaymentFailed>(message.Payload) is { } e
                ? new PaymentFailedIntegrationEvent(e.PaymentId, e.Reason) : null,

            nameof(PaymentCancelled) => JsonSerializer.Deserialize<PaymentCancelled>(message.Payload) is { } e
                ? new PaymentCancelledIntegrationEvent(e.PaymentId) : null,

            nameof(PaymentProcessingStarted) => null, // internal state transition — no downstream consumers

            _ => null
        };

    private static bool IsKnownEventType(string eventType) => eventType is
        nameof(PaymentCreated) or
        nameof(PaymentApproved) or
        nameof(PaymentFailed) or
        nameof(PaymentCancelled) or
        nameof(PaymentProcessingStarted);
}
