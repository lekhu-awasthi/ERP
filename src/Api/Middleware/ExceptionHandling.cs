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
                Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (
                    StatusCodes.Status409Conflict, "This record was modified by someone else. Please reload and try again."),
                NotFoundException => (StatusCodes.Status404NotFound, exception.Message),
                AuthenticationFailedException => (StatusCodes.Status401Unauthorized, exception.Message),
                EmailNotVerifiedException => (StatusCodes.Status403Forbidden, exception.Message),
                ForbiddenException => (StatusCodes.Status403Forbidden, exception.Message),
                InvalidVerificationCodeException => (StatusCodes.Status400BadRequest, exception.Message),
                System.Text.Json.JsonException => (
                    StatusCodes.Status400BadRequest, "The request body is malformed or contains a value of the wrong type."),
                Microsoft.AspNetCore.Http.BadHttpRequestException badHttpRequestException => (
                    StatusCodes.Status400BadRequest, badHttpRequestException.Message),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
            };

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = statusCode, Title = title });
        }));
    }
}
