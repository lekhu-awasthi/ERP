using ErpApp.Application.Accounting.Cash;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Pos.Commands.ClosePosSession;
using ErpApp.Application.Pos.Commands.CreatePosRefund;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Commands.OpenPosSession;
using ErpApp.Application.Pos.Commands.PrintPosRefundReceipt;
using ErpApp.Application.Pos.Commands.RecordPosCashMovement;
using ErpApp.Application.Pos.Queries.PreviewPosRefund;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Application.Sales.Commands.VoidCreditNote;
using ErpApp.Application.Sales.Commands.VoidInvoice;
using ErpApp.Application.Sales.Credit;
using ErpApp.Application.Sales.Posting;
using ErpApp.Application.Sales.Stock;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 61 -- one Retail till, set up the way phase 59's written service was: a 10% service charge,
/// rounding on, a Cash mode posting to Cash In Hand and a Card mode posting to the bank, a walk-in,
/// and the vendor's two products (Chicken Momo, a service carrying service charge, at 200; Coke
/// 250ml, goods without it, at 60), both 13% VAT. Every account a till posts to is a tenant default,
/// so a test that wants the location-level fallback sets it on top.
/// </summary>
internal sealed class PosTestTill
{
    public required IAppDbContext Db { get; init; }
    public required InventoryReportSeed.Seed Seed { get; init; }
    public required Guid UserId { get; init; }
    public required BillingLocation Location { get; init; }
    public required Guid CashAccountId { get; init; }
    public required Guid BankAccountId { get; init; }
    public required Guid ServiceChargeAccountId { get; init; }
    public required Guid RoundingAccountId { get; init; }
    public required Guid OverShortAccountId { get; init; }
    public required Guid VegetablesAccountId { get; init; }
    public required Guid CashModeId { get; init; }
    public required Guid CardModeId { get; init; }
    public required Guid WalkInId { get; init; }
    public required Guid MomoId { get; init; }
    public required Guid CokeId { get; init; }

    public Guid OrganizationId => Seed.OrganizationId;

    public static readonly string[] CashierKeys =
    [
        PermissionKeys.InvoiceCreate, PermissionKeys.InvoiceApprove, PermissionKeys.InvoiceVoid,
        PermissionKeys.PosSessionOperate, PermissionKeys.PosSessionViewAll,
        // Phase 63 -- a refund is a credit note created approved.
        PermissionKeys.CreditNoteView, PermissionKeys.CreditNoteCreate, PermissionKeys.CreditNoteApprove,
        PermissionKeys.CreditNoteVoid,
    ];

