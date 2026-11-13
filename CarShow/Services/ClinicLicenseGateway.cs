using System.Text.RegularExpressions;

namespace CarShow.Services;

public sealed partial class ClinicLicenseGateway(RequestDelegate next, IHttpClientFactory clients, IConfiguration config)
{
    private static readonly HashSet<string> ResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    { "Content-Type", "Cache-Control", "Set-Cookie", "Content-Security-Policy", "X-Content-Type-Options", "X-Frame-Options", "Referrer-Policy", "Retry-After" };

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/admin/licenses", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/api/license-management", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals("/api/licenses/check", StringComparison.OrdinalIgnoreCase))
        { await next(context); return; }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        var target = Resolve(path, context.Request.Method);
        if (target is null) { context.Response.StatusCode = 404; return; }
        if (!config.GetValue<bool>("ClinicLicensing:Enabled"))
        { context.Response.StatusCode = 503; return; }
        var origin = config["ClinicLicensing:PublicOrigin"] ?? "https://api.mayakhodro.com";
        var expected = new Uri(origin);
        if (!string.Equals(context.Request.Host.Value, expected.Authority, StringComparison.OrdinalIgnoreCase) ||
            (!context.Request.IsHttps && context.Request.Headers["X-Forwarded-Proto"] != "https"))
        { context.Response.StatusCode = 400; return; }
        if (context.Request.Method != "GET" && path != "/api/licenses/check" &&
            context.Request.Headers.Origin != origin)
        { context.Response.StatusCode = 403; return; }
        if (context.Request.ContentLength > 16384) { context.Response.StatusCode = 413; return; }

        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), "http://127.0.0.1:5105" + target);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", context.Connection.RemoteIpAddress?.ToString());
        foreach (var header in new[] { "Cookie", "X-CSRF-TOKEN", "X-Installation-Secret" })
            if (context.Request.Headers.TryGetValue(header, out var value)) request.Headers.TryAddWithoutValidation(header, value.ToArray());
        if (context.Request.Method != "GET")
        {
            // Bound chunked bodies too; never buffer arbitrary request sizes.
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                if (body.Length + read > 16384) { context.Response.StatusCode = 413; return; }
                body.Write(buffer, 0, read);
            }
            request.Content = new ByteArrayContent(body.ToArray());
            request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType ?? "application/json");
        }
        try
        {
            using var response = await clients.CreateClient("ClinicLicensing").SendAsync(request, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (ResponseHeaders.Contains(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { if (!context.Response.HasStarted) context.Response.StatusCode = 503; }
    }

    public static string? Resolve(string path, string method)
    {
        if (method == "GET")
        {
            if (path is "/admin/licenses" or "/admin/licenses/") return "/";
            if (path == "/admin/licenses/assets/app.js") return "/app.js";
            if (path == "/admin/licenses/assets/styles.css") return "/styles.css";
        }
        if (path == "/api/licenses/check" && method == "POST") return path;
        const string prefix = "/api/license-management/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var suffix = path[prefix.Length..];
        if ((method == "GET" && suffix is "csrf" or "licenses" or "signing-key") ||
            (method == "POST" && suffix is "login" or "logout" or "licenses") ||
            (method == "PUT" && LicenseId().IsMatch(suffix)) ||
            (method == "POST" && RotateId().IsMatch(suffix))) return "/admin/" + suffix;
        return null;
    }

    [GeneratedRegex("^licenses/[a-fA-F0-9-]{32,36}$")]
    private static partial Regex LicenseId();
    [GeneratedRegex("^licenses/[a-fA-F0-9-]{32,36}/rotate-secret$")]
    private static partial Regex RotateId();
}
