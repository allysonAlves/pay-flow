namespace PayFlow.Domain.Aggregates.Payments;

public enum PaymentStatus
{
    Pending,
    Processing,
    Approved,
    Failed,
    Cancelled
}