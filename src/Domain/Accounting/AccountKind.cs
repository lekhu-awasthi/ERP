namespace ErpApp.Domain.Accounting;

/// <summary>
/// Phase 17 -- orthogonal to <see cref="AccountRootType"/> (Asset/Liability/Equity/Income/Expense).
/// Live-confirmed against the Tigg reference product (docs/phase-17-status.md decision #3): the
/// "New Bank Account" dialog offers a strict two-way toggle, Bank or Cash -- there is no third
/// "Wallet" kind. E-wallets (E-sewa, Khalti) are Bank-kind accounts whose linked <see
/// cref="Bank"/> happens to be a wallet provider, not a distinct account kind. <see cref="Other"/>
/// is the CLR default (0) so existing/ordinary Chart-of-Accounts rows need no migration backfill
/// and no EF ValueGeneratedNever gotcha (CLAUDE.md's enum-default gotcha).
/// </summary>
public enum AccountKind
{
    Other = 0,
    Bank,
    Cash,
}
