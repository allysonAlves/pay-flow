using FluentValidation;

namespace PayFlow.Application.Features.Payments.Commands.CancelPayment;

public class CancelPaymentCommandValidator : AbstractValidator<CancelPaymentCommand>
{
    public CancelPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}
