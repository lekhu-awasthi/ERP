using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Sales;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos;

/// <summary>
/// Phase 63 -- turns the payment modes a till request names into frozen (mode, kind, account, amount)
/// rows, for a sale's tenders and a refund's payouts alike. Lifted out of
/// <c>CreatePosSaleCommandHandler</c> rather than copied, because the rules are the same in both
/// directions:
/// <list type="bullet">
/// <item>each mode must be one this till offers (phase 60: linked to the location, active, with an
/// account), or the request is a 400 naming <paramref name="fieldName"/>;</item>
/// <item>a Cash mode must post to <i>this</i> session's drawer account (phase 61 Decision D), or the
/// cash would move an account the count is never compared with -- a 409.</item>
/// </list>
/// </summary>
internal static class PosTenderModes
{
    public static async Task<List<Invoice.TenderInput>> ResolveAsync(
        IAppDbContext db,
        Guid organizationId,
        PosTillContext till,
        PosSession session,
        IReadOnlyList<(Guid PaymentModeId, decimal Amount)> requested,
        string fieldName,
        CancellationToken cancellationToken)
    {
        if (requested.Count == 0)
        {
            return [];
        }

        var modeIds = requested.Select(x => x.PaymentModeId).Distinct().ToList();

        var offered = await (
                from link in db.PosLocationPaymentModes
                join mode in db.PaymentModes on link.PaymentModeId equals mode.Id
                where link.OrganizationId == organizationId && link.BillingLocationId == till.Location.Id
                      && modeIds.Contains(mode.Id)
                select new { mode.Id, mode.Name, mode.Kind, mode.AccountId, mode.IsActive })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var refused = modeIds.Where(id => !offered.TryGetValue(id, out var m) || !m.IsActive || m.AccountId is null).ToList();
        if (refused.Count > 0)
        {
            throw new ValidationException([new ValidationFailure(
                fieldName,
                $"{refused.Count} payment mode(s) named here are not offered at '{till.Location.Name}', are inactive, "
                + "or name no payment account. A till takes the modes linked to it under Configurations > Point of Sale.")]);
        }

        var strayCash = offered.Values
            .Where(x => x.Kind == PaymentModeKind.Cash && x.AccountId != session.CashAccountId)
            .Select(x => $"'{x.Name}'")
            .ToList();
        if (strayCash.Count > 0)
        {
            throw new ConflictException(
                $"Cash in {string.Join(", ", strayCash)} would post to a different account from session "
                + $"{session.Code}'s drawer. Point every Cash mode this till offers at the drawer's account.");
        }

        return [.. requested.Select(x =>
        {
            var mode = offered[x.PaymentModeId];
            return new Invoice.TenderInput(mode.Id, mode.Kind, mode.AccountId!.Value, x.Amount);
        })];
    }
}
