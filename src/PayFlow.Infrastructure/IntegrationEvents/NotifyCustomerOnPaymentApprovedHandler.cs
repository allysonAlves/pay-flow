using MediatR;
using PayFlow.Application.IntegrationEvents;
using PayFlow.Application.Interfaces;

namespace PayFlow.Infrastructure.IntegrationEvents;

public class NotifyCustomerOnPaymentApprovedHandler : INotificationHandler<PaymentApprovedIntegrationEvent>
{
    private readonly ICustomerNotifier _notifier;

    public NotifyCustomerOnPaymentApprovedHandler(ICustomerNotifier notifier) => _notifier = notifier;

    public Task Handle(PaymentApprovedIntegrationEvent notification, CancellationToken cancellationToken)
        => _notifier.NotifyPaymentApprovedAsync(notification.PaymentId, cancellationToken);
}
