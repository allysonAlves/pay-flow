using MediatR;
using PayFlow.Application.Contracts;
using PayFlow.Application.Interfaces;
using PayFlow.Domain.Interfaces;
using PayFlow.Domain.ValueObjects;

namespace PayFlow.Application.Features.Payments.Commands.ProcessPayment;

public class ProcessPaymentCommandHandler : IRequestHandler<ProcessPaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentGateway _gateway;
    private readonly IOutboxPublisher _outboxPublisher;
    private readonly IUnitOfWork _unitOfWork;

    public ProcessPaymentCommandHandler(
        IPaymentRepository repository,
        IPaymentGateway gateway,
        IOutboxPublisher outboxPublisher,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _gateway = gateway;
        _outboxPublisher = outboxPublisher;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ProcessPaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(PaymentId.From(command.PaymentId), cancellationToken)
            ?? throw new KeyNotFoundException($"Payment {command.PaymentId} not found.");

        payment.StartProcessing();

        var request = new GatewayRequest(payment.Id.Value, payment.Amount.Amount, payment.Amount.Currency);
        var response = await _gateway.ProcessAsync(request, cancellationToken);

        if (response.Success)
            payment.Approve();
        else
            payment.Fail(response.ErrorMessage ?? "Gateway error");

        await _outboxPublisher.PublishAsync(payment.DomainEvents, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
    }
}
