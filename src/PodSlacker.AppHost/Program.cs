using Aspire.Hosting;

// ── PodSlacker Aspire AppHost ─────────────────────────────────────────────────
//
// Starts both the API service and the web UI, wires service discovery between
// them, and opens the Aspire dashboard.
//
// Run with:
//   dotnet run --project src/PodSlacker.AppHost
//
// The dashboard URL is printed at startup (usually http://localhost:18888).
// The web UI URL is shown in the dashboard under "podslacker-web".

var builder = DistributedApplication.CreateBuilder(args);

// ── Propagate required secrets from the host environment ─────────────────────
// These env vars must be set before running the AppHost.  Passing them through
// .WithEnvironment() makes it clear which secrets each service needs, and
// ensures they survive Aspire's process-launch isolation on all platforms.

static string? Env(string name) => Environment.GetEnvironmentVariable(name);

// ── API service ───────────────────────────────────────────────────────────────

var apiService = builder
    .AddProject<Projects.PodSlacker_ApiService>("podslacker-api")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    // LLM (OpenRouter by default; swap for OPENAI_API_KEY if using OpenAI direct)
    .WithEnvironment("OPENROUTER_API_KEY",  Env("OPENROUTER_API_KEY")  ?? "")
    .WithEnvironment("OPENAI_API_KEY",      Env("OPENAI_API_KEY")      ?? "")
    // YouTube Data API (optional — used for metadata fallback)
    .WithEnvironment("YOUTUBE_API_KEY",     Env("YOUTUBE_API_KEY")     ?? "")
    // GitHub Pages publishing (optional — only needed when publish_github=true)
    .WithEnvironment("GITHUB_TOKEN",        Env("GITHUB_TOKEN")        ?? "");

// ── Web UI ────────────────────────────────────────────────────────────────────
// WithReference injects the API service endpoint into the web app via Aspire's
// service discovery environment variables (services__podslacker-api__http__0).
// The web app resolves "http://podslacker-api" to the actual endpoint at runtime.

builder
    .AddProject<Projects.PodSlacker_Web>("podslacker-web")
    .WithReference(apiService)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    // Clear any manually-set service URL so Aspire's service discovery
    // (injected by WithReference above) is always used when orchestrated.
    .WithEnvironment("PODSLACKER_SERVICE_URL", "")
    .WithExternalHttpEndpoints();   // makes the web UI accessible from the browser

builder.Build().Run();
