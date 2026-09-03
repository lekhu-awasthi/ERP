namespace ErpApp.Application.Trade;

/// <summary>
/// Which half of the trade catalogue a shared analytics handler is answering for. Phase 26b's four
/// By-Customer/By-Item report pairs are mirror images of each other -- same filters, same columns,
/// one word different in a header -- so each pair is answered by a single handler discriminated by
/// this enum, the way <c>ContactAgeingSummaryQuery</c> uses <c>ContactType</c> and <c>Payment</c>
/// uses <c>Direction</c>.
///
/// <para>The two sides read different document pairs: <b>Sales</b> is Invoice net of CreditNote,
/// <b>Purchase</b> is PurchaseBill net of DebitNote. They differ in one user-visible way, which is
/// why the DTOs carry a label rather than hardcoding one: the live reports head the same column
/// "Net Sales" on one side and "Net Purchase" on the other.</para>
/// </summary>
public enum TradeSide
{
    Sales,
    Purchase,
}
