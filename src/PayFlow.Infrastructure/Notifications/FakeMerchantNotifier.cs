using Microsoft.Extensions.Logging;
using PayFlow.Application.Interfaces;

namespace PayFlow.Infrastructure.Notifications;

public class FakeMerchantNotifier : IMerchantNotifier
{
    private readonly ILogger<FakeMerchantNotifier> _logger;

    public FakeMerchantNotifier(ILogger<FakeMerchantNotifier> logger) => _logger = logger;

    public Task NotifyPaymentApprovedAsync(Guid paymentId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[MerchantNotifier] Payment {PaymentId} approved — dispatching webhook to merchant", paymentId);
        return Task.CompletedTask;
    }
}
