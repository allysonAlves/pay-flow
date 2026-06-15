using MediatR;
using PayFlow.Application.IntegrationEvents;
using PayFlow.Application.Interfaces;

namespace PayFlow.Infrastructure.IntegrationEvents;

public class NotifyCustomerOnPaymentCancelledHandler : INotificationHandler<PaymentCancelledIntegrationEvent>
{
    private readonly ICustomerNotifier _notifier;

    public NotifyCustomerOnPaymentCancelledHandler(ICustomerNotifier notifier) => _notifier = notifier;

    public Task Handle(PaymentCancelledIntegrationEvent notification, CancellationToken cancellationToken)
        => _notifier.NotifyPaymentCancelledAsync(notification.PaymentId, cancellationToken);
}
