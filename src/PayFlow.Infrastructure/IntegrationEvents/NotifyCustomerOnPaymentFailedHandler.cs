using MediatR;
using PayFlow.Application.IntegrationEvents;
using PayFlow.Application.Interfaces;

namespace PayFlow.Infrastructure.IntegrationEvents;

public class NotifyCustomerOnPaymentFailedHandler : INotificationHandler<PaymentFailedIntegrationEvent>
{
    private readonly ICustomerNotifier _notifier;

    public NotifyCustomerOnPaymentFailedHandler(ICustomerNotifier notifier) => _notifier = notifier;

    public Task Handle(PaymentFailedIntegrationEvent notification, CancellationToken cancellationToken)
        => _notifier.NotifyPaymentFailedAsync(notification.PaymentId, notification.Reason, cancellationToken);
}
