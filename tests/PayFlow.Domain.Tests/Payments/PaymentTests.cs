using FluentAssertions;
using PayFlow.Domain.Aggregates.Payments;
using PayFlow.Domain.Aggregates.Payments.Events;
using PayFlow.Domain.Exceptions;
using PayFlow.Domain.ValueObjects;

namespace PayFlow.Domain.Tests.Payments;

public class PaymentTests
{
    private static Payment CreatePayment(decimal amount = 100m) =>
        Payment.Create(
            CustomerId.From(Guid.NewGuid()),
            MerchantId.From(Guid.NewGuid()),
            Money.Of(amount, "BRL"));

    [Fact]
    public void Create_ShouldInitializeWithPendingStatus()
    {
        var payment = CreatePayment();

        payment.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public void Create_ShouldRaisePaymentCreatedEvent()
    {
        var payment = CreatePayment();

        payment.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<PaymentCreated>();
    }

    [Fact]
    public void StartProcessing_FromPending_ShouldTransitionToProcessing()
    {
        var payment = CreatePayment();

        payment.StartProcessing();

        payment.Status.Should().Be(PaymentStatus.Processing);
    }

    [Fact]
    public void StartProcessing_FromApproved_ShouldThrowDomainException()
    {
        var payment = CreatePayment();
        payment.StartProcessing();
        payment.Approve();

        var act = () => payment.StartProcessing();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Approve_FromProcessing_ShouldTransitionToApproved()
    {
        var payment = CreatePayment();
        payment.StartProcessing();

        payment.Approve();

        payment.Status.Should().Be(PaymentStatus.Approved);
        payment.DomainEvents.Should().ContainSingle(e => e is PaymentApproved);
    }

    [Fact]
    public void Approve_FromPending_ShouldThrowDomainException()
    {
        var payment = CreatePayment();

        var act = () => payment.Approve();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Fail_FromProcessing_ShouldTransitionToFailed()
    {
        var payment = CreatePayment();
        payment.StartProcessing();

        payment.Fail("Insufficient funds");

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.DomainEvents.OfType<PaymentFailed>().Single().Reason.Should().Be("Insufficient funds");
    }

    [Fact]
    public void Cancel_FromPending_ShouldTransitionToCancelled()
    {
        var payment = CreatePayment();

        payment.Cancel();

        payment.Status.Should().Be(PaymentStatus.Cancelled);
        payment.DomainEvents.Should().ContainSingle(e => e is PaymentCancelled);
    }

    [Fact]
    public void Cancel_FromProcessing_ShouldTransitionToCancelled()
    {
        var payment = CreatePayment();
        payment.StartProcessing();

        payment.Cancel();

        payment.Status.Should().Be(PaymentStatus.Cancelled);
    }

    [Fact]
    public void Cancel_FromApproved_ShouldThrowDomainException()
    {
        var payment = CreatePayment();
        payment.StartProcessing();
        payment.Approve();

        var act = () => payment.Cancel();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Money_WithZeroAmount_ShouldThrow()
    {
        var act = () => Money.Of(0, "BRL");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Money_WithNegativeAmount_ShouldThrow()
    {
        var act = () => Money.Of(-1, "BRL");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PaymentId_WithEmptyGuid_ShouldThrow()
    {
        var act = () => PaymentId.From(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
