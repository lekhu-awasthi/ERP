using ErpApp.Application.Common.Pagination;
using ErpApp.Domain.Common;
using FluentValidation;

namespace ErpApp.Application.Configuration.Queries.ListLookups;

/// <summary>
/// Registered explicitly per closed TLookup in Application/DependencyInjection.cs
/// (RegisterLookupHandlers), not discovered via AddValidatorsFromAssembly -- same open-generic
/// DI limitation as the handler itself (see ListLookupsQuery's doc comment).
/// </summary>
public sealed class ListLookupsQueryValidator<TLookup> : AbstractValidator<ListLookupsQuery<TLookup>>
    where TLookup : class, ITenantLookupEntity
{
    public ListLookupsQueryValidator()
    {
        this.ValidatePaging(x => x.Page, x => x.PageSize);
    }
}
