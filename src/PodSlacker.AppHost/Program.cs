using Aspire.Hosting;

// ── PodSlacker Aspire AppHost ─────────────────────────────────────────────────
//
// Run with:
//   dotnet run --project src/PodSlacker.AppHost
//
// This starts the PodSlacker API service and makes it available for the CLI.
// Once running, set PODSLACKER_SERVICE_URL to the displayed API address and
// the CLI will automatically use remote mode:
//
//   export PODSLACKER_SERVICE_URL=http://localhost:5100
//   dotnet run --project src/PodSlacker.Cli -- generate "https://youtu.be/dQw4w9WgXcQ"

var builder = DistributedApplication.CreateBuilder(args);

// The API service — Aspire assigns a random port in development.
// The Aspire dashboard will display the URL once it starts.
builder
    .AddProject<Projects.PodSlacker_ApiService>("podslacker-api")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

builder.Build().Run();
