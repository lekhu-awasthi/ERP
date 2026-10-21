using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Sales.Discounts;
using ErpApp.Domain.Sales;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.DiscountSchemes;

// Phase 97 -- the Discount Schemes settings screen's requests: list, read one, create, update, delete. See DiscountScheme
// for what a scheme is and why its rules are ours, and PermissionKeys.DiscountSchemeView for the keys.

public sealed record DiscountSchemeLineDto(Guid? ProductId, Guid? CategoryId, decimal? MinimumAmount, decimal DiscountPct);

public sealed record DiscountSchemeDto(
    Guid Id,
    string Code,
    string Name,
    DiscountSchemeKind Kind,
    DateOnly StartDate,
    DateOnly EndDate,
    bool AllLocations,
    IReadOnlyList<Guid> LocationIds,
    bool IsActive,
    IReadOnlyList<DiscountSchemeLineDto> Lines);

/// <summary>Every scheme, by code. A tenant keeps a handful, so the screen filters what it already has (as Payment
/// Modes does).</summary>
public sealed record ListDiscountSchemesQuery(Guid OrganizationId)
    : IRequest<IReadOnlyList<DiscountSchemeDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DiscountSchemeView;
}

public sealed class ListDiscountSchemesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListDiscountSchemesQuery, IReadOnlyList<DiscountSchemeDto>>
{
    public async Task<IReadOnlyList<DiscountSchemeDto>> Handle(ListDiscountSchemesQuery request, CancellationToken cancellationToken)
    {
        var schemes = await db.DiscountSchemes
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

        return await DiscountSchemeView.ReadAsync(db, request.OrganizationId, schemes, cancellationToken);
    }
}

/// <param name="LocationIds">The locations a scheme applies at when <paramref name="AllLocations"/> is false.</param>
public sealed record CreateDiscountSchemeCommand(
    Guid OrganizationId,
    string Code,
    string Name,
    DiscountSchemeKind Kind,
    DateOnly StartDate,
    DateOnly EndDate,
    bool AllLocations,
    IReadOnlyList<Guid> LocationIds,
    IReadOnlyList<DiscountSchemeLineDto> Lines)
    : IRequest<DiscountSchemeDto>, IRequirePermission, IOrganizationScoped, IDiscountSchemeInput
{
    public string PermissionKey => PermissionKeys.DiscountSchemeManage;
}

public sealed record UpdateDiscountSchemeCommand(
    Guid OrganizationId,
    Guid Id,
    string Code,
    string Name,
    DiscountSchemeKind Kind,
    DateOnly StartDate,
    DateOnly EndDate,
    bool AllLocations,
    IReadOnlyList<Guid> LocationIds,
    IReadOnlyList<DiscountSchemeLineDto> Lines,
    bool IsActive)
    : IRequest<DiscountSchemeDto>, IRequirePermission, IOrganizationScoped, IDiscountSchemeInput
{
    public string PermissionKey => PermissionKeys.DiscountSchemeManage;
}

public sealed record DeleteDiscountSchemeCommand(Guid OrganizationId, Guid Id)
    : IRequest, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DiscountSchemeManage;
}

/// <summary>What Create and Update share, so one validator and one writer serve both.</summary>
public interface IDiscountSchemeInput
{
    Guid OrganizationId { get; }
    string Code { get; }
    string Name { get; }
    DiscountSchemeKind Kind { get; }
    DateOnly StartDate { get; }
    DateOnly EndDate { get; }
    bool AllLocations { get; }
    IReadOnlyList<Guid> LocationIds { get; }
    IReadOnlyList<DiscountSchemeLineDto> Lines { get; }
}

/// <summary>Every rule the Domain enforces, as a 400 naming its field (CLAUDE.md: a Domain invariant reached through an
/// endpoint is otherwise a 500). The Domain keeps its checks as the backstop.</summary>
public static class DiscountSchemeValidation
{
    public const int MaxLines = 500;

