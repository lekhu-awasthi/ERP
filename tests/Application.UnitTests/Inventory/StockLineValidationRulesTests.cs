using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Pos.Commands.CreatePosRefund;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.CreateDebitNote;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Commands.UpdateDebitNote;
using ErpApp.Application.Purchasing.Commands.UpdatePurchaseBill;
using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Commands.UpdateCreditNote;
using ErpApp.Application.Sales.Commands.UpdateInvoice;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;
using FluentValidation;

namespace ErpApp.Application.UnitTests.Inventory;

/// <summary>
/// Phase 93 -- a serial or batch number too long for its column, or a blank serial, is a 400 naming the field on every
/// path that carries one, where <c>DocumentLineSerial.Create</c>'s and <c>ProductBatch.Create</c>'s invariants answered
/// a 500 (phase 92 § 5; phase 39's rule). Each validator is built here, since a validator a handler test never builds
/// can 500 every endpoint it guards (phase 64), and each is run on one long and one blank value.
/// </summary>
public class StockLineValidationRulesTests
{
    private static readonly string LongSerial = new('S', 101);
    private static readonly string LongBatch = new('B', 101);
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 15);

    public static TheoryData<string, IValidator, object, string> Cases()
    {
        string[] longSerials = [LongSerial];
        string[] blankSerials = ["  "];
        var data = new TheoryData<string, IValidator, object, string>();

        void Both<T>(string name, IValidator validator, Func<IReadOnlyList<string>, string?, T> make)
            where T : notnull
        {
            data.Add(name + " serial", validator, make(longSerials, null), "cannot be longer than 100 characters");
            data.Add(name + " blank serial", validator, make(blankSerials, null), "A serial number cannot be blank.");
            data.Add(name + " batch", validator, make([], LongBatch), "A batch number cannot be longer than 100");
        }

        InvoiceLineInput Sale(IReadOnlyList<string> s, string? b) =>
            new(Id, 1m, 1m, VatRate.NoVat, BatchNo: b, SerialNumbers: s);
        PurchaseBillLineInput Bill(IReadOnlyList<string> s, string? b) =>
            new(Id, 1m, 1m, VatRate.NoVat, ExpenditureClassification.Others, BatchNo: b, SerialNumbers: s);
        CreditNoteLineInput Credit(IReadOnlyList<string> s, string? b) =>
            new(Id, 1m, 1m, VatRate.NoVat, SerialNumbers: s, BatchNo: b);
        DebitNoteLineInput Debit(IReadOnlyList<string> s, string? b) =>
            new(Id, 1m, 1m, VatRate.NoVat, SerialNumbers: s, BatchNo: b);

        Both("create invoice", new CreateInvoiceCommandValidator(),
            (s, b) => new CreateInvoiceCommand(Id, Id, Id, Day, null, [Sale(s, b)]));
        Both("update invoice", new UpdateInvoiceCommandValidator(),
            (s, b) => new UpdateInvoiceCommand(Id, Id, Id, Id, Day, null, [Sale(s, b)]));
        Both("create bill", new CreatePurchaseBillCommandValidator(),
            (s, b) => new CreatePurchaseBillCommand(Id, Id, Id, Day, null, null, false, null, null, null, null, [Bill(s, b)]));
        Both("update bill", new UpdatePurchaseBillCommandValidator(),
            (s, b) => new UpdatePurchaseBillCommand(Id, Id, Id, Id, Day, null, null, false, null, null, null, null, [Bill(s, b)]));
        Both("create credit note", new CreateCreditNoteCommandValidator(),
            (s, b) => new CreateCreditNoteCommand(Id, Id, Day, null, [Credit(s, b)]));
        Both("update credit note", new UpdateCreditNoteCommandValidator(),
            (s, b) => new UpdateCreditNoteCommand(Id, Id, Id, Day, null, [Credit(s, b)]));
        Both("create debit note", new CreateDebitNoteCommandValidator(),
            (s, b) => new CreateDebitNoteCommand(Id, Id, Day, null, null, [Debit(s, b)]));
        Both("update debit note", new UpdateDebitNoteCommandValidator(),
            (s, b) => new UpdateDebitNoteCommand(Id, Id, Id, Day, null, null, [Debit(s, b)]));
        Both("till sale", new CreatePosSaleCommandValidator(),
            (s, b) => new CreatePosSaleCommand(
                Id, Id, Id, [new PosSaleLineInput(Id, 1m, 1m, BatchNo: b, SerialNumbers: s)], [new PosTenderInput(Id, 1m)]));

        data.Add("till refund serial", new CreatePosRefundCommandValidator(),
            new CreatePosRefundCommand(Id, Id, Id, Id, [new PosRefundLineInput(Id, 1m, [LongSerial])], [], "Back"),
            "cannot be longer than 100 characters");
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_serial_or_batch_number_its_column_cannot_hold_is_a_400_naming_the_line(
        string path, IValidator validator, object command, string message)
    {
        var context = (IValidationContext)Activator.CreateInstance(
            typeof(ValidationContext<>).MakeGenericType(command.GetType()), command)!;
        var result = validator.Validate(context);

        var error = Assert.Single(result.Errors, x => x.ErrorMessage.Contains(message, StringComparison.Ordinal));
        Assert.StartsWith("Lines[0].", error.PropertyName);
        Assert.False(string.IsNullOrEmpty(path));
    }

    [Fact]
    public void A_serial_of_exactly_one_hundred_characters_after_trimming_is_accepted()
    {
        var command = new CreateInvoiceCommand(
            Id, Id, Id, Day, null,
            [new InvoiceLineInput(Id, 1m, 1m, VatRate.NoVat, BatchNo: " " + new string('B', 100) + " ",
                SerialNumbers: [" " + new string('S', 100) + " "])]);

        var result = new CreateInvoiceCommandValidator().Validate(command);

        Assert.DoesNotContain(result.Errors, x => x.PropertyName.StartsWith("Lines[0].", StringComparison.Ordinal));
    }
}
