using PayFlow.Domain.Events;

namespace PayFlow.Domain.Aggregates.Payments.Events;

public sealed class PaymentFailed : DomainEvent
{
    public Guid PaymentId { get; init; }
    public string Reason { get; init; } = default!;
}