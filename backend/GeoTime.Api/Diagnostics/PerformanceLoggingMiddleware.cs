using System.Diagnostics;

namespace GeoTime.Api.Diagnostics;

/// <summary>Logs wall-clock timing for every /api request into the session performance log.</summary>
public sealed class PerformanceLoggingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SessionPerformanceLogger logger)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        await next(context);
        sw.Stop();

        long? responseBytes = null;
        if (context.Response.ContentLength is > 0)
            responseBytes = context.Response.ContentLength;

        logger.Write("api_request", "backend", new
        {
            method = context.Request.Method,
            path,
            statusCode = context.Response.StatusCode,
            wallMs = sw.ElapsedMilliseconds,
            responseBytes,
            query = context.Request.QueryString.HasValue
                ? context.Request.QueryString.Value
                : null,
        });
    }
}
