using MediatR;
using PayFlow.Application.Interfaces;
using PayFlow.Domain.Interfaces;
using PayFlow.Domain.ValueObjects;

namespace PayFlow.Application.Features.Payments.Commands.CancelPayment;

public class CancelPaymentCommandHandler : IRequestHandler<CancelPaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IOutboxPublisher _outboxPublisher;
    private readonly IUnitOfWork _unitOfWork;

    public CancelPaymentCommandHandler(
        IPaymentRepository repository,
        IOutboxPublisher outboxPublisher,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _outboxPublisher = outboxPublisher;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(CancelPaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(PaymentId.From(command.PaymentId), cancellationToken)
            ?? throw new KeyNotFoundException($"Payment {command.PaymentId} not found.");

        payment.Cancel();

        await _outboxPublisher.PublishAsync(payment.DomainEvents, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
    }
}
