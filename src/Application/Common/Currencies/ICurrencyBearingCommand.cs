namespace ErpApp.Application.Common.Currencies;

/// <summary>
/// Marks a command that carries a document's transaction currency and its rate to the base
/// currency (FR-2.5). Implemented by the Create and Update command of every document type whose
/// live form shows the Currency + "Exchange Rate To NPR*" pair, plus the opening-balance row
/// command, whose live form shows the same pair under the labels Currency + Conversion Rate.
///
/// <para>Purely a marker for a sweep test -- nothing in the MediatR pipeline reads it. It exists
/// because "every document type that shows these fields has them wired end to end" is the sort of
/// claim that rots silently as document types are added: a new type would simply never offer a
/// currency, with nothing failing. <c>CurrencyBearingCommandSweepGuardTests</c> enumerates the
/// document types from <c>DocumentMechanisms</c> and asserts a matching pair of commands
/// implements this, the same guard shape phase 27a used for its four sweeps.</para>
///
/// <para>Both members are nullable and both default to null, which means "the base currency at
/// rate 1" -- so every existing caller, every existing test and every single-currency client keeps
/// working untouched. The aggregates enforce the real invariants (see
/// <c>ErpApp.Domain.Common.ExchangeRates.Validate</c>); the command only transports them.</para>
/// </summary>
public interface ICurrencyBearingCommand
{
    string? CurrencyCode { get; }

    decimal? ExchangeRate { get; }
}
