namespace PayFlow.Application.Interfaces;

public interface IMerchantNotifier
{
    Task NotifyPaymentApprovedAsync(Guid paymentId, CancellationToken ct = default);
}
