using PayFlow.Domain.Events;

namespace PayFlow.Domain.Aggregates.Payments.Events;

public sealed class PaymentCreated : DomainEvent
{
    public Guid PaymentId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid MerchantId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = default!;
}