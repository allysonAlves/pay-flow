using Microsoft.Extensions.Logging;
using PayFlow.Application.Interfaces;

namespace PayFlow.Infrastructure.Notifications;

public class FakeCustomerNotifier : ICustomerNotifier
{
    private readonly ILogger<FakeCustomerNotifier> _logger;

    public FakeCustomerNotifier(ILogger<FakeCustomerNotifier> logger) => _logger = logger;

    public Task NotifyPaymentApprovedAsync(Guid paymentId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[CustomerNotifier] Payment {PaymentId} approved — sending confirmation email/push to customer", paymentId);
        return Task.CompletedTask;
    }

    public Task NotifyPaymentFailedAsync(Guid paymentId, string reason, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[CustomerNotifier] Payment {PaymentId} failed ({Reason}) — notifying customer", paymentId, reason);
        return Task.CompletedTask;
    }

    public Task NotifyPaymentCancelledAsync(Guid paymentId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[CustomerNotifier] Payment {PaymentId} cancelled — notifying customer", paymentId);
        return Task.CompletedTask;
    }
}
