namespace PayFlow.Application.Interfaces;

public interface ICustomerNotifier
{
    Task NotifyPaymentApprovedAsync(Guid paymentId, CancellationToken ct = default);
    Task NotifyPaymentFailedAsync(Guid paymentId, string reason, CancellationToken ct = default);
    Task NotifyPaymentCancelledAsync(Guid paymentId, CancellationToken ct = default);
}
