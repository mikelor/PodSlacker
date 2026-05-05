using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PodSlacker.ApiService.Models;
using PodSlacker.ApiService.Services;
using PodSlacker.Core.Services;
using PodSlacker.Core.Pipeline;
using PodSlacker.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ── Service defaults (health checks, service discovery, resilience, OTEL) ────

builder.AddServiceDefaults();

// ── JSON options — use snake_case to match the CLI's serialization and podslacker.json ──

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy        = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;   // tolerate mixed cases on input
    // Serialize enums as strings ("Completed") not integers (2) so the CLI
    // can compare status values with string equality.
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// ── API services ──────────────────────────────────────────────────────────────

builder.Services.AddOpenApi();

// PodSlacker.Core services — same registrations as the CLI.
builder.Services.AddTransient<TranscriptService>();
builder.Services.AddTransient<VideoMetadataService>();
builder.Services.AddTransient<LlmService>();
builder.Services.AddTransient<TtsService>();
builder.Services.AddTransient<KokoroTtsService>();
builder.Services.AddTransient<FrameCaptureService>();
builder.Services.AddTransient<GitHubPublishService>();
builder.Services.AddTransient<PodSlackerPipeline>();

// API-specific services.
builder.Services.AddSingleton<JobStore>();
builder.Services.AddTransient<PipelineRunner>();
builder.Services.AddHostedService<JobEvictionService>();

// ── Build ─────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────

// API key authentication — every request must supply X-Api-Key.
string? requiredApiKey = Environment.GetEnvironmentVariable("PODSLACKER_API_KEY");
if (string.IsNullOrEmpty(requiredApiKey))
    app.Logger.LogWarning(
        "PODSLACKER_API_KEY is not set. The API is running WITHOUT authentication.");

app.Use(async (context, next) =>
{
    // Skip auth on health/alive endpoints.
    if (context.Request.Path.StartsWithSegments("/health") ||
        context.Request.Path.StartsWithSegments("/alive"))
    {
        await next(context);
        return;
    }

    if (!string.IsNullOrEmpty(requiredApiKey))
    {
        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var supplied) ||
            supplied != requiredApiKey)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing X-Api-Key header." });
            return;
        }
    }

    await next(context);
});

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapDefaultEndpoints();

// ── API routes ────────────────────────────────────────────────────────────────

var api = app.MapGroup("/api");

// POST /api/jobs — submit a new job
api.MapPost("/jobs", (CreateJobRequest request,
                      JobStore         store,
                      PipelineRunner   runner,
                      HttpContext      ctx) =>
{
    if (string.IsNullOrWhiteSpace(request.VideoUrl))
        return Results.BadRequest(new { error = "VideoUrl is required." });

    var job = store.Create(request.VideoUrl);

    runner.StartBackground(job, request.VideoUrl, request.Config);

    string baseUrl  = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    string location = $"{baseUrl}/api/jobs/{job.Id}";

    return Results.Accepted(location, ToStatusResponse(job, baseUrl));
})
.WithName("CreateJob")
.WithSummary("Submit a new PodSlacker job")
.Produces<JobStatusResponse>(StatusCodes.Status202Accepted)
.Produces(StatusCodes.Status400BadRequest);

// GET /api/jobs/{id} — poll job status
api.MapGet("/jobs/{id}", (string id, JobStore store, HttpContext ctx) =>
{
    var job = store.Get(id);
    if (job is null)
        return Results.NotFound(new { error = $"Job '{id}' not found." });

    string baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    return Results.Ok(ToStatusResponse(job, baseUrl));
})
.WithName("GetJob")
.WithSummary("Get the status of a job")
.Produces<JobStatusResponse>()
.Produces(StatusCodes.Status404NotFound);

// GET /api/jobs/{id}/page — download the generated HTML page
api.MapGet("/jobs/{id}/page", (string id, JobStore store) =>
{
    var job = store.Get(id);
    if (job is null)
        return Results.NotFound(new { error = $"Job '{id}' not found." });

    if (job.Status != JobStatus.Completed || job.HtmlBytes is null)
    {
        return job.Status == JobStatus.Failed
            ? Results.Problem(detail: job.Error, title: "Job failed", statusCode: 500)
            : Results.Problem(
                detail: $"Job is {job.Status}. Poll /api/jobs/{job.Id} until status is Completed.",
                title: "Not ready yet",
                statusCode: StatusCodes.Status202Accepted);
    }

    return Results.File(
        job.HtmlBytes,
        contentType:    "text/html; charset=utf-8",
        fileDownloadName: $"podslacker_{job.Id}.html");
})
.WithName("DownloadPage")
.WithSummary("Download the generated HTML page for a completed job")
.Produces(StatusCodes.Status200OK, contentType: "text/html")
.Produces(StatusCodes.Status202Accepted)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────

static JobStatusResponse ToStatusResponse(JobRecord job, string baseUrl) => new()
{
    Id       = job.Id,
    VideoUrl = job.VideoUrl,
    Status   = job.Status,
    Message  = job.Message,
    Percent  = job.Percent,
    Error    = job.Error,
    PageUrl  = job.Status == JobStatus.Completed
        ? $"{baseUrl}/api/jobs/{job.Id}/page"
        : null,
};
