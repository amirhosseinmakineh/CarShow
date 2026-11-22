#nullable enable
using System.Diagnostics;

namespace CarShow.Diagnostics;

public sealed class ApiRequestLoggingMiddleware(RequestDelegate next, ILogger<ApiRequestLoggingMiddleware> logger)
{
    public const string Header = "X-Request-Id";
    public static bool IsCatalogUpload(HttpContext context) =>
        HttpMethods.IsPut(context.Request.Method)
        && context.Request.Path.StartsWithSegments("/api/admin/catalog/model-images");

    public static async Task TrackAuthorizedUploadAsync(HttpContext context, RequestDelegate next)
    {
        if (IsCatalogUpload(context))
        {
            var diagnostics = context.RequestServices.GetRequiredService<UploadDiagnostics>();
            diagnostics.Complete();
            diagnostics.Start("MultipartBinding");
        }

        await next(context);
    }

    public async Task InvokeAsync(HttpContext context, UploadDiagnostics diagnostics)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers[Header];
        var requestId = header.Count == 1 && Guid.TryParseExact(header[0], "D", out var id)
            ? id.ToString("D") : Guid.NewGuid().ToString("D");
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[Header] = requestId;
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId });
        var start = Stopwatch.GetTimestamp();
        var upload = IsCatalogUpload(context);
        if (upload)
            diagnostics.Start("RoutingAndAuthorization");

        Exception? failure = null;
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            var cancelled = context.RequestAborted.IsCancellationRequested;
            var status = failure is not null && !context.Response.HasStarted
                ? cancelled ? 499 : 500
                : (cancelled && !context.Response.HasStarted ? 499 : context.Response.StatusCode);
            var level = (status >= 500 || failure is not null && !cancelled) ? LogLevel.Error : status >= 400 ? LogLevel.Warning : LogLevel.Information;
            var duration = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var stage = status < 400 && failure is null ? null : upload ? diagnostics.Stage : "Pipeline";
            logger.Log(level,
                "API {Method} {Path} ended with {StatusCode} in {DurationMs} ms. RequestId={RequestId} FailureStage={FailureStage} StageDurationMs={StageDurationMs} ExceptionType={ExceptionType} ResponseStarted={ResponseStarted}",
                context.Request.Method, context.Request.Path.Value, status, duration, requestId, stage,
                upload ? diagnostics.StageDurationMs : (long?)null, failure?.GetType().Name, context.Response.HasStarted);
        }
    }
}
