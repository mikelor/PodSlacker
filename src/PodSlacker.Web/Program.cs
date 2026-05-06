using Microsoft.AspNetCore.HttpOverrides;
using PodSlacker.ServiceDefaults;
using PodSlacker.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Forwarded headers ─────────────────────────────────────────────────────────
// Required when running behind a reverse proxy or tunnel (e.g. Dev Tunnels,
// nginx, Azure App Service).  Tells ASP.NET Core to trust the X-Forwarded-For
// and X-Forwarded-Proto headers so that:
//   • Request.Scheme returns "https" (not "http")
//   • Blazor Server's SignalR hub produces the correct wss:// upgrade URL
// Without this the Blazor circuit silently fails to establish when accessed
// through a tunnel and button clicks do nothing.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust all proxies — fine for Dev Tunnels and single-hop Azure ingress.
    // Restrict to specific proxy IPs in a hardened multi-hop production setup.
    options.KnownIPNetworks.Clear();  // .NET 10: replaces the obsolete KnownNetworks
    options.KnownProxies.Clear();
});

// ── Service defaults (health checks, service discovery, resilience, OTEL) ────
// AddServiceDefaults also calls ConfigureHttpClientDefaults which wires
// service discovery and resilience onto ALL registered HttpClients automatically.

builder.AddServiceDefaults();

// ── Blazor ────────────────────────────────────────────────────────────────────

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── PodSlacker API client ─────────────────────────────────────────────────────
//
// Base address resolution priority:
//   1. PODSLACKER_SERVICE_URL env var            (manual startup)
//   2. PodSlacker:ServiceUrl in appsettings.json (manual startup)
//   3. "http://podslacker-api"                   (Aspire — resolved by service discovery)
//
// The X-Api-Key header is added when PODSLACKER_API_KEY env var or
// PodSlacker:ApiKey config value is set.

string apiServiceUrl =
    (Environment.GetEnvironmentVariable("PODSLACKER_SERVICE_URL") is { Length: > 0 } envUrl ? envUrl : null)
    ?? (builder.Configuration["PodSlacker:ServiceUrl"] is { Length: > 0 } cfgUrl ? cfgUrl : null)
    ?? "http://podslacker-api";   // resolved by Aspire service discovery when orchestrated

string? apiKey =
    Environment.GetEnvironmentVariable("PODSLACKER_API_KEY")
    ?? builder.Configuration["PodSlacker:ApiKey"];

builder.Services.AddHttpClient<PodSlackerApiClient>(client =>
{
    client.BaseAddress = new Uri(apiServiceUrl.TrimEnd('/') + "/");
    client.Timeout     = TimeSpan.FromMinutes(30);  // pipelines can take a while

    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
});

// ── Build ─────────────────────────────────────────────────────────────────────

var app = builder.Build();

// Must be first — rewrites Request.Scheme/Host before any other middleware reads them.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();   // only redirect to HTTPS in production
}
app.UseAntiforgery();
app.MapStaticAssets();

// ── Download proxy ────────────────────────────────────────────────────────────
// Proxies GET /download/{jobId} through to the API service so the browser can
// download the HTML page as a file without needing to know the API URL or key.

app.MapGet("/download/{jobId}", async (string jobId, string? title, PodSlackerApiClient apiClient, CancellationToken ct) =>
{
    try
    {
        byte[] bytes = await apiClient.DownloadPageAsync(jobId, ct);

        // Build a safe filename: title_first8charsOfJobId.html
        string safeTitle = string.IsNullOrWhiteSpace(title)
            ? "slackcast"
            : System.Text.RegularExpressions.Regex
                .Replace(title, @"[^\w]+", "_")
                .Trim('_')
                .ToLowerInvariant();
        if (safeTitle.Length > 60) safeTitle = safeTitle[..60].TrimEnd('_');
        string fileName = $"{safeTitle}_{jobId[..8]}.html";

        return Results.File(bytes, "text/html; charset=utf-8", fileName);
    }
    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound();
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "Download failed");
    }
});

app.MapRazorComponents<PodSlacker.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
