using ErpApp.Application.Common.Formatting;

namespace ErpApp.Api.Middleware;

/// <summary>
/// Phase 27b -- reads the client's calendar preference off the request and parks it in
/// <see cref="RequestCalendar"/> for the rest of the request, so server-rendered PDFs and
/// <c>.xlsx</c> exports can print business dates in Bikram Sambat (phase-23 Decision A's carried
/// limitation).
///
/// <para>Registered <b>before</b> authentication rather than after: this sets a formatting
/// preference, reads nothing but one header, and an anonymous request that produces no output at
/// all is no worse off for having a calendar set. Keeping it early also means every later
/// component -- endpoint, MediatR handler, and the <c>Results.Stream</c> callback that builds a
/// workbook after the endpoint has returned -- sees the same value.</para>
///
/// <para>A missing or unrecognised header is AD, never an error. A preference that fails to parse
/// must not fail the export it was attached to.</para>
/// </summary>
public static class CalendarPreferenceMiddleware
{
    public static IApplicationBuilder UseCalendarPreference(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            RequestCalendar.Current = RequestCalendar.Parse(context.Request.Headers[RequestCalendar.HeaderName]);
            await next(context);
        });
}
