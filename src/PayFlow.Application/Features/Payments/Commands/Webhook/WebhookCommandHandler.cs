using MediatR;
using PayFlow.Application.Interfaces;
using PayFlow.Domain.Aggregates.Payments;
using PayFlow.Domain.Interfaces;
using PayFlow.Domain.ValueObjects;

namespace PayFlow.Application.Features.Payments.Commands.Webhook;

public class WebhookCommandHandler : IRequestHandler<WebhookCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IOutboxPublisher _outboxPublisher;
    private readonly IUnitOfWork _unitOfWork;

    public WebhookCommandHandler(
        IPaymentRepository repository,
        IOutboxPublisher outboxPublisher,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _outboxPublisher = outboxPublisher;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(WebhookCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(PaymentId.From(command.PaymentId), cancellationToken)
            ?? throw new KeyNotFoundException($"Payment {command.PaymentId} not found.");

        // Duplicate webhook for an already-terminal payment — treat as idempotent
        if (payment.Status is PaymentStatus.Approved or PaymentStatus.Failed or PaymentStatus.Cancelled)
            return;

        if (payment.Status == PaymentStatus.Pending)
            payment.StartProcessing();

        if (command.Success)
            payment.Approve();
        else
            payment.Fail(command.ErrorMessage ?? "Gateway callback failure");

        await _outboxPublisher.PublishAsync(payment.DomainEvents, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
    }
}
