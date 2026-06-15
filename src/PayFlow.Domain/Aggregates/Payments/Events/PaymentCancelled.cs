using PayFlow.Domain.Events;

namespace PayFlow.Domain.Aggregates.Payments.Events;

public sealed class PaymentCancelled : DomainEvent
{
    public Guid PaymentId { get; init; }
}