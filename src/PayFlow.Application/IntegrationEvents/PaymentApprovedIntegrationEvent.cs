using MediatR;

namespace PayFlow.Application.IntegrationEvents;

public record PaymentApprovedIntegrationEvent(Guid PaymentId) : INotification;
