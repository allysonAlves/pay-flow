using FluentAssertions;
using NSubstitute;
using PayFlow.Application.Interfaces;
using PayFlow.Application.Features.Payments.Commands.CreatePayment;
using PayFlow.Domain.Aggregates.Payments;
using PayFlow.Domain.Interfaces;

namespace PayFlow.Application.Tests.Payments;

public class CreatePaymentTests
{
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly IOutboxPublisher _outboxPublisher = Substitute.For<IOutboxPublisher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private CreatePaymentCommandHandler CreateHandler() =>
        new(_repository, _outboxPublisher, _unitOfWork);

    [Fact]
    public async Task Handle_ValidCommand_ShouldReturnPaymentId()
    {
        var command = new CreatePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), 100m, "BRL");

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ValidCommand_ShouldPersistPayment()
    {
        var command = new CreatePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), 100m, "BRL");

        await CreateHandler().Handle(command, CancellationToken.None);

        await _repository.Received(1).AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_ShouldPublishDomainEvents()
    {
        var command = new CreatePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), 100m, "BRL");

        await CreateHandler().Handle(command, CancellationToken.None);

        await _outboxPublisher.Received(1).PublishAsync(
            Arg.Is<IReadOnlyList<Domain.Events.DomainEvent>>(list => list.Count > 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_ShouldCommit()
    {
        var command = new CreatePaymentCommand(Guid.NewGuid(), Guid.NewGuid(), 100m, "BRL");

        await CreateHandler().Handle(command, CancellationToken.None);

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }
}
