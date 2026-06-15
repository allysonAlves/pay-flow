namespace PayFlow.Application.Features.Payments.DTOs;

public record PaymentSummaryDto(
    Guid Id,
    decimal Amount,
    string Currency,
    string Status,
    DateTime CreatedAt
);