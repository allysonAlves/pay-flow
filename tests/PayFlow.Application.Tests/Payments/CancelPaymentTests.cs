using FluentAssertions;
using NSubstitute;
using PayFlow.Application.Interfaces;
using PayFlow.Application.Features.Payments.Commands.CancelPayment;
using PayFlow.Domain.Aggregates.Payments;
using PayFlow.Domain.Interfaces;
using PayFlow.Domain.ValueObjects;

namespace PayFlow.Application.Tests.Payments;

public class CancelPaymentTests
{
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly IOutboxPublisher _outboxPublisher = Substitute.For<IOutboxPublisher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private CancelPaymentCommandHandler CreateHandler() =>
        new(_repository, _outboxPublisher, _unitOfWork);

    private static Payment BuildPendingPayment()
    {
        var payment = Payment.Create(
            CustomerId.From(Guid.NewGuid()),
            MerchantId.From(Guid.NewGuid()),
            Money.Of(100m, "BRL"));
        payment.ClearDomainEvents();
        return payment;
    }

    [Fact]
    public async Task Handle_ExistingPendingPayment_ShouldCancelAndCommit()
    {
        var payment = BuildPendingPayment();
        _repository.GetByIdAsync(Arg.Any<PaymentId>(), Arg.Any<CancellationToken>())
            .Returns(payment);

        await CreateHandler().Handle(new CancelPaymentCommand(payment.Id.Value), CancellationToken.None);

        payment.Status.Should().Be(PaymentStatus.Cancelled);
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ShouldThrowKeyNotFoundException()
    {
        _repository.GetByIdAsync(Arg.Any<PaymentId>(), Arg.Any<CancellationToken>())
            .Returns((Payment?)null);

        var act = async () =>
            await CreateHandler().Handle(new CancelPaymentCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
