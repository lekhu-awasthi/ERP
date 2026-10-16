using System.Linq.Expressions;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Inventory;
using FluentValidation;

namespace ErpApp.Application.Inventory.Stock;

/// <summary>
/// Phase 93 -- the field rules a line's serial and batch numbers share, so an over-long or blank one is a 400 naming the
/// field instead of <c>DocumentLineSerial.Create</c>'s or <c>ProductBatch.Create</c>'s invariant surfacing as a 500 on
/// every serial path (phase 39's rule; phase 92 § 5). The Domain keeps its checks as the backstop.
///
/// <para>Each member is registered here and nowhere else, from the one expression: FluentValidation caches one
/// compiled accessor per member, and a second registration under another type throws when the validator is built
/// (phase 64).</para>
/// </summary>
public static class StockLineValidationRules
{
    public static void SerialNumberRules<TLine>(
        this AbstractValidator<TLine> line, Expression<Func<TLine, IEnumerable<string>?>> serialNumbers)
    {
        line.RuleForEach(serialNumbers!)
            .Must(x => !string.IsNullOrWhiteSpace(x))
            .WithMessage("A serial number cannot be blank.")
            .Must(x => x is null || x.Trim().Length <= DocumentLineSerial.SerialNoMaxLength)
            .WithMessage($"A serial number cannot be longer than {DocumentLineSerial.SerialNoMaxLength} characters.");
    }

    public static void BatchNoRule<TLine>(this AbstractValidator<TLine> line, Expression<Func<TLine, string?>> batchNo)
    {
        line.RuleFor(batchNo)
            .Must(x => x is null || x.Trim().Length <= ProductBatch.BatchNoMaxLength)
            .WithMessage($"A batch number cannot be longer than {ProductBatch.BatchNoMaxLength} characters.");
    }
}
