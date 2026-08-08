using FluentValidation;
using MediatR;

namespace ErpApp.Application.Common.Behaviors;

/// <summary>
/// Runs every registered FluentValidation validator for TRequest before the handler executes.
/// Phase 0: wired and functional, but no real commands/validators exist yet to exercise it
/// beyond this pipeline-fires proof. Phase 1's RegisterUserCommand etc. will be the first
/// real consumers.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
