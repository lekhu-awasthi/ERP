using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateBankReconciliation;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Commands.QuickApproveBankStatementLine;
using ErpApp.Application.Accounting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Payments.Commands.ApprovePayment;
using ErpApp.Application.Payments.Commands.CreatePayment;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Accounting;

/// <summary>
/// Phase 57 — Quick Approve: one statement line becomes this tenant's own document, and the two are
/// matched.
///
/// <para><b>What these tests are about, and what they deliberately are not.</b> The handler's own
/// job is the <i>routing</i> (which document, which direction, which way round the journal voucher
/// goes), the <i>reconciliation</i> it writes afterwards, and the <i>refusals</i>. The four commands
/// it sends are somebody else's tested behaviour, reached through <c>ISender</c> so that validation,
/// the lock date and authorization all apply — so the sender here is a double that records what was
/// asked for and posts the entry a real approval would. That the real commands run is proven by the
/// phase's E2E, in SQL, which is the only place it can honestly be proven.</para>
/// </summary>
public class QuickApproveBankStatementLineTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ActingUserId = Guid.NewGuid();
    private static readonly Guid ContactId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_deposit_against_a_contact_becomes_a_received_payment_for_the_lines_own_amount()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var sender = new RecordingSender(db, seed.BankId);

        var result = await QuickApproveAsync(
            db, sender, seed.BankId, seed.Deposit1500, QuickApproveTarget.Contact, ContactId);

        var created = Assert.IsType<CreatePaymentCommand>(sender.Sent[0]);

        Assert.Equal(PaymentDirection.Received, created.Direction);
        Assert.Equal(seed.BankId, created.AccountId);
        Assert.Equal(1500m, created.Amount);
        Assert.Equal(new DateOnly(2026, 9, 16), created.Date);
        Assert.Equal("SALARY CREDIT", created.Reference);
        Assert.Empty(created.Allocations);

        // ...and it is approved, not left in Draft: the control is called Quick *Approve*, and a
        // draft would post nothing and so leave nothing to reconcile against.
        Assert.IsType<ApprovePaymentCommand>(sender.Sent[1]);

        Assert.Equal(DocumentType.Payment, result.DocumentType);
        Assert.Equal(1500m, result.Amount);
    }

    [Fact]
    public async Task A_withdrawal_against_a_contact_becomes_a_paid_payment()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var sender = new RecordingSender(db, seed.BankId);

        await QuickApproveAsync(
            db, sender, seed.BankId, seed.Withdrawal250, QuickApproveTarget.Contact, ContactId);

        var created = Assert.IsType<CreatePaymentCommand>(sender.Sent[0]);

        Assert.Equal(PaymentDirection.Paid, created.Direction);

        // The magnitude, never the signed value: StatementAmount signs a withdrawal negative and a
        // Payment's Amount must be greater than zero.
        Assert.Equal(250.75m, created.Amount);
    }

    [Fact]
    public async Task A_deposit_against_an_account_debits_the_bank_and_credits_the_chosen_account()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var sender = new RecordingSender(db, seed.BankId);

        var result = await QuickApproveAsync(
            db, sender, seed.BankId, seed.Deposit1500, QuickApproveTarget.Account, seed.InterestId);

        var created = Assert.IsType<CreateJournalVoucherCommand>(sender.Sent[0]);

        Assert.Collection(
            created.Lines,
            line =>
            {
                Assert.Equal(seed.BankId, line.AccountId);
                Assert.Equal(1500m, line.Debit);
                Assert.Equal(0m, line.Credit);
            },
            line =>
            {
                Assert.Equal(seed.InterestId, line.AccountId);
                Assert.Equal(0m, line.Debit);
                Assert.Equal(1500m, line.Credit);
            });

        Assert.IsType<ApproveJournalVoucherCommand>(sender.Sent[1]);
        Assert.Equal(DocumentType.JournalVoucher, result.DocumentType);
    }

    [Fact]
    public async Task A_withdrawal_against_an_account_is_the_mirror()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var sender = new RecordingSender(db, seed.BankId);

        await QuickApproveAsync(
            db, sender, seed.BankId, seed.Withdrawal250, QuickApproveTarget.Account, seed.ChargesId);

        var created = Assert.IsType<CreateJournalVoucherCommand>(sender.Sent[0]);

        Assert.Collection(
            created.Lines,
            line =>
            {
                Assert.Equal(seed.ChargesId, line.AccountId);
                Assert.Equal(250.75m, line.Debit);
            },
            line =>
            {
                Assert.Equal(seed.BankId, line.AccountId);
                Assert.Equal(250.75m, line.Credit);
            });
    }

    /// <summary>
    /// The half that makes this more than a shortcut: the created document's own posting against the
    /// bank account is matched to the line it came from, so the matcher never offers the two rows
    /// side by side.
    /// </summary>
    [Fact]
    public async Task The_created_documents_posting_is_reconciled_against_the_line_it_came_from()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var sender = new RecordingSender(db, seed.BankId);

        var result = await QuickApproveAsync(
            db, sender, seed.BankId, seed.Deposit1500, QuickApproveTarget.Contact, ContactId);

        var line = await db.BankStatementLines.SingleAsync(x => x.Id == seed.Deposit1500);
        var bookLines = await db.GlLines.Where(x => x.ReconciliationId == result.ReconciliationId).ToListAsync();

        Assert.Equal(result.ReconciliationId, line.ReconciliationId);
        Assert.Single(bookLines);
        Assert.Equal(seed.BankId, bookLines[0].AccountId);

        // The sum rule holds by construction, and this is the assertion that says so.
        Assert.Equal(
            BankMovementTotal.OfStatementLines([line.Amount]),
            BankMovementTotal.OfGlLines(bookLines));

        var reconciliation = await db.BankReconciliations.SingleAsync(x => x.Id == result.ReconciliationId);
        Assert.Equal(ActingUserId, reconciliation.ReconciledByUserId);
    }

    /// <summary>
    /// A line already matched has nothing left to approve — and the refusal comes <b>before</b>
    /// anything is created, which is the point: a 409 raised after the document had posted would
    /// leave a document nobody asked for.
    /// </summary>
    [Fact]
    public async Task An_already_reconciled_line_is_refused_before_anything_is_created()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var sender = new RecordingSender(db, seed.BankId);

        await QuickApproveAsync(
            db, sender, seed.BankId, seed.Deposit1500, QuickApproveTarget.Contact, ContactId);

        var second = new RecordingSender(db, seed.BankId);

        await Assert.ThrowsAsync<ConflictException>(() =>
            QuickApproveAsync(db, second, seed.BankId, seed.Deposit1500, QuickApproveTarget.Contact, ContactId));

        Assert.Empty(second.Sent);
    }

    [Fact]
    public async Task A_line_on_another_account_or_no_line_at_all_is_a_404()
    {
        var db = TestAppDbContext.Create();
        var seed = await SeedAsync(db);
        var sender = new RecordingSender(db, seed.BankId);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            QuickApproveAsync(db, sender, seed.BankId, Guid.NewGuid(), QuickApproveTarget.Contact, ContactId));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            QuickApproveAsync(
                db, sender, seed.OtherBankId, seed.Deposit1500, QuickApproveTarget.Contact, ContactId));

        Assert.Empty(sender.Sent);
    }

    /// <summary>
    /// The mirror of phase 56's "a reconciled statement line cannot be deleted", and the door this
    /// phase had to keep shut. The guard itself lives in <c>SourceDocumentGlEntries</c>, which every
    /// one of the thirteen voids goes through, so it is proven where that mechanism is already
    /// pinned -- <c>VoidJournalVoucherCommandHandlerTests</c>.
    /// </summary>
    private static Task<QuickApproveBankStatementLineResult> QuickApproveAsync(
        IAppDbContext db, ISender sender, Guid bankAccountId, Guid statementLineId,
        QuickApproveTarget target, Guid targetId) =>
        new QuickApproveBankStatementLineCommandHandler(db, sender, new FakeCurrentUser()).Handle(
            new QuickApproveBankStatementLineCommand(
                OrganizationId, bankAccountId, statementLineId, target, targetId),
            CancellationToken.None);

    private sealed record Seeded(
        Guid BankId, Guid OtherBankId, Guid InterestId, Guid ChargesId, Guid Deposit1500, Guid Withdrawal250);

    private static async Task<Seeded> SeedAsync(IAppDbContext db)
    {
        var groupId = Guid.NewGuid();

        var bank = Account.Create(
            OrganizationId, "BC0001", "Nabil Bank", AccountRootType.Asset, groupId, AccountKind.Bank);
        var otherBank = Account.Create(
            OrganizationId, "BC0002", "Cash In Hand", AccountRootType.Asset, groupId, AccountKind.Cash);
        var interest = Account.Create(
            OrganizationId, "4200", "Interest Income", AccountRootType.Income, groupId, AccountKind.Other);
        var charges = Account.Create(
            OrganizationId, "5400", "Bank Charges", AccountRootType.Expense, groupId, AccountKind.Other);

        db.Accounts.AddRange(bank, otherBank, interest, charges);

        var deposit = BankStatementLine.Create(
            OrganizationId, bank.Id, new DateOnly(2026, 9, 16), "SALARY CREDIT",
            StatementAmount.Deposit(1500m), null, Now);
        var withdrawal = BankStatementLine.Create(
            OrganizationId, bank.Id, new DateOnly(2026, 9, 17), "ATM WITHDRAWAL",
            StatementAmount.Withdrawal(250.75m), null, Now.AddMinutes(1));

        db.BankStatementLines.AddRange(deposit, withdrawal);

        await db.SaveChangesAsync();

        return new Seeded(bank.Id, otherBank.Id, interest.Id, charges.Id, deposit.Id, withdrawal.Id);
    }

    /// <summary>
    /// Stands in for the pipeline: records each request and, on an Approve, posts the entry the real
    /// approval's posting rule would — one debit or credit against the bank account, balanced by a
    /// suspense line. What it must get right is the <i>shape</i> the handler then reads back
    /// (`SourceDocumentType`/`SourceDocumentId` and a line on the bank account), because that read
    /// is the handler's own logic and not the double's.
    /// </summary>
    private sealed class RecordingSender(IAppDbContext db, Guid bankAccountId) : ISender
    {
        private readonly Guid _contraId = Guid.NewGuid();

        public List<object> Sent { get; } = [];

        private Guid _paymentId;
        private Guid _journalVoucherId;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);

            object response = request switch
            {
                CreatePaymentCommand => Create(out _paymentId, PaymentStatus.Draft),
                ApprovePaymentCommand => Approve(DocumentType.Payment, _paymentId),
                CreateJournalVoucherCommand => CreateJv(out _journalVoucherId),
                ApproveJournalVoucherCommand => ApproveJv(_journalVoucherId),
                _ => throw new InvalidOperationException($"Unexpected request {request.GetType().Name}."),
            };

            return Task.FromResult((TResponse)response);
        }

        private CreatePaymentResult Create(out Guid id, PaymentStatus status)
        {
            id = Guid.NewGuid();
            return new CreatePaymentResult(id, Payment.DraftCode, status);
        }

        private CreateJournalVoucherResult CreateJv(out Guid id)
        {
            id = Guid.NewGuid();
            return new CreateJournalVoucherResult(id, "DRAFT", JournalVoucherStatus.Draft);
        }

        private ApprovePaymentResult Approve(DocumentType type, Guid id)
        {
            Post(type, id);
            return new ApprovePaymentResult(id, "PAY0001", PaymentStatus.Approved, Now);
        }

        private ApproveJournalVoucherResult ApproveJv(Guid id)
        {
            Post(DocumentType.JournalVoucher, id);
            return new ApproveJournalVoucherResult(id, "JV0001", JournalVoucherStatus.Approved, Now);
        }

        private void Post(DocumentType type, Guid id)
        {
            var amount = LastAmount();

            var entry = GlJournalEntry.Post(
                OrganizationId, type, id,
                amount > 0
                    ? [new GlLineInput(bankAccountId, amount, 0m), new GlLineInput(_contraId, 0m, amount)]
                    : [new GlLineInput(_contraId, -amount, 0m), new GlLineInput(bankAccountId, 0m, -amount)]);

            db.GlJournalEntries.Add(entry);
            db.SaveChangesAsync().GetAwaiter().GetResult();
        }

        /// <summary>The signed amount the create request carried, read back off the recorded request
        /// so the posting agrees with what was asked for rather than with a constant.</summary>
        private decimal LastAmount() => Sent[0] switch
        {
            CreatePaymentCommand payment =>
                payment.Direction == PaymentDirection.Received ? payment.Amount : -payment.Amount,
            CreateJournalVoucherCommand voucher =>
                voucher.Lines.Single(x => x.AccountId == bankAccountId).Debit
                - voucher.Lines.Single(x => x.AccountId == bankAccountId).Credit,
            _ => 0m,
        };

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public Guid UserId => ActingUserId;
    }
}
