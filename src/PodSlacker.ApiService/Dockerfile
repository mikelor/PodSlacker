# ── Stage 1: Build ────────────────────────────────────────────────────────────
# Build context: src/   (run: docker build -f PodSlacker.ApiService/Dockerfile -t podslacker-api .)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only project files first so the restore layer is cached as long as
# dependencies don't change — the most expensive step is kept fast.
COPY PodSlacker.Core/PodSlacker.Core.csproj                      PodSlacker.Core/
COPY PodSlacker.ServiceDefaults/PodSlacker.ServiceDefaults.csproj PodSlacker.ServiceDefaults/
COPY PodSlacker.ApiService/PodSlacker.ApiService.csproj           PodSlacker.ApiService/

RUN dotnet restore PodSlacker.ApiService/PodSlacker.ApiService.csproj

# Copy source and publish a self-contained, trimmed release build.
COPY PodSlacker.Core/          PodSlacker.Core/
COPY PodSlacker.ServiceDefaults/ PodSlacker.ServiceDefaults/
COPY PodSlacker.ApiService/    PodSlacker.ApiService/

RUN dotnet publish PodSlacker.ApiService/PodSlacker.ApiService.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# OpenCV native libraries required by OpenCvSharp4.runtime.ubuntu.22.04-x64.
# libgomp1   — OpenMP, used internally by OpenCV
# libdc1394-*— IEEE 1394 (FireWire) camera support; safe to include for completeness
RUN apt-get update && apt-get install -y --no-install-recommends \
    libglib2.0-0      \
    libgtk2.0-0       \
    libavcodec-extra  \
    libavformat-dev   \
    libswscale-dev    \
    libdc1394-dev     \
    libgomp1          \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Kokoro TTS downloads its ~320 MB ONNX model to AppContext.BaseDirectory on
# first use.  Mount a named volume at /app to persist it across container
# restarts and avoid re-downloading on every deploy (see docker-compose.yml).

# Listen on HTTP only — TLS termination is handled by the ingress / App Service.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "podslacker-api.dll"]
