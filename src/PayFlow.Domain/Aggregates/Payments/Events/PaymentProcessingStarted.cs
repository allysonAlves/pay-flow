using PayFlow.Domain.Events;

namespace PayFlow.Domain.Aggregates.Payments.Events;

public sealed class PaymentProcessingStarted : DomainEvent
{
    public Guid PaymentId { get; init; }
}