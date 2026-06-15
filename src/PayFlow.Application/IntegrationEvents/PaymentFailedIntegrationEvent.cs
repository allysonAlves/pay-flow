using MediatR;

namespace PayFlow.Application.IntegrationEvents;

public record PaymentFailedIntegrationEvent(Guid PaymentId, string Reason) : INotification;