    /// <param name="grantedKeys">What the cashier's role holds; every till key by default.</param>
    /// <param name="vatRegistered">Phase 62 -- the seller's registration, which decides whether a bill
    /// is a tax invoice at all. A real Organization row is seeded for it, which phase 61's tests never
    /// needed.</param>
    public static async Task<PosTestTill> CreateAsync(
        string[]? grantedKeys = null, int cokeInStock = 20, bool vatRegistered = true, bool restaurant = false)
    {
        var db = TestAppDbContext.Create();

        var organization = Organization.Create(
            "Momo Ghar", "Restaurant", "Thamel, Kathmandu", new DateOnly(2026, 1, 1), vatRegistered, "momo-ghar",
            null, null, vatRegistered ? "601234567" : null, null, Guid.NewGuid());
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();

        var seed = await InventoryReportSeed.CreateAsync(db, organization.Id);
        var organizationId = seed.OrganizationId;

        await TenantFeatureSeed.SeedAsync(db, organizationId, new AccountingFeatureSelections(
            TrackInventory: true, MultipleLocations: true, MultipleWarehouses: false,
            MultiCurrency: false, Manufacturing: false, PosRetail: true, PosRestaurant: restaurant));

        var user = Domain.Identity.User.Register("Sita Cashier", $"sita-{Guid.NewGuid():N}@example.com", "9800000000", "hash");
        db.Users.Add(user);
        var userId = user.Id;
        await PermissionGrantSeed.GrantAsync(db, organizationId, userId, grantedKeys ?? CashierKeys);

        var group = Guid.NewGuid();
        Account Add(string code, string name, AccountRootType root, AccountKind kind = AccountKind.Other)
        {
            var account = Account.Create(organizationId, code, name, root, group, kind);
            db.Accounts.Add(account);
            return account;
        }

        var cash = Add("1010", "Cash In Hand", AccountRootType.Asset, AccountKind.Cash);
        var bank = Add("1020", "Nabil Bank", AccountRootType.Asset, AccountKind.Bank);
        var serviceCharge = Add("4100", "Service Charge Income", AccountRootType.Income);
        var rounding = Add("4200", "Rounding", AccountRootType.Income);
        var overShort = Add("5900", "Cash Over/Short", AccountRootType.Expense);
        var vegetables = Add("5100", "Vegetables", AccountRootType.Expense);

        var settings = await db.TenantSettings.SingleAsync(x => x.OrganizationId == organizationId);
        settings.SetPosDefaults(serviceCharge.Id, rounding.Id, overShort.Id);

        var location = BillingLocation.CreateHeadOffice(organizationId);
        // Phase 64 -- the same till as a Restaurant location, for the order tests.
        var mode = restaurant ? PosMode.Restaurant : PosMode.Retail;
        location.SetPosMode(mode);
        db.BillingLocations.Add(location);

        var cashMode = PaymentMode.Create(organizationId, "Cash", kind: PaymentModeKind.Cash, accountId: cash.Id);
        var cardMode = PaymentMode.Create(organizationId, "Card", kind: PaymentModeKind.Card, accountId: bank.Id);
        db.PaymentModes.AddRange(cashMode, cardMode);
        db.PosLocationPaymentModes.AddRange(
            PosLocationPaymentMode.Create(organizationId, location.Id, cashMode.Id),
            PosLocationPaymentMode.Create(organizationId, location.Id, cardMode.Id));

        var posSettings = PosLocationSettings.CreateDefault(organizationId, location.Id);
        posSettings.Update(
            mode, serviceChargeEnabled: true, serviceChargeRate: 10m, serviceChargeAccountId: null,
            serviceChargeOnTakeAway: true, roundOffEnabled: true, roundOffAccountId: null, cashVerificationRequired: false,
            denominations: PosLocationSettings.DefaultDenominations, defaultTab: null,
            printEstimateBill: true, printInvoice: true, printCreditNote: true, printKot: false,
            abbreviatedTaxInvoiceEnabled: false);
        db.PosLocationSettings.Add(posSettings);

        var walkIn = Contact.CreateWalkInCustomer(organizationId);
        db.Contacts.Add(walkIn);
        await db.SaveChangesAsync();

        var category = seed.CategoryId;
        var unit = await db.Products.Where(x => x.Id == seed.ProductId).Select(x => x.PrimaryUnitId).SingleAsync();
        var momo = await new CreateProductCommandHandler(db, seed.NumberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Service, "Chicken Momo", category, unit, null, true, 200m, 0m,
                VatRate.ThirteenPercentVat, 0, false, ServiceChargeApplicable: true),
            CancellationToken.None);
        var coke = await new CreateProductCommandHandler(db, seed.NumberGenerator).Handle(
            new CreateProductCommand(
                organizationId, ProductType.Goods, "Coke 250ml", category, unit, null, true, 60m, 40m,
                VatRate.ThirteenPercentVat, 0, true),
            CancellationToken.None);

        var till = new PosTestTill
        {
            Db = db,
            Seed = seed,
            UserId = userId,
            Location = location,
            CashAccountId = cash.Id,
            BankAccountId = bank.Id,
            ServiceChargeAccountId = serviceCharge.Id,
            RoundingAccountId = rounding.Id,
            OverShortAccountId = overShort.Id,
            VegetablesAccountId = vegetables.Id,
            CashModeId = cashMode.Id,
            CardModeId = cardMode.Id,
            WalkInId = walkIn.Id,
            MomoId = momo.Id,
            CokeId = coke.Id,
        };

        if (cokeInStock > 0)
        {
            await InventoryReportSeed.PurchaseAsync(db, seed, Today, cokeInStock, 40m, coke.Id);
        }