    public static void Apply<T>(AbstractValidator<T> v)
        where T : IDiscountSchemeInput
    {
        v.RuleFor(x => x.OrganizationId).NotEmpty();
        v.RuleFor(x => x.Code).NotEmpty().MaximumLength(DiscountScheme.CodeMaxLength);
        v.RuleFor(x => x.Name).NotEmpty().MaximumLength(DiscountScheme.NameMaxLength);
        v.RuleFor(x => x.Kind).IsInEnum();
        v.RuleFor(x => x.StartDate).NotEmpty();
        v.RuleFor(x => x.EndDate).NotEmpty();
        v.RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).WithMessage("A scheme cannot end before it starts.");
        v.RuleFor(x => x.LocationIds).NotNull();
        v.RuleFor(x => x.LocationIds).NotEmpty().When(x => !x.AllLocations)
            .WithMessage("Choose the locations this scheme applies at, or All locations.");
        v.RuleFor(x => x.Lines).NotNull().NotEmpty().WithMessage("Add at least one row.");
        v.RuleFor(x => x.Lines.Count).LessThanOrEqualTo(MaxLines).When(x => x.Lines is not null)
            .OverridePropertyName("Lines").WithMessage($"A scheme has at most {MaxLines} rows.");
        v.RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.DiscountPct)
                .GreaterThan(0m).LessThanOrEqualTo(100m)
                .Must(p => decimal.Round(p, 2) == p).WithMessage("A discount has at most two decimal places.");
            line.RuleFor(l => l.MinimumAmount).GreaterThanOrEqualTo(0m);
        });
        v.RuleFor(x => x.Lines).Must((x, lines) => OneKeyPerRow(x.Kind, lines)).When(x => x.Lines is not null)
            .WithMessage(x => x.Kind switch
            {
                DiscountSchemeKind.ItemWise => "Each row of an item-wise scheme names one product.",
                DiscountSchemeKind.CategoryWise => "Each row of a category-wise scheme names one category.",
                _ => "Each row of a slab scheme names the bill amount it starts at.",
            });
        v.RuleFor(x => x.Lines).Must(Distinct).When(x => x.Lines is not null)
            .WithMessage(x => x.Kind switch
            {
                DiscountSchemeKind.ItemWise => "A product is listed twice in this scheme.",
                DiscountSchemeKind.CategoryWise => "A category is listed twice in this scheme.",
                _ => "Two slabs start at the same bill amount.",
            });
    }

    private static bool OneKeyPerRow(DiscountSchemeKind kind, IReadOnlyList<DiscountSchemeLineDto> lines) =>
        lines.All(l => kind switch
        {
            DiscountSchemeKind.ItemWise => l.ProductId is not null && l.CategoryId is null && l.MinimumAmount is null,
            DiscountSchemeKind.CategoryWise => l.CategoryId is not null && l.ProductId is null && l.MinimumAmount is null,
            _ => l.MinimumAmount is not null && l.ProductId is null && l.CategoryId is null,
        });

    private static bool Distinct(IReadOnlyList<DiscountSchemeLineDto> lines)
    {
        var keys = lines.Select(l => (l.ProductId, l.CategoryId, l.MinimumAmount)).ToList();
        return keys.Distinct().Count() == keys.Count;
    }
}

public sealed class CreateDiscountSchemeCommandValidator : AbstractValidator<CreateDiscountSchemeCommand>
{
    public CreateDiscountSchemeCommandValidator() => DiscountSchemeValidation.Apply(this);
}

