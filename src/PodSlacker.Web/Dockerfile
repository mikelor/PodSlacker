# ── Stage 1: Build ────────────────────────────────────────────────────────────
# Build context: src/   (run: docker build -f PodSlacker.Web/Dockerfile -t podslacker-web .)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only project files first for restore layer caching.
COPY PodSlacker.ServiceDefaults/PodSlacker.ServiceDefaults.csproj PodSlacker.ServiceDefaults/
COPY PodSlacker.Web/PodSlacker.Web.csproj                          PodSlacker.Web/

RUN dotnet restore PodSlacker.Web/PodSlacker.Web.csproj

# Copy source and publish.
COPY PodSlacker.ServiceDefaults/ PodSlacker.ServiceDefaults/
COPY PodSlacker.Web/             PodSlacker.Web/

RUN dotnet publish PodSlacker.Web/PodSlacker.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ──────────────────────────────────────────────────────────
# The web UI is a pure Blazor Server app — no native OpenCV dependency needed.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Listen on HTTP only — TLS termination is handled by the ingress / App Service.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "podslacker-web.dll"]
