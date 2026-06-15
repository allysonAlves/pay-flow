using PayFlow.Domain.Aggregates.Payments.Events;
using PayFlow.Domain.Events;
using PayFlow.Domain.Exceptions;
using PayFlow.Domain.ValueObjects;

namespace PayFlow.Domain.Aggregates.Payments;

public sealed class Payment
{
    private readonly List<DomainEvent> _domainEvents = new();

    public PaymentId Id { get; private set; } = default!;
    public CustomerId CustomerId { get; private set; } = default!;
    public MerchantId MerchantId { get; private set; } = default!;
    public Money Amount { get; private set; } = default!;
    public PaymentStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private Payment() { } // EF Core precisa do construtor sem parâmetros

    public static Payment Create(CustomerId customerId, MerchantId merchantId, Money amount)
    {
        var payment = new Payment
        {
            Id = PaymentId.New(),
            CustomerId = customerId,
            MerchantId = merchantId,
            Amount = amount,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        payment._domainEvents.Add(new PaymentCreated
        {
            PaymentId = payment.Id.Value,
            CustomerId = customerId.Value,
            MerchantId = merchantId.Value,
            Amount = amount.Amount,
            Currency = amount.Currency
        });

        return payment;
    }

    public void StartProcessing()
    {
        if (Status != PaymentStatus.Pending)
            throw new DomainException($"Cannot start processing a payment in status {Status}.");

        Status = PaymentStatus.Processing;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new PaymentProcessingStarted { PaymentId = Id.Value });
    }

    public void Approve()
    {
        if (Status != PaymentStatus.Processing)
            throw new DomainException($"Cannot approve a payment in status {Status}.");

        Status = PaymentStatus.Approved;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new PaymentApproved { PaymentId = Id.Value });
    }

    public void Fail(string reason)
    {
        if (Status != PaymentStatus.Processing)
            throw new DomainException($"Cannot fail a payment in status {Status}.");

        Status = PaymentStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new PaymentFailed { PaymentId = Id.Value, Reason = reason });
    }

    public void Cancel()
    {
        if (Status != PaymentStatus.Pending && Status != PaymentStatus.Processing)
            throw new DomainException($"Cannot cancel a payment in status {Status}.");

        Status = PaymentStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
        _domainEvents.Add(new PaymentCancelled { PaymentId = Id.Value });
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}