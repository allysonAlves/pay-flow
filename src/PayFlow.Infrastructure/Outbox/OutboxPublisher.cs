using System.Text.Json;
using PayFlow.Application.Interfaces;
using PayFlow.Domain.Events;
using PayFlow.Infrastructure.Persistence;

namespace PayFlow.Infrastructure.Outbox;

public class OutboxPublisher : IOutboxPublisher
{
    private readonly PaymentDbContext _context;

    public OutboxPublisher(PaymentDbContext context) => _context = context;

    public Task PublishAsync(IReadOnlyList<DomainEvent> events, CancellationToken ct = default)
    {
        foreach (var domainEvent in events)
        {
            var message = new OutboxMessage
            {
                EventType = domainEvent.GetType().Name,
                Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType())
            };
            _context.OutboxMessages.Add(message);
        }

        return Task.CompletedTask;
    }
}
