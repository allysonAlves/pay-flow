using MediatR;
using PayFlow.Application.Interfaces;
using PayFlow.Domain.Aggregates.Payments;
using PayFlow.Domain.Interfaces;
using PayFlow.Domain.ValueObjects;

namespace PayFlow.Application.Features.Payments.Commands.CreatePayment;

public class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, Guid>
{
    private readonly IPaymentRepository _repository;
    private readonly IOutboxPublisher _outboxPublisher;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePaymentCommandHandler(
        IPaymentRepository repository,
        IOutboxPublisher outboxPublisher,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _outboxPublisher = outboxPublisher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreatePaymentCommand command, CancellationToken ct)
    {
        var payment = Payment.Create(
            CustomerId.From(command.CustomerId),
            MerchantId.From(command.MerchantId),
            Money.Of(command.Amount, command.Currency)
        );

        await _repository.AddAsync(payment, ct);
        await _outboxPublisher.PublishAsync(payment.DomainEvents, ct);
        await _unitOfWork.CommitAsync(ct);  // persiste Payment + OutboxMessages na mesma transação

        return payment.Id.Value;
    }
}