using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace PodSlacker.ServiceDefaults;

/// <summary>
/// Extension methods that wire up the shared service defaults — health checks,
/// service discovery, HTTP resilience, and OpenTelemetry — for any PodSlacker
/// ASP.NET Core service.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Adds all shared PodSlacker service defaults to the <see cref="IHostApplicationBuilder"/>.
    /// Call this from your service's <c>Program.cs</c> before <c>builder.Build()</c>.
    /// </summary>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Automatic retry, circuit breaker, and timeout for all HttpClients.
            http.AddStandardResilienceHandler();

            // Service-discovery-based name resolution (e.g. http://podslacker-api → real port).
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry tracing and metrics with OTLP export and
    /// standard ASP.NET Core / HTTP / runtime instrumentation.
    /// </summary>
    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes           = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        bool useOtlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is not null;
        if (useOtlp)
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }

    /// <summary>
    /// Adds liveness and readiness health checks.
    /// </summary>
    public static IHostApplicationBuilder AddDefaultHealthChecks(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps the <c>/health</c> (liveness) and <c>/alive</c> endpoints.
    /// Call this from your service's <c>Program.cs</c> after <c>app.Build()</c>.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // /health — full readiness check (all registered checks)
        app.MapHealthChecks("/health");

        // /alive — liveness check (only checks tagged "live")
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        });

        return app;
    }
}
