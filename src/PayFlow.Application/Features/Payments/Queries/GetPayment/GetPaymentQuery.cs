using MediatR;
using PayFlow.Application.Features.Payments.DTOs;

namespace PayFlow.Application.Features.Payments.Queries.GetPayment;

public record GetPaymentQuery(Guid PaymentId) : IRequest<PaymentDetailDto>;
