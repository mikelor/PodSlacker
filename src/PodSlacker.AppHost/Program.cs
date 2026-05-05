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

// ── API service ───────────────────────────────────────────────────────────────

var apiService = builder
    .AddProject<Projects.PodSlacker_ApiService>("podslacker-api")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

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
