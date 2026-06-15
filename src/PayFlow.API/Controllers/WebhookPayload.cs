namespace PayFlow.API.Controllers;

public record WebhookPayload(Guid PaymentId, bool Success, string? TransactionId, string? ErrorMessage);
