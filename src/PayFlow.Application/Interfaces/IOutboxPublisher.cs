using PayFlow.Domain.Events;

namespace PayFlow.Application.Interfaces;

public interface IOutboxPublisher
{
    Task PublishAsync(IReadOnlyList<DomainEvent> events, CancellationToken ct = default);
}
