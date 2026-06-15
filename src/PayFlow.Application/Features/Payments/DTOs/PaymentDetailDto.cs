namespace PayFlow.Application.Features.Payments.DTOs;

public record PaymentDetailDto(
    Guid Id,
    Guid CustomerId,
    Guid MerchantId,
    decimal Amount,
    string Currency,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt
);