public sealed class UpdateDiscountSchemeCommandValidator : AbstractValidator<UpdateDiscountSchemeCommand>
{
    public UpdateDiscountSchemeCommandValidator()
    {
        DiscountSchemeValidation.Apply(this);
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class CreateDiscountSchemeCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateDiscountSchemeCommand, DiscountSchemeDto>
{
    public async Task<DiscountSchemeDto> Handle(CreateDiscountSchemeCommand request, CancellationToken cancellationToken)
    {
        await DiscountSchemeWriter.EnsureReferencesAsync(db, request, null, cancellationToken);

        var scheme = DiscountSchemeWriter.Run(() => DiscountScheme.Create(
            request.OrganizationId, request.Code, request.Name, request.Kind, request.StartDate, request.EndDate,
            request.AllLocations));
        db.DiscountSchemes.Add(scheme);
        DiscountSchemeWriter.AddRows(db, scheme, request);

        await db.SaveChangesAsync(cancellationToken);
        return (await DiscountSchemeView.ReadAsync(db, request.OrganizationId, [scheme], cancellationToken))[0];
    }
}

public sealed class UpdateDiscountSchemeCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateDiscountSchemeCommand, DiscountSchemeDto>
{
    public async Task<DiscountSchemeDto> Handle(UpdateDiscountSchemeCommand request, CancellationToken cancellationToken)
    {
        var scheme = await db.DiscountSchemes.SingleOrDefaultAsync(
                x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Discount scheme not found.");

        await DiscountSchemeWriter.EnsureReferencesAsync(db, request, scheme.Id, cancellationToken);

        DiscountSchemeWriter.Run(() =>
        {
            scheme.Update(
                request.Code, request.Name, request.Kind, request.StartDate, request.EndDate, request.AllLocations,
                request.IsActive);
            return scheme;
        });

        // The rows are replaced wholesale through their own sets (phase-4 bug #1's remedy).
        db.DiscountSchemeLines.RemoveRange(
            await db.DiscountSchemeLines.Where(x => x.DiscountSchemeId == scheme.Id).ToListAsync(cancellationToken));
        db.DiscountSchemeLocations.RemoveRange(
            await db.DiscountSchemeLocations.Where(x => x.DiscountSchemeId == scheme.Id).ToListAsync(cancellationToken));
        DiscountSchemeWriter.AddRows(db, scheme, request);

        await db.SaveChangesAsync(cancellationToken);
        return (await DiscountSchemeView.ReadAsync(db, request.OrganizationId, [scheme], cancellationToken))[0];
    }
}

/// <summary>A scheme is a rule, not a record of a sale: a sale keeps the percent it was given on its own line, so deleting
/// the scheme changes no document.</summary>
public sealed class DeleteDiscountSchemeCommandHandler(IAppDbContext db) : IRequestHandler<DeleteDiscountSchemeCommand>
{
    public async Task Handle(DeleteDiscountSchemeCommand request, CancellationToken cancellationToken)
    {
        var scheme = await db.DiscountSchemes.SingleOrDefaultAsync(
                x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Discount scheme not found.");

        db.DiscountSchemeLines.RemoveRange(
            await db.DiscountSchemeLines.Where(x => x.DiscountSchemeId == scheme.Id).ToListAsync(cancellationToken));
        db.DiscountSchemeLocations.RemoveRange(
            await db.DiscountSchemeLocations.Where(x => x.DiscountSchemeId == scheme.Id).ToListAsync(cancellationToken));
        db.DiscountSchemes.Remove(scheme);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal static class DiscountSchemeWriter
{
    public static T Run<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationException([new ValidationFailure(string.Empty, ex.Message)]);
        }
    }

    public static void AddRows(IAppDbContext db, DiscountScheme scheme, IDiscountSchemeInput input)
    {
        db.DiscountSchemeLines.AddRange(Run(() => scheme.BuildLines(
            [.. input.Lines.Select(x => new DiscountSchemeLineInput(x.ProductId, x.CategoryId, x.MinimumAmount, x.DiscountPct))])));
        db.DiscountSchemeLocations.AddRange(Run(() => scheme.BuildLocations(input.LocationIds)));
    }

    /// <summary>The code is the organization's own (a 409), and every product, category and location the scheme names is
    /// this organization's (a 400 naming the field).</summary>
    public static async Task EnsureReferencesAsync(
        IAppDbContext db, IDiscountSchemeInput input, Guid? exceptId, CancellationToken cancellationToken)
    {
        var code = input.Code.Trim();
        if (await db.DiscountSchemes.AnyAsync(
                x => x.OrganizationId == input.OrganizationId && x.Code == code && x.Id != exceptId, cancellationToken))
        {
            throw new ConflictException($"A discount scheme with code '{code}' already exists.");
        }

        var productIds = input.Lines.Where(x => x.ProductId is not null).Select(x => x.ProductId!.Value).Distinct().ToList();
        if (productIds.Count > 0 && await db.Products.CountAsync(
                x => x.OrganizationId == input.OrganizationId && productIds.Contains(x.Id), cancellationToken) != productIds.Count)
        {
            throw new ValidationException([new ValidationFailure("Lines", "A row names a product that does not exist.")]);
        }

        var categoryIds = input.Lines.Where(x => x.CategoryId is not null).Select(x => x.CategoryId!.Value).Distinct().ToList();
        if (categoryIds.Count > 0 && await db.ProductCategories.CountAsync(
                x => x.OrganizationId == input.OrganizationId && categoryIds.Contains(x.Id), cancellationToken) != categoryIds.Count)
        {
            throw new ValidationException([new ValidationFailure("Lines", "A row names a category that does not exist.")]);
        }

        var locationIds = input.AllLocations ? [] : input.LocationIds.Distinct().ToList();
        if (locationIds.Count > 0 && await db.BillingLocations.CountAsync(
                x => x.OrganizationId == input.OrganizationId && locationIds.Contains(x.Id), cancellationToken) != locationIds.Count)
        {
            throw new ValidationException([new ValidationFailure("LocationIds", "A location does not exist.")]);
        }
    }
}

internal static class DiscountSchemeView
{
    public static async Task<IReadOnlyList<DiscountSchemeDto>> ReadAsync(
        IAppDbContext db, Guid organizationId, IReadOnlyList<DiscountScheme> schemes, CancellationToken cancellationToken)
    {
        if (schemes.Count == 0)
        {
            return [];
        }

        // Every row of the organization's schemes in one read each (a tenant keeps a handful), never a list of ids.
        var lines = (await db.DiscountSchemeLines.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId)
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.DiscountSchemeId);
        var locations = (await db.DiscountSchemeLocations.AsNoTracking()
                .Where(x => x.OrganizationId == organizationId)
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.DiscountSchemeId);

        return
        [
            .. schemes.Select(s => new DiscountSchemeDto(
                s.Id, s.Code, s.Name, s.Kind, s.StartDate, s.EndDate, s.AllLocations,
                [.. locations[s.Id].Select(x => x.BillingLocationId)],
                s.IsActive,
                [.. lines[s.Id].OrderBy(x => x.SortOrder)
                    .Select(x => new DiscountSchemeLineDto(x.ProductId, x.CategoryId, x.MinimumAmount, x.DiscountPct))])),
        ];
    }
}

/// <summary>
/// What the schemes give a sale at <paramref name="LocationId"/> on <paramref name="Date"/>: each of
/// <paramref name="ProductIds"/>' percent, and the slab ladder for the bill. The ERP forms ask it as a line's product is
/// picked and as the document's location or date changes; the gate keeps exactly these figures.
/// </summary>
public sealed record ListApplicableDiscountsQuery(Guid OrganizationId, Guid? LocationId, DateOnly Date, IReadOnlyList<Guid> ProductIds)
    : IRequest<ApplicableDiscountsDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.DiscountSchemeView;
}

public sealed class ListApplicableDiscountsQueryValidator : AbstractValidator<ListApplicableDiscountsQuery>
{
    public ListApplicableDiscountsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ProductIds).NotNull();
        RuleFor(x => x.ProductIds.Count).LessThanOrEqualTo(DiscountSchemeValidation.MaxLines)
            .When(x => x.ProductIds is not null).OverridePropertyName("ProductIds");
    }
}

public sealed record ProductDiscountDto(Guid ProductId, decimal DiscountPct);

public sealed record ApplicableDiscountsDto(IReadOnlyList<ProductDiscountDto> Products, IReadOnlyList<SlabStep> Slabs);

public sealed class ListApplicableDiscountsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListApplicableDiscountsQuery, ApplicableDiscountsDto>
{
    public async Task<ApplicableDiscountsDto> Handle(ListApplicableDiscountsQuery request, CancellationToken cancellationToken)
    {
        var rates = await DiscountSchemeRates.LoadAsync(
            db, request.OrganizationId, request.LocationId, request.Date, request.ProductIds, cancellationToken);

        return new ApplicableDiscountsDto(
            [.. rates.ProductRates.Select(x => new ProductDiscountDto(x.Key, x.Value))], rates.Slabs);
    }
}