        return till;
    }

    public static DateOnly Today => NepalTime.LocalDate(DateTimeOffset.UtcNow);

    public FakeCurrentUserService CurrentUser(Guid? userId = null) => new(userId ?? UserId);

    public async Task<PosSessionDto> OpenAsync(
        decimal? amount = 1000m, IReadOnlyList<DenominationCount>? denominations = null, Guid? userId = null) =>
        await new OpenPosSessionCommandHandler(Db, Seed.NumberGenerator, CurrentUser(userId)).Handle(
            new OpenPosSessionCommand(OrganizationId, Location.Id, amount, denominations), CancellationToken.None);

    public CreatePosSaleCommandHandler SaleHandler(Guid? userId = null)
    {
        var ledger = new StockLedgerService(Db);
        return new CreatePosSaleCommandHandler(
            Db, Seed.NumberGenerator, CurrentUser(userId), new InvoicePostingRule(), new InvoiceTenderPostingRule(),
            new FifoStockAvailabilityPolicy(Db, ledger), ledger, new ContactCreditLimitPolicy(Db));
    }

    public Task<CreatePosSaleResult> SellAsync(
        Guid sessionId,
        IReadOnlyList<PosSaleLineInput> lines,
        IReadOnlyList<PosTenderInput> tenders,
        decimal change = 0m,
        Guid? contactId = null,
        Guid? userId = null) =>
        SaleHandler(userId).Handle(
            new CreatePosSaleCommand(
                OrganizationId, sessionId, Location.Id, lines, tenders, change, contactId, Seed.WarehouseId),
            CancellationToken.None);

    public PosSaleLineInput Momo(decimal quantity) => new(MomoId, quantity, 200m);

    public PosSaleLineInput Coke(decimal quantity) => new(CokeId, quantity, 60m);

    public PosTenderInput Cash(decimal amount) => new(CashModeId, amount);

    public PosTenderInput Card(decimal amount) => new(CardModeId, amount);

    public Task<PosSessionDto> CashMovementAsync(
        Guid sessionId, PosCashMovementDirection direction, decimal amount, Guid accountId, string? note = null) =>
        new RecordPosCashMovementCommandHandler(Db, CurrentUser(), new GlCashBalancePolicy(Db)).Handle(
            new RecordPosCashMovementCommand(OrganizationId, sessionId, direction, amount, accountId, note),
            CancellationToken.None);

    public Task<PosSessionDto> CloseAsync(Guid sessionId, decimal counted, string? note = null) =>
        new ClosePosSessionCommandHandler(Db, CurrentUser()).Handle(
            new ClosePosSessionCommand(OrganizationId, sessionId, counted, null, note), CancellationToken.None);

    public Task VoidAsync(Guid invoiceId) =>
        new VoidInvoiceCommandHandler(Db, CurrentUser(), new StockLedgerService(Db)).Handle(
            new VoidInvoiceCommand(OrganizationId, invoiceId), CancellationToken.None);

    // ---- Phase 63: refunds -----------------------------------------------------------------

    public CreatePosRefundCommandHandler RefundHandler(Guid? userId = null) =>
        new(Db, Seed.NumberGenerator, CurrentUser(userId), new CreditNotePostingRule(),
            new CreditNotePayoutPostingRule(), new StockLedgerService(Db));

    /// <summary>One line of a sale, by product: the sale's own line id, as the refund screen sends it.</summary>
    public async Task<PosRefundLineInput> ReturnAsync(Guid invoiceId, Guid productId, decimal quantity)
    {
        var lineId = await Db.InvoiceLines
            .Where(x => x.InvoiceId == invoiceId && x.ProductId == productId)
            .Select(x => x.Id)
            .FirstAsync();
        return new PosRefundLineInput(lineId, quantity);
    }

    public Task<CreatePosRefundResult> RefundAsync(
        Guid sessionId,
        Guid invoiceId,
        IReadOnlyList<PosRefundLineInput> lines,
        IReadOnlyList<PosTenderInput> payouts,
        string reason = "Customer changed their mind",
        Guid? userId = null) =>
        RefundHandler(userId).Handle(
            new CreatePosRefundCommand(OrganizationId, sessionId, Location.Id, invoiceId, lines, payouts, reason),
            CancellationToken.None);

    public Task<PosRefundPreviewDto> PreviewRefundAsync(
        Guid sessionId, Guid invoiceId, IReadOnlyList<PosRefundLineInput> lines) =>
        new PreviewPosRefundQueryHandler(Db, CurrentUser()).Handle(
            new PreviewPosRefundQuery(OrganizationId, sessionId, invoiceId, lines), CancellationToken.None);

    public Task VoidRefundAsync(Guid creditNoteId) =>
        new VoidCreditNoteCommandHandler(Db, CurrentUser(), new StockLedgerService(Db)).Handle(
            new VoidCreditNoteCommand(OrganizationId, creditNoteId), CancellationToken.None);

    public Task<PosRefundReceiptDto> PrintRefundAsync(Guid creditNoteId) =>
        new PrintPosRefundReceiptCommandHandler(Db, CurrentUser()).Handle(
            new PrintPosRefundReceiptCommand(OrganizationId, creditNoteId), CancellationToken.None);

    /// <summary>Debit minus credit on one account, across every entry the tenant has posted.</summary>
    public async Task<decimal> BalanceAsync(Guid accountId)
    {
        var rows = await (
            from line in Db.GlLines
            join entry in Db.GlJournalEntries on line.GlJournalEntryId equals entry.Id
            where entry.OrganizationId == OrganizationId && line.AccountId == accountId
            select new { line.Debit, line.Credit }).ToListAsync();
        return rows.Sum(x => x.Debit - x.Credit);
    }

    public async Task<Guid> ReceivableAccountIdAsync() =>
        (await Db.TenantSettings.SingleAsync(x => x.OrganizationId == OrganizationId)).DefaultAccountsReceivableId!.Value;

    public async Task<List<GlJournalEntry>> EntriesForAsync(DocumentType type, Guid id) =>
        await Db.GlJournalEntries.Include(x => x.Lines)
            .Where(x => x.SourceDocumentType == type && x.SourceDocumentId == id)
            .ToListAsync();
}
