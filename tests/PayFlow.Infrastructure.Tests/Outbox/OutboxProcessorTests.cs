using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PayFlow.Application.IntegrationEvents;
using PayFlow.Application.Interfaces;
using PayFlow.Domain.Aggregates.Payments.Events;
using PayFlow.Infrastructure.Outbox;

namespace PayFlow.Infrastructure.Tests.Outbox;

public class OutboxProcessorTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static OutboxMessage BuildMessage<TEvent>(TEvent domainEvent) where TEvent : notnull =>
        new()
        {
            EventType = typeof(TEvent).Name,
            Payload = JsonSerializer.Serialize(domainEvent, typeof(TEvent))
        };

    private static OutboxProcessor CreateProcessor(
        IOutboxStore store,
        IMediator mediator,
        IIdempotencyService idempotency)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(mediator);
        services.AddSingleton(idempotency);
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var logger = NullLogger<OutboxProcessor>.Instance;
        var options = Options.Create(new OutboxOptions { BatchSize = 10, IntervalSeconds = 5 });

        return new OutboxProcessor(scopeFactory, logger, options);
    }

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatchAsync_WithPaymentCreatedMessage_PublishesIntegrationEvent()
    {
        var paymentId = Guid.NewGuid();
        var domainEvent = new PaymentCreated
        {
            PaymentId = paymentId,
            CustomerId = Guid.NewGuid(),
            MerchantId = Guid.NewGuid(),
            Amount = 100m,
            Currency = "BRL"
        };
        var message = BuildMessage(domainEvent);

        var store = new FakeOutboxStore([message]);
        var mediator = Substitute.For<IMediator>();
        var idempotency = Substitute.For<IIdempotencyService>();
        idempotency.HasBeenProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var processor = CreateProcessor(store, mediator, idempotency);
        await processor.ProcessBatchAsync(CancellationToken.None);

        await mediator.Received(1).Publish(
            Arg.Is<PaymentCreatedIntegrationEvent>(e => e.PaymentId == paymentId),
            Arg.Any<CancellationToken>());
        message.ProcessedAt.Should().NotBeNull();
        store.CommitCalled.Should().BeTrue();
        await idempotency.Received(1).MarkAsProcessedAsync(
            Arg.Is<string>(k => k.Contains(nameof(PaymentCreated))),
            TimeSpan.FromHours(24),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_WithApprovedMessage_PublishesApprovedIntegrationEvent()
    {
        var paymentId = Guid.NewGuid();
        var message = BuildMessage(new PaymentApproved { PaymentId = paymentId });

        var store = new FakeOutboxStore([message]);
        var mediator = Substitute.For<IMediator>();
        var idempotency = Substitute.For<IIdempotencyService>();
        idempotency.HasBeenProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await CreateProcessor(store, mediator, idempotency).ProcessBatchAsync(CancellationToken.None);

        await mediator.Received(1).Publish(
            Arg.Is<PaymentApprovedIntegrationEvent>(e => e.PaymentId == paymentId),
            Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Idempotency
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatchAsync_WhenAlreadyProcessedInRedis_SkipsPublishAndStampsProcessedAt()
    {
        var message = BuildMessage(new PaymentApproved { PaymentId = Guid.NewGuid() });

        var store = new FakeOutboxStore([message]);
        var mediator = Substitute.For<IMediator>();
        var idempotency = Substitute.For<IIdempotencyService>();
        idempotency.HasBeenProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        await CreateProcessor(store, mediator, idempotency).ProcessBatchAsync(CancellationToken.None);

        await mediator.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
        message.ProcessedAt.Should().NotBeNull();
        await idempotency.DidNotReceive().MarkAsProcessedAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Error handling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatchAsync_WhenPublishThrows_SetsErrorAndIncrementsRetryCount()
    {
        var message = BuildMessage(new PaymentApproved { PaymentId = Guid.NewGuid() });

        var store = new FakeOutboxStore([message]);
        var mediator = Substitute.For<IMediator>();
        var idempotency = Substitute.For<IIdempotencyService>();
        idempotency.HasBeenProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        mediator.Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("handler failure"));

        await CreateProcessor(store, mediator, idempotency).ProcessBatchAsync(CancellationToken.None);

        message.Error.Should().Be("handler failure");
        message.RetryCount.Should().Be(1);
        message.ProcessedAt.Should().BeNull();
        await idempotency.DidNotReceive().MarkAsProcessedAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenMessageReachesMaxRetries_DeadLetters()
    {
        var message = BuildMessage(new PaymentApproved { PaymentId = Guid.NewGuid() });
        message.RetryCount = 4; // one more failure will hit MaxRetries (5)

        var store = new FakeOutboxStore([message]);
        var mediator = Substitute.For<IMediator>();
        var idempotency = Substitute.For<IIdempotencyService>();
        idempotency.HasBeenProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        mediator.Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("permanent failure"));

        await CreateProcessor(store, mediator, idempotency).ProcessBatchAsync(CancellationToken.None);

        message.RetryCount.Should().Be(5);
        message.ProcessedAt.Should().NotBeNull("message should be dead-lettered after MaxRetries");
    }

    // -------------------------------------------------------------------------
    // Unknown event type
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessBatchAsync_WithUnknownEventType_MarksProcessedWithoutPublishing()
    {
        var message = new OutboxMessage { EventType = "SomeUnknownEvent", Payload = "{}" };

        var store = new FakeOutboxStore([message]);
        var mediator = Substitute.For<IMediator>();
        var idempotency = Substitute.For<IIdempotencyService>();
        idempotency.HasBeenProcessedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await CreateProcessor(store, mediator, idempotency).ProcessBatchAsync(CancellationToken.None);

        await mediator.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
        message.ProcessedAt.Should().NotBeNull();
    }
}

// -------------------------------------------------------------------------
// Test double
// -------------------------------------------------------------------------

internal sealed class FakeOutboxStore : IOutboxStore
{
    private readonly List<OutboxMessage> _messages;

    public bool CommitCalled { get; private set; }

    public FakeOutboxStore(List<OutboxMessage> messages) => _messages = messages;

    public Task<List<OutboxMessage>> LockNextBatchAsync(int batchSize, CancellationToken ct)
        => Task.FromResult(_messages.Take(batchSize).ToList());

    public Task CommitAsync(CancellationToken ct)
    {
        CommitCalled = true;
        return Task.CompletedTask;
    }
}
