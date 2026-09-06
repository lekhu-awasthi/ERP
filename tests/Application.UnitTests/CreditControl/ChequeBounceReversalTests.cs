using ErpApp.Application.Accounting.Cash;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Payments;
using ErpApp.Application.Configuration.Commands.CreatePaymentMode;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.Payments.Commands.TransitionChequeStatus;
using ErpApp.Application.Payments.Posting;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.CreditControl;

/// <summary>
/// Phase 31 -- phase-17 decision #4 recorded "no automatic reversal on Bounced" as a deliberate
/// placeholder. This is the closure: a bounced cheque voids the payment it settled and posts the
/// mirroring GL entry.
/// </summary>
public class ChequeBounceReversalTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task Bouncing_a_cheque_voids_the_approved_payment_and_reverses_its_gl_entry()
    {
        var db = TestAppDbContext.Create();
        var (seed, paymentId, chequeId) = await SeedApprovedChequePaymentAsync(db);
        await GrantAsync(db, seed.OrganizationId, PermissionKeys.PaymentVoid);

        var result = await BounceAsync(db, seed.OrganizationId, chequeId);

        Assert.Equal(ChequeStatus.Bounced, result.Status);
        Assert.NotNull(result.VoidedPaymentCode);

        var payment = await db.Payments.SingleAsync(x => x.Id == paymentId);
        Assert.Equal(PaymentStatus.Void, payment.Status);

        var entries = await db.GlJournalEntries
            .Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == DocumentType.Payment && x.SourceDocumentId == paymentId)
            .ToListAsync();

        Assert.Equal(2, entries.Count);

        // The net effect on every account is zero -- the invariant phase 16a's reversals are
        // measured by, and the one phase-6 bug #3 showed a hand-derived reversal can silently miss.
        var netByAccount = entries
            .SelectMany(x => x.Lines)
            .GroupBy(x => x.AccountId)
            .Select(g => g.Sum(x => x.Debit) - g.Sum(x => x.Credit));
        Assert.All(netByAccount, net => Assert.Equal(0m, net));
    }

    /// <summary>
    /// Bouncing voids an approved financial document, so it needs <c>PaymentVoid</c> on top of the
    /// <c>ChequeManage</c> the pipeline checked -- and that requirement cannot live on
    /// <c>IRequirePermission.PermissionKey</c>, because it depends on the requested status and on the
    /// linked payment's own status. Phase-27a's AttachmentAccess pattern, second use.
    /// </summary>
    [Fact]
    public async Task Bouncing_without_the_payment_void_key_is_forbidden_and_changes_nothing()
    {
        var db = TestAppDbContext.Create();
        var (seed, paymentId, chequeId) = await SeedApprovedChequePaymentAsync(db);

        await Assert.ThrowsAsync<ForbiddenException>(() => BounceAsync(db, seed.OrganizationId, chequeId));

        var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == paymentId);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
    }

    /// <summary>A cheque on a still-Draft payment has nothing posted to unwind, so it bounces
    /// freely and needs no second permission.</summary>
    [Fact]
    public async Task Bouncing_a_cheque_on_a_draft_payment_has_no_ledger_effect()
    {
        var db = TestAppDbContext.Create();
        var (seed, paymentId, chequeId) = await SeedApprovedChequePaymentAsync(db, approve: false);

        var result = await BounceAsync(db, seed.OrganizationId, chequeId);

        Assert.Equal(ChequeStatus.Bounced, result.Status);
        Assert.Null(result.VoidedPaymentCode);
        Assert.Empty(await db.GlJournalEntries
            .Where(x => x.SourceDocumentType == DocumentType.Payment && x.SourceDocumentId == paymentId)
            .ToListAsync());
    }

    /// <summary>Every other transition still has no GL side effect at all -- phase 17's behaviour,
    /// unchanged.</summary>
    [Fact]
    public async Task Clearing_a_cheque_leaves_the_payment_and_its_entry_alone()
    {
        var db = TestAppDbContext.Create();
        var (seed, paymentId, chequeId) = await SeedApprovedChequePaymentAsync(db);

        await new TransitionChequeStatusCommandHandler(db, new FakeCurrentUserService(Actor)).Handle(
            new TransitionChequeStatusCommand(seed.OrganizationId, chequeId, ChequeStatus.Cleared),
            CancellationToken.None);

        Assert.Equal(PaymentStatus.Approved, (await db.Payments.SingleAsync(x => x.Id == paymentId)).Status);
        Assert.Single(await db.GlJournalEntries
            .Where(x => x.SourceDocumentType == DocumentType.Payment && x.SourceDocumentId == paymentId)
            .ToListAsync());
    }

    private static Task<TransitionChequeStatusResult> BounceAsync(IAppDbContext db, Guid organizationId, Guid chequeId) =>
        new TransitionChequeStatusCommandHandler(db, new FakeCurrentUserService(Actor)).Handle(
            new TransitionChequeStatusCommand(organizationId, chequeId, ChequeStatus.Bounced), CancellationToken.None);

    private static async Task GrantAsync(IAppDbContext db, Guid organizationId, string permissionKey)
    {
        db.OrganizationMemberships.Add(
            OrganizationMembership.CreateAccepted(organizationId, Actor, MembershipRole.Admin));
        db.RolePermissions.Add(RolePermission.Create(Guid.NewGuid(), Role.AdminId, permissionKey, true));
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<(CreditSeed Seed, Guid PaymentId, Guid ChequeId)> SeedApprovedChequePaymentAsync(
        IAppDbContext db, bool approve = true)
    {
        var seed = await CreditLimitTestSeed.SeedAsync(db);

        var bank = await new ErpApp.Application.Accounting.Commands.CreateAccount.CreateAccountCommandHandler(
                db, seed.NumberGenerator)
            .Handle(
                new ErpApp.Application.Accounting.Commands.CreateAccount.CreateAccountCommand(
                    seed.OrganizationId, "Everest Bank",
                    db.AccountGroups.First(x => x.OrganizationId == seed.OrganizationId
                        && x.RootType == Domain.Accounting.AccountRootType.Asset).Id),
                CancellationToken.None);

        var chequeMode = await new CreatePaymentModeCommandHandler(db).Handle(
            new CreatePaymentModeCommand(seed.OrganizationId, "Cheque", true), CancellationToken.None);

        var payment = await new CreatePaymentCommandHandler(db).Handle(
            new CreatePaymentCommand(
                seed.OrganizationId, seed.CustomerId, PaymentDirection.Received, new DateOnly(2026, 1, 12),
                chequeMode.Id, bank.Id, 500m, null, [],
                new ChequeDetailsInput("000123", new DateOnly(2026, 1, 12), null)),
            CancellationToken.None);

        if (approve)
        {
            await new ApprovePaymentCommandHandler(
                    db, seed.NumberGenerator, new FakeCurrentUserService(Actor), new PaymentPostingRule(),
                    new GlCashBalancePolicy(db))
                .Handle(new ApprovePaymentCommand(seed.OrganizationId, payment.Id), CancellationToken.None);
        }

        var cheque = await db.Cheques.SingleAsync(x => x.LinkedPaymentId == payment.Id);
        return (seed, payment.Id, cheque.Id);
    }
}
