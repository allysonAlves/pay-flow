using FluentValidation;

namespace PayFlow.Application.Features.Payments.Commands.Webhook;

public class WebhookCommandValidator : AbstractValidator<WebhookCommand>
{
    public WebhookCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.ErrorMessage)
            .NotEmpty()
            .When(x => !x.Success)
            .WithMessage("ErrorMessage is required when Success is false.");
    }
}
