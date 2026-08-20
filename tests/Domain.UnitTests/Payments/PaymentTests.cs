using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;

namespace ErpApp.Domain.UnitTests.Payments;

public class PaymentTests
{
    private static Payment CreatePayment(decimal amount = 100m) =>
        Payment.Create(Guid.NewGuid(), Guid.NewGuid(), PaymentDirection.Received, DateOnly.FromDateTime(DateTime.UtcNow), null, Guid.NewGuid(), amount, null);

    [Fact]
    public void Approve_succeeds_with_zero_allocations()
    {
        var payment = CreatePayment();

        payment.Approve(Guid.NewGuid(), "PAY-0001");

        Assert.Equal(PaymentStatus.Approved, payment.Status);
    }

    [Fact]
    public void Approve_succeeds_with_partial_allocation()
    {
        var payment = CreatePayment(100m);
        payment.AddAllocation(DocumentType.Invoice, Guid.NewGuid(), 40m);

        payment.Approve(Guid.NewGuid(), "PAY-0001");

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(40m, payment.Allocations.Sum(x => x.Amount));
    }

    [Fact]
    public void Approve_succeeds_with_full_allocation()
    {
        var payment = CreatePayment(100m);
        payment.AddAllocation(DocumentType.Invoice, Guid.NewGuid(), 100m);

        payment.Approve(Guid.NewGuid(), "PAY-0001");

        Assert.Equal(PaymentStatus.Approved, payment.Status);
    }

    [Fact]
    public void Approve_throws_when_allocations_exceed_amount()
    {
        var payment = CreatePayment(100m);
        payment.AddAllocation(DocumentType.Invoice, Guid.NewGuid(), 60m);
        payment.AddAllocation(DocumentType.Invoice, Guid.NewGuid(), 60m);

        var ex = Assert.Throws<InvalidOperationException>(() => payment.Approve(Guid.NewGuid(), "PAY-0001"));
        Assert.Contains("cannot exceed", ex.Message);
    }

    [Fact]
    public void Create_throws_when_amount_is_not_positive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Payment.Create(Guid.NewGuid(), Guid.NewGuid(), PaymentDirection.Received, DateOnly.FromDateTime(DateTime.UtcNow), null, Guid.NewGuid(), 0m, null));
    }

    [Fact]
    public void AddAllocation_throws_when_amount_is_not_positive()
    {
        var payment = CreatePayment();

        Assert.Throws<InvalidOperationException>(() => payment.AddAllocation(DocumentType.Invoice, Guid.NewGuid(), 0m));
    }

    [Fact]
    public void AllocateFurther_applies_more_of_an_approved_payments_remaining_balance()
    {
        var payment = CreatePayment(100m);
        payment.AddAllocation(DocumentType.Invoice, Guid.NewGuid(), 40m);
        payment.Approve(Guid.NewGuid(), "PAY-0001");

        payment.AllocateFurther(DocumentType.Invoice, Guid.NewGuid(), 60m);

        Assert.Equal(100m, payment.Allocations.Sum(x => x.Amount));
    }

    [Fact]
    public void AllocateFurther_throws_when_it_would_exceed_amount()
    {
        var payment = CreatePayment(100m);
        payment.AddAllocation(DocumentType.Invoice, Guid.NewGuid(), 40m);
        payment.Approve(Guid.NewGuid(), "PAY-0001");

        Assert.Throws<InvalidOperationException>(() => payment.AllocateFurther(DocumentType.Invoice, Guid.NewGuid(), 61m));
    }

    [Fact]
    public void AllocateFurther_throws_when_payment_is_not_approved()
    {
        var payment = CreatePayment(100m);

        Assert.Throws<InvalidOperationException>(() => payment.AllocateFurther(DocumentType.Invoice, Guid.NewGuid(), 10m));
    }

    [Fact]
    public void AddAllocation_creates_a_payment_sourced_allocation()
    {
        var payment = CreatePayment(100m);

        payment.AddAllocation(DocumentType.Invoice, Guid.NewGuid(), 40m);

        Assert.Equal(DocumentType.Payment, payment.Allocations[0].SourceType);
        Assert.Equal(payment.Id, payment.Allocations[0].SourceId);
    }

    [Fact]
    public void AttachAllocations_replaces_the_in_memory_collection()
    {
        var payment = CreatePayment(100m);
        payment.AddAllocation(DocumentType.Invoice, Guid.NewGuid(), 40m);

        var loaded = new[] { PaymentAllocation.Create(DocumentType.Payment, payment.Id, DocumentType.Invoice, Guid.NewGuid(), 25m) };
        payment.AttachAllocations(loaded);

        Assert.Equal(25m, payment.Allocations.Sum(x => x.Amount));
    }

    [Fact]
    public void Void_releases_allocations_implicitly_by_flipping_status()
    {
        var payment = CreatePayment(100m);
        payment.AddAllocation(DocumentType.Invoice, Guid.NewGuid(), 100m);
        payment.Approve(Guid.NewGuid(), "PAY-0001");

        payment.Void(Guid.NewGuid());

        Assert.Equal(PaymentStatus.Void, payment.Status);
        Assert.Single(payment.Allocations);
    }
}
