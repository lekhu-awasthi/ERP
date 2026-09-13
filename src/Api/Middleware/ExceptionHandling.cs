using ErpApp.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ErpApp.Api.Middleware;

/// <summary>
/// Maps Application-layer exceptions (and FluentValidation's ValidationException, thrown by
/// ValidationBehavior) to ProblemDetails responses, so every MediatR handler can just throw
/// rather than each Api endpoint needing its own try/catch.
/// </summary>
public static class ExceptionHandling
{
    public static void UseAppExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

            if (exception is FluentValidation.ValidationException validationException)
            {
                var errors = validationException.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new HttpValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Validation failed",
                });
                return;
            }

            var (statusCode, title) = exception switch
            {
                ConflictException => (StatusCodes.Status409Conflict, exception.Message),
                StockAvailabilityWarningException => (StatusCodes.Status422UnprocessableEntity, exception.Message),
                // Phase 31 -- the second confirmable warning. Same 422, told apart by the
                // warningKind extension below, because an Invoice can trip both and each Continue
                // must waive only the warning it was shown for.
                CreditLimitWarningException => (StatusCodes.Status422UnprocessableEntity, exception.Message),
                CashBalanceWarningException => (StatusCodes.Status422UnprocessableEntity, exception.Message),
                Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (
                    StatusCodes.Status409Conflict, "This record was modified by someone else. Please reload and try again."),
                NotFoundException => (StatusCodes.Status404NotFound, exception.Message),
                AuthenticationFailedException => (StatusCodes.Status401Unauthorized, exception.Message),
                EmailNotVerifiedException => (StatusCodes.Status403Forbidden, exception.Message),
                ForbiddenException => (StatusCodes.Status403Forbidden, exception.Message),
                // Phase 20f (FR-2.6): the tenant never opted into the feature. 403 alongside
                // ForbiddenException -- same "understood, authorization refused" semantics, just
                // decided by the Organization's entitlements rather than the user's role. The
                // message always names the feature, which is what tells the two apart.
                FeatureNotEnabledException => (StatusCodes.Status403Forbidden, exception.Message),
                InvalidVerificationCodeException => (StatusCodes.Status400BadRequest, exception.Message),

                // Phase 38: a file the user can fix. ImportFileException is normally caught by the
                // import runner and recorded on the job, so it only reaches here from the one path
                // that parses a file inside a real request -- the Additional Cost grid's Import.
                ErpApp.Application.Imports.ImportFileException => (
                    StatusCodes.Status400BadRequest, exception.Message),
                TurnstileVerificationFailedException => (StatusCodes.Status400BadRequest, exception.Message),
                System.Text.Json.JsonException => (
                    StatusCodes.Status400BadRequest, "The request body is malformed or contains a value of the wrong type."),
                Microsoft.AspNetCore.Http.BadHttpRequestException badHttpRequestException => (
                    StatusCodes.Status400BadRequest, badHttpRequestException.Message),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
            };

            context.Response.StatusCode = statusCode;

            var problem = new ProblemDetails { Status = statusCode, Title = title };

            // Phase 31 -- a machine-readable discriminator for the two confirmable warnings, so the
            // client can resubmit with the matching override flag rather than guessing from the
            // message text. Absent on every other status code.
            var warningKind = exception switch
            {
                StockAvailabilityWarningException => "StockAvailability",
                CreditLimitWarningException => "CreditLimit",
                CashBalanceWarningException => "NegativeCashBalance",
                _ => null,
            };

            if (warningKind is not null)
            {
                problem.Extensions["warningKind"] = warningKind;
            }

            await context.Response.WriteAsJsonAsync(problem);
        }));
    }
}
