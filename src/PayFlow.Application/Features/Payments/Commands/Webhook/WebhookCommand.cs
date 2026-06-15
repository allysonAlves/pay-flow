using MediatR;

namespace PayFlow.Application.Features.Payments.Commands.Webhook;

public record WebhookCommand(Guid PaymentId, bool Success, string? ErrorMessage) : IRequest;
