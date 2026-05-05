using PodSlacker.ServiceDefaults;
using PodSlacker.Web.Services;

var builder = WebApplication.CreateBuilder(args);

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

app.MapGet("/download/{jobId}", async (string jobId, PodSlackerApiClient apiClient, CancellationToken ct) =>
{
    try
    {
        byte[] bytes = await apiClient.DownloadPageAsync(jobId, ct);
        return Results.File(bytes, "text/html; charset=utf-8", $"podslacker_{jobId}.html");
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
